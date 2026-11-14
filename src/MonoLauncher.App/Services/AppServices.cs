using System.Net.Http;
using MonoLauncher.Agent.Local;
using MonoLauncher.Agent.Providers;
using MonoLauncher.Agent.Tools;
using MonoLauncher.Core;
using MonoLauncher.Core.Auth;
using MonoLauncher.Core.Config;
using MonoLauncher.Core.Download;
using MonoLauncher.Core.Instances;
using MonoLauncher.Core.Java;
using MonoLauncher.Core.Launch;
using MonoLauncher.Core.Loaders;
using MonoLauncher.Core.Logging;
using MonoLauncher.Core.Mods;
using MonoLauncher.Core.Security;
using MonoLauncher.Core.Update;
using MonoLauncher.Core.Versions;

namespace MonoLauncher.App.Services;

/// <summary>Composition root. One place that owns every long-lived service.</summary>
public static class AppServices
{
    public static AppPaths Paths { get; private set; } = AppPaths.Default;
    public static LogService Log { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;
    public static SecretStore Secrets { get; private set; } = null!;
    public static HttpClient Http { get; private set; } = null!;
    public static HttpClient DownloadHttp { get; private set; } = null!;
    public static VersionListService Versions { get; private set; } = null!;
    public static JavaScanner Java { get; private set; } = null!;
    public static AccountStore Accounts { get; private set; } = null!;
    public static MicrosoftAuthService MsAuth { get; private set; } = null!;
    public static InstanceStore Instances { get; private set; } = null!;
    public static LaunchService Launch { get; private set; } = null!;
    public static LoaderService Loaders { get; private set; } = null!;
    public static ModrinthService Modrinth { get; private set; } = null!;
    public static UpdateService Updates { get; private set; } = null!;
    public static LauncherTools Tools { get; private set; } = null!;
    public static AgentProviderFactory Providers { get; private set; } = null!;
    public static LocalLlamaService LocalAi { get; private set; } = null!;
    public static DownloadCenter Downloads { get; } = new();
    public static bool Initialized { get; private set; }

    public static event Action<Exception>? UnhandledError;

    public static void Initialize()
    {
        if (Initialized) return;
        Paths.EnsureDirectories();
        Log = new LogService(Paths);
        Settings = new SettingsService(Paths);
        Secrets = new SecretStore(Paths);

        Http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(180)
        };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("MonoLauncher/0.9 (launcher)");

        DownloadHttp = DownloadEngine.CreateClient(new DownloadOptions
        {
            MaxConcurrency = Settings.Current.DownloadConcurrency,
            ProxyUrl = Settings.Current.ProxyUrl
        });

        Versions = new VersionListService(Http, Paths);
        Java = new JavaScanner();
        Accounts = new AccountStore(Paths);
        MsAuth = new MicrosoftAuthService(Http);
        Instances = new InstanceStore(Paths);
        Launch = new LaunchService(Paths);
        Loaders = new LoaderService(Http, Paths, Versions);
        Modrinth = new ModrinthService(Http);
        Updates = new UpdateService(Http, Paths)
        {
            Repository = Settings.Current.UpdateRepository,
            CurrentVersion = "0.9.0"
        };
        Tools = new LauncherTools(
            Paths, Instances, Versions, Loaders, Modrinth, Launch, Java, Accounts,
            new DownloadCenterBridge(
                stage => Downloads.Begin(stage),
                message => Downloads.Complete(message),
                error => Downloads.Fail(error)));
        Providers = new AgentProviderFactory(Http, Secrets);
        LocalAi = new LocalLlamaService();

        ApplySettings();
        Initialized = true;
        Log.Info("app", $"MonoLauncher 启动，数据目录 {Paths.Root}");
    }

    public static void ApplySettings()
    {
        var s = Settings.Current;
        PerformanceState.Apply(ParseTier(s.PerformanceTier), s.AnimationsEnabled, s.PointerLighting);
        AutoDegrade.Enabled = s.AutoDowngrade;
        AutoDegrade.Threshold = s.AutoDowngradeFpsThreshold;
    }

    public static GlassTier ParseTier(string? value) => value switch
    {
        "HighPerformance" => GlassTier.HighPerformance,
        "LowPower" => GlassTier.LowPower,
        "Off" => GlassTier.Off,
        _ => GlassTier.Balanced
    };

    public static string TierKey(GlassTier tier) => tier switch
    {
        GlassTier.HighPerformance => "HighPerformance",
        GlassTier.LowPower => "LowPower",
        GlassTier.Off => "Off",
        _ => "Balanced"
    };

    public static HttpClient CreateDownloadClient() => DownloadEngine.CreateClient(new DownloadOptions
    {
        MaxConcurrency = Settings.Current.DownloadConcurrency,
        ProxyUrl = Settings.Current.ProxyUrl,
        UseMirror = Settings.Current.UseMirror,
        UserAgent = Settings.Current.UserAgent
    });

    public static void ReportError(Exception ex, string category = "app")
    {
        Log.Error(category, ex.Message, ex);
        UnhandledError?.Invoke(ex);
    }
}
