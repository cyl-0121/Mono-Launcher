using CommunityToolkit.Mvvm.ComponentModel;
using MonoLauncher.Core.Download;
using MonoLauncher.Core.Versions;

namespace MonoLauncher.App.Services;

/// <summary>Single source of truth for the currently running download job (install or repair).</summary>
public sealed partial class DownloadCenter : ObservableObject
{
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _stage = "";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private double _fraction;
    [ObservableProperty] private long _bytesDone;
    [ObservableProperty] private long _bytesTotal;
    [ObservableProperty] private double _speed;
    [ObservableProperty] private int _filesDone;
    [ObservableProperty] private int _filesTotal;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private DateTimeOffset _startedAt;
    [ObservableProperty] private DateTimeOffset? _finishedAt;

    public string BytesText => $"{FormatBytes(BytesDone)} / {FormatBytes(BytesTotal)}";
    public string SpeedText => Speed > 0 ? FormatBytes((long)Speed) + "/s" : "";
    public string FilesText => FilesTotal > 0 ? $"{FilesDone}/{FilesTotal} 个文件" : "";

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024 / 1024:0.00} GB",
        >= 1024L * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B"
    };

    public void Begin(string stage)
    {
        IsActive = true;
        Error = null;
        Stage = stage;
        Fraction = 0;
        BytesDone = 0;
        BytesTotal = 0;
        Speed = 0;
        FilesDone = 0;
        FilesTotal = 0;
        StartedAt = DateTimeOffset.Now;
        FinishedAt = null;
        Detail = "";
    }

    public void ReportInstall(InstallProgress p)
    {
        Stage = p.Stage;
        if (!string.IsNullOrWhiteSpace(p.Detail)) Detail = p.Detail!;
        Fraction = p.Fraction;
        FilesDone = p.FilesDone;
        FilesTotal = p.FilesTotal;
        BytesDone = p.BytesDone;
        BytesTotal = p.BytesTotal;
        OnPropertyChanged(nameof(BytesText));
        OnPropertyChanged(nameof(FilesText));
    }

    public void ReportDownload(DownloadProgress p)
    {
        Stage = p.CurrentStage ?? Stage;
        if (!string.IsNullOrWhiteSpace(p.CurrentFile)) Detail = p.CurrentFile!;
        Fraction = p.Fraction;
        FilesDone = p.CompletedFiles;
        FilesTotal = p.TotalFiles;
        BytesDone = p.DownloadedBytes;
        BytesTotal = p.TotalBytes;
        Speed = p.BytesPerSecond;
        OnPropertyChanged(nameof(BytesText));
        OnPropertyChanged(nameof(SpeedText));
        OnPropertyChanged(nameof(FilesText));
    }

    public void Complete(string? message = null)
    {
        IsActive = false;
        FinishedAt = DateTimeOffset.Now;
        Stage = "完成";
        Fraction = 1;
        Detail = message ?? "全部完成";
    }

    public void Fail(string message)
    {
        IsActive = false;
        FinishedAt = DateTimeOffset.Now;
        Stage = "失败";
        Error = message;
    }
}
