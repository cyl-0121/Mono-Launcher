using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SkiaSharp;

namespace GlassCraft.App.Controls;

/// <summary>Draws the procedural wallpaper behind everything, so glass cards have something to sample.</summary>
public class AuroraBackground : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;
        var scaling = topLevel.RenderScaling;
        var size = Bounds.Size;
        var w = (int)Math.Round(size.Width * scaling);
        var h = (int)Math.Round(size.Height * scaling);
        if (w < 4 || h < 4) return;
        GlassBackdrop.Ensure(w, h);
        if (GlassBackdrop.AvaloniaBitmap is { } bmp)
            context.DrawImage(bmp, new Rect(size));
    }
}

public static class SkiaInterop
{
    public static Avalonia.Media.Imaging.Bitmap ToAvaloniaBitmap(this SKImage image)
    {
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Avalonia.Media.Imaging.Bitmap(stream);
    }
}
