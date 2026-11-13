using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlassCraft.App.Services;
using GlassCraft.Core.Download;
using GlassCraft.Core.Java;
using GlassCraft.Core.Models;
using GlassCraft.Core.Versions;

namespace GlassCraft.App.ViewModels;

public sealed partial class VersionRow : ObservableObject
{
    public required VersionSummary Summary { get; init; }
    public string Id => Summary.Id;
    public string TypeText => Summary.Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "旧 Beta",
        "old_alpha" => "旧 Alpha",
        _ => Summary.Type
    };
    public string ReleaseDate => Summary.ReleaseTime == default
        ? ""
        : Summary.ReleaseTime.LocalDateTime.ToString("yyyy-MM-dd");
    public int JavaMajor => Summary.JavaVersion?.MajorVersion ?? 8;
    public string JavaText => Summary.JavaVersion is { } j ? $"Java {j.MajorVersion}+" : "";
    [ObservableProperty] private bool _isInstalled;

    public string TypeBadge => Summary.Type switch
    {
        "release" => "#3D6BFF",
        "snapshot" => "#8B5CF6",
        _ => "#64748B"
    };
}

public sealed partial class VersionsViewModel : ObservableObject
{
    private int _filterIndex;
    private bool _loaded;

    public ObservableCollection<VersionRow> AllVersions { get; } = new();
    public ObservableCollection<VersionRow> Versions { get; } = new();
    public ObservableCollection<string> LoaderKinds { get; } = new() { "原版", "NeoForge", "Fabric", "Quilt", "Forge" };
    public ObservableCollection<string> LoaderVersions { get; } = new();

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private VersionRow? _selectedVersion;
    [ObservableProperty] private string _selectedLoaderKind = "原版";
    [ObservableProperty] private string? _selectedLoaderVersion;
    [ObservableProperty] private string _statusText = "正在读取版本清单…";
    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isInstalling;

    public DownloadCenter Downloads => AppServices.Downloads;

    public string LatestRelease { get; private set; } = "";
    public string LatestSnapshot { get; private set; } = "";

    public VersionsViewModel()
    {
        _ = LoadManifestAsync();
    }

    public void EnsureLoaded()
    {
        if (!_loaded) _ = LoadManifestAsync();
    }

    public async Task LoadManifestAsync(bool force = false)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            StatusText = "正在读取官方版本清单…";
            var manifest = await AppServices.Versions.GetManifestAsync(force);
            LatestRelease = manifest.Latest.Release ?? "";
            LatestSnapshot = manifest.Latest.Snapshot ?? "";
            AllVersions.Clear();
            foreach (var v in manifest.Versions)
                AllVersions.Add(new VersionRow { Summary = v, IsInstalled = IsInstalled(v.Id) });
            ApplyFilter();
            _loaded = true;
            StatusText = $"共 {manifest.Versions.Count} 个版本 · 最新正式版 {LatestRelease}";
            ErrorText = null;
        }
        catch (Exception ex)
        {
            StatusText = "版本清单获取失败";
            ErrorText = ex.Message;
            AppServices.Log.Error("versions", "获取版本清单失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsInstalled(string id) =>
        File.Exists(AppServices.Paths.VersionJson(id)) ||
        File.Exists(Path.Combine(AppServices.Paths.VersionDir(id), "installed.json"));

    private void ApplyFilter()
    {
        var query = AllVersions.AsEnumerable();
        query = _filterIndex switch
        {
            1 => query.Where(v => v.Summary.Type == "release"),
            2 => query.Where(v => v.Summary.Type == "snapshot"),
            3 => query.Where(v => v.Summary.Type is "old_beta" or "old_alpha"),
            _ => query
        };
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            query = query.Where(v => v.Id.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        Versions.Clear();
        foreach (var v in query.Take(400)) Versions.Add(v);
        // Default to the newest release so "install" works without an extra click.
        SelectedVersion ??= Versions.FirstOrDefault(v => v.Summary.Type == "release") ?? Versions.FirstOrDefault();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public void SetFilter(int index)
    {
        _filterIndex = index;
        ApplyFilter();
    }

    partial void OnSelectedVersionChanged(VersionRow? value)
    {
        InstallCommand.NotifyCanExecuteChanged();
        if (value is null) return;
        StatusText = $"{value.Id} · {value.TypeText}" + (string.IsNullOrEmpty(value.JavaText) ? "" : $" · {value.JavaText}");
        OnPropertyChanged(nameof(SelectedVersionTitle));
    }

    public string SelectedVersionTitle => SelectedVersion?.Id ?? "选择一个版本";

    [RelayCommand]
    private async Task RefreshAsync() => await LoadManifestAsync(force: true);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (SelectedVersion is null) return;
        IsInstalling = true;
        Downloads.Begin($"安装 {SelectedVersion.Id}");
        ErrorText = null;
        AppServices.Log.Info("install", $"开始安装 {SelectedVersion.Id}");

        try
        {
            using var engine = new DownloadEngine(new DownloadOptions
            {
                MaxConcurrency = AppServices.Settings.Current.DownloadConcurrency,
                MaxRetries = AppServices.Settings.Current.DownloadRetries,
                ProxyUrl = AppServices.Settings.Current.ProxyUrl,
                UseMirror = AppServices.Settings.Current.UseMirror,
                ContinueOnError = true
            }, AppServices.DownloadHttp);

            var installer = new VersionInstaller(AppServices.Paths, AppServices.Versions);
            var summary = SelectedVersion.Summary;
            StatusText = "正在获取版本元数据…";
            var version = await AppServices.Versions.DownloadVersionJsonAsync(summary);
            var progress = new Progress<InstallProgress>(p => Downloads.ReportInstall(p));
            var resolved = await installer.InstallAsync(version, engine, progress);

            var loaderKey = LoaderKeyFor(SelectedLoaderKind);
            if (loaderKey is not null)
            {
                StatusText = $"安装 {SelectedLoaderKind}…";
                var javaList = AppServices.Java.Cached;
                if (javaList.Count == 0)
                    javaList = await AppServices.Java.ScanAsync(AppServices.Paths);
                var java = JavaScanner.SelectBest(javaList, resolved.JavaVersion?.MajorVersion ?? 21);
                var textProgress = new Progress<string>(s => StatusText = s);
                var loaderVersion = string.IsNullOrWhiteSpace(SelectedLoaderVersion) || SelectedLoaderVersion == "最新"
                    ? null
                    : SelectedLoaderVersion;
                var result = await AppServices.Loaders.InstallAsync(
                    loaderKey, resolved.Id, loaderVersion, java, textProgress);
                var loaderJson = System.Text.Json.JsonSerializer.Deserialize<VersionInfo>(
                    await File.ReadAllTextAsync(result.VersionJsonPath!), JsonDefaults.Options)!;
                await installer.InstallAsync(loaderJson, engine, progress);
                AppServices.Log.Info("install", $"{result.Kind} {result.LoaderVersion} 安装完成：{result.VersionId}");
                StatusText = $"{result.VersionId} 安装完成";
                Downloads.Complete($"已安装 {result.VersionId}");
            }
            else
            {
                StatusText = $"{resolved.Id} 安装完成";
                Downloads.Complete($"已安装 {resolved.Id}");
            }
            SelectedVersion.IsInstalled = true;
            AppServices.Log.Info("install", $"安装完成 {resolved.Id}");
        }
        catch (OperationCanceledException)
        {
            Downloads.Fail("安装已取消");
            StatusText = "安装已取消";
        }
        catch (InstallException ex)
        {
            Downloads.Fail(ex.Message);
            ErrorText = ex.Message;
            StatusText = "安装失败";
            AppServices.Log.Error("install", "安装失败", ex);
        }
        catch (Exception ex)
        {
            Downloads.Fail(ex.Message);
            ErrorText = ex.Message;
            StatusText = "安装失败";
            AppServices.Log.Error("install", "安装失败", ex);
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private bool CanInstall() => !IsInstalling && SelectedVersion is not null;

    partial void OnIsInstallingChanged(bool value)
    {
        InstallCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedLoaderKindChanged(string value)
    {
        LoaderVersions.Clear();
        SelectedLoaderVersion = null;
        OnPropertyChanged(nameof(ShowLoaderVersionPicker));
        if (LoaderKeyFor(value) is not null && SelectedVersion is not null)
            _ = LoadLoaderVersionsAsync();
    }

    public bool ShowLoaderVersionPicker => LoaderKeyFor(SelectedLoaderKind) is not null;

    private static string? LoaderKeyFor(string display) => display switch
    {
        "Fabric" => "fabric",
        "Quilt" => "quilt",
        "NeoForge" => "neoforge",
        "Forge" => "forge",
        _ => null
    };

    private async Task LoadLoaderVersionsAsync()
    {
        var key = LoaderKeyFor(SelectedLoaderKind);
        if (key is null || SelectedVersion is null) return;
        LoaderVersions.Clear();
        LoaderVersions.Add("最新");
        try
        {
            StatusText = $"获取 {SelectedLoaderKind} 版本列表…";
            var list = await AppServices.Loaders.GetLoaderVersionsAsync(key, SelectedVersion.Id);
            foreach (var v in list.Take(60)) LoaderVersions.Add(v);
            SelectedLoaderVersion = "最新";
            StatusText = list.Count == 0
                ? $"{SelectedLoaderKind} 没有适用于 {SelectedVersion.Id} 的版本"
                : $"共 {list.Count} 个 {SelectedLoaderKind} 版本";
        }
        catch (Exception ex)
        {
            StatusText = "Loader 版本获取失败：" + ex.Message;
        }
    }
}
