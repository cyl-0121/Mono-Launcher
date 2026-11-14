using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonoLauncher.Core.Network;

public sealed class TerracottaProfile
{
    [JsonPropertyName("machine_id")] public string? MachineId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("vendor")] public string? Vendor { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }

    public string KindText => Kind switch
    {
        "HOST" => "房主",
        "LOCAL" => "本机",
        "GUEST" => "玩家",
        _ => Kind ?? ""
    };
}

public sealed class TerracottaState
{
    [JsonPropertyName("state")] public string State { get; set; } = "waiting";
    [JsonPropertyName("index")] public long Index { get; set; }
    [JsonPropertyName("room")] public string? Room { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("difficulty")] public string? Difficulty { get; set; }
    [JsonPropertyName("type")] public int? Type { get; set; }
    [JsonPropertyName("profile_index")] public int? ProfileIndex { get; set; }
    [JsonPropertyName("profiles")] public List<TerracottaProfile> Profiles { get; set; } = new();

    public bool IsHostReady => State == "host-ok";
    public bool IsGuestReady => State == "guest-ok";
    public bool IsException => State == "exception";

    public string DisplayText => State switch
    {
        "waiting" => "待机",
        "host-scanning" => "正在扫描本机 Minecraft 端口",
        "host-starting" => "正在创建房间",
        "host-ok" => "房间已就绪",
        "guest-connecting" => "正在连接房间",
        "guest-starting" => $"正在建立隧道（{Difficulty ?? "UNKNOWN"}）",
        "guest-ok" => "已加入房间",
        "exception" => Type switch
        {
            0 => "无法连接房主（房间已关闭或网络不稳定）",
            1 => "与房主的连接中断",
            2 => "加入方隧道崩溃",
            3 => "房主隧道崩溃",
            4 => "房主端 Minecraft 服务器失联",
            5 => "协议错误，请反馈给 Terracotta 开发者",
            _ => "发生异常"
        },
        _ => State
    };
}

/// <summary>
/// Drives the official, unmodified Terracotta binary through its local HTTP API.
/// Terracotta is AGPL-3.0 with an exception that permits this kind of process-IPC integration
/// as long as the binary is unmodified and the UI credits the project.
/// https://github.com/burningtnt/Terracotta
/// </summary>
public sealed class TerracottaService : IDisposable
{
    public const string LatestVersion = "0.4.2";
    private const string Repo = "burningtnt/Terracotta";

    private readonly AppPaths _paths;
    private readonly HttpClient _http;
    private Process? _process;
    private int _port;
    private readonly object _lock = new();

    public TerracottaService(AppPaths paths, HttpClient http)
    {
        _paths = paths;
        _http = http;
    }

    public string InstallDir => Path.Combine(_paths.DataDir, "tools", "terracotta", LatestVersion);
    public string BinaryPath => Path.Combine(InstallDir, $"terracotta-{LatestVersion}-windows-x86_64.exe");
    public int Port { get { lock (_lock) return _port; } }
    public bool IsStarted { get { lock (_lock) return _port > 0; } }

    private static string DownloadUrl(string version) =>
        $"https://github.com/{Repo}/releases/download/v{version}/terracotta-{version}-windows-x86_64-pkg.tar.gz";

    private static string MirrorUrl(string version) =>
        $"https://gitee.com/burningtnt/Terracotta/releases/download/v{version}/terracotta-{version}-windows-x86_64-pkg.tar.gz";

    /// <summary>Downloads and extracts the official binary if it is not present yet.</summary>
    public async Task<bool> EnsureBinaryAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (File.Exists(BinaryPath)) return true;
        Directory.CreateDirectory(InstallDir);
        progress?.Report("正在下载 Terracotta 联机组件…");

        var tgz = Path.Combine(InstallDir, "pkg.tar.gz");
        Exception? last = null;
        foreach (var url in new[] { DownloadUrl(LatestVersion), MirrorUrl(LatestVersion) })
        {
            try
            {
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var fs = File.Create(tgz);
                await response.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
                last = null;
                break;
            }
            catch (Exception ex)
            {
                last = ex;
                progress?.Report("下载源失败，尝试备用源…");
            }
        }

        if (last is not null)
        {
            progress?.Report("Terracotta 下载失败：" + last.Message);
            return false;
        }

        progress?.Report("正在解压…");
        try
        {
            await using var file = File.OpenRead(tgz);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);
            while (await reader.GetNextEntryAsync(cancellationToken: ct).ConfigureAwait(false) is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;
                var name = Path.GetFileName(entry.Name);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var dest = Path.Combine(InstallDir, name);
                if (entry.DataStream is not null)
                {
                    await using var output = File.Create(dest);
                    await entry.DataStream.CopyToAsync(output, ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            progress?.Report("解压失败：" + ex.Message);
            return false;
        }
        finally
        {
            try { File.Delete(tgz); } catch { }
        }

        if (!File.Exists(BinaryPath))
        {
            // Fall back to any terracotta exe present.
            var any = Directory.EnumerateFiles(InstallDir, "terracotta-*.exe").FirstOrDefault();
            if (any is not null)
            {
                progress?.Report("已解压：" + Path.GetFileName(any));
                return true;
            }
            progress?.Report("压缩包中没有找到 terracotta.exe");
            return false;
        }

        progress?.Report("Terracotta 已就绪");
        return true;
    }

    /// <summary>Starts the binary in --hmcl mode and reads the port file.</summary>
    public async Task<bool> StartAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_port > 0) return true;
        }

        if (!await EnsureBinaryAsync(progress, ct).ConfigureAwait(false))
            return false;

        var portFile = Path.Combine(Path.GetTempPath(), $"MonoLauncher-terracotta-{Environment.ProcessId}.json");
        try { File.Delete(portFile); } catch { }

        var exe = File.Exists(BinaryPath)
            ? BinaryPath
            : Directory.EnumerateFiles(InstallDir, "terracotta-*.exe").First();

        progress?.Report("正在启动 Terracotta…");
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = InstallDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("--hmcl");
        psi.ArgumentList.Add(portFile);

        var process = Process.Start(psi);
        if (process is null)
        {
            progress?.Report("无法启动 Terracotta");
            return false;
        }
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        lock (_lock) _process = process;

        // Wait for the port file (written atomically as <file>.tmp then renamed).
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(portFile))
            {
                try
                {
                    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(portFile, ct).ConfigureAwait(false));
                    if (doc.RootElement.TryGetProperty("port", out var p) && p.TryGetInt32(out var port) && port > 0)
                    {
                        lock (_lock) _port = port;
                        progress?.Report("Terracotta 已启动");
                        return true;
                    }
                }
                catch
                {
                    // file may be mid-rename; retry
                }
            }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        progress?.Report("Terracotta 启动超时");
        return false;
    }

    public async Task<TerracottaState?> GetStateAsync(CancellationToken ct = default)
    {
        var port = Port;
        if (port <= 0) return null;
        try
        {
            var json = await _http.GetStringAsync($"http://127.0.0.1:{port}/state", ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<TerracottaState>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> GetMetaAsync(CancellationToken ct = default)
    {
        var port = Port;
        if (port <= 0) return null;
        try
        {
            return await _http.GetStringAsync($"http://127.0.0.1:{port}/meta", ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public Task HostAsync(string? room, string? player, CancellationToken ct = default)
    {
        var query = $"?player={Uri.EscapeDataString(player ?? "")}";
        if (!string.IsNullOrWhiteSpace(room))
            query += $"&room={Uri.EscapeDataString(room.Trim())}";
        return GetAsync($"/state/scanning{query}", ct);
    }

    public Task GuestAsync(string room, string? player, CancellationToken ct = default)
    {
        var query = $"?room={Uri.EscapeDataString(room.Trim())}&player={Uri.EscapeDataString(player ?? "")}";
        return GetAsync($"/state/guesting{query}", ct);
    }

    public Task ResetAsync(CancellationToken ct = default) => GetAsync("/state/ide", ct);

    public async Task ShutdownAsync()
    {
        var port = Port;
        if (port > 0)
        {
            try { await GetAsync("/panic?peaceful=true", CancellationToken.None).ConfigureAwait(false); }
            catch { }
        }
        try
        {
            lock (_lock)
            {
                if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
                _process?.Dispose();
                _process = null;
                _port = 0;
            }
        }
        catch { }
    }

    private async Task GetAsync(string path, CancellationToken ct)
    {
        var port = Port;
        if (port <= 0) return;
        try
        {
            using var response = await _http.GetAsync($"http://127.0.0.1:{port}{path}",
                HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            // 4xx responses mean the request was rejected (e.g. not in waiting state); the UI
            // surfaces the resulting exception state through polling.
            _ = response.StatusCode;
        }
        catch
        {
            // fire-and-forget by design
        }
    }

    public void Dispose()
    {
        try { ShutdownAsync().GetAwaiter().GetResult(); } catch { }
    }
}
