using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlassCraft.App.Services;

namespace GlassCraft.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly Dictionary<string, (string Title, string Subtitle)> _titles = new()
    {
        ["home"] = ("首页", "账户 / 实例 / 性能"),
        ["versions"] = ("游戏下载", "官方版本清单与安装"),
        ["instances"] = ("实例管理", "独立目录 / 启动 / 日志"),
        ["mods"] = ("Mod 管理", "扫描 / 启停 / Modrinth"),
        ["downloads"] = ("下载管理", "速度 / 队列 / 进度"),
        ["logs"] = ("日志", "结构化诊断"),
        ["agent"] = ("AI Agent", "多提供商 / 工具调用"),
        ["settings"] = ("设置", "Java / 内存 / 网络 / 视觉")
    };

    [ObservableProperty] private object? _currentViewModel;
    [ObservableProperty] private string _currentPage = "home";
    [ObservableProperty] private string _currentPageTitle = "首页";
    [ObservableProperty] private string _currentPageSubtitle = "";
    [ObservableProperty] private string _fpsText = "-- FPS";
    [ObservableProperty] private string _tierText = "";
    [ObservableProperty] private string _activeProfileText = "未登录";

    public HomeViewModel Home { get; }
    public AgentViewModel Agent { get; }
    public VersionsViewModel Versions { get; }
    public InstancesViewModel Instances { get; }
    public ModsViewModel Mods { get; }
    public DownloadsViewModel Downloads { get; }
    public LogsViewModel Logs { get; }
    public SettingsViewModel Settings { get; }

    public MainWindowViewModel()
    {
        Home = new HomeViewModel();
        Agent = new AgentViewModel();
        Versions = new VersionsViewModel();
        Instances = new InstancesViewModel();
        Mods = new ModsViewModel();
        Downloads = new DownloadsViewModel();
        Logs = new LogsViewModel();
        Settings = new SettingsViewModel();
        TierText = PerformanceState.TierDisplay;

        PerformanceState.TierChanged += _ => Dispatcher.UIThread.Post(() => TierText = PerformanceState.TierDisplay);
        Home.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HomeViewModel.SelectedAccount))
                ActiveProfileText = Home.SelectedAccount?.Name ?? "未登录";
        };
        ActiveProfileText = Home.SelectedAccount?.Name ?? "未登录";

        Navigate("home");
    }

    partial void OnCurrentPageChanged(string value)
    {
        if (_titles.TryGetValue(value, out var t))
        {
            CurrentPageTitle = t.Title;
            CurrentPageSubtitle = t.Subtitle;
        }
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        if (string.IsNullOrWhiteSpace(page)) return;
        CurrentPage = page;
        CurrentViewModel = page switch
        {
            "versions" => Versions,
            "instances" => Instances,
            "mods" => Mods,
            "downloads" => Downloads,
            "logs" => Logs,
            "agent" => Agent,
            "settings" => Settings,
            _ => Home
        };

        if (page == "versions") Versions.EnsureLoaded();
        if (page == "instances") Instances.Refresh();
        if (page == "mods") Mods.RefreshAll();
        if (page == "logs") Logs.Reload();
    }

    public void UpdateFps(double fps)
    {
        FpsText = $"{fps:0} FPS";
        TierText = PerformanceState.TierDisplay;
    }
}
