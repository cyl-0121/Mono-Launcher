using System.Collections.ObjectModel;
using Avalonia.Threading;
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

    public string AppVersion => "0.9.0";

    public HomeViewModel()
    {
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
        Instances.Clear();
        foreach (var rec in AppServices.Instances.List())
            Instances.Add(new InstanceItem { Record = rec });
        SelectedInstance = Instances.FirstOrDefault();
        InstanceSummary = Instances.Count == 0 ? "还没有实例" : $"{Instances.Count} 个实例";
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

    public void RefreshPerformance()
    {
        // consumed by the UI through bindings; kept for symmetry
    }
}

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private string _currentPage = "home";
    [ObservableProperty] private string _fpsText = "-- FPS";
    [ObservableProperty] private string _tierText = "";
    [ObservableProperty] private string _clockText = "";

    public HomeViewModel Home { get; }
    public AgentViewModel Agent { get; }

    public MainWindowViewModel()
    {
        Home = new HomeViewModel();
        Agent = new AgentViewModel();
        TierText = PerformanceState.TierDisplay;
        PerformanceState.TierChanged += _ => Dispatcher.UIThread.Post(() => TierText = PerformanceState.TierDisplay);
    }

    public bool IsHome => CurrentPage == "home";
    public bool IsAgent => CurrentPage == "agent";

    partial void OnCurrentPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsHome));
        OnPropertyChanged(nameof(IsAgent));
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        if (string.IsNullOrWhiteSpace(page)) return;
        CurrentPage = page;
    }

    public void UpdateFps(double fps)
    {
        FpsText = $"{fps:0} FPS";
        TierText = PerformanceState.TierDisplay;
    }
}
