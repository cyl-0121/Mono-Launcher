using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SkiaSharp;

namespace MonoLauncher.App.Controls;

/// <summary>Light blue gradient backdrop used behind content, similar to PCL's news panel.</summary>
public class AuroraBackground : Control
{
    private SKBitmap? _bitmap;
    private Avalonia.Media.Imaging.Bitmap? _avaloniaBitmap;
    private int _width;
    private int _height;

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

        if (_bitmap is null || _width != w || _height != h)
        {
            _bitmap?.Dispose();
            _avaloniaBitmap?.Dispose();
            _bitmap = Render(w, h);
            _width = w;
            _height = h;
            using var data = _bitmap.Encode(SKEncodedImageFormat.Png, 90);
            using var stream = new MemoryStream(data.ToArray());
            _avaloniaBitmap = new Avalonia.Media.Imaging.Bitmap(stream);
        }

        if (_avaloniaBitmap is not null)
            context.DrawImage(_avaloniaBitmap, new Rect(size));
    }

    private static SKBitmap Render(int w, int h)
    {
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);

        using (var paint = new SKPaint())
        {
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(w, h),
                new[] { new SKColor(0xF7, 0xF7, 0xF7), new SKColor(0xEF, 0xEF, 0xEF), new SKColor(0xF5, 0xF5, 0xF5) },
                new[] { 0f, 0.55f, 1f },
                SKShaderTileMode.Clamp);
            canvas.DrawRect(new SKRect(0, 0, w, h), paint);
        }

        // Soft light blobs for depth.
        DrawBlob(canvas, w * 0.82f, h * 0.18f, w * 0.42f, new SKColor(0xFF, 0xFF, 0xFF, 160));
        DrawBlob(canvas, w * 0.25f, h * 0.85f, w * 0.38f, new SKColor(0xE6, 0xE6, 0xE6, 90));

        return bitmap;
    }

    private static void DrawBlob(SKCanvas canvas, float cx, float cy, float radius, SKColor color)
    {
        using var paint = new SKPaint();
        paint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy), radius,
            new[] { color, color.WithAlpha(0) },
            null, SKShaderTileMode.Clamp);
        canvas.DrawCircle(cx, cy, radius, paint);
    }
}
