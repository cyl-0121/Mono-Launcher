using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace GlassCraft.App.Services;

public enum GlassTier
{
    HighPerformance,
    Balanced,
    LowPower,
    Off
}

public static class PerformanceState
{
    public static GlassTier Tier { get; private set; } = GlassTier.Balanced;
    public static bool AnimationsEnabled { get; private set; } = true;
    public static bool PointerLighting { get; private set; } = true;
    public static double CurrentFps { get; private set; }
    public static event Action<GlassTier>? TierChanged;

    public static string TierDisplay => Tier switch
    {
        GlassTier.HighPerformance => "高性能（完整玻璃管线）",
        GlassTier.Balanced => "均衡（模糊 + 色调 + 边缘高光）",
        GlassTier.LowPower => "低功耗（静态半透明）",
        GlassTier.Off => "关闭（纯色）",
        _ => Tier.ToString()
    };

    public static void Apply(GlassTier tier, bool animations, bool pointerLighting)
    {
        bool changed = Tier != tier;
        Tier = tier;
        AnimationsEnabled = animations;
        PointerLighting = pointerLighting;
        if (changed) TierChanged?.Invoke(tier);
    }

    public static void ReportFps(double fps) => CurrentFps = fps;
}

/// <summary>Measures the real compositor frame rate using Avalonia's animation frame callback.</summary>
public sealed class FpsCounter
{
    private long _frames;
    private DateTime _lastSample = DateTime.UtcNow;
    private readonly TopLevel _topLevel;

    public FpsCounter(TopLevel topLevel) => _topLevel = topLevel;

    public void Start() => _topLevel.RequestAnimationFrame(OnFrame);

    private void OnFrame(TimeSpan timestamp)
    {
        _frames++;
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastSample).TotalSeconds;
        if (elapsed >= 0.5)
        {
            var fps = _frames / elapsed;
            PerformanceState.ReportFps(fps);
            _frames = 0;
            _lastSample = now;
            AutoDegrade.Evaluate(fps);
        }
        _topLevel.RequestAnimationFrame(OnFrame);
    }
}

/// <summary>Watches sustained low frame rates and logs/suggests downgrades.</summary>
public static class AutoDegrade
{
    private static readonly Queue<double> Samples = new();
    private static DateTime _lastDowngrade = DateTime.MinValue;

    public static bool Enabled { get; set; } = true;
    public static double Threshold { get; set; } = 32;
    public static event Action<string>? Recommendation;
    public static event Action<GlassTier>? AutoSwitched;

    public static void Evaluate(double fps)
    {
        if (!Enabled) return;
        Samples.Enqueue(fps);
        while (Samples.Count > 10) Samples.Dequeue();
        if (Samples.Count < 6) return;

        var avg = Samples.Average();
        if (avg >= Threshold) return;
        if ((DateTime.UtcNow - _lastDowngrade).TotalSeconds < 20) return;
        _lastDowngrade = DateTime.UtcNow;
        Samples.Clear();

        if (PerformanceState.Tier == GlassTier.Off) return;
        var next = PerformanceState.Tier switch
        {
            GlassTier.HighPerformance => GlassTier.Balanced,
            GlassTier.Balanced => GlassTier.LowPower,
            GlassTier.LowPower => GlassTier.Off,
            _ => GlassTier.Off
        };
        Recommendation?.Invoke($"检测到平均帧率 {avg:0} FPS 低于阈值 {Threshold:0}，建议切换到「{next}」档位");
        if (Enabled)
        {
            PerformanceState.Apply(next, PerformanceState.AnimationsEnabled, PerformanceState.PointerLighting);
            AutoSwitched?.Invoke(next);
        }
    }
}
