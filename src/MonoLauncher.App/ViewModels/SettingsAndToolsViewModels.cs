using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoLauncher.App.Services;
using MonoLauncher.Core.Logging;

namespace MonoLauncher.App.ViewModels;

public sealed partial class DownloadsViewModel : ObservableObject
{
    public DownloadCenter Downloads => AppServices.Downloads;

    [ObservableProperty] private string _recentText = "";

    public DownloadsViewModel()
    {
        AppServices.Downloads.PropertyChanged += (_, _) => RefreshSummary();
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var d = AppServices.Downloads;
        RecentText = d.IsActive
            ? $"{d.Stage} · {d.BytesText} · {d.SpeedText}"
            : d.Error is not null
                ? "上次任务失败：" + d.Error
                : "当前没有进行中的下载任务。";
    }

    [RelayCommand]
    private void OpenFolder() => AppServices.Paths.OpenInExplorer(AppServices.Paths.DownloadsDir);
}

public sealed partial class LogsViewModel : ObservableObject
{
    public ObservableCollection<LogEntry> Entries { get; } = new();

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _logFilePath = "";
    [ObservableProperty] private bool _autoScroll = true;

    public LogsViewModel()
    {
        LogFilePath = AppServices.Log.SessionFile;
        AppServices.Log.EntryWritten += OnEntry;
        Reload();
    }

    private void OnEntry(LogEntry entry)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > 1500) Entries.RemoveAt(0);
        });
    }

    public void Reload()
    {
        Entries.Clear();
        foreach (var e in AppServices.Log.Recent) Entries.Add(e);
    }

    [RelayCommand]
    private void Refresh() => Reload();

    [RelayCommand]
    private void OpenFolder() => AppServices.Paths.OpenInExplorer(AppServices.Paths.LogsDir);

    [RelayCommand]
    private void CopySelected()
    {
        LastExport = string.Join("\n", Entries.TakeLast(200)
            .Select(e => $"{e.Time:HH:mm:ss} [{e.Level}] {e.Category}: {e.Message}"));
    }

    [ObservableProperty] private string _lastExport = "";
}

public sealed partial class SettingsViewModel : ObservableObject
{
    public ObservableCollection<MonoLauncher.Core.Java.JavaRuntime> JavaRuntimes { get; } = new();

    [ObservableProperty] private string? _javaPath;
    [ObservableProperty] private int _minMemoryMb = 512;
    [ObservableProperty] private int _maxMemoryMb = 4096;
    [ObservableProperty] private int _downloadConcurrency = 8;
    [ObservableProperty] private string? _proxyUrl;
    [ObservableProperty] private bool _useMirror;
    [ObservableProperty] private bool _animationsEnabled = true;
    [ObservableProperty] private bool _pointerLighting = true;
    [ObservableProperty] private bool _autoDowngrade = true;
    [ObservableProperty] private int _aiStreamFlushMs = 40;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _dataDirectory = "";
    [ObservableProperty] private string _updateRepository = "";
    [ObservableProperty] private string _downloadSiteUrl = "";

    public SettingsViewModel()
    {
        var s = AppServices.Settings.Current;
        JavaPath = s.JavaPath;
        MinMemoryMb = s.MinMemoryMb;
        MaxMemoryMb = s.MaxMemoryMb;
        DownloadConcurrency = s.DownloadConcurrency;
        ProxyUrl = s.ProxyUrl;
        UseMirror = s.UseMirror;
        AnimationsEnabled = s.AnimationsEnabled;
        PointerLighting = s.PointerLighting;
        AutoDowngrade = s.AutoDowngrade;
        AiStreamFlushMs = s.AiStreamFlushMs;
        UpdateRepository = s.UpdateRepository;
        DownloadSiteUrl = s.DownloadSiteUrl;
        DataDirectory = AppServices.Paths.Root;
        foreach (var rt in AppServices.Java.Cached) JavaRuntimes.Add(rt);
    }

    [RelayCommand]
    private void Save()
    {
        AppServices.Settings.Update(s =>
        {
            s.JavaPath = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath;
            s.MinMemoryMb = Math.Max(256, MinMemoryMb);
            s.MaxMemoryMb = Math.Max(s.MinMemoryMb + 256, MaxMemoryMb);
            s.DownloadConcurrency = Math.Clamp(DownloadConcurrency, 1, 32);
            s.ProxyUrl = string.IsNullOrWhiteSpace(ProxyUrl) ? null : ProxyUrl;
            s.UseMirror = UseMirror;
            s.AnimationsEnabled = AnimationsEnabled;
            s.PointerLighting = PointerLighting;
            s.AutoDowngrade = AutoDowngrade;
            s.AiStreamFlushMs = Math.Clamp(AiStreamFlushMs, 10, 500);
            s.UpdateRepository = UpdateRepository.Trim();
            s.DownloadSiteUrl = DownloadSiteUrl.Trim();
        });
        AppServices.Updates.Repository = UpdateRepository.Trim();
        AppServices.ApplySettings();
        StatusText = "设置已保存";
    }

    [RelayCommand]
    private async Task ScanJavaAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        StatusText = "正在扫描 Java…";
        try
        {
            var list = await AppServices.Java.ScanAsync(AppServices.Paths);
            JavaRuntimes.Clear();
            foreach (var rt in list) JavaRuntimes.Add(rt);
            StatusText = $"检测到 {list.Count} 个 Java 运行时";
        }
        catch (Exception ex)
        {
            StatusText = "扫描失败：" + ex.Message;
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void OpenDataFolder() => AppServices.Paths.OpenInExplorer(AppServices.Paths.Root);
}
