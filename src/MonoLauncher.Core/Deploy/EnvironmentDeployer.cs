using System.Text.Json;
using MonoLauncher.Core.Download;
using MonoLauncher.Core.Instances;
using MonoLauncher.Core.Java;
using MonoLauncher.Core.Loaders;
using MonoLauncher.Core.Mods;
using MonoLauncher.Core.Models;
using MonoLauncher.Core.Versions;

namespace MonoLauncher.Core.Deploy;

public sealed record DeployResult
{
    public required InstanceRecord Instance { get; init; }
    public required JavaRuntime Java { get; init; }
    public required IReadOnlyList<string> Steps { get; init; }
    public MemoryPlan? Memory { get; init; }

    public string Summary
    {
        get
        {
            var lines = new List<string>
            {
                $"实例 {Instance.Name} 已就绪（{Instance.MinecraftVersion} · {Instance.LoaderDisplay}）",
                $"Java {Java.MajorVersion}：{Java.ExecutablePath}"
            };
            if (Memory is not null)
                lines.Add($"内存优化：{Memory.MinMb}-{Memory.MaxMb} MB + G1 调优参数");
            lines.AddRange(Steps);
            return string.Join("\n", lines);
        }
    }
}

/// <summary>
/// The "one-click environment deploy" pipeline shared by the UI button and the AI agent:
/// Java runtime (downloaded if missing) -> version + loader -> isolated instance ->
/// PCL-style memory tuning -> Fabric API as the default prerequisite mod.
/// </summary>
public sealed class EnvironmentDeployer
{
    private readonly AppPaths _paths;
    private readonly HttpClient _http;
    private readonly VersionListService _versions;
    private readonly JavaScanner _java;
    private readonly JavaRuntimeInstaller _javaInstaller;
    private readonly InstanceStore _instances;
    private readonly LoaderService _loaders;
    private readonly ModrinthService _modrinth;
    private readonly ModManager _mods;

    public EnvironmentDeployer(
        AppPaths paths,
        HttpClient http,
        VersionListService versions,
        JavaScanner java,
        JavaRuntimeInstaller javaInstaller,
        InstanceStore instances,
        LoaderService loaders,
        ModrinthService modrinth,
        ModManager mods)
    {
        _paths = paths;
        _http = http;
        _versions = versions;
        _java = java;
        _javaInstaller = javaInstaller;
        _instances = instances;
        _loaders = loaders;
        _modrinth = modrinth;
        _mods = mods;
    }

    public async Task<DeployResult> DeployAsync(
        string version,
        string? loader,
        string? instanceName,
        bool installFabricApi,
        bool optimizeMemory,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        var steps = new List<string>();
        loader = string.IsNullOrWhiteSpace(loader) || loader == "vanilla" ? null : loader.Trim().ToLowerInvariant();

        progress?.Report("获取官方版本清单…");
        var manifest = await _versions.GetManifestAsync(ct: ct).ConfigureAwait(false);
        var summary = manifest.Versions.FirstOrDefault(v => v.Id == version)
                      ?? throw new InvalidOperationException($"版本 {version} 不在官方清单中");
        var info = await _versions.DownloadVersionJsonAsync(summary, ct).ConfigureAwait(false);
        var resolved = await _versions.ResolveInheritanceAsync(info, ct).ConfigureAwait(false);
        steps.Add($"版本 {resolved.Id} 清单已就绪");

        // 1. Java runtime (exact major for this Minecraft version; downloaded when missing).
        var requiredMajor = resolved.JavaVersion?.MajorVersion ?? 8;
        progress?.Report($"准备 Java {requiredMajor} 运行时…");
        var java = await _javaInstaller.EnsureAsync(requiredMajor, _java, progress, ct).ConfigureAwait(false);
        steps.Add($"Java {java.MajorVersion}（{java.Vendor}）可用");

        // 2. Version + loader files.
        using var engine = new DownloadEngine(new DownloadOptions
        {
            MaxConcurrency = 8,
            ContinueOnError = true,
            UseMirror = false
        });
        var installer = new VersionInstaller(_paths, _versions);
        var installProgress = progress is null ? null : new Progress<InstallProgress>(p =>
            progress.Report(p.Detail is null ? p.Stage : $"{p.Stage}… {p.Detail}"));
        progress?.Report($"下载 {resolved.Id} 核心与资源文件…");
        await installer.InstallAsync(info, engine, installProgress, ct).ConfigureAwait(false);
        steps.Add($"已下载并校验 {resolved.Id} 全部文件");

        string? loaderVersionId = null;
        if (loader is not null)
        {
            progress?.Report($"安装 {loader} Loader…");
            var result = await _loaders.InstallAsync(loader, version, null, java, progress, ct).ConfigureAwait(false);
            var loaderJson = JsonSerializer.Deserialize<VersionInfo>(
                await File.ReadAllTextAsync(result.VersionJsonPath!, ct).ConfigureAwait(false), JsonDefaults.Options)!;
            await installer.InstallAsync(loaderJson, engine, installProgress, ct).ConfigureAwait(false);
            loaderVersionId = result.VersionId;
            steps.Add($"Loader 已安装：{result.VersionId}");
        }

        // 3. Instance + memory plan.
        var name = string.IsNullOrWhiteSpace(instanceName) ? $"{version}{(loader is null ? "" : " · " + loader)}" : instanceName!.Trim();
        var record = _instances.Create(name, version, loader, null, loaderVersionId ?? version);
        MemoryPlan? plan = null;
        if (optimizeMemory)
        {
            plan = MemoryAdvisor.Recommend();
            MemoryAdvisor.ApplyTo(record.Launch, plan);
            _instances.Save(record);
            steps.Add($"内存优化已应用：{plan.MinMb}-{plan.MaxMb} MB + G1 调优参数");
        }
        steps.Add($"实例「{record.Name}」已创建");

        // 4. Fabric API as the default prerequisite mod.
        if (installFabricApi && loader is "fabric" or "quilt")
        {
            try
            {
                progress?.Report("安装前置 Fabric API…");
                var modsDir = Path.Combine(_instances.GameDirectory(record), "mods");
                var modInstaller = new ModInstaller(_http, _modrinth, _mods, _paths);
                var report = await modInstaller.InstallAsync(modsDir, "fabric-api", version, loader, progress, ct: ct).ConfigureAwait(false);
                steps.Add(report.Summary);
            }
            catch (Exception ex)
            {
                steps.Add($"Fabric API 安装失败（不影响环境）：{ex.Message}");
            }
        }

        progress?.Report("部署完成");
        return new DeployResult { Instance = record, Java = java, Steps = steps, Memory = plan };
    }
}
