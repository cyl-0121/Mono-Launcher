using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SkiaSharp;

namespace GlassCraft.App.Controls;

/// <summary>
/// Minecraft-flavoured backdrop: deep stone grey with a faint pixel grid, scattered darker blocks
/// and a green ambient glow at the top. Cheap to draw, no blur.
/// </summary>
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

        canvas.Clear(new SKColor(0x1B, 0x1B, 0x1B));

        using (var paint = new SKPaint())
        {
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, h),
                new[] { new SKColor(0x26, 0x2B, 0x26), new SKColor(0x1B, 0x1B, 0x1B), new SKColor(0x14, 0x14, 0x14) },
                new[] { 0f, 0.45f, 1f },
                SKShaderTileMode.Clamp);
            canvas.DrawRect(new SKRect(0, 0, w, h), paint);
        }

        var step = (int)Math.Max(8, 16 * (w / 1200.0));
        using (var grid = new SKPaint { Color = new SKColor(255, 255, 255, 6), StrokeWidth = 1 })
        {
            for (int x = 0; x < w; x += step) canvas.DrawLine(x, 0, x, h, grid);
            for (int y = 0; y < h; y += step) canvas.DrawLine(0, y, w, y, grid);
        }

        var rnd = new Random(20260101);
        using var block = new SKPaint { IsAntialias = false };
        int cells = (w / step + 1) * (h / step + 1);
        for (int i = 0; i < cells / 14; i++)
        {
            int cx = rnd.Next(Math.Max(1, w / step)) * step;
            int cy = rnd.Next(Math.Max(1, h / step)) * step;
            var v = rnd.Next(3);
            block.Color = v switch
            {
                0 => new SKColor(255, 255, 255, 5),
                1 => new SKColor(0, 0, 0, 18),
                _ => new SKColor(0, 0, 0, 8)
            };
            canvas.DrawRect(new SKRect(cx, cy, cx + step, cy + step), block);
        }

        using (var glow = new SKPaint())
        {
            glow.Shader = SKShader.CreateRadialGradient(
                new SKPoint(w * 0.5f, -h * 0.15f), h * 0.85f,
                new[] { new SKColor(0x4C, 0xAF, 0x50, 34), new SKColor(0x4C, 0xAF, 0x50, 0) },
                null, SKShaderTileMode.Clamp);
            canvas.DrawRect(new SKRect(0, 0, w, h), glow);
        }

        using (var vignette = new SKPaint())
        {
            vignette.Shader = SKShader.CreateRadialGradient(
                new SKPoint(w / 2f, h / 2f), Math.Max(w, h) * 0.72f,
                new[] { new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 110) },
                null, SKShaderTileMode.Clamp);
            canvas.DrawRect(new SKRect(0, 0, w, h), vignette);
        }

        return bitmap;
    }
}
