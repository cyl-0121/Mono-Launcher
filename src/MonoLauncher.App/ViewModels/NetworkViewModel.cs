using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoLauncher.App.Services;
using MonoLauncher.Core.Network;

namespace MonoLauncher.App.ViewModels;

public sealed partial class NetworkViewModel : ObservableObject
{
    private readonly InstancesViewModel _instances;
    private readonly Action<string> _navigate;
    private readonly TerracottaService _terracotta;
    private DispatcherTimer? _pollTimer;
    private long _lastIndex = -1;

    public ObservableCollection<InstanceItem> Instances { get; } = new();
    public ObservableCollection<LanServer> Servers { get; } = new();
    public ObservableCollection<TerracottaProfile> TcProfiles { get; } = new();

    [ObservableProperty] private InstanceItem? _selectedInstance;
    [ObservableProperty] private LanServer? _selectedServer;
    [ObservableProperty] private string _manualAddress = "";
    [ObservableProperty] private string _statusText = "点击「扫描局域网」，查找同一网络里开放到局域网的 Minecraft 世界。";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string? _errorText;

    // Terracotta
    [ObservableProperty] private string _tcStateText = "未启动";
    [ObservableProperty] private string? _tcRoom;
    [ObservableProperty] private string? _tcUrl;
    [ObservableProperty] private string _tcPlayerName = "Player";
    [ObservableProperty] private string _tcJoinCode = "";
    [ObservableProperty] private string _tcHint = "陶瓦联机：创建房间获得房间码，好友输入房间码即可加入。";
    [ObservableProperty] private bool _tcBusy;
    [ObservableProperty] private bool _tcRunning;
    public bool TcHasUrl => !string.IsNullOrWhiteSpace(TcUrl);
    [ObservableProperty] private bool _tcInRoom;

    public NetworkViewModel(InstancesViewModel instances, Action<string> navigate)
    {
        _instances = instances;
        _navigate = navigate;
        _terracotta = new TerracottaService(AppServices.Paths, AppServices.Http);
        RefreshInstances();
    }

    public void RefreshInstances()
    {
        var id = SelectedInstance?.Record.Id;
        Instances.Clear();
        foreach (var i in _instances.Instances) Instances.Add(i);
        SelectedInstance = Instances.FirstOrDefault(i => i.Record.Id == id) ?? Instances.FirstOrDefault();
        if (Instances.Count == 0)
            StatusText = "还没有实例。请先在「版本」里创建一个。";
    }

    // ---------------- Terracotta ----------------

    [RelayCommand]
    private async Task StartTerracottaAsync()
    {
        if (TcBusy) return;
        TcBusy = true;
        try
        {
            TcStateText = "正在下载 / 启动…";
            var progress = new Progress<string>(s => TcHint = s);
            var ok = await _terracotta.StartAsync(progress);
            TcRunning = ok;
            if (!ok)
            {
                TcStateText = "启动失败";
                return;
            }
            TcStateText = "待机";
            TcHint = "已就绪。可以创建房间，或输入好友的房间码加入。";
            StartPolling();
        }
        finally
        {
            TcBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        if (TcBusy) return;
        if (!TcRunning && !await EnsureStartedAsync()) return;
        TcBusy = true;
        try
        {
            TcHint = "正在创建房间（需要你已在游戏里对局域网开放）…";
            await _terracotta.HostAsync(null, TcPlayerName);
        }
        finally
        {
            TcBusy = false;
        }
    }

    [RelayCommand]
    private async Task JoinRoomAsync()
    {
        if (TcBusy) return;
        var code = TcJoinCode.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            TcHint = "请输入房间码";
            return;
        }
        if (!TcRunning && !await EnsureStartedAsync()) return;
        TcBusy = true;
        try
        {
            TcHint = "正在加入房间…";
            await _terracotta.GuestAsync(code, TcPlayerName);
        }
        finally
        {
            TcBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResetRoomAsync()
    {
        await _terracotta.ResetAsync();
        TcRoom = null;
        TcUrl = null;
        TcProfiles.Clear();
        TcInRoom = false;
        TcStateText = "待机";
        TcHint = "已返回待机状态。";
    }

    [RelayCommand]
    private async Task StopTerracottaAsync()
    {
        StopPolling();
        await _terracotta.ShutdownAsync();
        TcRunning = false;
        TcInRoom = false;
        TcRoom = null;
        TcUrl = null;
        TcProfiles.Clear();
        TcStateText = "已停止";
        TcHint = "陶瓦联机已停止。";
    }

    [RelayCommand]
    private async Task JoinDirectAsync()
    {
        if (string.IsNullOrWhiteSpace(TcUrl))
        {
            TcHint = "还没有可用的直连地址，请先加入房间并等待「已加入房间」。";
            return;
        }
        if (SelectedInstance is null)
        {
            TcHint = "请先选择一个实例";
            return;
        }
        var target = TcUrl!;
        var host = target;
        var port = 25565;
        var colon = target.LastIndexOf(':');
        if (colon > 0)
        {
            host = target[..colon];
            if (int.TryParse(target[(colon + 1)..], out var p)) port = p;
        }
        _navigate("instances");
        await _instances.LaunchWithServerAsync(SelectedInstance, host, port);
        TcHint = $"已启动游戏并直连 {target}";
    }

    private async Task<bool> EnsureStartedAsync()
    {
        var progress = new Progress<string>(s => TcHint = s);
        var ok = await _terracotta.StartAsync(progress);
        TcRunning = ok;
        if (ok) StartPolling();
        return ok;
    }

    private void StartPolling()
    {
        if (_pollTimer is not null) return;
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _pollTimer.Tick += async (_, _) => await PollAsync();
        _pollTimer.Start();
    }

    private void StopPolling()
    {
        _pollTimer?.Stop();
        _pollTimer = null;
        _lastIndex = -1;
    }

    private async Task PollAsync()
    {
        var state = await _terracotta.GetStateAsync();
        if (state is null) return;
        if (state.Index < _lastIndex) return; // ignore out-of-order responses
        _lastIndex = state.Index;

        TcStateText = state.DisplayText;
        TcInRoom = state.IsHostReady || state.IsGuestReady;
        TcRoom = state.Room;
        if (state.IsGuestReady && !string.IsNullOrWhiteSpace(state.Url))
            TcUrl = state.Url;

        if (state.Profiles.Count > 0)
        {
            TcProfiles.Clear();
            foreach (var p in state.Profiles) TcProfiles.Add(p);
        }

        TcHint = state.State switch
        {
            "host-scanning" => "正在扫描本机 Minecraft 端口：请先进入存档并按 Esc →「对局域网开放」。",
            "host-ok" => "房间已就绪，把房间码发给好友即可。",
            "guest-ok" => "已加入房间。可点「用当前实例直连」直接进入。",
            "exception" => state.DisplayText + "（点「返回待机」重试）",
            _ => TcHint
        };

        if (state.State == "waiting")
            TcUrl = null;
    }

    // ---------------- LAN ----------------

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        ErrorText = null;
        StatusText = "正在监听局域网广播（3 秒）…";
        try
        {
            var found = await new LanDiscovery().DiscoverAsync(TimeSpan.FromSeconds(3));
            Servers.Clear();
            foreach (var s in found) Servers.Add(s);
            SelectedServer = Servers.FirstOrDefault();
            StatusText = found.Count == 0
                ? "没有发现房间。请确认对方在游戏里点了「对局域网开放」，且与本机在同一网络。"
                : $"发现 {found.Count} 个局域网房间";
        }
        catch (Exception ex)
        {
            ErrorText = "扫描失败：" + ex.Message;
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private Task JoinAsync(LanServer? server)
    {
        var target = server ?? SelectedServer;
        if (target is null)
        {
            ErrorText = "请先扫描并选择一个房间";
            return Task.CompletedTask;
        }
        return ConnectAsync(target.Address, target.Port);
    }

    [RelayCommand]
    private Task JoinManualAsync()
    {
        var text = ManualAddress.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            ErrorText = "请输入地址，例如 192.168.1.10:25565";
            return Task.CompletedTask;
        }
        if (!TryParseAddress(text, out var host, out var port))
        {
            ErrorText = "地址格式不正确";
            return Task.CompletedTask;
        }
        return ConnectAsync(host, port);
    }

    private async Task ConnectAsync(string host, int port)
    {
        if (SelectedInstance is null)
        {
            ErrorText = "请先选择一个实例";
            return;
        }

        ErrorText = null;
        StatusText = $"正在启动 {SelectedInstance.Name} 并连接 {host}:{port} …";
        _navigate("instances");
        try
        {
            await _instances.LaunchWithServerAsync(SelectedInstance, host, port);
            StatusText = $"已发起连接：{host}:{port}";
            AppServices.Log.Info("network", $"联机启动 {SelectedInstance.Name} -> {host}:{port}");
        }
        catch (Exception ex)
        {
            ErrorText = "连接失败：" + ex.Message;
        }
    }

    public static bool TryParseAddress(string text, out string host, out int port)
    {
        host = text;
        port = 25565;
        var colon = text.LastIndexOf(':');
        if (colon > 0)
        {
            host = text[..colon];
            if (!int.TryParse(text[(colon + 1)..], out port) || port is < 1 or > 65535)
                return false;
        }
        return host.Length > 0;
    }
}
