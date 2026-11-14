using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlassCraft.App.Services;
using GlassCraft.Core.Auth;
using GlassCraft.Core.Instances;
using GlassCraft.Core.Java;

namespace GlassCraft.App.ViewModels;

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

    public string AppVersion => "0.9.0";

    public HomeViewModel(InstancesViewModel instances, Action<string> navigate)
    {
        _instances = instances;
        _navigate = navigate;
        DataDirectory = AppServices.Paths.Root;
        ReloadAccounts();
        ReloadInstances();
        _ = ScanJavaAsync();
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
