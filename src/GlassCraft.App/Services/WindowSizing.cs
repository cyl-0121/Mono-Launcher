using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace GlassCraft.App.Services;

/// <summary>
/// Some virtual display / DPI configurations create the window at logical size while Avalonia
/// renders at logical*scaling, which pushes content outside the window. This forces the native
/// window size to match the render size.
/// </summary>
public static class WindowSizing
{
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    public static void Fix(Window window)
    {
        try
        {
            // SizeToContent windows size themselves; forcing a size would fight the layout.
            if (window.SizeToContent != SizeToContent.Manual) return;
            var handle = window.TryGetPlatformHandle();
            if (handle is null) return;
            var scale = window.RenderScaling;
            if (scale <= 0.01) return;

            var expectedW = (int)Math.Round(window.ClientSize.Width * scale);
            var expectedH = (int)Math.Round(window.ClientSize.Height * scale);
            if (expectedW <= 0 || expectedH <= 0) return;

            if (GetWindowRect(handle.Handle, out var rect))
            {
                var actualW = rect.Right - rect.Left;
                var actualH = rect.Bottom - rect.Top;
                // Already correct within a few pixels: nothing to do.
                if (Math.Abs(actualW - expectedW) <= 8 && Math.Abs(actualH - expectedH) <= 8)
                    return;
            }

            SetWindowPos(handle.Handle, IntPtr.Zero, 0, 0, expectedW, expectedH,
                SwpNoMove | SwpNoZOrder);
            AppServices.Log.Info("layout",
                $"窗口物理尺寸已校正为 {expectedW}x{expectedH} (scale {scale})");
        }
        catch (Exception ex)
        {
            AppServices.Log.Error("layout", "窗口尺寸校正失败", ex);
        }
    }
}
