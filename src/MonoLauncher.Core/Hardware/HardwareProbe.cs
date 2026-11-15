using Microsoft.Win32;
using MonoLauncher.Core.Java;

namespace MonoLauncher.Core.Hardware;

public enum LocalAiVerdict
{
    /// <summary>Too little memory; a local model cannot be deployed.</summary>
    Unsupported,
    /// <summary>Enough for the smallest model (1.5B Q4) on CPU.</summary>
    Low,
    /// <summary>Comfortable for 3B; 7B works but slowly.</summary>
    Medium,
    /// <summary>Can run 7B comfortably.</summary>
    High
}

public sealed record HardwareReport
{
    public required int CpuCores { get; init; }
    public required string CpuName { get; init; }
    public required int TotalRamMb { get; init; }
    public required int FreeRamMb { get; init; }
    public required IReadOnlyList<string> Gpus { get; init; }
    public required LocalAiVerdict Verdict { get; init; }
    public required bool CanRunLocalAi { get; init; }
    public required string Summary { get; init; }

    public string CpuText => string.IsNullOrWhiteSpace(CpuName) ? $"{CpuCores} 线程" : $"{CpuName}（{CpuCores} 线程）";
    public string RamText => $"{TotalRamMb / 1024.0:0.#} GB（当前可用 {FreeRamMb / 1024.0:0.#} GB）";
    public string GpuText => Gpus.Count == 0 ? "未检测到独立显卡（将使用 CPU 推理）" : string.Join(" / ", Gpus);
    public string VerdictText => Verdict switch
    {
        LocalAiVerdict.High => "本机已满足，推荐 7B 模型",
        LocalAiVerdict.Medium => "本机已满足，推荐 3B 模型",
        LocalAiVerdict.Low => "本机勉强够用，建议 1.5B 小模型",
        _ => "性能过低，无法部署本地 AI"
    };
}

/// <summary>
/// Reads real hardware facts so the launcher can decide whether a local LLM is viable
/// and which model tier to recommend. No external tooling required (registry + runtime).
/// </summary>
public static class HardwareProbe
{
    public const int MinTotalRamMb = 4096;

    public static HardwareReport Probe()
    {
        var (total, free) = MemoryAdvisor.GetMachineMemory();
        var cores = Math.Max(1, Environment.ProcessorCount);
        var cpu = ReadCpuName();
        var gpus = ReadGpuNames();

        var verdict = total switch
        {
            >= 16384 => LocalAiVerdict.High,
            >= 8192 => LocalAiVerdict.Medium,
            >= MinTotalRamMb => LocalAiVerdict.Low,
            _ => LocalAiVerdict.Unsupported
        };

        var summary = verdict == LocalAiVerdict.Unsupported
            ? $"内存仅 {total / 1024.0:0.#} GB，低于 {MinTotalRamMb / 1024} GB 的最低要求，无法部署本地 AI。"
            : $"内存 {total / 1024.0:0.#} GB、{cores} 线程，可以部署本地 AI（当前发布版为 CPU 推理）。";

        return new HardwareReport
        {
            CpuCores = cores,
            CpuName = cpu,
            TotalRamMb = total,
            FreeRamMb = free,
            Gpus = gpus,
            Verdict = verdict,
            CanRunLocalAi = verdict != LocalAiVerdict.Unsupported && Environment.Is64BitOperatingSystem,
            Summary = summary
        };
    }

    private static string ReadCpuName()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return "";
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
        }
        catch { return ""; }
    }

    /// <summary>Enumerates display adapter names from the display class registry key.</summary>
    private static IReadOnlyList<string> ReadGpuNames()
    {
        var result = new List<string>();
        if (!OperatingSystem.IsWindows()) return result;
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls is null) return result;
            foreach (var sub in cls.GetSubKeyNames())
            {
                if (!sub.StartsWith("0", StringComparison.Ordinal)) continue; // 0000, 0001...
                using var dev = cls.OpenSubKey(sub);
                var name = dev?.GetValue("DriverDesc") as string;
                if (!string.IsNullOrWhiteSpace(name) &&
                    !result.Contains(name!, StringComparer.OrdinalIgnoreCase))
                    result.Add(name!.Trim());
            }
        }
        catch { /* non-fatal */ }
        return result;
    }
}
