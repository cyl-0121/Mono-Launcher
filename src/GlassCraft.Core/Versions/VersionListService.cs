using System.Text.Json;
using GlassCraft.Core.Models;

namespace GlassCraft.Core.Versions;

public sealed class VersionListService
{
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private readonly HttpClient _http;
    private readonly AppPaths _paths;
    private VersionManifest? _cache;

    public VersionListService(HttpClient http, AppPaths paths)
    {
        _http = http;
        _paths = paths;
    }

    public VersionManifest? Cached => _cache;

    public async Task<VersionManifest> GetManifestAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (_cache is not null && !forceRefresh) return _cache;

        var cacheFile = Path.Combine(_paths.CacheDir, "version_manifest_v2.json");
        try
        {
            using var resp = await _http.GetAsync(ManifestUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var manifest = await JsonSerializer.DeserializeAsync<VersionManifest>(stream, JsonDefaults.Options, ct).ConfigureAwait(false)
                           ?? throw new InvalidDataException("empty version manifest");
            _cache = manifest;
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(manifest, JsonDefaults.Options), ct).ConfigureAwait(false);
            return manifest;
        }
        catch (Exception ex) when (File.Exists(cacheFile))
        {
            // Offline friendly: fall back to the last successful manifest.
            var manifest = JsonSerializer.Deserialize<VersionManifest>(await File.ReadAllTextAsync(cacheFile, ct).ConfigureAwait(false), JsonDefaults.Options)
                           ?? throw new InvalidDataException("cached manifest unreadable", ex);
            _cache = manifest;
            return manifest;
        }
    }

    public async Task<VersionInfo> DownloadVersionJsonAsync(VersionSummary summary, CancellationToken ct = default)
    {
        var dest = _paths.VersionJson(summary.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        if (File.Exists(dest))
        {
            var existing = JsonSerializer.Deserialize<VersionInfo>(await File.ReadAllTextAsync(dest, ct).ConfigureAwait(false), JsonDefaults.Options);
            if (existing is not null && string.Equals(existing.Id, summary.Id, StringComparison.OrdinalIgnoreCase))
                return existing;
        }

        using var resp = await _http.GetAsync(summary.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var info = JsonSerializer.Deserialize<VersionInfo>(json, JsonDefaults.Options)
                   ?? throw new InvalidDataException($"version json for {summary.Id} is invalid");
        await File.WriteAllTextAsync(dest, json, ct).ConfigureAwait(false);
        return info;
    }

    public async Task<VersionInfo?> TryReadLocalVersionAsync(string id, CancellationToken ct = default)
    {
        var path = _paths.VersionJson(id);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<VersionInfo>(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), JsonDefaults.Options);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves inheritance (inheritsFrom) by merging parent fields into the child.
    /// Child values win; lists (libraries / arguments) are concatenated parent-first, matching
    /// the behaviour of the official launcher and other third-party launchers.
    /// </summary>
    public async Task<VersionInfo> ResolveInheritanceAsync(VersionInfo version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version.InheritsFrom) || version.IsMerged)
            return version;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { version.Id };
        var chain = new List<VersionInfo> { version };
        var current = version;

        while (!string.IsNullOrWhiteSpace(current.InheritsFrom))
        {
            var parentId = current.InheritsFrom!;
            if (!visited.Add(parentId))
                throw new InvalidDataException($"cyclic inheritance detected at {parentId}");
            var parent = await TryReadLocalVersionAsync(parentId, ct).ConfigureAwait(false)
                         ?? throw new FileNotFoundException($"parent version {parentId} is not installed");
            chain.Add(parent);
            current = parent;
            if (chain.Count > 16)
                throw new InvalidDataException("inheritance chain too deep");
        }

        // Merge from root parent down to the child.
        var merged = chain[^1];
        for (int i = chain.Count - 2; i >= 0; i--)
            merged = Merge(merged, chain[i]);

        merged.IsMerged = true;
        merged.Id = version.Id;
        merged.InheritsFrom = null;
        // Records which jar actually holds the client classes.
        merged.Jar ??= chain.Count > 1 ? chain[^1].Id : version.Id;
        return merged;
    }

    public static VersionInfo Merge(VersionInfo parent, VersionInfo child)
    {
        var result = new VersionInfo
        {
            Id = child.Id,
            Type = child.Type,
            Jar = child.Jar ?? parent.Jar,
            MainClass = child.MainClass ?? parent.MainClass,
            Assets = child.Assets ?? parent.Assets,
            AssetIndex = child.AssetIndex ?? parent.AssetIndex,
            Downloads = MergeDownloads(parent.Downloads, child.Downloads),
            JavaVersion = child.JavaVersion ?? parent.JavaVersion,
            Logging = child.Logging ?? parent.Logging,
            ComplianceLevel = Math.Max(parent.ComplianceLevel, child.ComplianceLevel),
            MinimumLauncherVersion = Math.Max(parent.MinimumLauncherVersion, child.MinimumLauncherVersion),
            ReleaseTime = child.ReleaseTime == default ? parent.ReleaseTime : child.ReleaseTime,
            Time = child.Time == default ? parent.Time : child.Time,
            MinecraftArguments = child.MinecraftArguments ?? parent.MinecraftArguments,
            LoaderKind = child.LoaderKind ?? parent.LoaderKind,
            LoaderVersion = child.LoaderVersion ?? parent.LoaderVersion
        };

        result.Libraries.AddRange(parent.Libraries);
        result.Libraries.AddRange(child.Libraries);

        var args = new Arguments();
        args.Jvm.AddRange(parent.Arguments?.Jvm ?? new List<JsonElement>());
        args.Jvm.AddRange(child.Arguments?.Jvm ?? new List<JsonElement>());
        args.Game.AddRange(parent.Arguments?.Game ?? new List<JsonElement>());
        args.Game.AddRange(child.Arguments?.Game ?? new List<JsonElement>());
        if (args.Jvm.Count > 0 || args.Game.Count > 0)
            result.Arguments = args;

        return result;
    }

    private static VersionDownloads? MergeDownloads(VersionDownloads? parent, VersionDownloads? child)
    {
        if (parent is null) return child;
        if (child is null) return parent;
        return new VersionDownloads
        {
            Client = child.Client ?? parent.Client,
            Server = child.Server ?? parent.Server,
            ClientMappings = child.ClientMappings ?? parent.ClientMappings,
            ServerMappings = child.ServerMappings ?? parent.ServerMappings
        };
    }
}
