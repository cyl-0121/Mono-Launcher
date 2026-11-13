using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlassCraft.App.Services;
using GlassCraft.Core.Mods;

namespace GlassCraft.App.ViewModels;

public sealed partial class ModItem : ObservableObject
{
    public required ModEntry Entry { get; init; }
    public string Name => Entry.Name;
    public string Version => string.IsNullOrWhiteSpace(Entry.Version) ? "-" : Entry.Version;
    public string FileName => Entry.FileName;
    public string Loader => Entry.Loader;
    public string SizeText => Entry.SizeText;
    public bool Enabled => Entry.Enabled;

    [ObservableProperty] private bool _isBusy;
}

public sealed record ModSearchItem(ModrinthProject Project)
{
    public string Title => Project.Title;
    public string Description => Project.Description;
    public string DownloadsText => Project.Downloads >= 1_000_000
        ? $"{Project.Downloads / 1_000_000.0:0.0}M"
        : Project.Downloads >= 1000 ? $"{Project.Downloads / 1000.0:0}K" : Project.Downloads.ToString();
}

public sealed partial class ModsViewModel : ObservableObject
{
    private readonly ModManager _manager = new(AppServices.Paths);

    public ObservableCollection<InstanceItem> Instances { get; } = new();
    public ObservableCollection<ModItem> Mods { get; } = new();
    public ObservableCollection<ModSearchItem> SearchResults { get; } = new();

    [ObservableProperty] private InstanceItem? _selectedInstance;
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private ModSearchItem? _selectedSearchItem;

    public ModsViewModel()
    {
        RefreshInstances();
    }

    public void RefreshInstances()
    {
        var selectedId = SelectedInstance?.Record.Id;
        Instances.Clear();
        foreach (var rec in AppServices.Instances.List())
            Instances.Add(new InstanceItem { Record = rec });
        SelectedInstance = Instances.FirstOrDefault(i => i.Record.Id == selectedId) ?? Instances.FirstOrDefault();
    }

    partial void OnSelectedInstanceChanged(InstanceItem? value) => RefreshMods();

    public void RefreshMods()
    {
        Mods.Clear();
        if (SelectedInstance is null)
        {
            StatusText = "请先创建或选择一个实例";
            return;
        }
        var dir = Path.Combine(AppServices.Instances.GameDirectory(SelectedInstance.Record), "mods");
        foreach (var entry in _manager.Scan(dir))
            Mods.Add(new ModItem { Entry = entry });
        StatusText = $"{Mods.Count} 个 Mod · {Mods.Count(m => m.Enabled)} 个启用";
    }

    [RelayCommand]
    public void RefreshAll()
    {
        RefreshInstances();
        RefreshMods();
    }

    [RelayCommand]
    private void Toggle(ModItem? item)
    {
        if (item is null) return;
        if (_manager.SetEnabled(item.Entry, !item.Enabled))
        {
            RefreshMods();
            StatusText = $"{(item.Enabled ? "已禁用" : "已启用")} {item.Name}";
        }
        else
        {
            ErrorText = "操作失败：文件可能被占用";
        }
    }

    [RelayCommand]
    private void Delete(ModItem? item)
    {
        if (item is null) return;
        var moved = _manager.MoveToTrash(item.Entry);
        RefreshMods();
        StatusText = moved is null ? "删除失败" : $"已移入回收站：{moved}";
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        if (SelectedInstance is null) return;
        var dir = Path.Combine(AppServices.Instances.GameDirectory(SelectedInstance.Record), "mods");
        Directory.CreateDirectory(dir);
        AppServices.Paths.OpenInExplorer(dir);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (SelectedInstance is null)
        {
            ErrorText = "请先选择实例";
            return;
        }
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;
        IsSearching = true;
        ErrorText = null;
        StatusText = "正在搜索 Modrinth…";
        try
        {
            var rec = SelectedInstance.Record;
            var loader = rec.Loader is null or "vanilla" ? null : rec.Loader;
            var results = await AppServices.Modrinth.SearchAsync(SearchQuery, rec.MinecraftVersion, loader, 12);
            SearchResults.Clear();
            foreach (var r in results) SearchResults.Add(new ModSearchItem(r));
            StatusText = results.Count == 0 ? "没有找到兼容的 Mod" : $"找到 {results.Count} 个结果";
        }
        catch (Exception ex)
        {
            ErrorText = "搜索失败：" + ex.Message;
            StatusText = "搜索失败";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task InstallSelectedAsync()
    {
        if (SelectedInstance is null || SelectedSearchItem is null) return;
        try
        {
            var rec = SelectedInstance.Record;
            var loader = rec.Loader is null or "vanilla" ? null : rec.Loader;
            StatusText = $"获取 {SelectedSearchItem.Title} 的兼容版本…";
            var versions = await AppServices.Modrinth.GetVersionsAsync(
                SelectedSearchItem.Project.Id, rec.MinecraftVersion, loader);
            var version = versions.FirstOrDefault()
                          ?? throw new InvalidOperationException("没有兼容当前版本与 Loader 的文件");
            var file = version.PrimaryFile ?? throw new InvalidOperationException("该版本没有可下载文件");

            var tempDir = Path.Combine(AppServices.Paths.CacheDir, "mods");
            Directory.CreateDirectory(tempDir);
            var temp = Path.Combine(tempDir, file.Filename);
            StatusText = $"下载 {file.Filename}…";
            var bytes = await AppServices.Http.GetByteArrayAsync(file.Url);
            if (file.Sha1 is { Length: 40 })
            {
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(hash, file.Sha1, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-1 校验失败，文件可能损坏");
            }
            await File.WriteAllBytesAsync(temp, bytes);

            var modsDir = Path.Combine(AppServices.Instances.GameDirectory(rec), "mods");
            var installed = _manager.InstallFile(modsDir, temp);
            RefreshMods();
            StatusText = $"已安装 {Path.GetFileName(installed)}";
            AppServices.Log.Info("mods", $"安装 Mod {file.Filename} -> {installed}");
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            StatusText = "安装失败";
        }
    }
}
