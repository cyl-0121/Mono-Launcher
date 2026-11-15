using System.Net.Http;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MonoLauncher.Agent.Local;
using MonoLauncher.Agent.Providers;
using MonoLauncher.Agent.Tools;
using MonoLauncher.Core;
using MonoLauncher.Core.Auth;
using MonoLauncher.Core.Config;
using MonoLauncher.Core.Deploy;
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
    public static ModManager Mods { get; private set; } = null!;
    public static JavaRuntimeInstaller JavaInstaller { get; private set; } = null!;
    public static EnvironmentDeployer Deployer { get; private set; } = null!;
    public static ModInstaller ModsInstaller { get; private set; } = null!;
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
        Mods = new ModManager(Paths);
        JavaInstaller = new JavaRuntimeInstaller(Http, Paths);
        Deployer = new EnvironmentDeployer(
            Paths, Http, Versions, Java, JavaInstaller, Instances, Loaders, Modrinth, Mods);
        ModsInstaller = new ModInstaller(Http, Modrinth, Mods, Paths);
        Updates = new UpdateService(Http, Paths)
        {
            Repository = Settings.Current.UpdateRepository,
            CurrentVersion = "1.0.3",
            DownloadSiteUrl = Settings.Current.DownloadSiteUrl
        };
        Tools = new LauncherTools(
            Paths, Instances, Versions, Loaders, Modrinth, Launch, Java, Accounts,
            new DownloadCenterBridge(
                stage => Downloads.Begin(stage),
                message => Downloads.Complete(message),
                error => Downloads.Fail(error)),
            Deployer, ModsInstaller, Mods, Settings);
        // Default AI channel for tools; the Agent page overrides it with its on-screen selection.
        Tools.QuickChat = QuickChatAsync;
        Providers = new AgentProviderFactory(Http, Secrets);
        LocalAi = new LocalLlamaService();

        ApplySettings();
        Initialized = true;
        Log.Info("app", $"MonoLauncher 启动，数据目录 {Paths.Root}");
    }

    /// <summary>
    /// One-shot chat using the saved provider settings. Lets features outside the Agent page
    /// (Mod intro translation, mod recommendation) work even if that page was never opened.
    /// </summary>
    public static async Task<string> QuickChatAsync(string prompt, CancellationToken ct = default)
    {
        var cfg = new ViewModels.AgentConfigStore(Paths).Load();
        var providerId = string.IsNullOrWhiteSpace(cfg.ProviderId) ? "mock" : cfg.ProviderId;

        IChatCompletionService chat;
        if (string.Equals(providerId, "mock", StringComparison.OrdinalIgnoreCase))
        {
            chat = new MockChatService();
        }
        else
        {
            var apiKey = Providers.LoadApiKey(providerId);
            var def = ProviderCatalog.Find(providerId);
            if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(cfg.BaseUrl))
                throw new InvalidOperationException("还没有配置 AI 提供商，请先到「AI」页填写并保存提供商与密钥");
            chat = Providers.Create(new AgentProviderConfig
            {
                ProviderId = providerId,
                BaseUrl = string.IsNullOrWhiteSpace(cfg.BaseUrl) ? null : cfg.BaseUrl,
                ApiKey = apiKey,
                Model = string.IsNullOrWhiteSpace(cfg.Model) ? def?.DefaultModel ?? "" : cfg.Model
            });
        }

        var reply = await chat.GetChatMessageContentsAsync(
            new ChatHistory { new ChatMessageContent(AuthorRole.User, prompt) },
            null, null, ct).ConfigureAwait(false);
        return string.Join("\n", reply.Select(r => r.Content ?? "")).Trim();
    }

    public static void ApplySettings()    {
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
