using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MonoLauncher.Core.Java;
using MonoLauncher.Core.Versions;

namespace MonoLauncher.Core.Loaders;

public enum LoaderKind { Vanilla, Fabric, Quilt, Forge, NeoForge }

public sealed class LoaderInstallResult
{
    public required string VersionId { get; init; }
    public required string Kind { get; init; }
    public required string LoaderVersion { get; init; }
    public string? VersionJsonPath { get; init; }
}

/// <summary>
/// Installs mod loaders.
/// Fabric/Quilt: official meta profile JSON (no Java required).
/// NeoForge/Forge: official installer jar executed with the detected Java runtime.
/// </summary>
public sealed class LoaderService
{
    private readonly HttpClient _http;
    private readonly AppPaths _paths;
    private readonly VersionListService _versions;

    public LoaderService(HttpClient http, AppPaths paths, VersionListService versions)
    {
        _http = http;
        _paths = paths;
        _versions = versions;
    }

    public static string DisplayName(string kind) => kind switch
    {
        "fabric" => "Fabric",
        "quilt" => "Quilt",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        _ => "原版"
    };

    public async Task<IReadOnlyList<string>> GetLoaderVersionsAsync(string kind, string mcVersion, CancellationToken ct = default)
    {
        return kind switch
        {
            "fabric" => await GetFabricFamilyVersionsAsync("https://meta.fabricmc.net/v2/versions/loader/", mcVersion, ct),
            "quilt" => await GetQuiltVersionsAsync(mcVersion, ct),
            "neoforge" => await GetNeoForgeVersionsAsync(mcVersion, ct),
            "forge" => await GetForgeVersionsAsync(mcVersion, ct),
            _ => Array.Empty<string>()
        };
    }

    private async Task<IReadOnlyList<string>> GetFabricFamilyVersionsAsync(string baseUrl, string mcVersion, CancellationToken ct)
    {
        try
        {
            var json = await _http.GetStringAsync(baseUrl + Uri.EscapeDataString(mcVersion), ct).ConfigureAwait(false);
            var node = JsonNode.Parse(json);
            var result = new List<string>();
            if (node is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    var v = item?["loader"]?["version"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(v)) result.Add(v);
                }
            }
            return result;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private async Task<IReadOnlyList<string>> GetQuiltVersionsAsync(string mcVersion, CancellationToken ct)
    {
        try
        {
            var json = await _http.GetStringAsync(
                $"https://meta.quiltmc.org/v3/versions/loader/{Uri.EscapeDataString(mcVersion)}", ct).ConfigureAwait(false);
            var node = JsonNode.Parse(json);
            var result = new List<string>();
            if (node is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    var v = item?["loader"]?["version"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(v)) result.Add(v);
                }
            }
            return result;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private async Task<IReadOnlyList<string>> GetNeoForgeVersionsAsync(string mcVersion, CancellationToken ct)
    {
        // Official maven first; the BMCLAPI mirror is used when the official endpoint is unreachable.
        try
        {
            var json = await _http.GetStringAsync(
                "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge", ct).ConfigureAwait(false);
            var node = JsonNode.Parse(json);
            var all = node?["versions"]?.AsArray().Select(v => v?.GetValue<string>()).Where(v => v is not null).Select(v => v!).ToList()
                      ?? new List<string>();
            // NeoForge versions look like 21.1.209 for MC 1.21.1 (and 20.4.x for 1.20.4).
            var prefix = NeoForgePrefixFor(mcVersion);
            var filtered = all.Where(v => v.StartsWith(prefix, StringComparison.Ordinal)).Reverse().ToList();
            if (filtered.Count > 0) return filtered;
        }
        catch { /* fall through to mirror */ }

        try
        {
            var json = await _http.GetStringAsync(
                $"https://bmclapi2.bangbang93.com/neoforge/list/{Uri.EscapeDataString(mcVersion)}", ct).ConfigureAwait(false);
            var arr = JsonNode.Parse(json) as JsonArray;
            if (arr is null) return Array.Empty<string>();
            return arr.Select(x => x?["version"]?.GetValue<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string NeoForgePrefixFor(string mcVersion)
    {
        // 1.21.1 -> 21.1. ; 1.20.4 -> 20.4. ; 1.21 -> 21.0.
        var parts = mcVersion.Split('.');
        if (parts.Length >= 3 && int.TryParse(parts[1], out var minor))
            return $"{minor}.{parts[2]}.";
        if (parts.Length == 2 && int.TryParse(parts[1], out var minorOnly))
            return $"{minorOnly}.0.";
        return mcVersion + ".";
    }

    private async Task<IReadOnlyList<string>> GetForgeVersionsAsync(string mcVersion, CancellationToken ct)
    {
        try
        {
            var json = await _http.GetStringAsync(
                "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json", ct).ConfigureAwait(false);
            var node = JsonNode.Parse(json)?["promos"];
            var result = new List<string>();
            if (node is JsonObject obj)
            {
                foreach (var (key, value) in obj)
                {
                    if (!key.StartsWith(mcVersion + "-", StringComparison.Ordinal)) continue;
                    var v = value?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(v)) result.Add(v!);
                }
            }
            return result.Distinct().ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Installs loader metadata. Returns the generated version id; caller then runs VersionInstaller.</summary>
    public async Task<LoaderInstallResult> InstallAsync(
        string kind,
        string mcVersion,
        string? loaderVersion,
        JavaRuntime? java,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return kind switch
        {
            "fabric" => await InstallFabricFamilyAsync("fabric", "https://meta.fabricmc.net/v2/versions/loader/", mcVersion, loaderVersion, progress, ct),
            "quilt" => await InstallFabricFamilyAsync("quilt", "https://meta.quiltmc.org/v3/versions/loader/", mcVersion, loaderVersion, progress, ct),
            "neoforge" => await InstallInstallerJarAsync("neoforge", mcVersion, loaderVersion, java, progress, ct),
            "forge" => await InstallInstallerJarAsync("forge", mcVersion, loaderVersion, java, progress, ct),
            _ => throw new NotSupportedException($"不支持的 Loader: {kind}")
        };
    }

    private async Task<LoaderInstallResult> InstallFabricFamilyAsync(
        string kind, string metaBase, string mcVersion, string? loaderVersion,
        IProgress<string>? progress, CancellationToken ct)
    {
        var version = loaderVersion;
        if (string.IsNullOrWhiteSpace(version) || version == "最新")
        {
            progress?.Report("获取最新 Loader 版本…");
            var list = await GetLoaderVersionsAsync(kind, mcVersion, ct);
            version = list.FirstOrDefault() ?? throw new InvalidOperationException($"{DisplayName(kind)} 没有适用于 {mcVersion} 的版本");
        }

        progress?.Report($"下载 {DisplayName(kind)} profile {version} …");
        var url = $"{metaBase}{Uri.EscapeDataString(mcVersion)}/{Uri.EscapeDataString(version!)}/profile/json";
        var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
        var node = JsonNode.Parse(json) ?? throw new InvalidDataException("profile JSON 为空");
        var id = node["id"]?.GetValue<string>() ?? $"{kind}-loader-{version}-{mcVersion}";
        node["loaderKind"] = kind;
        node["loaderVersion"] = version;

        var dir = _paths.VersionDir(id);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, id + ".json");
        await File.WriteAllTextAsync(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        progress?.Report($"已写入版本元数据 {id}");
        return new LoaderInstallResult
        {
            VersionId = id,
            Kind = kind,
            LoaderVersion = version!,
            VersionJsonPath = path
        };
    }

    private async Task<LoaderInstallResult> InstallInstallerJarAsync(
        string kind, string mcVersion, string? loaderVersion, JavaRuntime? java,
        IProgress<string>? progress, CancellationToken ct)
    {
        if (java is null)
            throw new InvalidOperationException("安装 Forge/NeoForge 需要可用的 Java 运行时");

        var version = loaderVersion;
        if (string.IsNullOrWhiteSpace(version) || version == "最新")
        {
            progress?.Report("获取 Loader 版本列表…");
            var list = await GetLoaderVersionsAsync(kind, mcVersion, ct);
            version = list.FirstOrDefault() ?? throw new InvalidOperationException($"{DisplayName(kind)} 没有适用于 {mcVersion} 的版本");
        }

        var urls = new List<string>();
        if (kind == "neoforge")
        {
            urls.Add($"https://maven.neoforged.net/releases/net/neoforged/neoforge/{version}/neoforge-{version}-installer.jar");
            urls.Add($"https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/{version}/neoforge-{version}-installer.jar");
        }
        else
        {
            urls.Add($"https://maven.minecraftforge.net/net/minecraftforge/forge/{mcVersion}-{version}/forge-{mcVersion}-{version}-installer.jar");
            urls.Add($"https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/{mcVersion}-{version}/forge-{mcVersion}-{version}-installer.jar");
        }

        Directory.CreateDirectory(_paths.DownloadsDir);
        var installer = Path.Combine(_paths.DownloadsDir, $"{kind}-{version}-installer.jar");
        if (!File.Exists(installer))
        {
            progress?.Report($"下载安装器 {DisplayName(kind)} {version} …");
            Exception? lastError = null;
            foreach (var url in urls)
            {
                try
                {
                    var bytes = await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(installer, bytes, ct).ConfigureAwait(false);
                    lastError = null;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }
            if (lastError is not null)
                throw new InvalidOperationException("安装器下载失败：" + lastError.Message, lastError);
        }

        EnsureLauncherProfile();
        // The official NeoForge maven is unreachable from some networks (CN). Rewriting the URLs
        // inside the installer to the BMCLAPI mirror keeps the official installer logic intact
        // while making the download step work. The installer itself is still the official binary.
        await RewriteInstallerMirrorsAsync(installer, progress, ct).ConfigureAwait(false);
        if (kind == "neoforge")
            await EnsureNeoFormAsync(installer, progress, ct).ConfigureAwait(false);

        progress?.Report($"运行安装器（Java {java.MajorVersion}）…");
        var log = await RunInstallerAsync(java, installer, ct).ConfigureAwait(false);
        progress?.Report("安装器完成，解析生成的版本…");

        var generated = FindGeneratedVersion(kind);
        if (generated is null)
            throw new InvalidOperationException($"安装器未生成版本 JSON。输出：\n{log}");

        var id = Path.GetFileNameWithoutExtension(generated);
        var jsonNode = JsonNode.Parse(await File.ReadAllTextAsync(generated, ct).ConfigureAwait(false));
        if (jsonNode is JsonObject obj)
        {
            obj["loaderKind"] = kind;
            obj["loaderVersion"] = version;
            await File.WriteAllTextAsync(generated, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        }

        progress?.Report($"已安装 {id}");
        return new LoaderInstallResult
        {
            VersionId = id,
            Kind = kind,
            LoaderVersion = version!,
            VersionJsonPath = generated
        };
    }

    /// <summary>Rewrites maven.neoforged.net URLs inside the installer's JSON payloads to BMCLAPI.</summary>
    private async Task RewriteInstallerMirrorsAsync(string installerPath, IProgress<string>? progress, CancellationToken ct)
    {
        const string official = "https://maven.neoforged.net/releases/";
        const string mirror = "https://bmclapi2.bangbang93.com/maven/";
        var marker = installerPath + ".mirrored";
        if (File.Exists(marker)) return;

        try
        {
            using var zip = System.IO.Compression.ZipFile.Open(installerPath, System.IO.Compression.ZipArchiveMode.Update);
            foreach (var name in new[] { "install_profile.json", "version.json" })
            {
                var entry = zip.GetEntry(name);
                if (entry is null) continue;
                string text;
                using (var reader = new StreamReader(entry.Open(), System.Text.Encoding.UTF8))
                    text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                if (!text.Contains(official, StringComparison.Ordinal)) continue;
                text = text.Replace(official, mirror, StringComparison.Ordinal)
                           .Replace("https://maven.neoforged.net/", "https://bmclapi2.bangbang93.com/", StringComparison.Ordinal);
                entry.Delete();
                var newEntry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
                await using var writer = new StreamWriter(newEntry.Open(), new System.Text.UTF8Encoding(false));
                await writer.WriteAsync(text.AsMemory(), ct).ConfigureAwait(false);
            }
            progress?.Report("安装器下载源已切换到镜像（BMCLAPI）");
        }
        catch (Exception ex)
        {
            progress?.Report("安装器源重写失败（将继续使用官方源）：" + ex.Message);
            return;
        }
        await File.WriteAllTextAsync(marker, DateTimeOffset.Now.ToString("O"), ct).ConfigureAwait(false);
    }

    /// <summary>Pre-downloads the neoform archive; the installer's own download often fails on this network.</summary>
    private async Task EnsureNeoFormAsync(string installerPath, IProgress<string>? progress, CancellationToken ct)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(installerPath);
            var entry = zip.GetEntry("install_profile.json");
            if (entry is null) return;
            using var reader = new StreamReader(entry.Open(), System.Text.Encoding.UTF8);
            var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            var node = JsonNode.Parse(json);
            var libraries = node?["libraries"]?.AsArray();
            if (libraries is null) return;

            foreach (var lib in libraries)
            {
                var path = lib?["downloads"]?["artifact"]?["path"]?.GetValue<string>();
                var url = lib?["downloads"]?["artifact"]?["url"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(url)) continue;
                if (!path!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                var dest = Path.Combine(_paths.LibrariesDir, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(dest)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                progress?.Report($"预下载 {Path.GetFileName(path)} …");
                try
                {
                    var bytes = await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(dest, bytes, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    progress?.Report($"预下载失败（安装器会自行重试）：{ex.Message}");
                }
            }
        }
        catch
        {
            /* best effort */
        }
    }

    /// <summary>Forge/NeoForge installers refuse to run without a launcher_profiles.json.</summary>
    private void EnsureLauncherProfile()
    {
        Directory.CreateDirectory(_paths.DataDir);
        var path = Path.Combine(_paths.DataDir, "launcher_profiles.json");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, """
                {
                  "profiles": {},
                  "settings": {},
                  "version": 3
                }
                """);
        }
    }

    private string? FindGeneratedVersion(string kind)
    {
        if (!Directory.Exists(_paths.VersionsDir)) return null;
        var keyword = kind == "neoforge" ? "neoforge" : "forge";
        return Directory.EnumerateDirectories(_paths.VersionsDir)
            .Select(d => Path.Combine(d, Path.GetFileName(d) + ".json"))
            .Where(File.Exists)
            .Where(f => Path.GetFileNameWithoutExtension(f).Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private async Task<string> RunInstallerAsync(JavaRuntime java, string installerJar, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(java.ExecutablePath)
        {
            WorkingDirectory = _paths.DataDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installerJar);
        psi.ArgumentList.Add("--installClient");
        psi.ArgumentList.Add(_paths.DataDir);

        using var process = new Process { StartInfo = psi };
        var output = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        if (!process.Start()) throw new InvalidOperationException("无法启动安装器进程");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("安装器超时（10 分钟）");
        }

        lock (output)
            return output.ToString();
    }
}
