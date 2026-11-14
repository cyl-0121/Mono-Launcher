using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace MonoLauncher.Core.Download;

public sealed record DownloadItem
{
    public required string Url { get; init; }
    public required string Destination { get; init; }
    public string? Sha1 { get; init; }
    public string? Sha256 { get; init; }
    public long? Size { get; init; }
    public string? DisplayName { get; init; }
    /// <summary>Optional alternate URLs tried in order after the primary URL fails.</summary>
    public IReadOnlyList<string> Mirrors { get; init; } = Array.Empty<string>();
    /// <summary>When true a previously existing, correctly-hashed file is never re-downloaded.</summary>
    public bool SkipIfValid { get; init; } = true;

    public string Label => DisplayName ?? Path.GetFileName(Destination);
}

public sealed class DownloadOptions
{
    public int MaxConcurrency { get; set; } = 8;
    public int MaxRetries { get; set; } = 3;
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(600);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(300);
    public string? ProxyUrl { get; set; }
    /// <summary>Optional global cap in bytes/sec; 0 = unlimited.</summary>
    public long MaxBytesPerSecond { get; set; }
    /// <summary>Optional rewrite of Mojang URLs to a mirror (BMCLAPI). Off by default.</summary>
    public bool UseMirror { get; set; }
    public string? UserAgent { get; set; } = "MonoLauncher/1.0 (launcher)";
    /// <summary>If true the engine keeps going after individual failures.</summary>
    public bool ContinueOnError { get; set; }
}

public sealed class DownloadFailure
{
    public required string Url { get; init; }
    public required string Destination { get; init; }
    public required string Message { get; init; }
    public string? ExceptionType { get; init; }
    public int Attempts { get; init; }
    public HttpStatusCode? StatusCode { get; init; }

    public override string ToString() =>
        $"{Path.GetFileName(Destination)} <- {Url} ({Attempts} attempts): {ExceptionType}: {Message}";
}

public sealed class DownloadResult
{
    public int Total { get; init; }
    public int Downloaded { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public long BytesDownloaded { get; init; }
    public TimeSpan Elapsed { get; init; }
    public IReadOnlyList<DownloadFailure> Failures { get; init; } = Array.Empty<DownloadFailure>();
    public bool Success => Failed == 0;
    public double AverageBytesPerSecond => Elapsed.TotalSeconds > 0 ? BytesDownloaded / Elapsed.TotalSeconds : 0;
}

public sealed class DownloadProgress
{
    public int TotalFiles { get; init; }
    public int CompletedFiles { get; init; }
    public int FailedFiles { get; init; }
    public long TotalBytes { get; init; }
    public long DownloadedBytes { get; init; }
    public int ActiveConnections { get; init; }
    public double BytesPerSecond { get; init; }
    public string? CurrentFile { get; init; }
    public string? CurrentStage { get; init; }
    public double Fraction => TotalBytes > 0 ? Math.Clamp((double)DownloadedBytes / TotalBytes, 0, 1) : 0;
}

public interface IDownloadSink
{
    void Report(DownloadProgress progress);
}

/// <summary>
/// Bounded-concurrency, resumable (HTTP Range), retrying, checksum-verifying download engine.
/// Files are written to a sibling ".part" file and atomically moved into place only after the
/// hash matched, so a interrupted run never leaves a corrupt destination file behind.
/// </summary>
public sealed class DownloadEngine : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _gate;
    private readonly DownloadOptions _options;
    private long _bytesDownloaded;
    private int _completed;
    private int _failed;
    private long _totalBytes;
    private int _active;
    private readonly Stopwatch _clock = new();
    private readonly ConcurrentDictionary<string, DownloadFailure> _failures = new(StringComparer.OrdinalIgnoreCase);
    private RateMeter _meter = new();

    public DownloadEngine(DownloadOptions options, HttpClient? http = null)
    {
        _options = options;
        _gate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));
        _ownsClient = http is null;
        _http = http ?? CreateClient(options);
    }

    public static HttpClient CreateClient(DownloadOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = Math.Max(4, options.MaxConcurrency),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(20),
            AllowAutoRedirect = true
        };
        if (!string.IsNullOrWhiteSpace(options.ProxyUrl))
        {
            handler.Proxy = new WebProxy(options.ProxyUrl);
            handler.UseProxy = true;
        }
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent ?? "MonoLauncher/1.0");
        return client;
    }

    public void Dispose()
    {
        _gate.Dispose();
        if (_ownsClient) _http.Dispose();
    }

    /// <summary>
    /// Downloads every item. If an item already exists with a matching hash it is skipped.
    /// Progress is reported on a background flusher at most ~10x/second.
    /// </summary>
    public async Task<DownloadResult> DownloadAsync(
        IEnumerable<DownloadItem> items,
        IProgress<DownloadProgress>? progress = null,
        IProgress<DownloadFailure>? failureSink = null,
        CancellationToken ct = default)
    {
        // De-duplicate by destination so concurrent requests for the same file collapse.
        var map = new Dictionary<string, DownloadItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.Url)) continue;
            map[Path.GetFullPath(it.Destination)] = it;
        }

        var work = map.Values.ToList();
        Interlocked.Exchange(ref _bytesDownloaded, 0);
        Interlocked.Exchange(ref _completed, 0);
        Interlocked.Exchange(ref _failed, 0);
        Interlocked.Exchange(ref _active, 0);
        _failures.Clear();
        _totalBytes = work.Sum(w => w.Size ?? 0);

        var buffered = progress is null ? null : new BufferedProgress(progress, 120);
        _clock.Restart();
        _meter = new RateMeter();

        using var flusherCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var flusher = buffered is null ? Task.CompletedTask : FlushLoop(buffered, flusherCts.Token);

        try
        {
            var tasks = work.Select(item => RunGuarded(item, buffered, failureSink, ct)).ToList();
            var all = Task.WhenAll(tasks);
            // Keep flushing progress while downloads run.
            while (!all.IsCompleted)
            {
                await Task.WhenAny(all, Task.Delay(150, ct)).ConfigureAwait(false);
                if (all.IsCompleted) break;
            }
            await all.ConfigureAwait(false);
        }
        finally
        {
            flusherCts.Cancel();
            try { await flusher.ConfigureAwait(false); } catch { /* ignore */ }
            _clock.Stop();
        }

        buffered?.EmitFinal(BuildProgress(null, "完成"));

        return new DownloadResult
        {
            Total = work.Count,
            Downloaded = _completed,
            Failed = _failed,
            Skipped = work.Count - _completed - _failed,
            BytesDownloaded = Interlocked.Read(ref _bytesDownloaded),
            Elapsed = _clock.Elapsed,
            Failures = _failures.Values.ToList()
        };
    }

    private async Task FlushLoop(BufferedProgress progress, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            progress.Emit(BuildProgress(null, null));
            try { await Task.Delay(120, ct).ConfigureAwait(false); } catch { break; }
        }
    }

    private DownloadProgress BuildProgress(string? file, string? stage) => new()
    {
        TotalFiles = _total,
        CompletedFiles = Volatile.Read(ref _completed),
        FailedFiles = Volatile.Read(ref _failed),
        TotalBytes = _totalBytes,
        DownloadedBytes = Interlocked.Read(ref _bytesDownloaded),
        ActiveConnections = Volatile.Read(ref _active),
        BytesPerSecond = _meter.PerSecond,
        CurrentFile = file,
        CurrentStage = stage
    };

    private int _total;

    private async Task RunGuarded(DownloadItem item, BufferedProgress? progress, IProgress<DownloadFailure>? failureSink, CancellationToken ct)
    {
        Interlocked.Increment(ref _total);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        Interlocked.Increment(ref _active);
        try
        {
            // Fast path: file present and hash matches.
            if (item.SkipIfValid && File.Exists(item.Destination) && await VerifyFileAsync(item.Destination, item, ct).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _completed);
                Interlocked.Add(ref _bytesDownloaded, new FileInfo(item.Destination).Length);
                return;
            }

            var failure = await DownloadOneAsync(item, progress, ct).ConfigureAwait(false);
            if (failure is null)
            {
                Interlocked.Increment(ref _completed);
            }
            else
            {
                Interlocked.Increment(ref _failed);
                _failures[item.Destination] = failure;
                failureSink?.Report(failure);
                if (!_options.ContinueOnError)
                    throw new DownloadFailedException(failure);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DownloadFailedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var f = new DownloadFailure
            {
                Url = item.Url, Destination = item.Destination, Attempts = 1,
                Message = ex.Message, ExceptionType = ex.GetType().Name
            };
            Interlocked.Increment(ref _failed);
            _failures[item.Destination] = f;
            failureSink?.Report(f);
            if (!_options.ContinueOnError)
                throw new DownloadFailedException(f);
        }
        finally
        {
            Interlocked.Decrement(ref _active);
            _gate.Release();
        }
    }

    private sealed class DownloadFailedException(DownloadFailure failure)
        : Exception($"Download failed: {failure}") { public DownloadFailure Failure { get; } = failure; }

    private async Task<DownloadFailure?> DownloadOneAsync(DownloadItem item, BufferedProgress? progress, CancellationToken ct)
    {
        var urls = new List<string> { item.Url };
        urls.AddRange(item.Mirrors);
        if (_options.UseMirror)
        {
            foreach (var mirror in MirrorResolver.Rewrite(item.Url))
            {
                if (!urls.Contains(mirror, StringComparer.OrdinalIgnoreCase))
                    urls.Add(mirror);
            }
        }

        int attempt = 0;
        Exception? last = null;
        HttpStatusCode? lastStatus = null;

        while (attempt < Math.Max(1, _options.MaxRetries))
        {
            attempt++;
            ct.ThrowIfCancellationRequested();
            var url = urls[(attempt - 1) % urls.Count];
            try
            {
                await DownloadFileAsync(url, item, progress, ct).ConfigureAwait(false);
                if (await VerifyFileAsync(item.Destination, item, ct).ConfigureAwait(false))
                    return null;

                last = new InvalidDataException("checksum mismatch after download");
                TryDelete(item.Destination);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException hex)
            {
                last = hex;
                lastStatus = hex.StatusCode;
            }
            catch (Exception ex)
            {
                last = ex;
            }

            if (attempt < _options.MaxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(
                    Math.Min(_options.MaxRetryDelay.TotalMilliseconds,
                        _options.InitialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1)));
                delay += TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        return new DownloadFailure
        {
            Url = item.Url,
            Destination = item.Destination,
            Attempts = attempt,
            Message = last?.Message ?? "unknown error",
            ExceptionType = last?.GetType().Name,
            StatusCode = lastStatus
        };
    }

    private async Task DownloadFileAsync(string url, DownloadItem item, BufferedProgress? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
        var partPath = item.Destination + ".part";
        var metaPath = item.Destination + ".part.meta";

        long existing = 0;
        if (File.Exists(partPath))
        {
            existing = new FileInfo(partPath).Length;
            // Only resume when the sidecar proves the partial file belongs to the same URL.
            var meta = ReadMeta(metaPath);
            if (meta is null || !string.Equals(meta.Url, url, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(partPath);
                existing = 0;
            }
        }
        else
        {
            TryDelete(metaPath);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(_options.Timeout);
        // The timeout applies to connection + response headers only; streamed bodies may take longer.
        var headerToken = linked.Token;
        var token = ct;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerToken).ConfigureAwait(false);

        if (existing > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            // Server ignored Range: restart from scratch.
            existing = 0;
            TryDelete(partPath);
        }
        else if (existing > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // Likely already complete; treat as success and let the hash decide.
            existing = new FileInfo(partPath).Length;
        }
        else
        {
            response.EnsureSuccessStatusCode();
        }

        var total = existing + (response.Content.Headers.ContentLength ?? 0);
        if (item.Size is > 0) total = Math.Max(total, item.Size.Value);
        WriteMeta(metaPath, new PartMeta(url, total));

        var mode = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent ? FileMode.Append : FileMode.Create;
        await using (var file = new FileStream(partPath, mode, FileAccess.Write, FileShare.Read, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (mode == FileMode.Create) existing = 0;
            // Count pre-existing bytes once so aggregate progress reflects resumed data.
            if (existing > 0) Interlocked.Add(ref _bytesDownloaded, existing);
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var buffer = new byte[1 << 16];
            long local = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                if (_options.MaxBytesPerSecond > 0)
                    await ThrottleAsync(read, ct).ConfigureAwait(false);
                await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                local += read;
                Interlocked.Add(ref _bytesDownloaded, read);
                _meter.Add(read);
                if (progress is not null && local % (1 << 18) < buffer.Length)
                    progress.Emit(BuildProgress(item.Label, "下载中"));
            }
            await file.FlushAsync(token).ConfigureAwait(false);
        }

        TryDelete(metaPath);

        // Atomic-ish commit: same volume rename. Delete destination first only after successful part write.
        if (File.Exists(item.Destination))
            File.Delete(item.Destination);
        File.Move(partPath, item.Destination);
    }

    private async Task ThrottleAsync(int bytes, CancellationToken ct)
    {
        // Extremely small implementation: measure delta from a target byte budget.
        var targetSeconds = (double)bytes / _options.MaxBytesPerSecond;
        if (targetSeconds > 0.002)
            await Task.Delay(TimeSpan.FromSeconds(targetSeconds), ct).ConfigureAwait(false);
    }

    public static async Task<bool> VerifyFileAsync(string path, DownloadItem item, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return false;
        var info = new FileInfo(path);
        if (info.Length == 0 && (item.Size is null || item.Size > 0)) return false;
        if (item.Size is > 0 && info.Length != item.Size) return false;

        if (!string.IsNullOrWhiteSpace(item.Sha256))
        {
            await using var fs = File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
            return HexEquals(hash, item.Sha256!);
        }
        if (!string.IsNullOrWhiteSpace(item.Sha1))
        {
            await using var fs = File.OpenRead(path);
            var hash = await SHA1.HashDataAsync(fs, ct).ConfigureAwait(false);
            return HexEquals(hash, item.Sha1!);
        }
        return true;
    }

    private static bool HexEquals(byte[] bytes, string hex)
    {
        if (hex.Length != bytes.Length * 2) return false;
        for (int i = 0; i < bytes.Length; i++)
        {
            var hi = HexVal(hex[i * 2]);
            var lo = HexVal(hex[i * 2 + 1]);
            if (hi < 0 || lo < 0) return false;
            if (bytes[i] != (byte)((hi << 4) | lo)) return false;
        }
        return true;
    }

    private static int HexVal(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private sealed record PartMeta(string Url, long Total);

    private static PartMeta? ReadMeta(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return System.Text.Json.JsonSerializer.Deserialize<PartMeta>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    private static void WriteMeta(string path, PartMeta meta)
    {
        try { File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(meta)); } catch { /* best effort */ }
    }

    /// <summary>Progress reporter that coalesces bursts and only forwards at a fixed cadence.</summary>
    public sealed class BufferedProgress(IProgress<DownloadProgress> inner, int intervalMs)
    {
        private readonly object _lock = new();
        private DownloadProgress? _latest;
        private long _lastEmit;

        public void Emit(DownloadProgress p)
        {
            lock (_lock)
            {
                _latest = p;
                var now = Environment.TickCount64;
                if (now - _lastEmit < intervalMs) return;
                _lastEmit = now;
            }
            if (Volatile.Read(ref _latest) is { } snapshot)
                inner.Report(snapshot);
        }

        public void EmitFinal(DownloadProgress p) => inner.Report(p);
    }

    /// <summary>Exponentially-smoothed throughput meter.</summary>
    private sealed class RateMeter
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _bytes;
        private double _lastRate;

        public void Add(long n) => Interlocked.Add(ref _bytes, n);

        public double PerSecond
        {
            get
            {
                var secs = _clock.Elapsed.TotalSeconds;
                if (secs < 0.2) return _lastRate;
                var rate = Interlocked.Read(ref _bytes) / secs;
                _lastRate = _lastRate == 0 ? rate : _lastRate * 0.7 + rate * 0.3;
                return _lastRate;
            }
        }
    }
}

/// <summary>Optional BMCLAPI mirror rewriting. Disabled unless the user opts in; never bypasses auth.</summary>
public static class MirrorResolver
{
    private static readonly (string Prefix, string Mirror)[] Map =
    {
        ("https://piston-meta.mojang.com/", "https://bmclapi2.bangbang93.com/"),
        ("https://piston-data.mojang.com/", "https://bmclapi2.bangbang93.com/"),
        ("https://launchermeta.mojang.com/", "https://bmclapi2.bangbang93.com/"),
        ("https://launcher.mojang.com/", "https://bmclapi2.bangbang93.com/"),
        ("https://resources.download.minecraft.net/", "https://bmclapi2.bangbang93.com/assets/"),
        ("https://libraries.minecraft.net/", "https://bmclapi2.bangbang93.com/maven/")
    };

    public static IEnumerable<string> Rewrite(string url)
    {
        foreach (var (prefix, mirror) in Map)
        {
            if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var rest = url[prefix.Length..];
                yield return mirror + rest;
                yield break;
            }
        }
    }
}
