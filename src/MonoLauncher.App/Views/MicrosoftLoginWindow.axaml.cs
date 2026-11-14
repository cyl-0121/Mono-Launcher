using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MonoLauncher.App.Services;
using MonoLauncher.Core.Auth;

namespace MonoLauncher.App.Views;

public partial class MicrosoftLoginWindow : Window
{
    private CancellationTokenSource _flowCts = new();
    private DispatcherTimer? _countdown;
    private DateTimeOffset _expiresAt;
    private bool _closed;
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
        Opened += async (_, _) => await StartFlowAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            _countdown?.Stop();
            _flowCts.Cancel();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // x:Name fields are not generated for this window (hand-written InitializeComponent),
    // so look the controls up explicitly instead of relying on generated fields.
    private void SetHint(string text) =>
        this.FindControl<TextBlock>("HintText")?.SetCurrentValue(TextBlock.TextProperty, text);
    private void SetStatus(string text) =>
        this.FindControl<TextBlock>("StatusText")?.SetCurrentValue(TextBlock.TextProperty, text);
    private void SetTiming(string text) =>
        this.FindControl<TextBlock>("TimingText")?.SetCurrentValue(TextBlock.TextProperty, text);

    private async Task StartFlowAsync()
    {
        var token = _flowCts.Token;
        try
        {
            AppServices.Log.Info("auth", "开始申请设备代码");
            SetStatus("正在申请登录代码…");
            SetTiming("");
            var code = await AppServices.MsAuth.StartDeviceCodeAsync(token);
            _deviceCode = code;
            _expiresAt = DateTimeOffset.Now.AddSeconds(code.ExpiresIn);
            AppServices.Log.Info("auth", $"设备代码已获取 user_code={code.UserCode}");

            var codeBlock = this.FindControl<SelectableTextBlock>("CodeText");
            if (codeBlock is not null) codeBlock.Text = code.UserCode;
            var uriBlock = this.FindControl<TextBlock>("UriText");
            if (uriBlock is not null) uriBlock.Text = code.VerificationUri;

            SetHint("1. 点「打开浏览器并登录」，访问下方地址。\n2. 输入上面的代码（区分 0/O、1/I，建议直接复制）。\n3. 登录完成后回到这里，会自动继续。");
            SetStatus("等待你在浏览器中完成登录…");
            StartCountdown();

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(code.VerificationUri)
                {
                    UseShellExecute = true
                });
            }
            catch { /* user can open manually */ }

            var account = await AppServices.MsAuth.PollForAccountAsync(
                code,
                s => Dispatcher.UIThread.Post(() => SetStatus(s)),
                token);

            Account = account;
            Dispatcher.UIThread.Post(() =>
            {
                _countdown?.Stop();
                SetTiming("");
                SetStatus($"登录成功：{account.Name}");
                Close();
            });
        }
        catch (OperationCanceledException)
        {
            // Either the window closed or the user asked for a new code; nothing to report.
        }
        catch (AuthException ex) when (ex.Code == "device_code_expired")
        {
            Dispatcher.UIThread.Post(() =>
            {
                _countdown?.Stop();
                SetTiming("");
                SetStatus("这个代码已过期（15 分钟）。点「换一个代码」重新获取。");
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _countdown?.Stop();
                SetHint("登录失败");
                SetStatus(ex.Message);
            });
            AppServices.Log.Error("auth", "Microsoft 登录失败", ex);
        }
    }

    private void StartCountdown()
    {
        _countdown?.Stop();
        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) =>
        {
            var left = _expiresAt - DateTimeOffset.Now;
            if (left <= TimeSpan.Zero)
            {
                SetTiming("代码已过期");
                _countdown?.Stop();
                return;
            }
            SetTiming($"代码剩余 {left.Minutes:00}:{left.Seconds:00} 有效 · 一个代码只能用一次");
        };
        _countdown.Start();
        var left = _expiresAt - DateTimeOffset.Now;
        SetTiming($"代码剩余 {left.Minutes:00}:{left.Seconds:00} 有效 · 一个代码只能用一次");
    }

    private void OnRefresh(object? sender, RoutedEventArgs e)
    {
        // Cancel the current poll and request a fresh device code.
        _flowCts.Cancel();
        _flowCts = new CancellationTokenSource();
        _ = StartFlowAsync();
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
        _closed = true;
        _flowCts.Cancel();
        Close();
    }
}
