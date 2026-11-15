using Microsoft.Win32;
using MonoLauncher.Core.Java;

namespace MonoLauncher.Core.Hardware;

public enum LocalAiVerdict
{
    /// <summary>Too little memory; a local model cannot be deployed.</summary>
    Unsupported,
    /// <summary>Enough for the smallest model (1.5B Q4).</summary>
    Low,
    /// <summary>Comfortable for 3B; 7B works.</summary>
    Medium,
    /// <summary>Can run 7B comfortably, 14B with a big GPU.</summary>
    High
}

public sealed record HardwareReport
{
    public required int CpuCores { get; init; }
    public required string CpuName { get; init; }
    public required int TotalRamMb { get; init; }
    public required int FreeRamMb { get; init; }
    /// <summary>Largest discrete VRAM found, in MB. 0 means unknown / integrated-shared.</summary>
    public required int VramMb { get; init; }
    public required IReadOnlyList<string> Gpus { get; init; }
    public required LocalAiVerdict Verdict { get; init; }
    public required bool CanRunLocalAi { get; init; }
    public required string Summary { get; init; }

    public string CpuText => string.IsNullOrWhiteSpace(CpuName) ? $"{CpuCores} 线程" : $"{CpuName}（{CpuCores} 线程）";
    public string RamText => $"{TotalRamMb / 1024.0:0.#} GB（当前可用 {FreeRamMb / 1024.0:0.#} GB）";
    public string VramText => VramMb >= 512 ? $"{VramMb / 1024.0:0.#} GB" : "未检测到独立显存（核显共享内存）";
    public string GpuText => Gpus.Count == 0 ? "未检测到独立显卡（将使用 CPU 推理）" : string.Join(" / ", Gpus);

    /// <summary>
    /// Model sizing budget: a discrete GPU's VRAM is the real limit when present (the model is
    /// offloaded there), otherwise system RAM. Integrated GPUs share RAM, so we ignore small VRAM.
    /// </summary>
    public int EffectiveMemoryMb => VramMb >= 4096 ? Math.Min(TotalRamMb, VramMb) : TotalRamMb;

    public string VerdictText => Verdict switch
    {
        LocalAiVerdict.High => "本机已满足，可跑 7B，14B 也可尝试",
        LocalAiVerdict.Medium => "本机已满足，推荐 3B / 7B",
        LocalAiVerdict.Low => "本机勉强够用，建议 1.5B 小模型",
        _ => "性能过低，无法部署本地 AI"
    };
}

/// <summary>
/// Reads real hardware facts so the launcher can decide whether a local LLM is viable,
/// which model tier fits, and whether GPU offload is realistic. No external tooling needed.
/// </summary>
public static class HardwareProbe
{
    public const int MinTotalRamMb = 4096;

    public static HardwareReport Probe()
    {
        var (total, free) = MemoryAdvisor.GetMachineMemory();
        var cores = Math.Max(1, Environment.ProcessorCount);
        var cpu = ReadCpuName();
        var (gpus, vram) = ReadGpus();

        var budget = vram >= 4096 ? Math.Min(total, vram) : total;
        var verdict = budget switch
        {
            >= 12288 => LocalAiVerdict.High,
            >= 8192 => LocalAiVerdict.Medium,
            >= MinTotalRamMb => LocalAiVerdict.Low,
            _ => LocalAiVerdict.Unsupported
        };

        var gpuNote = vram >= 4096
            ? $"显存 {vram / 1024.0:0.#} GB，模型将全部放到显卡"
            : $"内存 {total / 1024.0:0.#} GB";

        var summary = verdict == LocalAiVerdict.Unsupported
            ? $"可用资源仅 {budget / 1024.0:0.#} GB，低于 {MinTotalRamMb / 1024} GB 的最低要求，无法部署本地 AI。"
            : $"{gpuNote}、{cores} 线程，可以部署本地 AI（CPU + Vulkan 双后端）。";

        return new HardwareReport
        {
            CpuCores = cores,
            CpuName = cpu,
            TotalRamMb = total,
            FreeRamMb = free,
            VramMb = vram,
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

    /// <summary>Reads display adapter names and the largest reported VRAM (qwMemorySize).</summary>
    private static (IReadOnlyList<string> Names, int VramMb) ReadGpus()
    {
        var names = new List<string>();
        long bestVram = 0;
        if (!OperatingSystem.IsWindows()) return (names, 0);
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls is null) return (names, 0);
            foreach (var sub in cls.GetSubKeyNames())
            {
                if (!sub.StartsWith("0", StringComparison.Ordinal)) continue; // 0000, 0001, ...
                using var dev = cls.OpenSubKey(sub);
                if (dev is null) continue;
                var name = dev.GetValue("DriverDesc") as string;
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name!, StringComparer.OrdinalIgnoreCase))
                    names.Add(name!.Trim());

                long vram = 0;
                var raw = dev.GetValue("HardwareInformation.qwMemorySize");
                if (raw is byte[] bytes && bytes.Length >= 8) vram = BitConverter.ToInt64(bytes, 0);
                else if (raw is long l) vram = l;
                else if (raw is int i) vram = (uint)i;
                if (vram > bestVram) bestVram = vram;
            }
        }
        catch { /* non-fatal */ }

        // Guard against bogus shared-memory values (some drivers report > 1 TB on iGPUs).
        if (bestVram > 256L * 1024 * 1024 * 1024) bestVram = 0;
        return (names, (int)Math.Max(0, bestVram / 1024 / 1024));
    }
}
