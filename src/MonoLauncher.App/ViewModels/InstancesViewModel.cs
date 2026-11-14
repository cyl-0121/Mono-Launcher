using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoLauncher.App.Services;
using MonoLauncher.Core.Auth;
using MonoLauncher.Core.Importers;
using MonoLauncher.Core.Instances;
using MonoLauncher.Core.Java;
using MonoLauncher.Core.Launch;
using MonoLauncher.Core.Models;
using MonoLauncher.Core.Versions;

namespace MonoLauncher.App.ViewModels;

public sealed partial class InstancesViewModel : ObservableObject
{
    private GameSession? _session;

    public ObservableCollection<InstanceItem> Instances { get; } = new();
    public ObservableCollection<string> InstalledVersions { get; } = new();
    public ObservableCollection<ImportCandidate> Candidates { get; } = new();

    [ObservableProperty] private InstanceItem? _selectedInstance;
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string? _newVersion;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private bool _isLaunching;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _gameLog = "";
    [ObservableProperty] private string _gameState = "未启动";
    [ObservableProperty] private ImportCandidate? _selectedCandidate;

    public InstancesViewModel()
    {
        Refresh();
    }

    public void Refresh()
    {
        var selectedId = SelectedInstance?.Record.Id;
        Instances.Clear();
        foreach (var rec in AppServices.Instances.List())
            Instances.Add(new InstanceItem { Record = rec });
        SelectedInstance = Instances.FirstOrDefault(i => i.Record.Id == selectedId) ?? Instances.FirstOrDefault();

        InstalledVersions.Clear();
        foreach (var dir in InstalledVersionsOnDisk())
            InstalledVersions.Add(dir);
        NewVersion ??= InstalledVersions.FirstOrDefault();
    }

    private static IEnumerable<string> InstalledVersionsOnDisk()
    {
        if (!Directory.Exists(AppServices.Paths.VersionsDir)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(AppServices.Paths.VersionsDir))
        {
            var id = Path.GetFileName(dir);
            if (File.Exists(Path.Combine(dir, id + ".json"))) yield return id;
        }
    }

    [RelayCommand]
    private void Create()
    {
        if (string.IsNullOrWhiteSpace(NewVersion))
        {
            ErrorText = "请先在「游戏下载」里安装一个版本";
            return;
        }
        try
        {
            var name = string.IsNullOrWhiteSpace(NewName) ? NewVersion! : NewName.Trim();
            var record = AppServices.Instances.Create(name, NewVersion!, installedVersionId: NewVersion);
            NewName = "";
            Refresh();
            SelectedInstance = Instances.FirstOrDefault(i => i.Record.Id == record.Id);
            StatusText = $"已创建实例 {record.Name}";
            ErrorText = null;
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
    }

    [RelayCommand]
    private void ScanImport()
    {
        try
        {
            var importer = new InstanceImporter(AppServices.Paths, AppServices.Instances);
            Candidates.Clear();
            foreach (var c in importer.Detect()) Candidates.Add(c);
            SelectedCandidate = Candidates.FirstOrDefault();
            StatusText = Candidates.Count == 0
                ? "没有找到可导入的实例（.minecraft / PCL2 / HMCL / Prism）"
                : $"找到 {Candidates.Count} 个可导入实例";
            AppServices.Log.Info("import", StatusText);
        }
        catch (Exception ex)
        {
            ErrorText = "扫描失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (SelectedCandidate is null)
        {
            ErrorText = "请先扫描并选择一个实例";
            return;
        }
        try
        {
            StatusText = $"正在导入 {SelectedCandidate.Name}…";
            var importer = new InstanceImporter(AppServices.Paths, AppServices.Instances);
            var progress = new Progress<string>(s => StatusText = s);
            var result = await importer.ImportAsync(SelectedCandidate, progress);
            Refresh();
            SelectedInstance = Instances.FirstOrDefault(i => i.Record.Id == result.Instance.Id);
            StatusText = $"已导入 {result.Instance.Name}：存档 {result.CopiedSaves} 个，Mod {result.CopiedMods} 个（源目录未改动）";
            AppServices.Log.Info("import", StatusText);
        }
        catch (Exception ex)
        {
            ErrorText = "导入失败：" + ex.Message;
            AppServices.Log.Error("import", "导入失败", ex);
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedInstance is null) return;
        var rec = SelectedInstance.Record;
        try
        {
            var moved = AppServices.Instances.MoveToTrash(rec.Id);
            Refresh();
            StatusText = moved is null
                ? "实例目录不存在"
                : $"实例已移入回收站：{moved}（未删除存档，可手动恢复）";
            ErrorText = null;
        }
        catch (Exception ex)
        {
            ErrorText = "删除失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (SelectedInstance is null) return;
        var dir = AppServices.Instances.GameDirectory(SelectedInstance.Record);
        Directory.CreateDirectory(dir);
        AppServices.Paths.OpenInExplorer(dir);
    }

    [RelayCommand]
    private Task LaunchAsync() => LaunchCoreAsync(null, null);

    /// <summary>Starts an instance and optionally connects straight to a server.</summary>
    public async Task LaunchWithServerAsync(InstanceItem item, string? host, int? port)
    {
        SelectedInstance = item;
        await LaunchCoreAsync(host, port);
    }

    private async Task LaunchCoreAsync(string? serverHost, int? serverPort)
    {
        if (SelectedInstance is null)
        {
            ErrorText = "请选择一个实例";
            return;
        }
        if (IsLaunching || IsRunning) return;

        IsLaunching = true;
        ErrorText = null;
        GameLog = "";
        var rec = SelectedInstance.Record;
        try
        {
            GameState = "准备中…";
            var versionId = rec.VersionId;
            var versionJson = AppServices.Paths.VersionJson(versionId);
            if (!File.Exists(versionJson) && !string.IsNullOrWhiteSpace(rec.MinecraftVersion))
                versionJson = AppServices.Paths.VersionJson(rec.MinecraftVersion);

            if (!File.Exists(versionJson))
                throw new FileNotFoundException($"版本 {versionId} 尚未安装，请先在游戏下载中安装");

            var raw = System.Text.Json.JsonSerializer.Deserialize<VersionInfo>(
                await File.ReadAllTextAsync(versionJson), JsonDefaults.Options)
                      ?? throw new InvalidDataException("版本 JSON 无法解析");
            var resolved = await AppServices.Versions.ResolveInheritanceAsync(raw);

            // Java: explicit override > detected best match for the version requirement.
            JavaRuntime? java = null;
            if (!string.IsNullOrWhiteSpace(rec.JavaPathOverride))
                java = await JavaScanner.ProbeAsync(rec.JavaPathOverride!);
            if (java is null && !string.IsNullOrWhiteSpace(AppServices.Settings.Current.JavaPath))
                java = await JavaScanner.ProbeAsync(AppServices.Settings.Current.JavaPath!);
            if (java is null)
            {
                var cached = AppServices.Java.Cached;
                if (cached.Count == 0) cached = await AppServices.Java.ScanAsync(AppServices.Paths);
                java = JavaScanner.SelectBest(cached, resolved.JavaVersion?.MajorVersion ?? 8);
            }
            if (java is null)
                throw new InvalidOperationException("未找到可用的 Java 运行时");

            var account = AppServices.Accounts.DefaultAccount;
            if (account is null)
            {
                account = OfflineIdentity.Create("Player");
                AppServices.Accounts.Upsert(account);
                StatusText = "未登录账户，已使用离线身份 Player";
            }
            else if (account.Kind == AccountKind.Microsoft && account.IsExpired)
            {
                GameState = "刷新登录令牌…";
                account = await AppServices.MsAuth.RefreshAsync(account);
                AppServices.Accounts.Upsert(account);
            }

            var gameDir = AppServices.Instances.GameDirectory(rec);
            var settings = new InstanceLaunchSettings
            {
                MinMemoryMb = rec.Launch.MinMemoryMb > 0 ? rec.Launch.MinMemoryMb : AppServices.Settings.Current.MinMemoryMb,
                MaxMemoryMb = rec.Launch.MaxMemoryMb > 0 ? rec.Launch.MaxMemoryMb : AppServices.Settings.Current.MaxMemoryMb,
                Width = rec.Launch.Width > 0 ? rec.Launch.Width : 1280,
                Height = rec.Launch.Height > 0 ? rec.Launch.Height : 720,
                ServerAddress = serverHost,
                ServerPort = serverPort,
                ExtraJvmArguments = rec.Launch.ExtraJvmArguments,
                ExtraGameArguments = rec.Launch.ExtraGameArguments
            };

            var plan = AppServices.Launch.CreatePlan(resolved, account, java, gameDir, settings);
            if (!plan.Ready)
                throw new MissingFileException(plan.MissingFiles);

            GameState = "启动游戏…";
            _session = new GameLauncher(AppServices.Paths).Launch(plan.Plan);
            _session.OutputReceived += line =>
            {
                GameLog = (_session?.RecentOutput ?? "").Length > 12000
                    ? _session!.RecentOutput[^12000..]
                    : _session?.RecentOutput ?? "";
            };
            IsRunning = true;
            GameState = $"运行中 (pid {_session.ProcessId})";
            AppServices.Instances.MarkPlayed(rec.Id);
            AppServices.Log.Info("launch", $"启动实例 {rec.Name} 版本 {versionId}，Java {java.MajorVersion}");

            var startedAt = DateTimeOffset.Now;
            _ = Task.Run(async () =>
            {
                int code;
                try { code = await _session.WaitForExitAsync(); }
                catch { code = -1; }
                var crash = GameLauncher.FindCrashReport(gameDir, startedAt);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsRunning = false;
                    GameState = code == 0 ? "已退出" : $"异常退出 (code {code})";
                    if (crash is not null)
                    {
                        var summary = GameLauncher.SummariseCrash(crash);
                        ErrorText = "检测到崩溃报告：" + crash + "\n" + summary;
                        GameLog = summary;
                        AppServices.Log.Error("launch", "游戏崩溃：" + crash);
                    }
                });
            });
        }
        catch (MissingFileException ex)
        {
            ErrorText = ex.Message;
            GameState = "文件缺失";
            AppServices.Log.Error("launch", "启动文件缺失", ex);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            GameState = "启动失败";
            AppServices.Log.Error("launch", "启动失败", ex);
        }
        finally
        {
            IsLaunching = false;
        }
    }

    [RelayCommand]
    private void Kill()
    {
        _session?.Kill();
        GameState = "已结束进程";
    }

    [RelayCommand]
    private void ReadLog()
    {
        if (_session is not null) GameLog = _session.RecentOutput;
        else if (SelectedInstance is not null)
        {
            var logDir = AppServices.Paths.LogsDir;
            var latest = Directory.Exists(logDir)
                ? Directory.EnumerateFiles(logDir, "game-*.log").OrderByDescending(File.GetLastWriteTime).FirstOrDefault()
                : null;
            GameLog = latest is null ? "暂无游戏日志" : string.Join("\n", File.ReadLines(latest).TakeLast(200));
        }
    }

    [RelayCommand]
    private void OpenLogsFolder() => AppServices.Paths.OpenInExplorer(AppServices.Paths.LogsDir);
}
