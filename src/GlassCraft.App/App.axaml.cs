using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GlassCraft.App.Services;
using GlassCraft.Core.Security;

namespace GlassCraft.App;

public partial class App : Application
{
    public static string? ScreenshotPath { get; set; }
    public static bool ExitAfterScreenshot { get; set; }
    public static string? StartupPage { get; set; }
    public static string? AgentStartupMessage { get; set; }
    public static string? LaunchInstanceId { get; set; }
    public static bool AgreementScreenshot { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                AppServices.Initialize();
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-crash.log"), ex.ToString());
                }
                catch { }
                throw;
            }

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    AppServices.Log?.Error("crash", SecretRedactor.Redact(ex.ToString()));
                }
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                AppServices.Log?.Error("crash", "未观察的任务异常: " + SecretRedactor.Redact(e.Exception.ToString()));
                e.SetObserved();
            };

            desktop.MainWindow = new Views.MainWindow();
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Terms of use: block the UI until the user agrees, unless this is an automated run.
            if (string.IsNullOrWhiteSpace(ScreenshotPath) && !Views.ConsentStore.IsAccepted())
            {
                var main = desktop.MainWindow;
                main.Opened += (_, _) =>
                {
                    DispatcherTimer.RunOnce(async () =>
                    {
                        var agreement = new Views.AgreementWindow();
                        await agreement.ShowDialog(main);
                        if (!agreement.Accepted)
                        {
                            AppServices.Log.Info("app", "用户拒绝了用户协议，退出");
                            desktop.Shutdown();
                        }
                    }, TimeSpan.FromMilliseconds(200));
                };
            }
            else if (!string.IsNullOrWhiteSpace(ScreenshotPath) && AgreementScreenshot)
            {
                var agreement = new Views.AgreementWindow();
                agreement.Show();
                DispatcherTimer.RunOnce(() =>
                {
                    agreement.RenderSnapshot(ScreenshotPath!);
                    AppServices.Log.Info("ui", "协议窗口截图已保存");
                    desktop.Shutdown();
                }, TimeSpan.FromSeconds(1.2));
            }

            if (!string.IsNullOrWhiteSpace(StartupPage) && desktop.MainWindow.DataContext is ViewModels.MainWindowViewModel vm)
                vm.NavigateCommand.Execute(StartupPage);

            if (!string.IsNullOrWhiteSpace(ScreenshotPath))
            {
                var window = desktop.MainWindow;
                DispatcherTimer.RunOnce(() =>
                {
                    try
                    {
                        var scaling = window.RenderScaling;
                        var w = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Width * scaling));
                        var h = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Height * scaling));
                        var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96 * scaling, 96 * scaling));
                        rtb.Render(window);
                        Directory.CreateDirectory(Path.GetDirectoryName(ScreenshotPath!)!);
                        using var fs = File.Create(ScreenshotPath!);
                        rtb.Save(fs);
                        AppServices.Log.Info("ui", $"截图已保存 {ScreenshotPath} ({w}x{h})");
                    }
                    catch (Exception ex)
                    {
                        AppServices.Log.Error("ui", "截图失败", ex);
                    }
                    if (ExitAfterScreenshot)
                        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
                }, TimeSpan.FromSeconds(
                    !string.IsNullOrWhiteSpace(LaunchInstanceId) ? 22 :
                    !string.IsNullOrWhiteSpace(AgentStartupMessage) ? 14 : 4));
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
