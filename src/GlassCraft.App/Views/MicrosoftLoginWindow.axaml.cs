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

    /// <summary>Renders the window through Avalonia (bypasses DPI-virtualised screen captures).</summary>
    public void RenderSnapshot(string path)
    {
        var scale = RenderScaling;
        var w = Math.Max(1, (int)Math.Ceiling(ClientSize.Width * scale));
        var h = Math.Max(1, (int)Math.Ceiling(ClientSize.Height * scale));
        var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new Avalonia.PixelSize(w, h), new Avalonia.Vector(96 * scale, 96 * scale));
        rtb.Render(this);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        rtb.Save(fs);
    }

    public MicrosoftLoginWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await StartAsync();
        Closed += (_, _) => _cts.Cancel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // x:Name fields are not generated for this window (hand-written InitializeComponent),
    // so look the controls up explicitly instead of relying on generated fields.
    private void SetHint(string text) => this.FindControl<TextBlock>("HintText")?.SetCurrentValue(TextBlock.TextProperty, text);
    private void SetStatus(string text) => this.FindControl<TextBlock>("StatusText")?.SetCurrentValue(TextBlock.TextProperty, text);

    private async Task StartAsync()
    {
        try
        {
            AppServices.Log.Info("auth", "开始申请设备代码");
            _deviceCode = await AppServices.MsAuth.StartDeviceCodeAsync(_cts.Token);
            AppServices.Log.Info("auth", $"设备代码已获取 user_code={_deviceCode.UserCode}");

            var codeBlock = this.FindControl<SelectableTextBlock>("CodeText");
            if (codeBlock is not null)
            {
                codeBlock.Text = _deviceCode.UserCode;
                AppServices.Log.Info("auth", $"代码写入 UI：{_deviceCode.UserCode}");
            }
            else
            {
                AppServices.Log.Info("auth", "找不到 CodeText 控件");
            }

            var uriBlock = this.FindControl<TextBlock>("UriText");
            if (uriBlock is not null) uriBlock.Text = _deviceCode.VerificationUri;

            SetHint("1. 点「打开浏览器并登录」，访问下面的地址。\n2. 输入上方的登录代码，用你的 Microsoft 账户登录。\n3. 登录完成后回到这里，会自动继续。");
            SetStatus("等待你在浏览器中完成登录…");

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
                s => Dispatcher.UIThread.Post(() => SetStatus(s)),
                _cts.Token);

            Account = account;
            Dispatcher.UIThread.Post(() =>
            {
                SetStatus($"登录成功：{account.Name}");
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
                SetHint("登录失败");
                SetStatus(ex.Message);
            });
            AppServices.Log.Error("auth", "Microsoft 登录失败", ex);
        }
    }

    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (_deviceCode is null) return;
        try
        {
            this.FindControl<SelectableTextBlock>("CodeText")?.SelectAll();
            SetStatus("代码已全选，按 Ctrl+C 复制");
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
