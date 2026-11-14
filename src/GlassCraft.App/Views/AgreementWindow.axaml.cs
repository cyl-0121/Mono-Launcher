using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GlassCraft.App.Services;

namespace GlassCraft.App.Views;

public partial class AgreementWindow : Window
{
    public bool Accepted { get; private set; }

    public AgreementWindow()
    {
        InitializeComponent();
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
