using System.IO.Compression;
using System.Text.Json;
using GlassCraft.Core.Download;
using GlassCraft.Core.Models;

namespace GlassCraft.Core.Versions;

public sealed class InstallProgress
{
    public string Stage { get; init; } = "";
    public string? Detail { get; init; }
    public int FilesDone { get; init; }
    public int FilesTotal { get; init; }
    public long BytesDone { get; init; }
    public long BytesTotal { get; init; }
    public double Fraction => BytesTotal > 0
        ? Math.Clamp((double)BytesDone / BytesTotal, 0, 1)
        : FilesTotal > 0 ? Math.Clamp((double)FilesDone / FilesTotal, 0, 1) : 0;

    public override string ToString() =>
        $"[{Stage}] {Fraction:P0} {FilesDone}/{FilesTotal} {(Detail ?? "")}".Trim();
}

/// <summary>
/// Downloads everything required to run a version: client jar, libraries, natives, asset index
/// and asset objects, plus the log4j configuration. Mirrors the official launcher's file layout.
/// </summary>
public sealed class VersionInstaller
{
    public const string ResourcesBase = "https://resources.download.minecraft.net/";
    public const string LibrariesBase = "https://libraries.minecraft.net/";
    public const string PistonDataBase = "https://piston-data.mojang.com/";
    public const string PistonMetaBase = "https://piston-meta.mojang.com/";

    private readonly AppPaths _paths;
    private readonly VersionListService _versions;

    public VersionInstaller(AppPaths paths, VersionListService versions)
    {
        _paths = paths;
        _versions = versions;
    }

    /// <summary>Returns every file required by the version, ready for the download engine.</summary>
    public async Task<List<DownloadItem>> CollectFilesAsync(VersionInfo resolved, IProgress<InstallProgress>? progress, CancellationToken ct = default)
    {
        var items = new List<DownloadItem>();

        // 1) client jar
        var jarId = string.IsNullOrWhiteSpace(resolved.Jar) ? resolved.Id : resolved.Jar!;
        var jarPath = _paths.VersionJar(jarId);
        if (resolved.Downloads?.Client is { } client && !string.IsNullOrWhiteSpace(client.Url))
        {
            items.Add(new DownloadItem
            {
                Url = client.Url,
                Destination = jarPath,
                Sha1 = client.Sha1,
                Size = client.Size,
                DisplayName = $"{jarId}.jar"
            });
        }
        else if (!File.Exists(jarPath))
        {
            // Loader-only versions inherit the jar; if the parent is missing this is a hard error later.
        }

        progress?.Report(new InstallProgress { Stage = "解析依赖", Detail = "分析资源列表" });

        // 2) logging configuration
        if (resolved.Logging?.Client?.File is { } logFile && !string.IsNullOrWhiteSpace(logFile.Url))
        {
            var dest = Path.Combine(_paths.VersionDir(resolved.Id), "log4j.xml");
            items.Add(new DownloadItem
            {
                Url = logFile.Url,
                Destination = dest,
                Sha1 = logFile.Sha1,
                Size = logFile.Size,
                DisplayName = "log4j config",
                SkipIfValid = true
            });
        }

        // 3) libraries
        foreach (var lib in resolved.Libraries)
        {
            if (!IsAllowed(lib, new RuleContext())) continue;
            var artifact = lib.Downloads?.Artifact;
            if (artifact is not null && !string.IsNullOrWhiteSpace(artifact.Url))
            {
                items.Add(new DownloadItem
                {
                    Url = artifact.Url,
                    Destination = _paths.LibraryPath(lib.Name),
                    Sha1 = artifact.Sha1,
                    Size = artifact.Size,
                    DisplayName = lib.Name
                });
            }
            else if (!string.IsNullOrWhiteSpace(lib.Name) && lib.Name.Contains(':'))
            {
                // Legacy metadata without a downloads block: derive from maven path.
                var path = MavenCoordinate.ToPath(lib.Name);
                items.Add(new DownloadItem
                {
                    Url = LibrariesBase + path.Replace('\\', '/'),
                    Destination = Path.Combine(_paths.LibrariesDir, path),
                    DisplayName = lib.Name
                });
            }

            // natives (1.19+ style: classifier "natives-windows", still an explicit file)
            var nativeClassifier = lib.Natives is not null && lib.Natives.TryGetValue("windows", out var cls) ? cls : null;
            nativeClassifier = nativeClassifier?.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
            if (nativeClassifier is not null && lib.Downloads?.Classifiers is not null &&
                lib.Downloads.Classifiers.TryGetValue(nativeClassifier, out var nativeFile) &&
                !string.IsNullOrWhiteSpace(nativeFile.Url))
            {
                var dest = Path.Combine(_paths.LibraryPath(lib.Name));
                var dir = Path.GetDirectoryName(dest)!;
                var baseName = Path.GetFileNameWithoutExtension(dest);
                var file = Path.Combine(dir, baseName + "-" + nativeClassifier + ".jar");
                items.Add(new DownloadItem
                {
                    Url = nativeFile.Url,
                    Destination = file,
                    Sha1 = nativeFile.Sha1,
                    Size = nativeFile.Size,
                    DisplayName = $"{lib.Name}:{nativeClassifier}"
                });
            }
        }

        // 4) assets
        if (resolved.AssetIndex is { } idx && !string.IsNullOrWhiteSpace(idx.Url))
        {
            var indexPath = _paths.AssetIndexFile(idx.Id);
            items.Add(new DownloadItem
            {
                Url = idx.Url,
                Destination = indexPath,
                Sha1 = idx.Sha1,
                Size = idx.Size,
                DisplayName = $"asset index {idx.Id}"
            });

            var objects = await ReadAssetObjectsAsync(indexPath, ct).ConfigureAwait(false);
            if (objects is not null)
            {
                progress?.Report(new InstallProgress
                {
                    Stage = "准备资源",
                    Detail = $"资源索引 {idx.Id}: {objects.Count} 个对象",
                    FilesTotal = objects.Count
                });
                foreach (var (name, obj) in objects)
                {
                    var dest = _paths.AssetObject(obj.Hash);
                    if (string.IsNullOrWhiteSpace(obj.Hash) || obj.Hash.Length < 2) continue;
                    items.Add(new DownloadItem
                    {
                        Url = ResourcesBase + obj.Hash[..2] + "/" + obj.Hash,
                        Destination = dest,
                        Sha1 = obj.Hash,
                        Size = obj.Size,
                        DisplayName = name
                    });
                }
            }
        }

        return items;
    }

    private static async Task<Dictionary<string, AssetObject>?> ReadAssetObjectsAsync(string indexPath, CancellationToken ct)
    {
        if (!File.Exists(indexPath)) return null;
        try
        {
            await using var fs = File.OpenRead(indexPath);
            var doc = await JsonSerializer.DeserializeAsync<AssetIndexDocument>(fs, JsonDefaults.Options, ct).ConfigureAwait(false);
            return doc?.Objects;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Runs the full install and returns the resolved (merged) version.</summary>
    public async Task<VersionInfo> InstallAsync(
        VersionInfo version,
        DownloadEngine engine,
        IProgress<InstallProgress>? progress,
        CancellationToken ct = default)
    {
        _paths.EnsureDirectories();
        progress?.Report(new InstallProgress { Stage = "解析版本", Detail = version.Id });

        var resolved = await _versions.ResolveInheritanceAsync(version, ct).ConfigureAwait(false);

        progress?.Report(new InstallProgress { Stage = "分析文件" });
        var files = await CollectFilesAsync(resolved, progress, ct).ConfigureAwait(false);

        var totalBytes = files.Sum(f => f.Size ?? 0);
        progress?.Report(new InstallProgress
        {
            Stage = "下载文件",
            Detail = $"{files.Count} 个文件",
            FilesTotal = files.Count,
            BytesTotal = totalBytes
        });

        var lastReport = 0L;
        var downloadProgress = new Progress<DownloadProgress>(p =>
        {
            var done = Interlocked.Read(ref lastReport);
            if (p.DownloadedBytes - done < 1_000_000 && p.CompletedFiles < p.TotalFiles) return;
            Interlocked.Exchange(ref lastReport, p.DownloadedBytes);
            progress?.Report(new InstallProgress
            {
                Stage = "下载文件",
                Detail = p.CurrentFile,
                FilesDone = p.CompletedFiles,
                FilesTotal = p.TotalFiles,
                BytesDone = p.DownloadedBytes,
                BytesTotal = Math.Max(p.TotalBytes, totalBytes)
            });
        });

        var result = await engine.DownloadAsync(files, downloadProgress, null, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            var msg = string.Join("\n", result.Failures.Take(5).Select(f => $"· {f}"));
            throw new InstallException($"有 {result.Failed} 个文件下载失败：\n{msg}", result.Failures);
        }

        progress?.Report(new InstallProgress
        {
            Stage = "解压原生库",
            FilesTotal = files.Count,
            FilesDone = files.Count,
            BytesTotal = totalBytes,
            BytesDone = totalBytes
        });
        await ExtractNativesAsync(resolved, ct).ConfigureAwait(false);

        // Persist a small completion marker; the canonical JSON stays untouched for inheritance.
        var marker = Path.Combine(_paths.VersionDir(resolved.Id), "installed.json");
        await File.WriteAllTextAsync(marker, JsonSerializer.Serialize(new
        {
            id = resolved.Id,
            installedAt = DateTimeOffset.Now,
            files = files.Count,
            bytes = totalBytes
        }, JsonDefaults.Options), ct).ConfigureAwait(false);

        progress?.Report(new InstallProgress
        {
            Stage = "完成",
            Detail = $"已安装 {resolved.Id}",
            FilesDone = files.Count,
            FilesTotal = files.Count,
            BytesTotal = totalBytes,
            BytesDone = totalBytes
        });
        return resolved;
    }

    /// <summary>Extracts every native jar for the active platform into &lt;version&gt;/natives.</summary>
    public async Task ExtractNativesAsync(VersionInfo resolved, CancellationToken ct = default)
    {
        var nativesDir = Path.Combine(_paths.VersionDir(resolved.Id), "natives");
        Directory.CreateDirectory(nativesDir);
        await Task.Run(() =>
        {
            foreach (var lib in resolved.Libraries)
            {
                if (!IsAllowed(lib, new RuleContext())) continue;
                if (lib.Natives is null) continue;
                if (!lib.Natives.TryGetValue("windows", out var cls)) continue;
                cls = cls.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                if (lib.Downloads?.Classifiers is null || !lib.Downloads.Classifiers.ContainsKey(cls)) continue;

                var jar = FindNativeJar(lib, cls);
                if (jar is null || !File.Exists(jar)) continue;
                var excludes = lib.Extract?.Exclude ?? new List<string> { "META-INF/" };
                try
                {
                    using var archive = ZipFile.OpenRead(jar);
                    foreach (var entry in archive.Entries)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (string.IsNullOrEmpty(entry.Name)) continue; // directory
                        if (excludes.Any(e => entry.FullName.StartsWith(e, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        var dest = Path.Combine(nativesDir, entry.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        entry.ExtractToFile(dest, overwrite: true);
                    }
                }
                catch (InvalidDataException)
                {
                    // corrupt jar: it will be re-validated on the next install
                }
            }
        }, ct).ConfigureAwait(false);
    }

    private string? FindNativeJar(Library lib, string classifier)
    {
        var artifactPath = _paths.LibraryPath(lib.Name);
        var dir = Path.GetDirectoryName(artifactPath)!;
        var baseName = Path.GetFileNameWithoutExtension(artifactPath);
        var candidate = Path.Combine(dir, $"{baseName}-{classifier}.jar");
        return File.Exists(candidate) ? candidate : null;
    }

    public static bool IsAllowed(Library lib, RuleContext ctx)
    {
        if (lib.Rules is null || lib.Rules.Count == 0) return true;
        // The official rule semantics: last matching rule wins; default is disallow when rules exist.
        bool allowed = false;
        foreach (var rule in lib.Rules)
        {
            if (rule.Matches(ctx))
                allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
        }
        return allowed;
    }
}

public sealed class InstallException(string message, IReadOnlyList<DownloadFailure> failures) : Exception(message)
{
    public IReadOnlyList<DownloadFailure> Failures { get; } = failures;
}
