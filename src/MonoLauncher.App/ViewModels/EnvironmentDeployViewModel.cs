using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoLauncher.App.Services;
using MonoLauncher.Core.Deploy;
using MonoLauncher.Core.Java;

namespace MonoLauncher.App.ViewModels;

/// <summary>Drives the one-click "deploy a playable environment" pipeline from the UI.</summary>
public sealed partial class EnvironmentDeployViewModel : ObservableObject
{
    public ObservableCollection<string> Versions { get; } = new();
    public ObservableCollection<string> Loaders { get; } = new()
        { "vanilla", "fabric", "quilt", "neoforge", "forge" };

    [ObservableProperty] private string? _version;
    [ObservableProperty] private string _loader = "fabric";
    [ObservableProperty] private string _instanceName = "";
    [ObservableProperty] private bool _installFabricApi = true;
    [ObservableProperty] private bool _optimizeMemory = true;
    [ObservableProperty] private bool _isDeploying;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _hasSummary;
    [ObservableProperty] private string _memoryHint = "";

    partial void OnSummaryChanged(string value) => HasSummary = !string.IsNullOrWhiteSpace(value);

    public bool CanDeploy => !IsDeploying && !string.IsNullOrWhiteSpace(Version);

    /// <summary>Raised after a successful deploy so views can refresh instance lists.</summary>
    public event Action? Deployed;

    public EnvironmentDeployViewModel()
    {
        foreach (var v in new[] { "1.21.1", "1.21.4", "1.20.1", "1.19.2", "1.18.2", "1.16.5", "1.12.2", "1.8.9" })
            Versions.Add(v);
        Version = Versions.FirstOrDefault();
        var plan = MemoryAdvisor.Recommend();
        MemoryHint = $"本机内存 {plan.TotalText}（可用 {plan.FreeText}），将自动分配约 {plan.MaxText} 并应用 G1 调优";
    }

    partial void OnIsDeployingChanged(bool value) => OnPropertyChanged(nameof(CanDeploy));
    partial void OnVersionChanged(string? value) => OnPropertyChanged(nameof(CanDeploy));

    [RelayCommand(CanExecute = nameof(CanDeploy))]
    private async Task DeployAsync()
    {
        IsDeploying = true;
        ErrorText = null;
        Summary = "";
        StatusText = "开始部署…";
        try
        {
            var progress = new Progress<string>(s => StatusText = s);
            var result = await AppServices.Deployer.DeployAsync(
                Version!.Trim(), Loader, string.IsNullOrWhiteSpace(InstanceName) ? null : InstanceName.Trim(),
                InstallFabricApi, OptimizeMemory, progress).ConfigureAwait(true);
            Summary = result.Summary;
            StatusText = "部署完成，可以直接启动了";
            AppServices.Log.Info("deploy", $"一键部署 {result.Instance.Name}（{result.Instance.VersionId}）");
            Deployed?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorText = "部署失败：" + ex.Message;
            StatusText = "部署失败";
            AppServices.Log.Error("deploy", "一键部署失败", ex);
        }
        finally
        {
            IsDeploying = false;
        }
    }
}
