using System.Text.Json;
using System.Text.Json.Nodes;
using GlassCraft.Core.Instances;

namespace GlassCraft.Core.Importers;

public enum ImportSourceKind { Vanilla, Pcl, Hmcl, Prism, MultiMc }

public sealed class ImportCandidate
{
    public required string Name { get; init; }
    public required string SourceDirectory { get; init; }
    public ImportSourceKind Kind { get; init; }
    public string? MinecraftVersion { get; init; }
    public string? Loader { get; init; }
    public string? LoaderVersion { get; init; }
    public int ModCount { get; init; }
    public int SaveCount { get; init; }
    public string SourceText => Kind switch
    {
        ImportSourceKind.Pcl => "PCL2",
        ImportSourceKind.Hmcl => "HMCL",
        ImportSourceKind.Prism => "Prism Launcher",
        ImportSourceKind.MultiMc => "MultiMC",
        _ => "原版 .minecraft"
    };
}

public sealed class ImportResult
{
    public required InstanceRecord Instance { get; init; }
    public int CopiedSaves { get; init; }
    public int CopiedMods { get; init; }
    public bool CopiedOptions { get; init; }
    public string TargetDirectory { get; init; } = "";
}

/// <summary>
/// Detects and imports existing launcher installations. Importing copies saves/mods/config into a
/// new isolated instance; the source installation is never modified or deleted.
/// </summary>
public sealed class InstanceImporter
{
    private readonly AppPaths _paths;
    private readonly InstanceStore _instances;

    public InstanceImporter(AppPaths paths, InstanceStore instances)
    {
        _paths = paths;
        _instances = instances;
    }

    public IReadOnlyList<ImportCandidate> Detect()
    {
        var result = new List<ImportCandidate>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Vanilla / PCL2 / HMCL all normally live in %APPDATA%\.minecraft
        var dotMinecraft = Path.Combine(appData, ".minecraft");
        if (Directory.Exists(dotMinecraft))
            result.AddRange(DetectDotMinecraft(dotMinecraft, DetectDotMinecraftOwner(dotMinecraft)));

        // PCL2 can also be configured with a custom .minecraft next to PCL.exe
        foreach (var pclRoot in new[]
                 {
                     Path.Combine(localAppData, "Programs", "PCL"),
                     Path.Combine(userProfile, "Desktop"),
                     @"C:\PCL"
                 })
        {
            if (!Directory.Exists(pclRoot)) continue;
            var mc = Path.Combine(pclRoot, ".minecraft");
            if (Directory.Exists(mc)) result.AddRange(DetectDotMinecraft(mc, ImportSourceKind.Pcl));
        }

        // Prism / MultiMC instances
        foreach (var (root, kind) in new[]
                 {
                     (Path.Combine(appData, "PrismLauncher", "instances"), ImportSourceKind.Prism),
                     (Path.Combine(localAppData, "PrismLauncher", "instances"), ImportSourceKind.Prism),
                     (Path.Combine(appData, "MultiMC", "instances"), ImportSourceKind.MultiMc)
                 })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var candidate = DetectPrismInstance(dir, kind);
                if (candidate is not null) result.Add(candidate);
            }
        }

        return result
            .GroupBy(c => c.SourceDirectory, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static ImportSourceKind DetectDotMinecraftOwner(string dir)
    {
        if (Directory.Exists(Path.Combine(dir, "PCL"))) return ImportSourceKind.Pcl;
        if (File.Exists(Path.Combine(dir, "PCL", "Setup.ini"))) return ImportSourceKind.Pcl;
        if (Directory.Exists(Path.Combine(dir, "HMCL"))) return ImportSourceKind.Hmcl;
        if (File.Exists(Path.Combine(dir, "hmcl.json"))) return ImportSourceKind.Hmcl;
        return ImportSourceKind.Vanilla;
    }

    private IEnumerable<ImportCandidate> DetectDotMinecraft(string dir, ImportSourceKind kind)
    {
        var versionsDir = Path.Combine(dir, "versions");
        if (Directory.Exists(versionsDir))
        {
            foreach (var versionDir in Directory.EnumerateDirectories(versionsDir))
            {
                var id = Path.GetFileName(versionDir);
                var json = Path.Combine(versionDir, id + ".json");
                if (!File.Exists(json)) continue;
                var (mc, loader, loaderVersion) = ReadVersionInfo(json, id);
                yield return new ImportCandidate
                {
                    Name = id,
                    SourceDirectory = dir,
                    Kind = kind,
                    MinecraftVersion = mc,
                    Loader = loader,
                    LoaderVersion = loaderVersion,
                    ModCount = CountFiles(Path.Combine(dir, "mods")),
                    SaveCount = Directory.Exists(Path.Combine(dir, "saves"))
                        ? Directory.GetDirectories(Path.Combine(dir, "saves")).Length
                        : 0
                };
            }
        }
        else
        {
            yield return new ImportCandidate
            {
                Name = kind == ImportSourceKind.Vanilla ? ".minecraft" : $"{kind} 实例",
                SourceDirectory = dir,
                Kind = kind,
                ModCount = CountFiles(Path.Combine(dir, "mods")),
                SaveCount = Directory.Exists(Path.Combine(dir, "saves"))
                    ? Directory.GetDirectories(Path.Combine(dir, "saves")).Length
                    : 0
            };
        }
    }

    private static (string? Mc, string? Loader, string? LoaderVersion) ReadVersionInfo(string jsonPath, string id)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(jsonPath));
            var inherits = node?["inheritsFrom"]?.GetValue<string>();
            var loaderKind = node?["loaderKind"]?.GetValue<string>();
            if (loaderKind is null)
            {
                if (id.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase)) loaderKind = "neoforge";
                else if (id.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase)) loaderKind = "fabric";
                else if (id.StartsWith("quilt-loader-", StringComparison.OrdinalIgnoreCase)) loaderKind = "quilt";
                else if (id.Contains("-forge-", StringComparison.OrdinalIgnoreCase)) loaderKind = "forge";
            }
            var loaderVersion = node?["loaderVersion"]?.GetValue<string>();
            return (inherits ?? id, loaderKind, loaderVersion);
        }
        catch
        {
            return (id, null, null);
        }
    }

    private static ImportCandidate? DetectPrismInstance(string dir, ImportSourceKind kind)
    {
        var pack = Path.Combine(dir, "mmc-pack.json");
        var mcDir = Path.Combine(dir, ".minecraft");
        if (!Directory.Exists(mcDir)) return null;

        string? mc = null, loader = null, loaderVersion = null;
        if (File.Exists(pack))
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(pack));
                if (node?["components"] is JsonArray components)
                {
                    foreach (var c in components)
                    {
                        var uid = c?["uid"]?.GetValue<string>() ?? "";
                        var version = c?["version"]?.GetValue<string>();
                        if (uid == "net.minecraft") mc = version;
                        else if (uid == "net.neoforged") { loader = "neoforge"; loaderVersion = version; }
                        else if (uid == "net.minecraftforge") { loader = "forge"; loaderVersion = version; }
                        else if (uid == "org.quiltmc.quilt-loader") { loader = "quilt"; loaderVersion = version; }
                        else if (uid == "net.fabricmc.fabric-loader") { loader = "fabric"; loaderVersion = version; }
                    }
                }
            }
            catch { /* best effort */ }
        }

        return new ImportCandidate
        {
            Name = Path.GetFileName(dir),
            SourceDirectory = dir,
            Kind = kind,
            MinecraftVersion = mc,
            Loader = loader,
            LoaderVersion = loaderVersion,
            ModCount = CountFiles(Path.Combine(mcDir, "mods")),
            SaveCount = Directory.Exists(Path.Combine(mcDir, "saves"))
                ? Directory.GetDirectories(Path.Combine(mcDir, "saves")).Length
                : 0
        };
    }

    private static int CountFiles(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir).Length : 0;

    /// <summary>
    /// Creates a new isolated instance and copies user data from the detected source.
    /// Only whitelisted folders are copied, existing targets are never overwritten.
    /// </summary>
    public async Task<ImportResult> ImportAsync(ImportCandidate candidate, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var version = candidate.MinecraftVersion ?? "1.21.1";
        var record = _instances.Create(
            candidate.Name,
            version,
            candidate.Loader,
            candidate.LoaderVersion,
            installedVersionId: ResolveInstalledVersionId(candidate));

        record.ImportedFrom = candidate.SourceDirectory;
        record.Notes = $"从 {candidate.SourceText} 导入";
        _instances.Save(record);

        var target = _instances.GameDirectory(record);
        Directory.CreateDirectory(target);

        var mcSource = candidate.Kind is ImportSourceKind.Prism or ImportSourceKind.MultiMc
            ? Path.Combine(candidate.SourceDirectory, ".minecraft")
            : candidate.SourceDirectory;

        int saves = 0, mods = 0;
        foreach (var folder in new[] { "saves", "mods", "config", "resourcepacks", "shaderpacks", "screenshots" })
        {
            ct.ThrowIfCancellationRequested();
            var src = Path.Combine(mcSource, folder);
            if (!Directory.Exists(src)) continue;
            progress?.Report($"复制 {folder}…");
            var dst = Path.Combine(target, folder);
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(src, file);
                var outFile = Path.Combine(dst, rel);
                if (File.Exists(outFile)) continue; // never overwrite
                Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
                try
                {
                    File.Copy(file, outFile);
                    if (folder == "mods") mods++;
                }
                catch { /* skip locked files */ }
            }
            if (folder == "saves" && Directory.Exists(dst))
                saves = Directory.GetDirectories(dst).Length;
        }

        var optionsCopied = false;
        var optionsSource = Path.Combine(mcSource, "options.txt");
        if (File.Exists(optionsSource))
        {
            var optionsTarget = Path.Combine(target, "options.txt");
            if (!File.Exists(optionsTarget))
            {
                try
                {
                    File.Copy(optionsSource, optionsTarget);
                    optionsCopied = true;
                }
                catch { }
            }
        }

        progress?.Report($"已导入到 {target}");
        return new ImportResult
        {
            Instance = record,
            CopiedSaves = saves,
            CopiedMods = mods,
            CopiedOptions = optionsCopied,
            TargetDirectory = target
        };
    }

    private string ResolveInstalledVersionId(ImportCandidate candidate)
    {
        // Prefer an already installed version that matches the imported one.
        if (!string.IsNullOrWhiteSpace(candidate.Loader))
        {
            var candidateId = candidate.Loader == "neoforge" && candidate.LoaderVersion is not null
                ? $"neoforge-{candidate.LoaderVersion}"
                : $"{candidate.MinecraftVersion}-{candidate.Loader}-{candidate.LoaderVersion}";
            if (File.Exists(_paths.VersionJson(candidateId))) return candidateId;
        }
        if (!string.IsNullOrWhiteSpace(candidate.MinecraftVersion) &&
            File.Exists(_paths.VersionJson(candidate.MinecraftVersion!)))
            return candidate.MinecraftVersion!;
        return candidate.Loader is not null && candidate.LoaderVersion is not null
            ? $"neoforge-{candidate.LoaderVersion}"
            : candidate.MinecraftVersion ?? "1.21.1";
    }
}
