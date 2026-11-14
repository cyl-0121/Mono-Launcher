using System.Runtime.InteropServices;
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
    private const int SwMinimize = 6;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private readonly MainWindowViewModel _vm;
    private FpsCounter? _fps;
    private DispatcherTimer? _uiTimer;
    private PixelPoint _restorePosition;
    private Size _restoreSize;
    private bool _hasRestoreState;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainWindowViewModel();
        DataContext = _vm;
        Opened += OnOpened;
        Closed += OnClosed;
        PropertyChanged += OnWindowPropertyChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != WindowStateProperty) return;
        if (WindowState == WindowState.Normal)
        {
            // Coming back from minimize/maximize: keep the pre-minimize geometry, so a
            // borderless window on Win11 does not come back at a wrong size/position.
            if (_hasRestoreState)
            {
                if (Math.Abs(Width - _restoreSize.Width) > 1 || Math.Abs(Height - _restoreSize.Height) > 1)
                {
                    Width = _restoreSize.Width;
                    Height = _restoreSize.Height;
                }
                Position = _restorePosition;
            }
            else
            {
                CaptureRestoreState();
            }
        }
        else if (WindowState == WindowState.Maximized)
        {
            if (this.FindControl<Button>("MaxButton") is { } max) max.Content = "❐";
        }
        else if (WindowState == WindowState.Minimized && _hasRestoreState == false)
        {
            CaptureRestoreState();
        }
    }

    private void CaptureRestoreState()
    {
        if (WindowState != WindowState.Normal) return;
        _restorePosition = Position;
        _restoreSize = new Size(Width, Height);
        _hasRestoreState = true;
    }

    private void ApplyWin11Chrome()
    {
        try
        {
            var handle = TryGetPlatformHandle();
            if (handle is null) return;
            var preference = DwmwcpRound;
            _ = DwmSetWindowAttribute(handle.Handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch
        {
            // Older Windows builds do not support the corner attribute; ignore.
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        FitToScreen();
        WindowSizing.Fix(this);
        ApplyWin11Chrome();
        CaptureRestoreState();
        AppServices.Log.Info("layout", $"opened client={ClientSize} scaling={RenderScaling}");

        _fps = new FpsCounter(this);
        _fps.Start();

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _uiTimer.Tick += (_, _) =>
        {
            _vm.UpdateFps(PerformanceState.CurrentFps);
            UpdateNavHighlight();
        };
        _uiTimer.Start();

        if (this.FindControl<Grid>("TitleBar") is { } bar)
        {
            bar.PointerPressed += (_, args) =>
            {
                // Ignore clicks that land on the window buttons.
                if (args.Source is Control control && control.FindAncestorOfType<Button>() is not null)
                    return;
                if (args.ClickCount == 2)
                {
                    ToggleMaximize();
                    return;
                }
                if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    BeginMoveDrag(args);
            };
        }

        if (!string.IsNullOrWhiteSpace(App.AgentStartupMessage))
        {
            DispatcherTimer.RunOnce(() =>
            {
                _vm.NavigateCommand.Execute("agent");
                _vm.Agent.InputText = App.AgentStartupMessage!;
                if (_vm.Agent.SendCommand.CanExecute(null))
                    _vm.Agent.SendCommand.Execute(null);
            }, TimeSpan.FromSeconds(1.0));
        }

        if (!string.IsNullOrWhiteSpace(App.LaunchInstanceId))
        {
            DispatcherTimer.RunOnce(() =>
            {
                _vm.NavigateCommand.Execute("instances");
                var item = _vm.Instances.Instances.FirstOrDefault(
                    i => string.Equals(i.Record.Id, App.LaunchInstanceId, StringComparison.OrdinalIgnoreCase));
                if (item is not null)
                {
                    _vm.Instances.SelectedInstance = item;
                    if (_vm.Instances.LaunchCommand.CanExecute(null))
                        _vm.Instances.LaunchCommand.Execute(null);
                }
                else
                {
                    AppServices.Log.Error("ui", $"找不到实例 {App.LaunchInstanceId}");
                }
            }, TimeSpan.FromSeconds(1.5));
        }

        UpdateNavHighlight();
    }

    /// <summary>Keeps the window inside the working area on high-DPI / small screens.</summary>
    private void FitToScreen()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;
        var scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        var usableW = screen.WorkingArea.Width / scale;
        var usableH = screen.WorkingArea.Height / scale;
        Width = Math.Min(Width, Math.Max(920, usableW - 40));
        Height = Math.Min(Height, Math.Max(540, usableH - 40));
    }

    private void UpdateNavHighlight()
    {
        var map = new Dictionary<string, string>
        {
            ["home"] = "NavHome",
            ["versions"] = "NavVersions",
            ["instances"] = "NavInstances",
            ["mods"] = "NavMods",
            ["network"] = "NavNetwork",
            ["downloads"] = "NavDownloads",
            ["logs"] = "NavLogs",
            ["agent"] = "NavAgent",
            ["settings"] = "NavSettings"
        };
        foreach (var (page, name) in map)
        {
            if (this.FindControl<Button>(name) is not { } button) continue;
            var active = page == _vm.CurrentPage;
            if (active && !button.Classes.Contains("active")) button.Classes.Add("active");
            if (!active) button.Classes.Remove("active");
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        if (this.FindControl<Button>("MaxButton") is { } max)
            max.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void OnClosed(object? sender, EventArgs e) => _uiTimer?.Stop();

    public void RefreshGlass()
    {
        foreach (var card in this.GetVisualDescendants().OfType<GlassCard>())
            card.InvalidateVisual();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        CaptureRestoreState();
        // Use the native minimize so Windows 11 plays its animation and keeps the
        // taskbar thumbnail correct for a borderless window.
        var handle = TryGetPlatformHandle();
        if (handle is not null)
        {
            _ = ShowWindow(handle.Handle, SwMinimize);
        }
        else
        {
            WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
