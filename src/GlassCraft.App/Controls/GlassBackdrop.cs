using SkiaSharp;

namespace GlassCraft.App.Controls;

/// <summary>
/// Procedural wallpaper used as the backdrop for the glass pipeline.
/// Two textures are kept: a sharp one and a pre-blurred/downsampled one, so the glass cards
/// sample a real blurred backdrop instead of faking it with flat translucency.
/// </summary>
public static class GlassBackdrop
{
    private static readonly object Gate = new();
    private static SKImage? _sharp;
    private static SKImage? _blurred;
    private static int _width;
    private static int _height;

    public static int Width => _width;
    public static int Height => _height;
    public static SKImage? Sharp => _sharp;
    public static SKImage? Blurred => _blurred;
    public static Avalonia.Media.Imaging.Bitmap? AvaloniaBitmap { get; private set; }

    /// <summary>Regenerates the wallpaper when the window size changed. Safe to call from the UI thread.</summary>
    public static void Ensure(int width, int height)
    {
        width = Math.Clamp(width, 320, 3840);
        height = Math.Clamp(height, 240, 2160);
        lock (Gate)
        {
            if (_sharp is not null && _width == width && _height == height) return;
            _sharp?.Dispose();
            _blurred?.Dispose();
            AvaloniaBitmap?.Dispose();
            _sharp = RenderSharp(width, height);
            _blurred = RenderBlurred(_sharp, width, height);
            AvaloniaBitmap = _sharp.ToAvaloniaBitmap();
            _width = width;
            _height = height;
        }
    }

    private static SKImage RenderSharp(int w, int h)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x07, 0x0A, 0x12));

        // Base vertical gradient
        using (var basePaint = new SKPaint())
        {
            basePaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(w * 0.3f, h),
                new[]
                {
                    new SKColor(0x08, 0x0C, 0x18),
                    new SKColor(0x0C, 0x12, 0x24),
                    new SKColor(0x11, 0x16, 0x2E)
                },
                null,
                SKShaderTileMode.Clamp);
            canvas.DrawRect(new SKRect(0, 0, w, h), basePaint);
        }

        // Aurora blobs (the colour source the glass samples from)
        DrawBlob(canvas, w * 0.18f, h * 0.22f, w * 0.55f, new SKColor(0x25, 0x63, 0xEB), 0.55f);
        DrawBlob(canvas, w * 0.78f, h * 0.18f, w * 0.48f, new SKColor(0x7C, 0x3A, 0xED), 0.45f);
        DrawBlob(canvas, w * 0.62f, h * 0.82f, w * 0.52f, new SKColor(0x06, 0xB6, 0xD4), 0.30f);
        DrawBlob(canvas, w * 0.10f, h * 0.88f, w * 0.42f, new SKColor(0xEC, 0x48, 0x99), 0.18f);
        DrawBlob(canvas, w * 0.92f, h * 0.62f, w * 0.30f, new SKColor(0x22, 0xD3, 0xEE), 0.16f);

        // Subtle grid gives the refraction something to bend
        using (var grid = new SKPaint { Color = new SKColor(255, 255, 255, 10), StrokeWidth = 1 })
        {
            const int step = 72;
            for (int x = 0; x < w; x += step)
                canvas.DrawLine(x, 0, x, h, grid);
            for (int y = 0; y < h; y += step)
                canvas.DrawLine(0, y, w, y, grid);
        }

        // Vignette
        using (var vignette = new SKPaint())
        {
            vignette.Shader = SKShader.CreateRadialGradient(
                new SKPoint(w / 2f, h / 2f), Math.Max(w, h) * 0.75f,
                new[] { new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 150) },
                null, SKShaderTileMode.Clamp);
            canvas.DrawRect(new SKRect(0, 0, w, h), vignette);
        }

        // Fine noise
        var rnd = new Random(20260101);
        using (var noise = new SKPaint { Color = new SKColor(255, 255, 255, 8), StrokeWidth = 1 })
        {
            for (int i = 0; i < w * h / 900; i++)
            {
                canvas.DrawPoint(rnd.Next(w), rnd.Next(h), noise);
            }
        }

        return surface.Snapshot();
    }

    private static void DrawBlob(SKCanvas canvas, float cx, float cy, float radius, SKColor color, float alpha)
    {
        using var paint = new SKPaint();
        paint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy), radius,
            new[]
            {
                color.WithAlpha((byte)(255 * alpha)),
                color.WithAlpha((byte)(120 * alpha)),
                color.WithAlpha(0)
            },
            new[] { 0f, 0.45f, 1f },
            SKShaderTileMode.Clamp);
        canvas.DrawCircle(cx, cy, radius, paint);
    }

    /// <summary>Downsamples to 1/2 then blurs: the "background downsample blur" stage of the pipeline.</summary>
    private static SKImage RenderBlurred(SKImage sharp, int w, int h)
    {
        int dw = Math.Max(1, w / 2), dh = Math.Max(1, h / 2);
        var info = new SKImageInfo(dw, dh, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateBlur(18f, 18f, SKShaderTileMode.Clamp)
        };
        canvas.DrawImage(sharp, new SKRect(0, 0, dw, dh), paint);
        return surface.Snapshot();
    }

    /// <summary>Re-composites after a resize without forcing a full rebuild on every frame.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _sharp?.Dispose();
            _blurred?.Dispose();
            _sharp = null;
            _blurred = null;
            _width = 0;
            _height = 0;
        }
    }
}
