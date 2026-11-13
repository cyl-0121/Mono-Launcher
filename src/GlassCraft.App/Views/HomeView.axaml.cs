using Avalonia.Controls;
using Avalonia.Interactivity;
using GlassCraft.App.Services;
using GlassCraft.App.ViewModels;

namespace GlassCraft.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private bool _initializing = true;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TierCombo is not null)
        {
            TierCombo.SelectedIndex = PerformanceState.Tier switch
            {
                GlassTier.HighPerformance => 0,
                GlassTier.Balanced => 1,
                GlassTier.LowPower => 2,
                _ => 3
            };
        }
        _initializing = false;
    }

    private void OnTierChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (TierCombo.SelectedIndex < 0) return;
        var tier = TierCombo.SelectedIndex switch
        {
            0 => GlassTier.HighPerformance,
            1 => GlassTier.Balanced,
            2 => GlassTier.LowPower,
            _ => GlassTier.Off
        };
        PerformanceState.Apply(tier, PerformanceState.AnimationsEnabled, PerformanceState.PointerLighting);
        AppServices.Settings.Update(s => s.PerformanceTier = AppServices.TierKey(tier));
        AppServices.Log.Info("perf", $"视觉档位切换为 {tier}");
        if (TopLevel.GetTopLevel(this) is MainWindow main)
            main.RefreshGlass();
    }
}
