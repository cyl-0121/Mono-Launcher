using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MonoLauncher.Core.Java;

public sealed record JavaRuntime
{
    public required string ExecutablePath { get; init; }
    public string? Home { get; init; }
    public required int MajorVersion { get; init; }
    public string Version { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string Architecture { get; init; } = "";
    public bool Is64Bit { get; init; }
    public string? Source { get; init; }

    public string DisplayName =>
        $"Java {MajorVersion} ({(string.IsNullOrEmpty(Vendor) ? "unknown" : Vendor)})";
}

public sealed class JavaScanner
{
    private readonly List<JavaRuntime> _cache = new();
    private readonly object _lock = new();

    public IReadOnlyList<JavaRuntime> Cached
    {
        get { lock (_lock) return _cache.ToList(); }
    }

    /// <summary>Probes a java executable by running -XshowSettings:properties -version.</summary>
    public static async Task<JavaRuntime?> ProbeAsync(string exePath, CancellationToken ct = default)
    {
        if (!File.Exists(exePath)) return null;
        try
        {
            var psi = new ProcessStartInfo(exePath)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-XshowSettings:properties");
            psi.ArgumentList.Add("-version");

            using var p = Process.Start(psi);
            if (p is null) return null;
            var stderrTask = p.StandardError.ReadToEndAsync(ct);
            var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            var text = (await stdoutTask.ConfigureAwait(false)) + "\n" + (await stderrTask.ConfigureAwait(false));
            var home = MatchProperty(text, "java.home");
            var version = MatchProperty(text, "java.version") ?? MatchVersionLine(text);
            var vendor = MatchProperty(text, "java.vendor") ?? "";
            var arch = MatchProperty(text, "os.arch") ?? "";
            if (string.IsNullOrWhiteSpace(version)) return null;

            var major = ParseMajor(version);
            var is64 = arch.Contains("64", StringComparison.OrdinalIgnoreCase);
            return new JavaRuntime
            {
                ExecutablePath = Path.GetFullPath(exePath),
                Home = home,
                MajorVersion = major,
                Version = version,
                Vendor = vendor,
                Architecture = arch,
                Is64Bit = is64
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? MatchProperty(string text, string key)
    {
        var m = Regex.Match(text, $@"^\s*{Regex.Escape(key)}\s*=\s*(.+)$", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string? MatchVersionLine(string text)
    {
        var m = Regex.Match(text, @"(?:openjdk|java) version ""([^""]+)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Parses "21.0.7", "1.8.0_442", "17-ea" into a major number (21, 8, 17).</summary>
    public static int ParseMajor(string version)
    {
        var v = version.Trim();
        var m = Regex.Match(v, @"^(\d+)(?:\.(\d+))?");
        if (!m.Success) return 0;
        var first = int.Parse(m.Groups[1].Value);
        if (first == 1 && m.Groups[2].Success)
            return int.Parse(m.Groups[2].Value);
        return first;
    }

    /// <summary>Enumerates known installations: registry, PATH, common folders, launcher runtimes.</summary>
    public async Task<IReadOnlyList<JavaRuntime>> ScanAsync(AppPaths paths, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in RegistryCandidates())
            candidates.Add(p);

        foreach (var p in PathCandidates())
            candidates.Add(p);

        foreach (var p in CommonFolderCandidates(paths))
            candidates.Add(p);

        var found = new List<JavaRuntime>();
        int i = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"正在检测 {Path.GetFileName(Path.GetDirectoryName(candidate))} …");
            i++;
            var rt = await ProbeAsync(candidate, ct).ConfigureAwait(false);
            if (rt is not null && !found.Any(f => string.Equals(f.ExecutablePath, rt.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                found.Add(rt);
        }

        found.Sort((a, b) => a.MajorVersion != b.MajorVersion
            ? b.MajorVersion.CompareTo(a.MajorVersion)
            : string.Compare(a.ExecutablePath, b.ExecutablePath, StringComparison.OrdinalIgnoreCase));

        lock (_lock)
        {
            _cache.Clear();
            _cache.AddRange(found);
        }
        progress?.Report($"检测到 {found.Count} 个 Java 运行时");
        return found;
    }

    private static IEnumerable<string> RegistryCandidates()
    {
        if (!OperatingSystem.IsWindows()) yield break;
        var keys = new (RegistryHive Hive, string Path)[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\JavaSoft\JDK"),
            (RegistryHive.LocalMachine, @"SOFTWARE\JavaSoft\JRE"),
            (RegistryHive.LocalMachine, @"SOFTWARE\JavaSoft\Java Development Kit"),
            (RegistryHive.LocalMachine, @"SOFTWARE\JavaSoft\Java Runtime Environment"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Eclipse Adoptium\JDK"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Eclipse Foundation\JDK"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\JDK"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Azul Systems\Zulu"),
            (RegistryHive.LocalMachine, @"SOFTWARE\BellSoft\LibericaJDK"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Amazon Corretto"),
            (RegistryHive.CurrentUser, @"SOFTWARE\JavaSoft\JDK"),
            (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\JDK")
        };

        foreach (var (hive, keyPath) in keys)
        {
            using var baseKey = TryOpen(hive, keyPath);
            if (baseKey is null) continue;
            foreach (var sub in SafeSubKeys(baseKey))
            {
                using var subKey = TryOpen(hive, keyPath + "\\" + sub);
                var home = subKey?.GetValue("JavaHome") as string ?? subKey?.GetValue("Path") as string;
                if (!string.IsNullOrWhiteSpace(home))
                {
                    var exe = Path.Combine(home, "bin", "java.exe");
                    if (File.Exists(exe)) yield return exe;
                }
            }
        }

        // Uninstall entries occasionally carry JavaHome.
        using var uninstall = TryOpen(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        if (uninstall is not null)
        {
            foreach (var sub in SafeSubKeys(uninstall))
            {
                using var k = TryOpen(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + sub);
                if (k is null) continue;
                var name = k.GetValue("DisplayName") as string;
                if (name is null || !name.Contains("Java", StringComparison.OrdinalIgnoreCase)) continue;
                var home = k.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(home)) continue;
                var exe = Path.Combine(home, "bin", "java.exe");
                if (File.Exists(exe)) yield return exe;
            }
        }
    }

    private static RegistryKey? TryOpen(RegistryHive hive, string path)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            return baseKey.OpenSubKey(path);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeSubKeys(RegistryKey key)
    {
        try { return key.GetSubKeyNames(); } catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> PathCandidates()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string exe;
            try { exe = Path.Combine(dir.Trim('"'), "java.exe"); } catch { continue; }
            if (File.Exists(exe)) yield return exe;
        }
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            var exe = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(exe)) yield return exe;
        }
    }

    private static IEnumerable<string> CommonFolderCandidates(AppPaths paths)
    {
        yield return Path.Combine(paths.JavaDir, "bin", "java.exe");

        var roots = new List<string>
        {
            paths.JavaDir, // managed runtimes land in data/java/java-<major>/
            @"C:\Program Files\Java",
            @"C:\Program Files (x86)\Java",
            @"C:\Program Files\Eclipse Adoptium",
            @"C:\Program Files\Microsoft",
            @"C:\Program Files\Zulu",
            @"C:\Program Files\BellSoft",
            @"C:\Program Files\Amazon Corretto",
            @"C:\Program Files\Semeru",
            @"C:\Program Files\JetBrains",
            @"C:\Program Files (x86)\Eclipse Adoptium",
            @"C:\Program Files (x86)\Microsoft"
        };
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            roots.Add(Path.Combine(appData, ".minecraft", "runtime"));
            roots.Add(Path.Combine(appData, ".minecraft", "jre"));
        }
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            // PCL2 / HMCL bundled runtimes
            roots.Add(Path.Combine(localAppData, "Programs", "PCL"));
            roots.Add(Path.Combine(localAppData, "Programs", "HMCL"));
        }

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            // 1) java.exe directly below root\bin
            var direct = Path.Combine(root, "bin", "java.exe");
            if (File.Exists(direct)) yield return direct;

            // 2) root\<jdk>\bin\java.exe and root\<jdk>\jre\bin\java.exe
            string[] children;
            try { children = Directory.GetDirectories(root); } catch { continue; }
            foreach (var child in children)
            {
                var exe = Path.Combine(child, "bin", "java.exe");
                if (File.Exists(exe)) { yield return exe; continue; }
                var jre = Path.Combine(child, "jre", "bin", "java.exe");
                if (File.Exists(jre)) yield return jre;
            }
        }
    }

    /// <summary>Picks the best runtime for a required Java major version.</summary>
    public static JavaRuntime? SelectBest(IEnumerable<JavaRuntime> runtimes, int requiredMajor, bool require64Bit = true)
    {
        var list = runtimes.Where(r => !require64Bit || r.Is64Bit).ToList();
        if (list.Count == 0) list = runtimes.ToList();
        return list
            .Where(r => r.MajorVersion >= requiredMajor)
            .OrderBy(r => r.MajorVersion)
            .ThenByDescending(r => r.Vendor.Contains("Adoptium") || r.Vendor.Contains("Microsoft") || r.Vendor.Contains("Oracle"))
            .FirstOrDefault()
            ?? list.OrderByDescending(r => r.MajorVersion).FirstOrDefault();
    }
}
