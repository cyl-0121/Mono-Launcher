using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GlassCraft.App.Controls;
using GlassCraft.App.Services;
using GlassCraft.App.ViewModels;

namespace GlassCraft.App.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private FpsCounter? _fps;
    private DispatcherTimer? _uiTimer;
    private bool _initializing = true;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainWindowViewModel();
        DataContext = _vm;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    /// <summary>Keeps the window inside the working area on high-DPI / small screens.</summary>
    private void FitToScreen()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;
        var scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        var usableW = screen.WorkingArea.Width / scale;
        var usableH = screen.WorkingArea.Height / scale;
        Width = Math.Min(Width, Math.Max(900, usableW - 60));
        Height = Math.Min(Height, Math.Max(540, usableH - 60));
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnOpened(object? sender, EventArgs e)
    {
        FitToScreen();
        AppServices.Log.Info("layout", $"opened client={ClientSize} scaling={RenderScaling} bounds={Bounds}");
        SizeChanged += (_, args) =>
            AppServices.Log.Info("layout", $"size {args.NewSize} client={ClientSize} scaling={RenderScaling}");
        _fps = new FpsCounter(this);
        _fps.Start();

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _uiTimer.Tick += (_, _) => _vm.UpdateFps(PerformanceState.CurrentFps);
        _uiTimer.Start();

        if (this.FindControl<ComboBox>("TierCombo") is { } combo)
        {
            combo.SelectedIndex = PerformanceState.Tier switch
            {
                GlassTier.HighPerformance => 0,
                GlassTier.Balanced => 1,
                GlassTier.LowPower => 2,
                _ => 3
            };
        }
        _initializing = false;

        _vm.Agent.Messages.CollectionChanged += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (this.FindControl<ScrollViewer>("ChatScroll") is { } scroll)
                    scroll.ScrollToEnd();
            }, DispatcherPriority.Background);
        };

        if (this.FindControl<Grid>("TitleBar") is { } bar)
        {
            bar.PointerPressed += (_, args) =>
            {
                if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    BeginMoveDrag(args);
            };
        }

        if (!string.IsNullOrWhiteSpace(App.AgentStartupMessage))
        {
            DispatcherTimer.RunOnce(() =>
            {
                _vm.Agent.InputText = App.AgentStartupMessage!;
                if (_vm.Agent.SendCommand.CanExecute(null))
                    _vm.Agent.SendCommand.Execute(null);
            }, TimeSpan.FromSeconds(1.2));
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _uiTimer?.Stop();
    }

    private void OnTierChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var tier = combo.SelectedIndex switch
        {
            0 => GlassTier.HighPerformance,
            1 => GlassTier.Balanced,
            2 => GlassTier.LowPower,
            _ => GlassTier.Off
        };
        PerformanceState.Apply(tier, PerformanceState.AnimationsEnabled, PerformanceState.PointerLighting);
        AppServices.Settings.Update(s => s.PerformanceTier = AppServices.TierKey(tier));
        AppServices.Log.Info("perf", $"视觉档位切换为 {tier}");
        RefreshGlass();
    }

    private void RefreshGlass()
    {
        foreach (var card in this.GetVisualDescendants().OfType<GlassCard>())
            card.InvalidateVisual();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
