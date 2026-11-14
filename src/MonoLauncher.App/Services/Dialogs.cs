using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using MonoLauncher.Agent;
using MonoLauncher.Core.Auth;

namespace MonoLauncher.App.Services;

/// <summary>Native tool-approval dialog. High-risk tools never run without an explicit click.</summary>
public static class Dialogs
{
    /// <summary>Shows the Microsoft device-code login dialog; returns the account on success.</summary>
    public static async Task<MinecraftAccount?> ShowMicrosoftLoginAsync()
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return null;
        var window = new Views.MicrosoftLoginWindow();
        await window.ShowDialog(owner);
        return window.Account;
    }
    public static async Task<bool> ConfirmToolAsync(ToolCallRequest request)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null) return false;

        var result = false;
        var dialog = new Window
        {
            Title = "Agent 请求执行高风险操作",
            Width = 560,
            Height = 340,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#10141F")),
            ShowInTaskbar = false
        };

        var approve = new Button
        {
            Content = "允许执行",
            Classes = { "primary" },
            Width = 120,
            Height = 38
        };
        var deny = new Button
        {
            Content = "拒绝",
            Classes = { "ghost" },
            Width = 100,
            Height = 38
        };
        approve.Click += (_, _) => { result = true; dialog.Close(); };
        deny.Click += (_, _) => { result = false; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "确认执行工具调用",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#EAF1FF"))
                },
                new TextBlock
                {
                    Text = $"工具：{request.Name}    风险：{request.Risk}",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.Parse("#9EB0C9"))
                },
                new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#12FFFFFF")),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    MaxHeight = 150,
                    Child = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            Text = request.ArgumentsJson,
                            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                            FontSize = 11,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#C7D2E5"))
                        }
                    }
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { deny, approve }
                }
            }
        };

        await dialog.ShowDialog(window);
        return result;
    }
}
