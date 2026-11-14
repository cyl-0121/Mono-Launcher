using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlassCraft.App.Services;
using GlassCraft.Core.Network;

namespace GlassCraft.App.ViewModels;

public sealed partial class NetworkViewModel : ObservableObject
{
    private readonly InstancesViewModel _instances;
    private readonly Action<string> _navigate;

    public ObservableCollection<InstanceItem> Instances { get; } = new();
    public ObservableCollection<LanServer> Servers { get; } = new();

    [ObservableProperty] private InstanceItem? _selectedInstance;
    [ObservableProperty] private LanServer? _selectedServer;
    [ObservableProperty] private string _manualAddress = "";
    [ObservableProperty] private string _statusText = "点击「扫描局域网」，查找同一网络里开放到局域网的 Minecraft 世界。";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string? _errorText;

    public NetworkViewModel(InstancesViewModel instances, Action<string> navigate)
    {
        _instances = instances;
        _navigate = navigate;
        RefreshInstances();
    }

    public void RefreshInstances()
    {
        var id = SelectedInstance?.Record.Id;
        Instances.Clear();
        foreach (var i in _instances.Instances) Instances.Add(i);
        SelectedInstance = Instances.FirstOrDefault(i => i.Record.Id == id) ?? Instances.FirstOrDefault();
        if (Instances.Count == 0)
            StatusText = "还没有实例。请先在「实例管理」里创建一个。";
    }

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
        if (SelectedInstance.Record.VersionId.Length == 0)
        {
            ErrorText = "实例没有关联版本";
            return;
        }

        ErrorText = null;
        StatusText = $"正在启动 {SelectedInstance.Name} 并连接 {host}:{port} …";
        _navigate("instances");
        try
        {
            await _instances.LaunchWithServerAsync(SelectedInstance, host, port);
            StatusText = $"已发起连接：{host}:{port}（游戏启动后会自动进入服务器）";
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
