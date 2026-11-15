using System.Runtime.InteropServices;
using MonoLauncher.Core.Launch;

namespace MonoLauncher.Core.Java;

/// <summary>Recommended heap sizing for one machine.</summary>
public sealed record MemoryPlan(int MinMb, int MaxMb, int TotalMb, int FreeMb, string Note)
{
    public string TotalText => $"{TotalMb / 1024.0:0.0} GB";
    public string FreeText => $"{FreeMb / 1024.0:0.0} GB";
    public string MaxText => MaxMb >= 1024 ? $"{MaxMb / 1024.0:0.#} GB" : $"{MaxMb} MB";
}

/// <summary>
/// PCL-style memory advice: read the real machine memory, suggest a heap size that leaves
/// room for the OS, and apply Aikar-style G1 tuning flags (the same set PCL2/HMCL users enable).
/// </summary>
public static class MemoryAdvisor
{
    /// <summary>Reads total/free physical memory on Windows (falls back to GC info).</summary>
    public static (int TotalMb, int FreeMb) GetMachineMemory()
    {
        long totalBytes = 0, freeBytes = 0;
        if (OperatingSystem.IsWindows() && TryGetGlobalMemory(out var total, out var free))
        {
            totalBytes = total;
            freeBytes = free;
        }
        else
        {
            totalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            freeBytes = totalBytes / 2; // no cheap portable free-memory probe; assume half free
        }
        return (Math.Max(0, (int)(totalBytes / 1024 / 1024)), Math.Max(0, (int)(freeBytes / 1024 / 1024)));
    }

    private static bool TryGetGlobalMemory(out long totalBytes, out long freeBytes)
    {
        totalBytes = freeBytes = 0;
        try
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(status)) return false;
            totalBytes = (long)status.ullTotalPhys;
            freeBytes = (long)status.ullAvailPhys;
            return true;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

    /// <summary>
    /// Standard plan: game heap grows with the machine but always leaves several GB for the OS.
    /// <paramref name="aggressive"/> uses up to 3/4 of total memory (for heavy modpacks).
    /// </summary>
    public static MemoryPlan Recommend(bool aggressive = false)
    {
        var (total, free) = GetMachineMemory();
        int max = aggressive
            ? Math.Clamp(total * 3 / 4, 1024, 16384)
            : total switch
            {
                <= 2048 => 1024,
                <= 4096 => 2048,
                <= 8192 => 4096,
                <= 16384 => 6144,
                _ => 8192
            };
        // Never promise more than the machine can spare right now.
        if (free > 0 && max > free + 1024) max = Math.Max(1024, free + 1024 - free % 512);
        max = Math.Min(max, Math.Max(1024, total - 2048));
        var min = Math.Clamp(max / 4, 256, 2048);
        var note = aggressive
            ? $"性能档：按总内存 {total / 1024.0:0.#} GB 的 3/4 分配，适合大型整合包"
            : $"标准档：按总内存 {total / 1024.0:0.#} GB 分配，同时为系统保留足够空间";
        return new MemoryPlan(min, max, total, free, note);
    }

    /// <summary>Aikar-style G1 tuning (safe on Java 8+), the flag set PCL2/HMCL users enable.</summary>
    public static IReadOnlyList<string> JvmOptimizationFlags => new[]
    {
        "-XX:+UseG1GC",
        "-XX:+ParallelRefProcEnabled",
        "-XX:MaxGCPauseMillis=200",
        "-XX:+UnlockExperimentalVMOptions",
        "-XX:+DisableExplicitGC",
        "-XX:G1NewSizePercent=30",
        "-XX:G1MaxNewSizePercent=40",
        "-XX:G1HeapRegionSize=8M",
        "-XX:G1ReservePercent=20",
        "-XX:G1HeapWastePercent=5",
        "-XX:G1MixedGCCountTarget=4",
        "-XX:InitiatingHeapOccupancyPercent=15",
        "-XX:G1MixedGCLiveThresholdPercent=90",
        "-XX:G1RSetUpdatingPauseTimePercent=5",
        "-XX:SurvivorRatio=32",
        "-XX:+PerfDisableSharedMem",
        "-XX:MaxTenuringThreshold=1",
        "-XX:+UseStringDeduplication"
    };

    /// <summary>Flags managed by the advisor; applying again removes these before re-adding, so it never duplicates.</summary>
    public static bool IsManagedFlag(string arg) =>
        arg.StartsWith("-XX:", StringComparison.Ordinal) || arg.StartsWith("-Xms", StringComparison.Ordinal) || arg.StartsWith("-Xmx", StringComparison.Ordinal);

    /// <summary>Removes previously applied optimization flags from a custom argument list.</summary>
    public static List<string> StripManagedFlags(IEnumerable<string> args) =>
        args.Where(a => !IsManagedFlag(a)).ToList();

    /// <summary>Applies a memory plan to instance launch settings (idempotent).</summary>
    public static void ApplyTo(InstanceLaunchSettings settings, MemoryPlan plan)
    {
        settings.MinMemoryMb = plan.MinMb;
        settings.MaxMemoryMb = plan.MaxMb;
        settings.OptimizeJvmArguments = true;
    }
}
