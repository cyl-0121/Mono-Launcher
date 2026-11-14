using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MonoLauncher.Core.Auth;
using MonoLauncher.Core.Java;
using MonoLauncher.Core.Models;
using MonoLauncher.Core.Versions;

namespace MonoLauncher.Core.Launch;

public sealed class MissingFileException(IReadOnlyList<string> missing)
    : Exception($"启动所需文件缺失（{missing.Count} 个）：\n" + string.Join("\n", missing.Take(12)))
{
    public IReadOnlyList<string> Missing { get; } = missing;
}

public sealed class LaunchPlanResult
{
    public required LaunchPlan Plan { get; init; }
    public required IReadOnlyList<string> MissingFiles { get; init; }
    public bool Ready => MissingFiles.Count == 0;
}

public sealed class LaunchService
{
    private readonly AppPaths _paths;

    public LaunchService(AppPaths paths) => _paths = paths;

    /// <summary>Builds the classpath from the resolved version (honouring rules) plus the client jar.</summary>
    public string BuildClasspath(VersionInfo resolved)
    {
        var entries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lib in resolved.Libraries)
        {
            if (!VersionInstaller.IsAllowed(lib, new RuleContext())) continue;
            // Pure-native entries only carry classifiers and must not go on the classpath.
            if (lib.Downloads?.Artifact is null && lib.Natives is not null) continue;
            var p = _paths.LibraryPath(lib.Name);
            if (seen.Add(p)) entries.Add(p);
        }

        // Forge/NeoForge replace the vanilla jar with a patched client jar that modlauncher finds
        // through -DlibraryDirectory. Adding the vanilla jar as well makes both end up as modules
        // and Java fails with "Modules minecraft and _1._21._1 export package ...".
        var bootstrapped = resolved.MainClass?.Contains("BootstrapLauncher", StringComparison.OrdinalIgnoreCase) == true
                           || resolved.Libraries.Any(l =>
                               l.Name.Contains("neoforge", StringComparison.OrdinalIgnoreCase) &&
                               l.Name.Contains("client", StringComparison.OrdinalIgnoreCase));

        if (!bootstrapped)
        {
            var jarId = string.IsNullOrWhiteSpace(resolved.Jar) ? resolved.Id : resolved.Jar!;
            var jar = _paths.VersionJar(jarId);
            if (seen.Add(jar)) entries.Add(jar);
        }
        // For bootstrapped loaders the patched client jar lives under libraries and is discovered
        // by modlauncher via -DlibraryDirectory, so it must not be added to the classpath here.

        return string.Join(Path.PathSeparator, entries);
    }

    public LaunchPlanResult CreatePlan(
        VersionInfo resolved,
        MinecraftAccount account,
        JavaRuntime java,
        string gameDirectory,
        InstanceLaunchSettings settings)
    {
        Directory.CreateDirectory(gameDirectory);
        var classpath = BuildClasspath(resolved);
        var natives = Path.Combine(_paths.VersionDir(resolved.Id), "natives");
        var log4j = Path.Combine(_paths.VersionDir(resolved.Id), "log4j.xml");

        var options = new LaunchOptions
        {
            Version = resolved,
            Account = account,
            GameDirectory = gameDirectory,
            AssetsDirectory = _paths.AssetsDir,
            LibrariesDirectory = _paths.LibrariesDir,
            NativesDirectory = natives,
            Classpath = classpath,
            JavaPath = java.ExecutablePath,
            MinMemoryMb = settings.MinMemoryMb,
            MaxMemoryMb = settings.MaxMemoryMb,
            Width = settings.Width,
            Height = settings.Height,
            Fullscreen = settings.Fullscreen,
            DemoMode = settings.DemoMode,
            ServerAddress = settings.ServerAddress,
            ServerPort = settings.ServerPort,
            ExtraJvmArguments = settings.ExtraJvmArguments,
            ExtraGameArguments = settings.ExtraGameArguments,
            Log4jConfigPath = File.Exists(log4j) ? log4j : null,
            VersionType = resolved.Type,
            LauncherName = "MonoLauncher",
            LauncherVersion = "0.9.0"
        };

        var missing = new List<string>();
        foreach (var entry in classpath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!File.Exists(entry)) missing.Add(entry);
        }
        if (!File.Exists(java.ExecutablePath)) missing.Add(java.ExecutablePath);
        if (resolved.AssetIndex is { } idx && !File.Exists(_paths.AssetIndexFile(idx.Id)))
            missing.Add(_paths.AssetIndexFile(idx.Id));

        return new LaunchPlanResult
        {
            Plan = LaunchArguments.Build(options),
            MissingFiles = missing
        };
    }
}

public sealed class InstanceLaunchSettings
{
    public int MinMemoryMb { get; set; } = 512;
    public int MaxMemoryMb { get; set; } = 4096;
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool Fullscreen { get; set; }
    public bool DemoMode { get; set; }
    public string? ServerAddress { get; set; }
    public int? ServerPort { get; set; }
    public List<string> ExtraJvmArguments { get; set; } = new();
    public List<string> ExtraGameArguments { get; set; } = new();
}

public sealed class GameSession : IDisposable
{
    private readonly Process _process;
    private readonly StreamWriter? _log;
    private readonly StringBuilder _tail = new();
    private readonly object _lock = new();
    private readonly List<string> _crashLines = new();

    public int ProcessId => _process.Id;
    public string LogPath { get; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public event Action<string>? OutputReceived;

    internal GameSession(Process process, string logPath, StreamWriter? log)
    {
        _process = process;
        LogPath = logPath;
        _log = log;
    }

    public string RecentOutput
    {
        get { lock (_lock) return _tail.ToString(); }
    }

    public bool HasExited
    {
        get { try { return _process.HasExited; } catch { return true; } }
    }

    public int? ExitCode
    {
        get { try { return _process.HasExited ? _process.ExitCode : null; } catch { return null; } }
    }

    internal void AppendLine(string line)
    {
        lock (_lock)
        {
            _tail.AppendLine(line);
            if (_tail.Length > 64_000) _tail.Remove(0, _tail.Length - 48_000);
        }
        try { _log?.WriteLine(line); } catch { }
        OutputReceived?.Invoke(line);
    }

    public Task<int> WaitForExitAsync(CancellationToken ct = default) =>
        _process.WaitForExitAsync(ct).ContinueWith(_ => _process.ExitCode, TaskScheduler.Default);

    public void Kill()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
    }

    public void Dispose()
    {
        try { _log?.Flush(); _log?.Dispose(); } catch { }
        _process.Dispose();
    }
}

public sealed class GameLauncher
{
    private readonly AppPaths _paths;

    public GameLauncher(AppPaths paths) => _paths = paths;

    public GameSession Launch(LaunchPlan plan)
    {
        if (!File.Exists(plan.JavaPath))
            throw new FileNotFoundException("Java 可执行文件不存在", plan.JavaPath);

        Directory.CreateDirectory(plan.WorkingDirectory);
        Directory.CreateDirectory(_paths.LogsDir);
        var logPath = Path.Combine(_paths.LogsDir,
            $"game-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var log = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };

        var psi = new ProcessStartInfo(plan.JavaPath)
        {
            WorkingDirectory = plan.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in plan.Arguments)
            psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var session = new GameSession(process, logPath, log);
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) session.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) session.AppendLine(e.Data); };

        log.WriteLine($"# MonoLauncher launch {DateTimeOffset.Now:O}");
        log.WriteLine($"# java: {plan.JavaPath}");
        log.WriteLine($"# workdir: {plan.WorkingDirectory}");
        log.WriteLine($"# command: {LaunchArguments.RedactCommandLine(plan.CommandLinePreview)}");
        log.WriteLine();

        if (!process.Start())
            throw new InvalidOperationException("无法启动 Java 进程");

        session.AppendLine($"[MonoLauncher] 已启动 Minecraft (pid {process.Id})");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return session;
    }

    /// <summary>Finds the newest crash report produced after the session started.</summary>
    public static string? FindCrashReport(string gameDirectory, DateTimeOffset since)
    {
        var dir = Path.Combine(gameDirectory, "crash-reports");
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "*.txt")
            .Select(f => new FileInfo(f))
            .Where(f => f.LastWriteTimeUtc >= since.UtcDateTime.AddSeconds(-5))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .FirstOrDefault();
    }

    public static string SummariseCrash(string reportPath, int maxLines = 40)
    {
        try
        {
            var lines = File.ReadLines(reportPath).Take(maxLines * 3).ToList();
            var sb = new StringBuilder();
            var descriptionSeen = false;
            foreach (var line in lines)
            {
                sb.AppendLine(line);
                if (line.StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
                    descriptionSeen = true;
                if (descriptionSeen && sb.ToString().Split('\n').Length > maxLines) break;
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"无法读取崩溃报告: {ex.Message}";
        }
    }
}
