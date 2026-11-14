using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GlassCraft.App.Services;
using GlassCraft.Core.Auth;

namespace GlassCraft.App.Views;

public partial class MicrosoftLoginWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private MsaDeviceCode? _deviceCode;

    public MinecraftAccount? Account { get; private set; }

    public MicrosoftLoginWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await StartAsync();
        Closed += (_, _) => _cts.Cancel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async Task StartAsync()
    {
        try
        {
            _deviceCode = await AppServices.MsAuth.StartDeviceCodeAsync(_cts.Token);
            if (CodeText is not null) CodeText.Text = _deviceCode.UserCode;
            if (UriText is not null) UriText.Text = _deviceCode.VerificationUri;
            if (HintText is not null)
                HintText.Text =
                    "1. 点「打开浏览器」，访问下面的地址。\n2. 输入左侧的登录代码，用你的 Microsoft 账户登录。\n3. 登录完成后回到这里，会自动继续。";
            if (StatusText is not null) StatusText.Text = "等待你在浏览器中完成登录…";

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_deviceCode.VerificationUri)
                {
                    UseShellExecute = true
                });
            }
            catch { /* user can open manually */ }

            var account = await AppServices.MsAuth.PollForAccountAsync(
                _deviceCode,
                s => Dispatcher.UIThread.Post(() =>
                {
                    if (StatusText is not null) StatusText.Text = s;
                }),
                _cts.Token);

            Account = account;
            Dispatcher.UIThread.Post(() =>
            {
                if (StatusText is not null) StatusText.Text = $"登录成功：{account.Name}";
                Close();
            });
        }
        catch (OperationCanceledException)
        {
            Close();
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (HintText is not null) HintText.Text = "登录失败";
                if (StatusText is not null) StatusText.Text = ex.Message;
            });
            AppServices.Log.Error("auth", "Microsoft 登录失败", ex);
        }
    }

    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (_deviceCode is null) return;
        try
        {
            CodeText?.SelectAll();
            if (StatusText is not null) StatusText.Text = "代码已全选，按 Ctrl+C 复制";
        }
        catch { }
    }

    private void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (_deviceCode is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_deviceCode.VerificationUri)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        Close();
    }
}
