using System.ComponentModel;
using System.Text.Json;
using MonoLauncher.Core;
using MonoLauncher.Core.Auth;
using MonoLauncher.Core.Download;
using MonoLauncher.Core.Instances;
using MonoLauncher.Core.Java;
using MonoLauncher.Core.Launch;
using MonoLauncher.Core.Loaders;
using MonoLauncher.Core.Models;
using MonoLauncher.Core.Mods;
using MonoLauncher.Core.Versions;
using Microsoft.SemanticKernel;

namespace MonoLauncher.Agent.Tools;

/// <summary>
/// Tools exposed to the agent. Every tool reports real launcher state; nothing is simulated.
/// High-risk tools (install/launch) are gated by AgentSession's approval flow before invocation.
/// </summary>
public sealed class LauncherTools
{
    private readonly AppPaths _paths;
    private readonly InstanceStore _instances;
    private readonly VersionListService _versions;
    private readonly LoaderService _loaders;
    private readonly ModrinthService _modrinth;
    private readonly LaunchService _launch;
    private readonly JavaScanner _java;
    private readonly AccountStore _accounts;
    private readonly DownloadCenterBridge _downloads;
    private readonly Dictionary<string, GameSession> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public LauncherTools(
        AppPaths paths,
        InstanceStore instances,
        VersionListService versions,
        LoaderService loaders,
        ModrinthService modrinth,
        LaunchService launch,
        JavaScanner java,
        AccountStore accounts,
        DownloadCenterBridge downloads)
    {
        _paths = paths;
        _instances = instances;
        _versions = versions;
        _loaders = loaders;
        _modrinth = modrinth;
        _launch = launch;
        _java = java;
        _accounts = accounts;
        _downloads = downloads;
    }

    public static KernelPlugin CreatePlugin(LauncherTools tools) =>
        KernelPluginFactory.CreateFromObject(tools, "launcher");

    [KernelFunction("list_instances")]
    [Description("列出本机所有 Minecraft 实例及其版本、Loader、最近游玩时间。返回 JSON 数组。")]
    public string ListInstances()
    {
        var list = _instances.List().Select(r => new
        {
            id = r.Id,
            name = r.Name,
            version = r.VersionId,
            loader = r.Loader ?? "vanilla",
            lastPlayed = r.LastPlayedAt == default ? null : r.LastPlayedAt.ToString("O"),
            gameDirectory = _instances.GameDirectory(r),
            running = IsRunning(r.Id)
        });
        return JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = false });
    }

    [KernelFunction("list_installed_versions")]
    [Description("列出本机已下载的所有 Minecraft 版本（含 Loader 版本），返回 JSON 数组。")]
    public string ListInstalledVersions()
    {
        if (!Directory.Exists(_paths.VersionsDir)) return "[]";
        var list = Directory.EnumerateDirectories(_paths.VersionsDir)
            .Select(Path.GetFileName)
            .Where(id => id is not null && File.Exists(Path.Combine(_paths.VersionDir(id), id + ".json")))
            .Select(id => new
            {
                id,
                hasClientJar = File.Exists(_paths.VersionJar(id!))
            });
        return JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = false });
    }

    [KernelFunction("get_instance_status")]
    [Description("查询单个实例的状态：是否正在运行、进程 id、最近日志片段。参数 id 为实例 id。")]
    public string GetInstanceStatus(
        [Description("实例 id，可用 list_instances 获取")] string id)
    {
        var rec = _instances.Get(id);
        if (rec is null) return Error($"实例 {id} 不存在");
        var status = new
        {
            id = rec.Id,
            name = rec.Name,
            version = rec.VersionId,
            running = IsRunning(rec.Id),
            pid = GetSession(rec.Id)?.ProcessId,
            exitCode = GetSession(rec.Id)?.ExitCode,
            gameDirectory = _instances.GameDirectory(rec),
            recentLog = Tail(GetSession(rec.Id)?.RecentOutput, 20)
        };
        return JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction("install_version")]
    [Description("安装一个 Minecraft 版本，可选 Mod Loader（fabric/quilt/neoforge/forge）。会下载并校验全部文件，可能耗时数分钟。")]
    public async Task<string> InstallVersion(
        [Description("Minecraft 版本号，如 1.21.1")] string version,
        [Description("可选 Loader：fabric / quilt / neoforge / forge，留空为原版")] string? loader = null,
        [Description("可选 Loader 版本号，留空使用最新")] string? loader_version = null,
        CancellationToken ct = default)
    {
        try
        {
            _downloads.Begin($"安装 {version}" + (string.IsNullOrWhiteSpace(loader) ? "" : $" + {loader}"));
            var manifest = await _versions.GetManifestAsync(ct: ct).ConfigureAwait(false);
            var summary = manifest.Versions.FirstOrDefault(v => v.Id == version)
                          ?? throw new InvalidOperationException($"版本 {version} 不在官方清单中");
            var info = await _versions.DownloadVersionJsonAsync(summary, ct).ConfigureAwait(false);

            using var engine = new DownloadEngine(new DownloadOptions
            {
                MaxConcurrency = 8,
                ContinueOnError = true,
                UseMirror = false
            });
            var installer = new VersionInstaller(_paths, _versions);
            var resolved = await installer.InstallAsync(info, engine, null, ct).ConfigureAwait(false);

            string? loaderVersionId = null;
            if (!string.IsNullOrWhiteSpace(loader) && loader != "vanilla")
            {
                var javaList = _java.Cached.Count > 0
                    ? _java.Cached
                    : await _java.ScanAsync(_paths, null, ct).ConfigureAwait(false);
                var java = JavaScanner.SelectBest(javaList, resolved.JavaVersion?.MajorVersion ?? 21);
                var result = await _loaders.InstallAsync(loader!, version, loader_version, java, null, ct).ConfigureAwait(false);
                var loaderJson = JsonSerializer.Deserialize<VersionInfo>(
                    await File.ReadAllTextAsync(result.VersionJsonPath!, ct).ConfigureAwait(false), JsonDefaults.Options)!;
                await installer.InstallAsync(loaderJson, engine, null, ct).ConfigureAwait(false);
                loaderVersionId = result.VersionId;
            }

            _downloads.Complete("安装完成");
            return JsonSerializer.Serialize(new
            {
                ok = true,
                version = resolved.Id,
                loader,
                loaderVersion = loaderVersionId,
                message = "安装完成，文件已通过 SHA-1 校验"
            });
        }
        catch (Exception ex)
        {
            _downloads.Fail(ex.Message);
            return Error(ex.Message);
        }
    }

    [KernelFunction("launch_instance")]
    [Description("启动一个实例。返回进程 id；随后可用 get_instance_status / read_log 检查运行状态和崩溃原因。")]
    public async Task<string> LaunchInstance(
        [Description("实例 id，可用 list_instances 获取")] string id,
        CancellationToken ct = default)
    {
        try
        {
            var rec = _instances.Get(id) ?? throw new InvalidOperationException($"实例 {id} 不存在");
            if (IsRunning(id)) return Error($"实例 {rec.Name} 已在运行");

            var versionJson = _paths.VersionJson(rec.VersionId);
            if (!File.Exists(versionJson))
                versionJson = _paths.VersionJson(rec.MinecraftVersion);
            if (!File.Exists(versionJson))
                throw new FileNotFoundException($"版本 {rec.VersionId} 尚未安装，请先调用 install_version");

            var raw = JsonSerializer.Deserialize<VersionInfo>(
                await File.ReadAllTextAsync(versionJson, ct).ConfigureAwait(false), JsonDefaults.Options)!;
            var resolved = await _versions.ResolveInheritanceAsync(raw, ct).ConfigureAwait(false);

            var java = await ProbeJavaAsync(resolved.JavaVersion?.MajorVersion ?? 8, ct).ConfigureAwait(false)
                       ?? throw new InvalidOperationException("没有可用的 Java 运行时");
            var account = _accounts.DefaultAccount ?? OfflineIdentity.Create("Player");
            if (_accounts.GetType() is not null && _accounts.Accounts.Count == 0)
                _accounts.Upsert(account);

            var gameDir = _instances.GameDirectory(rec);
            var plan = _launch.CreatePlan(resolved, account, java, gameDir, rec.Launch);
            if (!plan.Ready) throw new MissingFileException(plan.MissingFiles);

            var session = new GameLauncher(_paths).Launch(plan.Plan);
            lock (_lock) _running[rec.Id] = session;
            _instances.MarkPlayed(rec.Id);

            return JsonSerializer.Serialize(new
            {
                ok = true,
                instance = rec.Name,
                pid = session.ProcessId,
                version = resolved.Id,
                java = java.ExecutablePath,
                logFile = session.LogPath,
                message = "已启动，游戏窗口出现通常需要 10-30 秒"
            });
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [KernelFunction("read_log")]
    [Description("读取实例最近的游戏日志（默认 80 行）。可用于诊断崩溃。")]
    public string ReadLog(
        [Description("实例 id")] string instance_id,
        [Description("读取行数，默认 80")] int lines = 80)
    {
        var session = GetSession(instance_id);
        if (session is not null)
            return Tail(session.RecentOutput, Math.Clamp(lines, 10, 400)) ?? "（暂无输出）";

        var rec = _instances.Get(instance_id);
        var logDir = _paths.LogsDir;
        if (!Directory.Exists(logDir)) return "（没有日志）";
        var latest = Directory.EnumerateFiles(logDir, "game-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (latest is null) return "（没有游戏日志）";

        var all = File.ReadLines(latest).ToList();
        var tail = all.TakeLast(Math.Clamp(lines, 10, 400));
        var crash = rec is null ? null : GameLauncher.FindCrashReport(_instances.GameDirectory(rec), DateTimeOffset.MinValue);
        var text = string.Join("\n", tail);
        if (crash is not null)
            text += "\n\n[崩溃报告] " + crash + "\n" + GameLauncher.SummariseCrash(crash, 20);
        return text;
    }

    [KernelFunction("search_modrinth")]
    [Description("在 Modrinth 上搜索 Mod。返回项目名、摘要、下载量与项目 id。")]
    public async Task<string> SearchModrinth(
        [Description("搜索关键词")] string query,
        [Description("可选游戏版本过滤，如 1.21.1")] string? game_version = null,
        [Description("可选 Loader 过滤：fabric / quilt / neoforge / forge")] string? loader = null,
        CancellationToken ct = default)
    {
        try
        {
            var results = await _modrinth.SearchAsync(query, game_version, loader, 8, "mod", ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(results.Select(r => new
            {
                id = r.Id,
                slug = r.Slug,
                title = r.Title,
                description = r.Description,
                downloads = r.Downloads,
                categories = r.Categories
            }), new JsonSerializerOptions { WriteIndented = false });
        }
        catch (Exception ex)
        {
            return Error("Modrinth 请求失败：" + ex.Message);
        }
    }

    [KernelFunction("get_instance_crash_report")]
    [Description("查找实例最近一次崩溃报告并返回摘要；没有崩溃则返回提示。")]
    public string GetInstanceCrashReport(
        [Description("实例 id")] string instance_id)
    {
        var rec = _instances.Get(instance_id);
        if (rec is null) return Error($"实例 {instance_id} 不存在");
        var dir = _instances.GameDirectory(rec);
        var report = GameLauncher.FindCrashReport(dir, DateTimeOffset.MinValue);
        return report is null
            ? "没有找到崩溃报告（crash-reports 目录为空）"
            : $"{report}\n\n{GameLauncher.SummariseCrash(report, 30)}";
    }

    private async Task<JavaRuntime?> ProbeJavaAsync(int requiredMajor, CancellationToken ct)
    {
        var cached = _java.Cached;
        if (cached.Count == 0)
            cached = await _java.ScanAsync(_paths, null, ct).ConfigureAwait(false);
        return JavaScanner.SelectBest(cached, requiredMajor);
    }

    private bool IsRunning(string id)
    {
        var session = GetSession(id);
        return session is not null && !session.HasExited;
    }

    private GameSession? GetSession(string id)
    {
        lock (_lock) return _running.TryGetValue(id, out var s) ? s : null;
    }

    private static string Tail(string? text, int lines)
    {
        if (string.IsNullOrEmpty(text)) return "（暂无输出）";
        var arr = text.Split('\n');
        return string.Join("\n", arr.TakeLast(lines));
    }

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { ok = false, error = message });
}

/// <summary>Small abstraction so the tools project does not depend on the UI download panel.</summary>
public sealed class DownloadCenterBridge
{
    private readonly Action<string> _begin;
    private readonly Action<string?> _complete;
    private readonly Action<string> _fail;

    public DownloadCenterBridge(Action<string> begin, Action<string?> complete, Action<string> fail)
    {
        _begin = begin;
        _complete = complete;
        _fail = fail;
    }

    public void Begin(string stage) => _begin(stage);
    public void Complete(string? message) => _complete(message);
    public void Fail(string message) => _fail(message);
}
