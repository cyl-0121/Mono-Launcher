using System.ComponentModel;
using System.Text.Json;
using MonoLauncher.Core;
using MonoLauncher.Core.Auth;
using MonoLauncher.Core.Config;
using MonoLauncher.Core.Deploy;
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
    private readonly EnvironmentDeployer _deployer;
    private readonly ModInstaller _modInstaller;
    private readonly ModManager _mods;
    private readonly SettingsService _settings;
    private readonly Dictionary<string, GameSession> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>
    /// Optional one-shot chat channel (wired from the active agent session) used by
    /// translate_text / recommend_mods so they share the provider the user configured.
    /// </summary>
    public Func<string, CancellationToken, Task<string>>? QuickChat { get; set; }

    public LauncherTools(
        AppPaths paths,
        InstanceStore instances,
        VersionListService versions,
        LoaderService loaders,
        ModrinthService modrinth,
        LaunchService launch,
        JavaScanner java,
        AccountStore accounts,
        DownloadCenterBridge downloads,
        EnvironmentDeployer deployer,
        ModInstaller modInstaller,
        ModManager mods,
        SettingsService settings)
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
        _deployer = deployer;
        _modInstaller = modInstaller;
        _mods = mods;
        _settings = settings;
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

    // ================= environment deploy =================

    [KernelFunction("deploy_environment")]
    [Description("一键部署完整可玩环境：自动下载缺失的 Java 运行时、安装版本与 Loader、创建隔离实例、应用内存优化（可选），并可安装 Fabric API 前置。返回每一步结果。")]
    public async Task<string> DeployEnvironment(
        [Description("Minecraft 版本号，如 1.21.1")] string version,
        [Description("Loader：fabric / quilt / neoforge / forge / vanilla（默认 vanilla）")] string? loader = null,
        [Description("实例名，留空自动命名")] string? instance_name = null,
        [Description("是否安装 Fabric API 前置（仅 fabric/quilt 生效），默认 true")] bool install_fabric_api = true,
        [Description("是否应用内存优化（自动内存 + G1 调优），默认 true")] bool optimize_memory = true,
        CancellationToken ct = default)
    {
        try
        {
            _downloads.Begin($"一键部署 {version}" + (loader is null or "vanilla" ? "" : $" + {loader}"));
            var progress = new Progress<string>(s => _downloads.Begin(s));
            var result = await _deployer.DeployAsync(version, loader, instance_name, install_fabric_api, optimize_memory, progress, ct).ConfigureAwait(false);
            _downloads.Complete("部署完成");
            return JsonSerializer.Serialize(new
            {
                ok = true,
                instanceId = result.Instance.Id,
                instanceName = result.Instance.Name,
                version = result.Instance.VersionId,
                java = result.Java.ExecutablePath,
                steps = result.Steps,
                summary = result.Summary
            });
        }
        catch (Exception ex)
        {
            _downloads.Fail(ex.Message);
            return Error("部署失败：" + ex.Message);
        }
    }

    [KernelFunction("optimize_memory")]
    [Description("PCL 风格内存优化：按机器真实内存推荐堆大小并应用 G1 调优参数。可作用于一个实例或全局默认设置。返回推荐值与说明。")]
    public string OptimizeMemory(
        [Description("可选实例 id；留空则优化全局默认设置")] string? instance_id = null,
        [Description("激进档（总内存 3/4，适合大型整合包）")] bool aggressive = false)
    {
        try
        {
            var plan = MemoryAdvisor.Recommend(aggressive);
            if (!string.IsNullOrWhiteSpace(instance_id))
            {
                var rec = _instances.Get(instance_id) ?? throw new InvalidOperationException($"实例 {instance_id} 不存在");
                MemoryAdvisor.ApplyTo(rec.Launch, plan);
                _instances.Save(rec);
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    target = "instance",
                    instance = rec.Name,
                    plan.MinMb, plan.MaxMb, plan.TotalMb, plan.FreeMb, plan.Note,
                    flags = MemoryAdvisor.JvmOptimizationFlags
                });
            }
            return JsonSerializer.Serialize(new
            {
                ok = true,
                target = "global",
                hint = "已生成推荐值；用建议的 min_mb/max_mb 调用 set_memory 即可应用到全局默认设置",
                plan.MinMb, plan.MaxMb, plan.TotalMb, plan.FreeMb, plan.Note,
                flags = MemoryAdvisor.JvmOptimizationFlags
            });
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [KernelFunction("set_memory")]
    [Description("设置实例或全局的 JVM 内存范围（MB）。")]
    public string SetMemory(
        [Description("最小内存 MB")] int min_mb,
        [Description("最大内存 MB")] int max_mb,
        [Description("可选实例 id；留空改全局默认")] string? instance_id = null,
        [Description("是否同时启用 G1 调优参数，默认 true")] bool optimize_flags = true)
    {
        try
        {
            min_mb = Math.Clamp(min_mb, 256, 65536);
            max_mb = Math.Clamp(max_mb, min_mb + 256, 65536);
            if (!string.IsNullOrWhiteSpace(instance_id))
            {
                var rec = _instances.Get(instance_id) ?? throw new InvalidOperationException($"实例 {instance_id} 不存在");
                rec.Launch.MinMemoryMb = min_mb;
                rec.Launch.MaxMemoryMb = max_mb;
                rec.Launch.OptimizeJvmArguments = optimize_flags;
                _instances.Save(rec);
                return JsonSerializer.Serialize(new { ok = true, target = "instance", instance = rec.Name, min_mb, max_mb, optimize_flags });
            }
            using (var scope = _settings.BeginUpdate())
            {
                scope.Settings.MinMemoryMb = min_mb;
                scope.Settings.MaxMemoryMb = max_mb;
                scope.Settings.OptimizeJvmArguments = optimize_flags;
            }
            return JsonSerializer.Serialize(new { ok = true, target = "global", min_mb, max_mb, optimize_flags });
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    // ================= mods: prerequisites / translation / recommendation =================

    [KernelFunction("install_mod_full")]
    [Description("安装 Mod 并自动解析安装全部前置依赖（前置模组），按实例的游戏版本与 Loader 匹配兼容文件。返回每个 Mod 的安装结果。")]
    public async Task<string> InstallModFull(
        [Description("实例 id")] string instance_id,
        [Description("Modrinth 项目 id 或 slug（可先 search_modrinth 查询）")] string project_slug,
        [Description("是否安装 required 前置依赖，默认 true")] bool with_dependencies = true,
        CancellationToken ct = default)
    {
        try
        {
            var rec = _instances.Get(instance_id) ?? throw new InvalidOperationException($"实例 {instance_id} 不存在");
            var loader = rec.Loader is null or "vanilla" ? null : rec.Loader;
            var modsDir = Path.Combine(_instances.GameDirectory(rec), "mods");
            var progress = new Progress<string>(s => _downloads.Begin(s));
            _downloads.Begin($"安装 Mod {project_slug}…");
            var report = await _modInstaller.InstallAsync(modsDir, project_slug, rec.MinecraftVersion, loader, progress, with_dependencies, ct).ConfigureAwait(false);
            _downloads.Complete("Mod 安装完成");
            return JsonSerializer.Serialize(new
            {
                ok = true,
                instance = rec.Name,
                main = report.MainTitle,
                entries = report.Entries.Select(e => new { e.ProjectTitle, e.FileName, e.Downloaded, e.Note }),
                summary = report.Summary
            });
        }
        catch (Exception ex)
        {
            _downloads.Fail(ex.Message);
            return Error("安装失败：" + ex.Message);
        }
    }

    [KernelFunction("get_project_detail")]
    [Description("获取 Modrinth 项目完整介绍（英文 body）。配合 translate_text 可把简介翻译成中文。")]
    public async Task<string> GetProjectDetail(
        [Description("项目 id 或 slug")] string project_slug,
        CancellationToken ct = default)
    {
        try
        {
            var detail = await _modrinth.GetProjectAsync(project_slug, ct).ConfigureAwait(false)
                         ?? throw new InvalidOperationException($"没有找到项目 {project_slug}");
            var body = detail.Body.Length > 4000 ? detail.Body[..4000] + "…" : detail.Body;
            return JsonSerializer.Serialize(new
            {
                ok = true,
                title = detail.Title,
                slug = detail.Slug,
                description = detail.Description,
                categories = detail.Categories,
                downloads = detail.Downloads,
                license = detail.License,
                body
            });
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [KernelFunction("translate_text")]
    [Description("调用当前配置的 AI 提供商把文本翻译成简体中文（用于 Mod 简介等）。返回译文。")]
    public async Task<string> TranslateText(
        [Description("要翻译的英文文本")] string text,
        [Description("内容主题提示，如 mod 介绍 / 崩溃日志")] string? context = null,
        CancellationToken ct = default)
    {
        if (QuickChat is null)
            return Error("当前会话没有可用的 AI 通道（请检查提供商配置）");
        try
        {
            var prompt = (string.IsNullOrWhiteSpace(context) ? "" : $"（背景：{context}）") +
                         "把下面的内容翻译成简体中文，保留 Markdown 格式与专有名词，只输出译文：\n\n" +
                         (text.Length > 6000 ? text[..6000] : text);
            var translated = await QuickChat(prompt, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { ok = true, translation = translated.Trim() });
        }
        catch (Exception ex)
        {
            return Error("翻译失败：" + ex.Message);
        }
    }

    [KernelFunction("recommend_mods")]
    [Description("按用户描述的游玩偏好，结合真实 Modrinth 数据推荐 Mod 并说明理由。需要已配置 AI 提供商。")]
    public async Task<string> RecommendMods(
        [Description("用户想玩什么，例如：探索、科技、魔法、光影、联机生存")] string preference,
        [Description("游戏版本，留空取第一个实例的版本")] string? game_version = null,
        [Description("Loader：fabric / quilt / neoforge / forge，留空取第一个实例")] string? loader = null,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(game_version) || string.IsNullOrWhiteSpace(loader))
            {
                var first = _instances.List().FirstOrDefault();
                game_version ??= first?.MinecraftVersion;
                if (string.IsNullOrWhiteSpace(loader)) loader = first?.Loader is null or "vanilla" ? null : first?.Loader;
            }
            if (string.IsNullOrWhiteSpace(game_version))
                return Error("没有实例可参考，请先指定 game_version");

            var keywords = string.IsNullOrWhiteSpace(loader)
                ? preference
                : $"{preference} {loader}";
            var candidates = await _modrinth.SearchAsync(keywords, game_version, loader, 12, "mod", ct).ConfigureAwait(false);
            var pool = candidates.Select(c => new
            {
                slug = c.Slug,
                title = c.Title,
                description = c.Description,
                downloads = c.Downloads,
                categories = c.Categories
            }).ToList();
            string? reasoning = null;
            if (QuickChat is not null)
            {
                var prompt = "用户想玩：" + preference + $"（Minecraft {game_version}" + (loader is null ? "，原版" : "，" + loader) + "）。\n" +
                             "下面是 Modrinth 候选列表（JSON）。从中挑出 5-8 个最合适的，输出一个 JSON 数组：" +
                             "[{\"slug\":\"…\",\"reason\":\"一句话中文推荐理由\"}]。只输出 JSON。\n" +
                             JsonSerializer.Serialize(pool);
                reasoning = (await QuickChat(prompt, ct).ConfigureAwait(false)).Trim();
            }
            return JsonSerializer.Serialize(new { ok = true, game_version, loader = loader ?? "vanilla", candidates = pool, ai_pick = reasoning });
        }
        catch (Exception ex)
        {
            return Error("推荐失败：" + ex.Message);
        }
    }

    // ================= crash diagnosis =================

    [KernelFunction("diagnose_crash")]
    [Description("收集一个实例的完整崩溃现场：最新崩溃报告摘要、游戏日志尾部、Java、内存设置。配合 translate_text/模型推理给出原因与修复建议。")]
    public string DiagnoseCrash(
        [Description("实例 id")] string instance_id,
        [Description("日志尾部行数，默认 120")] int log_lines = 120)
    {
        try
        {
            var rec = _instances.Get(instance_id) ?? throw new InvalidOperationException($"实例 {instance_id} 不存在");
            var dir = _instances.GameDirectory(rec);
            var report = GameLauncher.FindCrashReport(dir, DateTimeOffset.MinValue);
            var latestLog = Directory.Exists(_paths.LogsDir)
                ? Directory.EnumerateFiles(_paths.LogsDir, "game-*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
            var logTail = latestLog is null ? "" : string.Join("\n", File.ReadLines(latestLog).TakeLast(Math.Clamp(log_lines, 20, 400)));
            var mods = _mods.Scan(Path.Combine(dir, "mods"));
            return JsonSerializer.Serialize(new
            {
                ok = true,
                instance = rec.Name,
                version = rec.VersionId,
                javaMemory = new { rec.Launch.MinMemoryMb, rec.Launch.MaxMemoryMb, rec.Launch.OptimizeJvmArguments },
                crashReportPath = report,
                crashSummary = report is null ? null : GameLauncher.SummariseCrash(report, 40),
                logTail,
                mods = mods.Select(m => new { m.Name, m.FileName, m.Loader, m.Enabled, m.Version })
            });
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
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
