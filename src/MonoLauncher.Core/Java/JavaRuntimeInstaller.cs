using System.IO.Compression;
using System.Net.Http.Headers;

namespace MonoLauncher.Core.Java;

/// <summary>
/// Downloads and unpacks a managed Java runtime (Adoptium Temurin) into
/// <c>data/java/java-&lt;major&gt;</c> so the launcher can deploy an environment
/// on a machine that has no Java installed.
/// </summary>
public sealed class JavaRuntimeInstaller
{
    private const string AdoptiumBinary =
        "https://api.adoptium.net/v3/binary/latest/{0}/ga/windows/x64/jre/hotspot/normal/eclipse?project=jdk";

    private readonly HttpClient _http;
    private readonly AppPaths _paths;

    public JavaRuntimeInstaller(HttpClient http, AppPaths paths)
    {
        _http = http;
        _paths = paths;
    }

    /// <summary>Returns an exact-major runtime when present, otherwise downloads one.</summary>
    public async Task<JavaRuntime> EnsureAsync(
        int major, JavaScanner scanner, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Fresh scan first: a runtime installed earlier in this session (or by the deployer)
        // may not be in the cache yet.
        progress?.Report($"检测本机 Java {major}…");
        var existing = await scanner.ScanAsync(_paths, null, ct).ConfigureAwait(false);
        var match = existing.FirstOrDefault(r => r.MajorVersion == major && r.Is64Bit)
                    ?? existing.FirstOrDefault(r => r.MajorVersion == major);
        if (match is not null) return match;

        progress?.Report($"本机没有 Java {major}，正在下载 Temurin JRE {major}…");
        return await InstallAsync(major, progress, ct).ConfigureAwait(false);
    }

    /// <summary>Downloads a Temurin JRE, unpacks it under data/java and probes the result.</summary>
    public async Task<JavaRuntime> InstallAsync(int major, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var targetDir = Path.Combine(_paths.JavaDir, $"java-{major}");
        Directory.CreateDirectory(targetDir);

        var url = string.Format(AdoptiumBinary, major);
        var zipPath = Path.Combine(_paths.CacheDir, $"temurin-jre-{major}.zip");
        Directory.CreateDirectory(_paths.CacheDir);

        await DownloadAsync(url, zipPath, progress, ct).ConfigureAwait(false);

        progress?.Report("解压 Java 运行时…");
        var staging = zipPath + ".extract";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        ZipFile.ExtractToDirectory(zipPath, staging);

        // The archive contains one root folder (jdk-21.0.x+y-jre); hoist its contents.
        var root = Directory.GetDirectories(staging).FirstOrDefault() ?? staging;
        foreach (var entry in Directory.GetFileSystemEntries(root))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(entry));
            if (Directory.Exists(entry)) Directory.Move(entry, dest);
            else File.Move(entry, dest);
        }
        try { Directory.Delete(staging, true); } catch { }
        try { File.Delete(zipPath); } catch { }

        var exe = Path.Combine(targetDir, "bin", "java.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException($"解压完成但没有找到 {exe}，Java 目录布局不符合预期");

        progress?.Report("验证 Java 运行时…");
        var runtime = await JavaScanner.ProbeAsync(exe, ct).ConfigureAwait(false)
                      ?? throw new InvalidOperationException($"Java {major} 下载后验证失败（java.exe 无法运行）");
        if (runtime.MajorVersion != major)
            throw new InvalidOperationException($"下载的 Java 报告版本 {runtime.MajorVersion}，与请求的 {major} 不符");
        return runtime;
    }

    /// <summary>Streams a download to disk with progress, then verifies it is a real zip.</summary>
    private async Task DownloadAsync(string url, string destFile, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("连接下载源…");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long done = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            if (progress is not null && sw.ElapsedMilliseconds > 300)
            {
                sw.Restart();
                progress.Report(total is > 0
                    ? $"下载 Java 运行时… {done / 1024.0 / 1024:0.0} / {total / 1024.0 / 1024:0.0} MB"
                    : $"下载 Java 运行时… {done / 1024.0 / 1024:0.0} MB");
            }
        }

        // Adoptium may answer with an HTML error page; verify the local file header ("PK").
        await using var check = File.OpenRead(destFile);
        var header = new byte[2];
        if (await check.ReadAsync(header, ct).ConfigureAwait(false) < 2 || header[0] != 0x50 || header[1] != 0x4B)
            throw new InvalidDataException("Java 运行时下载失败（响应不是有效的 zip，可能是网络被拦截），请检查网络或代理设置");
    }
}
