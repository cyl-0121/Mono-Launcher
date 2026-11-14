using System.Runtime.InteropServices;
using Avalonia;

namespace MonoLauncher.App;

internal static class Program
{
    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
    private static readonly IntPtr PerMonitorV2 = new(-4);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [STAThread]
    public static void Main(string[] args)
    {
        var shotIndex = Array.IndexOf(args, "--screenshot");
        if (shotIndex >= 0 && shotIndex + 1 < args.Length)
        {
            App.ScreenshotPath = args[shotIndex + 1];
            App.ExitAfterScreenshot = true;
        }
        var pageIndex = Array.IndexOf(args, "--page");
        if (pageIndex >= 0 && pageIndex + 1 < args.Length)
            App.StartupPage = args[pageIndex + 1];
        var messageIndex = Array.IndexOf(args, "--agent-message");
        if (messageIndex >= 0 && messageIndex + 1 < args.Length)
            App.AgentStartupMessage = args[messageIndex + 1];
        var launchIndex = Array.IndexOf(args, "--launch-instance");
        if (launchIndex >= 0 && launchIndex + 1 < args.Length)
            App.LaunchInstanceId = args[launchIndex + 1];
        if (args.Contains("--screenshot-agreement"))
            App.AgreementScreenshot = true;
        if (args.Contains("--screenshot-login"))
            App.LoginScreenshot = true;

        try
        {
            // The manifest already asks for per-monitor-v2, but some launch paths (dotnet apphost
            // under a virtual display) end up virtualising the window. Force it explicitly.
            SetProcessDpiAwarenessContext(PerMonitorV2);
        }
        catch
        {
            // already set or unsupported: harmless
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(AppContext.BaseDirectory, "startup-crash.log"),
                    ex.ToString());
            }
            catch { }
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
