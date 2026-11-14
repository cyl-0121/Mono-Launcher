using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoLauncher.App.Services;
using MonoLauncher.Core.Auth;
using MonoLauncher.Core.Instances;
using MonoLauncher.Core.Java;

namespace MonoLauncher.App.ViewModels;

public sealed partial class AccountItem : ObservableObject
{
    public required MinecraftAccount Account { get; init; }
    public string Name => Account.Name;
    public string KindText => Account.Kind == AccountKind.Microsoft ? "Microsoft" : "离线";
    public string Uuid => Account.Uuid;
    public string Initial => string.IsNullOrEmpty(Account.Name) ? "?" : Account.Name[..1].ToUpperInvariant();
    public bool IsMicrosoft => Account.Kind == AccountKind.Microsoft;
}

public sealed partial class InstanceItem : ObservableObject
{
    public required InstanceRecord Record { get; init; }
    public string Name => Record.Name;
    public string VersionText => Record.VersionId;
    public string LoaderText => Record.LoaderDisplay;
    public string LastPlayedText => Record.LastPlayedAt == default
        ? "从未启动"
        : $"上次游玩 {Record.LastPlayedAt:MM-dd HH:mm}";
}

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly InstancesViewModel _instances;
    private readonly Action<string> _navigate;

    public ObservableCollection<AccountItem> Accounts { get; } = new();
    public ObservableCollection<InstanceItem> Instances { get; } = new();
    public ObservableCollection<JavaRuntime> JavaRuntimes { get; } = new();

    [ObservableProperty] private AccountItem? _selectedAccount;
    [ObservableProperty] private string _newOfflineName = "";
    [ObservableProperty] private string _javaSummary = "未检测";
    [ObservableProperty] private string _instanceSummary = "还没有实例";
    [ObservableProperty] private string _dataDirectory = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private InstanceItem? _selectedInstance;
    [ObservableProperty] private string _launchHint = "选择一个实例后点「启动游戏」";

    // Announcement / update
    [ObservableProperty] private string _announcementTitle = "更新公告";
    [ObservableProperty] private string _announcementBody = "";
    [ObservableProperty] private string _announcementDate = "";
    [ObservableProperty] private string _updateStatus = "正在检查更新…";
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateTag = "";
    [ObservableProperty] private string _updateUrl = "";
    [ObservableProperty] private bool _isCheckingUpdate;

    public string AppVersion => "1.0.1";

    public HomeViewModel(InstancesViewModel instances, Action<string> navigate)
    {
        _instances = instances;
        _navigate = navigate;
        DataDirectory = AppServices.Paths.Root;
        ReloadAccounts();
        ReloadInstances();
        _ = ScanJavaAsync();
        _ = LoadUpdateAsync();
    }

    /// <summary>Fetches the latest GitHub release and shows it in the announcement panel.</summary>
    public async Task LoadUpdateAsync()
    {
        if (IsCheckingUpdate) return;
        IsCheckingUpdate = true;
        try
        {
            UpdateStatus = "正在检查更新…";
            var info = await AppServices.Updates.CheckAsync();
            if (info is null)
            {
                var error = AppServices.Updates.LastError;
                UpdateStatus = string.IsNullOrWhiteSpace(AppServices.Settings.Current.UpdateRepository)
                    ? "未配置更新源"
                    : "更新检查失败";
                AnnouncementBody = string.IsNullOrWhiteSpace(AppServices.Settings.Current.UpdateRepository)
                    ? "在「设置」里填写 GitHub 仓库（owner/repo），启动时会自动拉取最新 Release 作为更新公告。"
                    : "暂时无法连接 GitHub，稍后可点右上角「检查更新」重试。" +
                      (string.IsNullOrWhiteSpace(error) ? "" : $"\n\n错误信息：{error}");
                AnnouncementDate = "";
                return;
            }

            AnnouncementTitle = string.IsNullOrWhiteSpace(info.Name) ? $"更新公告 {info.Tag}" : info.Name;
            AnnouncementBody = string.IsNullOrWhiteSpace(info.Body) ? "（该版本没有填写更新说明）" : info.Body;
            AnnouncementDate = info.PublishedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            UpdateAvailable = info.IsNewer;
            UpdateTag = info.Tag;
            UpdateUrl = info.HtmlUrl;
            UpdatePackageUrl = info.DownloadUrl;
            UpdateStatus = info.IsNewer ? $"发现新版本 {info.Tag}" : "已是最新版本";
            AppServices.Log.Info("update", $"最新版本 {info.Tag}（来源 {info.Source}），是否更新={info.IsNewer}");
        }
        catch (Exception ex)
        {
            UpdateStatus = "检查更新失败：" + ex.Message;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private Task CheckUpdateAsync() => LoadUpdateAsync();

    public string UpdateButtonText => string.IsNullOrWhiteSpace(UpdateTag) ? "下载并更新" : $"更新到 v{UpdateTag.TrimStart('v', 'V')}";

    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private string? _updatePackageUrl;
    [ObservableProperty] private double _updateProgress;

    partial void OnUpdateTagChanged(string value) => OnPropertyChanged(nameof(UpdateButtonText));
    partial void OnIsUpdatingChanged(bool value) => UpdateAppCommand.NotifyCanExecuteChanged();

    private bool CanUpdateApp() => !IsUpdating && UpdateAvailable && !string.IsNullOrWhiteSpace(UpdateTag);

    /// <summary>Downloads the new build, swaps the exe and restarts the app.</summary>
    [RelayCommand(CanExecute = nameof(CanUpdateApp))]
    private async Task UpdateAppAsync()
    {
        if (string.IsNullOrWhiteSpace(UpdateTag)) return;
        IsUpdating = true;
        UpdateProgress = 0;
        try
        {
            // Prefer the package URL reported by the mirror; fall back to building one.
            var url = !string.IsNullOrWhiteSpace(UpdatePackageUrl)
                ? UpdatePackageUrl!
                : AppUpdater.BuildPackageUrl(
                    AppServices.Settings.Current.DownloadSiteUrl,
                    AppServices.Settings.Current.UpdateRepository,
                    UpdateTag);
            AppServices.Log.Info("update", $"开始下载更新包 {url}");

            var progress = new Progress<(double Fraction, string Stage)>(p =>
            {
                UpdateProgress = p.Fraction;
                UpdateStatus = p.Stage;
            });

            var (ok, message) = await AppUpdater.DownloadAndInstallAsync(url, progress, CancellationToken.None);
            UpdateStatus = message;
            AppServices.Log.Info("update", $"更新结果：ok={ok} {message}");

            if (ok)
            {
                await Task.Delay(900);
                (Avalonia.Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = "更新失败：" + ex.Message;
            AppServices.Log.Error("update", "自动更新失败", ex);
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private void GoDownload() => _navigate("versions");

    [RelayCommand]
    private void GoVersions() => _navigate("instances");

    [RelayCommand]
    private void OpenUpdate()
    {
        var site = AppServices.Settings.Current.DownloadSiteUrl;
        var target = string.IsNullOrWhiteSpace(site) ? UpdateUrl : site.Trim();
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppServices.Log.Error("update", "打开更新页面失败", ex);
        }
    }

    public void ReloadAccounts()
    {
        Accounts.Clear();
        foreach (var acc in AppServices.Accounts.Accounts.OrderByDescending(a => a.LastUsed))
            Accounts.Add(new AccountItem { Account = acc });
        SelectedAccount = Accounts.FirstOrDefault();
    }

    public void ReloadInstances()
    {
        _instances.Refresh();
        Instances.Clear();
        foreach (var item in _instances.Instances)
            Instances.Add(new InstanceItem { Record = item.Record });
        SelectedInstance = Instances.FirstOrDefault();
        InstanceSummary = Instances.Count == 0 ? "还没有实例" : $"{Instances.Count} 个实例";
        OnPropertyChanged(nameof(HasInstances));
    }

    public bool HasInstances => Instances.Count > 0;

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (SelectedInstance is null)
        {
            LaunchHint = "还没有实例，请先去「下载」安装版本，再在「版本列表」里创建实例";
            return;
        }
        LaunchHint = "正在启动…";
        _navigate("instances");
        await _instances.LaunchWithServerAsync(SelectedInstance, null, null);
        LaunchHint = "已发送启动请求，可在「版本列表」查看日志";
    }

    [RelayCommand]
    private async Task LoginMicrosoftAsync()
    {
        try
        {
            var account = await Dialogs.ShowMicrosoftLoginAsync();
            if (account is null) return;
            AppServices.Accounts.Upsert(account);
            ReloadAccounts();
            SelectedAccount = Accounts.FirstOrDefault(a => a.Account.Id == account.Id) ?? SelectedAccount;
            Status = $"已登录正版账户 {account.Name}";
            AppServices.Log.Info("auth", $"Microsoft 登录成功：{account.Name}");
        }
        catch (Exception ex)
        {
            Status = "登录失败：" + ex.Message;
            AppServices.Log.Error("auth", "Microsoft 登录异常", ex);
        }
    }

    [RelayCommand]
    private async Task ScanJavaAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        JavaSummary = "正在检测 Java…";
        try
        {
            var list = await AppServices.Java.ScanAsync(AppServices.Paths);
            JavaRuntimes.Clear();
            foreach (var rt in list) JavaRuntimes.Add(rt);
            JavaSummary = list.Count == 0
                ? "未找到 Java，请安装 Java 21"
                : string.Join(" · ", list.Take(3).Select(r => $"Java {r.MajorVersion}"));
            Status = list.Count == 0 ? "未找到 Java" : $"检测到 {list.Count} 个 Java 运行时";
            AppServices.Log.Info("java", $"检测到 {list.Count} 个 Java 运行时");
        }
        catch (Exception ex)
        {
            JavaSummary = "检测失败";
            Status = ex.Message;
            AppServices.Log.Error("java", "Java 检测失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddOfflineAccount()
    {
        try
        {
            var name = string.IsNullOrWhiteSpace(NewOfflineName) ? "Steve" : NewOfflineName.Trim();
            var account = OfflineIdentity.Create(name);
            AppServices.Accounts.Upsert(account);
            ReloadAccounts();
            SelectedAccount = Accounts.FirstOrDefault(a => a.Account.Id == account.Id);
            NewOfflineName = "";
            Status = $"已创建离线账户 {account.Name}";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private void RemoveAccount()
    {
        if (SelectedAccount is null) return;
        AppServices.Accounts.Remove(SelectedAccount.Account.Id);
        ReloadAccounts();
        Status = "账户已移除";
    }

    [RelayCommand]
    private void OpenDataFolder() => AppServices.Paths.OpenInExplorer(AppServices.Paths.Root);
}
