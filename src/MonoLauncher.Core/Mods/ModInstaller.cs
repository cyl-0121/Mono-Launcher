using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace MonoLauncher.Core.Mods;

public sealed record ModInstallEntry(string ProjectTitle, string ProjectId, string FileName, bool Downloaded, string Note);

public sealed record ModInstallReport
{
    public string MainTitle { get; set; } = "";
    public List<ModInstallEntry> Entries { get; init; } = new();

    public string Summary
    {
        get
        {
            var installed = Entries.Where(e => e.Downloaded).ToList();
            var skipped = Entries.Where(e => !e.Downloaded).ToList();
            var text = $"已安装 {installed.Count} 个 Mod：{string.Join("、", installed.Select(e => e.ProjectTitle))}";
            if (skipped.Count > 0)
                text += $"；跳过 {skipped.Count} 个已存在：{string.Join("、", skipped.Select(e => e.ProjectTitle))}";
            return text;
        }
    }
}

/// <summary>
/// Installs a Modrinth project into a mods directory and recursively installs every
/// required dependency ("前置模组"), deduplicating by project id and file name.
/// </summary>
public sealed class ModInstaller
{
    private readonly HttpClient _http;
    private readonly ModrinthService _modrinth;
    private readonly ModManager _manager;
    private readonly AppPaths _paths;

    public ModInstaller(HttpClient http, ModrinthService modrinth, ModManager manager, AppPaths paths)
    {
        _http = http;
        _modrinth = modrinth;
        _manager = manager;
        _paths = paths;
    }

    public async Task<ModInstallReport> InstallAsync(
        string modsDir,
        string projectIdOrSlug,
        string gameVersion,
        string? loader,
        IProgress<string>? progress = null,
        bool withDependencies = true,
        CancellationToken ct = default)
    {
        var report = new ModInstallReport();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await InstallRecursiveAsync(modsDir, projectIdOrSlug, gameVersion, loader, withDependencies, progress, report, visited, isRoot: true, ct).ConfigureAwait(false);
        return report;
    }

    private async Task InstallRecursiveAsync(
        string modsDir,
        string projectIdOrSlug,
        string gameVersion,
        string? loader,
        bool withDependencies,
        IProgress<string>? progress,
        ModInstallReport report,
        HashSet<string> visited,
        bool isRoot,
        CancellationToken ct)
    {
        if (!visited.Add(projectIdOrSlug)) return;

        progress?.Report($"获取项目信息 {projectIdOrSlug}…");
        var project = await _modrinth.GetProjectAsync(projectIdOrSlug, ct).ConfigureAwait(false)
                      ?? throw new InvalidOperationException($"Modrinth 上没有项目 {projectIdOrSlug}");
        if (isRoot) report.MainTitle = project.Title;

        progress?.Report($"查询 {project.Title} 的兼容版本…");
        var versions = await _modrinth.GetVersionsAsync(project.Id, gameVersion, loader, ct).ConfigureAwait(false);
        var version = versions.FirstOrDefault()
                      ?? throw new InvalidOperationException($"{project.Title} 没有兼容 {gameVersion}（{loader ?? "原版"}）的文件");
        var file = version.PrimaryFile ?? throw new InvalidOperationException($"{project.Title} 该版本没有可下载文件");

        // Fast path: the exact file is already in the mods directory.
        if (Directory.Exists(modsDir) && File.Exists(Path.Combine(modsDir, file.Filename)))
        {
            // Keep walking dependencies: an installed main Mod may still be missing a prerequisite.
            report.Entries.Add(new ModInstallEntry(project.Title, project.Id, file.Filename, Downloaded: false, Note: "已存在，跳过"));
        }
        else
        {
            progress?.Report($"下载 {file.Filename}…");
            var tempDir = Path.Combine(_paths.CacheDir, "mods");
            Directory.CreateDirectory(tempDir);
            var temp = Path.Combine(tempDir, file.Filename);
            var bytes = await _http.GetByteArrayAsync(file.Url, ct).ConfigureAwait(false);
            if (file.Sha1 is { Length: 40 })
            {
                var hash = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(hash, file.Sha1, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{file.Filename} SHA-1 校验失败，文件可能损坏");
            }
            await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
            _manager.InstallFile(modsDir, temp);
            report.Entries.Add(new ModInstallEntry(project.Title, project.Id, file.Filename, Downloaded: true, Note: $"已安装 {version.VersionNumber}"));
        }

        if (!withDependencies || version.Dependencies.Count == 0) return;
        var required = version.Dependencies
            .Where(d => d.DependencyType == "required" && !string.IsNullOrWhiteSpace(d.ProjectId))
            .Select(d => d.ProjectId!)
            .Where(id => !visited.Contains(id))
            .ToList();
        foreach (var dep in required)
        {
            ct.ThrowIfCancellationRequested();
            await InstallRecursiveAsync(modsDir, dep, gameVersion, loader, withDependencies, progress, report, visited, isRoot: false, ct).ConfigureAwait(false);
        }
    }
}
