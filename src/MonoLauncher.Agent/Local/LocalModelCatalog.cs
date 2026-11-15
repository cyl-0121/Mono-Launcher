using MonoLauncher.Core;
using MonoLauncher.Core.Hardware;

namespace MonoLauncher.Agent.Local;

public enum LocalModelUse
{
    Chat,
    Coding
}

/// <summary>A downloadable GGUF model with its resource requirements.</summary>
public sealed record LocalModelSpec(
    string Id,
    string Name,
    string FileName,
    long SizeBytes,
    int MinRamMb,
    int ContextSize,
    string Note,
    IReadOnlyList<string> Urls,
    LocalModelUse Use = LocalModelUse.Chat,
    int MaxTokens = 512)
{
    public bool IsCoding => Use == LocalModelUse.Coding;

    public string SizeText => SizeBytes >= 1L << 30
        ? $"{SizeBytes / 1024.0 / 1024 / 1024:0.0} GB"
        : $"{SizeBytes / 1024.0 / 1024:0} MB";

    public string RequirementText => MinRamMb >= 1024
        ? $"{MinRamMb / 1024} GB 显存/内存起"
        : $"{MinRamMb} MB 显存/内存起";

    public string DisplayName => IsCoding ? $"{Name}（写代码）" : Name;
}

/// <summary>
/// Curated GGUF models: Qwen2.5-Instruct for chat and Qwen2.5-Coder-Instruct for coding,
/// all Q4_K_M (good quality at a size that fits consumer GPUs). hf-mirror is tried first.
/// </summary>
public static class LocalModelCatalog
{
    private const string Mirror = "https://hf-mirror.com";
    private const string Upstream = "https://huggingface.co";
    private const string ModelScope = "https://modelscope.cn";

    public static IReadOnlyList<LocalModelSpec> All { get; } = new List<LocalModelSpec>
    {
        // ---------- chat ----------
        new(
            Id: "qwen2.5-1.5b",
            Name: "Qwen2.5 1.5B 轻量",
            FileName: "qwen2.5-1.5b-instruct-q4_k_m.gguf",
            SizeBytes: 1_117_320_736,
            MinRamMb: 4096,
            ContextSize: 4096,
            Note: "最省资源、回复最快，适合低配机器简单聊天 / 翻译",
            Urls: new[]
            {
                $"{Mirror}/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf",
                $"{ModelScope}/models/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/master/qwen2.5-1.5b-instruct-q4_k_m.gguf",
                $"{Upstream}/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf"
            }),
        new(
            Id: "qwen2.5-3b",
            Name: "Qwen2.5 3B 均衡",
            FileName: "qwen2.5-3b-instruct-q4_k_m.gguf",
            SizeBytes: 2_104_932_768,
            MinRamMb: 4096,
            ContextSize: 4096,
            Note: "效果明显更好，6 GB 显存以上可用",
            Urls: new[]
            {
                $"{Mirror}/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf",
                $"{ModelScope}/models/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/master/qwen2.5-3b-instruct-q4_k_m.gguf",
                $"{Upstream}/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf"
            }),
        new(
            Id: "qwen2.5-7b",
            Name: "Qwen2.5 7B 高质量",
            FileName: "Qwen2.5-7B-Instruct-Q4_K_M.gguf",
            SizeBytes: 4_683_074_240,
            MinRamMb: 8192,
            ContextSize: 8192,
            Note: "日常聊天/翻译的最佳性价比，8 GB 显存可全放",
            Urls: new[]
            {
                $"{Mirror}/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf",
                $"{Upstream}/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf"
            }),

        // ---------- coding ----------
        new(
            Id: "coder-7b",
            Name: "Qwen2.5-Coder 7B",
            FileName: "qwen2.5-coder-7b-instruct-q4_k_m.gguf",
            SizeBytes: 4_683_073_536,
            MinRamMb: 8192,
            ContextSize: 8192,
            Note: "写脚本、改单个文件、解释报错；8 GB 显存起步。独显很流畅，核显约需半分钟到一分钟出一段代码",
            Urls: new[]
            {
                $"{Mirror}/Qwen/Qwen2.5-Coder-7B-Instruct-GGUF/resolve/main/qwen2.5-coder-7b-instruct-q4_k_m.gguf",
                $"{ModelScope}/models/Qwen/Qwen2.5-Coder-7B-Instruct-GGUF/resolve/master/qwen2.5-coder-7b-instruct-q4_k_m.gguf",
                $"{Upstream}/Qwen/Qwen2.5-Coder-7B-Instruct-GGUF/resolve/main/qwen2.5-coder-7b-instruct-q4_k_m.gguf"
            },
            Use: LocalModelUse.Coding,
            MaxTokens: 1024),
        new(
            Id: "coder-14b",
            Name: "Qwen2.5-Coder 14B",
            FileName: "Qwen2.5-Coder-14B-Instruct-Q4_K_M.gguf",
            SizeBytes: 8_988_111_072,
            MinRamMb: 12288,
            ContextSize: 8192,
            Note: "多文件、复杂逻辑明显更强，需 12～16 GB 显存",
            Urls: new[]
            {
                $"{Mirror}/bartowski/Qwen2.5-Coder-14B-Instruct-GGUF/resolve/main/Qwen2.5-Coder-14B-Instruct-Q4_K_M.gguf",
                $"{Upstream}/bartowski/Qwen2.5-Coder-14B-Instruct-GGUF/resolve/main/Qwen2.5-Coder-14B-Instruct-Q4_K_M.gguf"
            },
            Use: LocalModelUse.Coding,
            MaxTokens: 1536)
    };

    public static LocalModelSpec? ById(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The largest model of the requested use that this machine can run.</summary>
    public static LocalModelSpec? Recommend(HardwareReport hardware, LocalModelUse use = LocalModelUse.Chat) =>
        Usable(hardware, use).OrderByDescending(m => m.MinRamMb).FirstOrDefault();

    /// <summary>Models of the requested use that fit the detected memory / VRAM budget.</summary>
    public static IReadOnlyList<LocalModelSpec> Usable(HardwareReport hardware, LocalModelUse? use = null) =>
        All.Where(m => hardware.EffectiveMemoryMb >= m.MinRamMb)
           .Where(m => use is null || m.Use == use)
           .ToList();

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

    /// <summary>Model files are multi-GB, so the client must not impose a total timeout.</summary>
    public LocalAiDeployer(AppPaths paths)
    {
        _paths = paths;
        _http = new HttpClient(new System.Net.Http.SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(30)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MonoLauncher/1.0 (launcher; local-model)");
    }

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

    /// <summary>Number of parallel range requests. hf-mirror/ModelScope both answer 206, so this multiplies throughput.</summary>
    private const int ParallelParts = 4;
    private const int StallSeconds = 30;

    /// <summary>
    /// Downloads with several range requests in parallel, resuming each chunk from its own
    /// <c>.partN</c> file, then merges them. Falls back to a single stream when the size is unknown.
    /// </summary>
    private async Task DownloadAsync(
        string url, string partFile, long expectedSize, IProgress<LocalDeployProgress>? progress, CancellationToken ct)
    {
        if (expectedSize <= 0)
        {
            await DownloadSingleAsync(url, partFile, expectedSize, progress, ct).ConfigureAwait(false);
            return;
        }

        var parts = ParallelParts;
        var chunk = (expectedSize + parts - 1) / parts;
        var done = new long[1];
        for (int i = 0; i < parts; i++)
        {
            var pf = partFile + "." + i;
            if (File.Exists(pf)) done[0] += Math.Min(new FileInfo(pf).Length, chunk);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        progress?.Report(new LocalDeployProgress((double)Volatile.Read(ref done[0]) / expectedSize,
            Volatile.Read(ref done[0]), expectedSize, "开始下载"));

        var tasks = new List<Task>(parts);
        for (int i = 0; i < parts; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(async () =>
            {
                var start = (long)idx * chunk;
                var end = Math.Min(expectedSize - 1, start + chunk - 1);
                var want = end - start + 1;
                var pf = partFile + "." + idx;
                var have = File.Exists(pf) ? new FileInfo(pf).Length : 0;
                if (have > want) { File.Delete(pf); have = 0; }
                if (have == want) return;

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start + have, end);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                    throw new InvalidDataException($"下载源不支持分块（HTTP {(int)response.StatusCode}）");

                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var fs = new FileStream(pf, have > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write, FileShare.None);
                var buffer = new byte[1 << 16];
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                void Arm() => idle.CancelAfter(TimeSpan.FromSeconds(StallSeconds));
                Arm();

                long left = want - have;
                var lastBytes = Volatile.Read(ref done[0]);
                var lastTicks = sw.ElapsedMilliseconds;
                while (left > 0)
                {
                    int n;
                    try
                    {
                        n = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), idle.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new TimeoutException($"下载停滞超过 {StallSeconds} 秒（网络中断或源不可用）");
                    }
                    if (n <= 0) break;
                    await fs.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    left -= n;
                    Interlocked.Add(ref done[0], n);
                    Arm();

                    var now = sw.ElapsedMilliseconds;
                    if (now - lastTicks >= 500)
                    {
                        var cur = Volatile.Read(ref done[0]);
                        var speed = (cur - lastBytes) / ((now - lastTicks) / 1000.0);
                        lastBytes = cur; lastTicks = now;
                        var eta = speed > 65536 ? TimeSpan.FromSeconds((expectedSize - cur) / speed) : TimeSpan.Zero;
                        progress?.Report(new LocalDeployProgress(
                            Math.Min(1, (double)cur / expectedSize), cur, expectedSize,
                            $"下载中… {cur / 1024.0 / 1024:0} / {expectedSize / 1024.0 / 1024:0} MB · {speed / 1024 / 1024:0.0} MB/s" +
                            (eta > TimeSpan.Zero ? $" · 剩余 {eta:mm\\:ss}" : "")));
                    }
                }
            }, ct));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);

        for (int i = 0; i < parts; i++)
        {
            var start = (long)i * chunk;
            var want = Math.Min(expectedSize - 1, start + chunk - 1) - start + 1;
            var pf = partFile + "." + i;
            if (!File.Exists(pf) || new FileInfo(pf).Length != want)
                throw new InvalidDataException("分块下载不完整，请重试（会自动续传）");
        }

        progress?.Report(new LocalDeployProgress(1, expectedSize, expectedSize, "合并分块…"));
        await using (var outFs = new FileStream(partFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
        {
            for (int i = 0; i < parts; i++)
            {
                var pf = partFile + "." + i;
                await using (var fs = File.OpenRead(pf))
                    await fs.CopyToAsync(outFs, ct).ConfigureAwait(false);
                File.Delete(pf);
            }
        }
        progress?.Report(new LocalDeployProgress(1, expectedSize, expectedSize, "校验中…"));
    }

    private async Task DownloadSingleAsync(
        string url, string partFile, long expectedSize, IProgress<LocalDeployProgress>? progress, CancellationToken ct)
    {
        var existing = File.Exists(partFile) ? new FileInfo(partFile).Length : 0;
        if (existing > expectedSize) { File.Delete(partFile); existing = 0; }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (existing > 0 && response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            return; // .part is already complete; the caller verifies and renames.
        response.EnsureSuccessStatusCode();

        var append = existing > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent ? existing : 0;
        if (append == 0) existing = 0;

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(partFile, append > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None);
        var buffer = new byte[1024 * 128];
        long done = existing;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        progress?.Report(new LocalDeployProgress(expectedSize > 0 ? (double)done / expectedSize : 0, done, expectedSize, "开始下载"));

        // Watchdog: abort only when the stream actually stalls, not because the whole file is big.
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        void Arm() => idle.CancelAfter(TimeSpan.FromSeconds(90));
        Arm();
        while (true)
        {
            int read;
            try
            {
                read = await source.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("下载停滞超过 90 秒（网络中断或下载源不可用）");
            }
            if (read <= 0) break;

            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            Arm();
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
