using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using MonoLauncher.App.Services;

namespace MonoLauncher.App.Views;

public partial class AgreementWindow : Window
{
    public bool Accepted { get; private set; }

    public AgreementWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            AppServices.Log.Info("agreement",
                $"client={ClientSize} bounds={Bounds} scale={RenderScaling} desired={DesiredSize}");
            WindowSizing.Fix(this);
        };
    }

    /// <summary>Renders the window through Avalonia (bypasses DPI-virtualised screen captures).</summary>
    public void RenderSnapshot(string path)
    {
        var scale = RenderScaling;
        var w = Math.Max(1, (int)Math.Ceiling(ClientSize.Width * scale));
        var h = Math.Max(1, (int)Math.Ceiling(ClientSize.Height * scale));
        var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96 * scale, 96 * scale));
        rtb.Render(this);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        rtb.Save(fs);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        Accepted = true;
        ConsentStore.SaveAccepted();
        Close();
    }

    private void OnDecline(object? sender, RoutedEventArgs e)
    {
        Accepted = false;
        Close();
    }
}

/// <summary>Persists the user's agreement to the terms; a new version re-prompts.</summary>
public static class ConsentStore
{
    public const string CurrentVersion = "1.0";

    private static string FilePath =>
        Path.Combine(AppServices.Paths.ConfigDir, "consent.json");

    public static bool IsAccepted()
    {
        try
        {
            if (!File.Exists(FilePath)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            var version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
            return version == CurrentVersion;
        }
        catch
        {
            return false;
        }
    }

    public static void SaveAccepted()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new
            {
                accepted = true,
                version = CurrentVersion,
                time = DateTimeOffset.Now
            }));
        }
        catch
        {
            // best effort; the prompt will simply appear again next launch
        }
    }
}
