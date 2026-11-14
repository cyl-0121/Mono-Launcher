using System.Text.Json;

namespace MonoLauncher.Core.Config;

public sealed class AppSettings
{
    // Java
    public string? JavaPath { get; set; }
    public int AutoJavaMajor { get; set; } = 21;
    public bool NotifyJavaMissing { get; set; } = true;

    // Memory
    public int MinMemoryMb { get; set; } = 512;
    public int MaxMemoryMb { get; set; } = 4096;

    // Download
    public int DownloadConcurrency { get; set; } = 8;
    public int DownloadRetries { get; set; } = 3;
    public bool UseMirror { get; set; }
    public string? ProxyUrl { get; set; }
    public long MaxBytesPerSecond { get; set; }
    public string UserAgent { get; set; } = "MonoLauncher/1.0 (launcher)";

    // UI
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "zh-CN";
    public string PerformanceTier { get; set; } = "Balanced"; // HighPerformance / Balanced / LowPower / Off
    public bool AnimationsEnabled { get; set; } = true;
    public bool PointerLighting { get; set; } = true;
    public bool AutoDowngrade { get; set; } = true;
    public int AutoDowngradeFpsThreshold { get; set; } = 32;
    public double UiScale { get; set; } = 1.0;

    // Updates / announcement
    /// <summary>GitHub "owner/repo" used for the update announcement. Empty disables fetching.</summary>
    public string UpdateRepository { get; set; } = "cyl-0121/Mono-Launcher";
    public bool CheckUpdateOnStartup { get; set; } = true;

    /// <summary>Optional mirror site (e.g. https://launcher.example.com). Opened instead of GitHub when set.</summary>
    public string DownloadSiteUrl { get; set; } = "https://ada1f3c4ba9a13ac3.app.workbuddy.host/?sharecode=2TEW6KGZWT";

    // AI
    public string DefaultProviderId { get; set; } = "deepseek";
    public string? DefaultModel { get; set; }
    public int AiCloudConcurrency { get; set; } = 2;
    public int AiStreamFlushMs { get; set; } = 40;

    // Local model
    public string? LocalModelPath { get; set; }
    public int LocalGpuLayers { get; set; } = 0;
    public int LocalContextSize { get; set; } = 4096;
    public int LocalThreads { get; set; } = 0;
    public int LocalBatchSize { get; set; } = 512;

    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.Now;
}

public sealed class SettingsService
{
    private readonly AppPaths _paths;
    private readonly object _lock = new();
    private AppSettings _current = new();

    public SettingsService(AppPaths paths)
    {
        _paths = paths;
        Load();
    }

    public AppSettings Current
    {
        get { lock (_lock) return _current; }
    }

    public event Action<AppSettings>? Changed;

    public void Load()
    {
        try
        {
            if (File.Exists(_paths.SettingsFile))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_paths.SettingsFile));
                if (settings is not null) _current = settings;
            }
        }
        catch { _current = new AppSettings(); }
    }

    public void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(_paths.ConfigDir);
            _current.LastUpdated = DateTimeOffset.Now;
            var json = JsonSerializer.Serialize(_current, Models.JsonDefaults.Options);
            var tmp = _paths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _paths.SettingsFile, overwrite: true);
            Changed?.Invoke(_current);
        }
    }

    public void Update(Action<AppSettings> mutate)
    {
        lock (_lock) mutate(_current);
        Save();
    }
}
