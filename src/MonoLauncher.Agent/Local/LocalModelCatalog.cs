using MonoLauncher.Core;
using MonoLauncher.Core.Hardware;

namespace MonoLauncher.Agent.Local;

/// <summary>A downloadable GGUF model with its resource requirements.</summary>
public sealed record LocalModelSpec(
    string Id,
    string Name,
    string FileName,
    long SizeBytes,
    int MinRamMb,
    int ContextSize,
    string Note,
    IReadOnlyList<string> Urls)
{
    public string SizeText => SizeBytes >= 1L << 30
        ? $"{SizeBytes / 1024.0 / 1024 / 1024:0.0} GB"
        : $"{SizeBytes / 1024.0 / 1024:0} MB";

    public string RequirementText => MinRamMb >= 1024
        ? $"{MinRamMb / 1024} GB 内存起"
        : $"{MinRamMb} MB 内存起";
}

/// <summary>
/// Curated small/medium GGUF models. All are Qwen2.5-Instruct Q4_K_M: good Chinese ability,
/// small enough to run on CPU. hf-mirror (domestic) is tried before huggingface.co.
/// </summary>
public static class LocalModelCatalog
{
    private const string Mirror = "https://hf-mirror.com";
    private const string Upstream = "https://huggingface.co";

    public static IReadOnlyList<LocalModelSpec> All { get; } = new List<LocalModelSpec>
    {
        new(
            Id: "qwen2.5-1.5b",
            Name: "Qwen2.5 1.5B（轻量）",
            FileName: "qwen2.5-1.5b-instruct-q4_k_m.gguf",
            SizeBytes: 1_117_320_736,
            MinRamMb: 4096,
            ContextSize: 4096,
            Note: "最省资源，回复速度快，适合低配机器做聊天 / 翻译",
            Urls: new[]
            {
                $"{Mirror}/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf",
                $"{Upstream}/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf"
            }),
        new(
            Id: "qwen2.5-3b",
            Name: "Qwen2.5 3B（均衡）",
            FileName: "qwen2.5-3b-instruct-q4_k_m.gguf",
            SizeBytes: 2_104_932_768,
            MinRamMb: 8192,
            ContextSize: 4096,
            Note: "效果明显更好，8 GB 内存以上的均衡选择",
            Urls: new[]
            {
                $"{Mirror}/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf",
                $"{Upstream}/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf"
            }),
        new(
            Id: "qwen2.5-7b",
            Name: "Qwen2.5 7B（高质量）",
            FileName: "Qwen2.5-7B-Instruct-Q4_K_M.gguf",
            SizeBytes: 4_683_074_240,
            MinRamMb: 16384,
            ContextSize: 4096,
            Note: "回答质量最好，但 CPU 推理较慢，建议 16 GB 内存以上",
            Urls: new[]
            {
                $"{Mirror}/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf",
                $"{Upstream}/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf"
            })
    };

    public static LocalModelSpec? ById(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The largest model this machine can comfortably run.</summary>
    public static LocalModelSpec? Recommend(HardwareReport hardware) => hardware.Verdict switch
    {
        LocalAiVerdict.High => ById("qwen2.5-7b"),
        LocalAiVerdict.Medium => ById("qwen2.5-3b"),
        LocalAiVerdict.Low => ById("qwen2.5-1.5b"),
        _ => null
    };

    /// <summary>Models that fit in the detected memory (used to populate the picker).</summary>
    public static IReadOnlyList<LocalModelSpec> Usable(HardwareReport hardware) =>
        All.Where(m => hardware.TotalRamMb >= m.MinRamMb).ToList();

    public static string ModelDirectory(AppPaths paths) => Path.Combine(paths.DataDir, "models");
}

public sealed record LocalDeployProgress(double Fraction, long DoneBytes, long TotalBytes, string Stage)
{
    public string DoneText => TotalBytes > 0
        ? $"{DoneBytes / 1024.0 / 1024:0} / {TotalBytes / 1024.0 / 1024:0} MB"
        : $"{DoneBytes / 1024.0 / 1024:0} MB";
}

/// <summary>
/// Downloads a GGUF model into <c>data/models</c> and reports progress. Resumes from a
/// <c>.part</c> file so a dropped connection does not restart a multi-GB download.
/// </summary>
public sealed class LocalAiDeployer
{
    private readonly HttpClient _http;
    private readonly AppPaths _paths;

    public LocalAiDeployer(HttpClient http, AppPaths paths)
    {
        _http = http;
        _paths = paths;
    }

    public string ModelsDir => LocalModelCatalog.ModelDirectory(_paths);

    public bool IsInstalled(LocalModelSpec spec)
    {
        var path = Path.Combine(ModelsDir, spec.FileName);
        return File.Exists(path) && new FileInfo(path).Length == spec.SizeBytes;
    }

    public async Task<string> EnsureModelAsync(
        LocalModelSpec spec, IProgress<LocalDeployProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ModelsDir);
        var target = Path.Combine(ModelsDir, spec.FileName);
        if (IsInstalled(spec))
        {
            progress?.Report(new LocalDeployProgress(1, spec.SizeBytes, spec.SizeBytes, "模型已存在，跳过下载"));
            return target;
        }

        var part = target + ".part";
        var errors = new List<string>();
        foreach (var url in spec.Urls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await DownloadAsync(url, part, spec.SizeBytes, progress, ct).ConfigureAwait(false);
                if (new FileInfo(part).Length != spec.SizeBytes)
                    throw new InvalidDataException($"文件大小不符（{new FileInfo(part).Length} / {spec.SizeBytes}）");
                File.Move(part, target, overwrite: true);
                progress?.Report(new LocalDeployProgress(1, spec.SizeBytes, spec.SizeBytes, "下载完成"));
                return target;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errors.Add($"{new Uri(url).Host}: {ex.Message}");
                progress?.Report(new LocalDeployProgress(0, 0, spec.SizeBytes, "该下载源失败，尝试下一个…"));
            }
        }
        throw new InvalidOperationException("模型下载失败：\n" + string.Join("\n", errors));
    }

    private async Task DownloadAsync(
        string url, string partFile, long expectedSize, IProgress<LocalDeployProgress>? progress, CancellationToken ct)
    {
        var existing = File.Exists(partFile) ? new FileInfo(partFile).Length : 0;
        if (existing > expectedSize) { File.Delete(partFile); existing = 0; }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (existing > 0 && response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // .part is already complete; let the caller verify and rename.
            return;
        }
        response.EnsureSuccessStatusCode();

        var append = existing > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent ? existing : 0;
        if (append == 0) existing = 0;

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(partFile, append > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None);
        var buffer = new byte[1024 * 128];
        long done = existing;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int read;
        progress?.Report(new LocalDeployProgress(expectedSize > 0 ? (double)done / expectedSize : 0, done, expectedSize, "开始下载"));
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            if (sw.ElapsedMilliseconds > 400)
            {
                sw.Restart();
                progress?.Report(new LocalDeployProgress(
                    expectedSize > 0 ? Math.Min(1, (double)done / expectedSize) : 0, done, expectedSize,
                    $"下载中… {done / 1024.0 / 1024:0} / {expectedSize / 1024.0 / 1024:0} MB"));
            }
        }
        progress?.Report(new LocalDeployProgress(expectedSize > 0 ? (double)done / expectedSize : 1, done, expectedSize, "校验中…"));
    }
}
