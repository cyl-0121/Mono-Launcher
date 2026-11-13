using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using GlassCraft.App.Services;
using SkiaSharp;

namespace GlassCraft.App.Controls;

/// <summary>
/// Native liquid-glass surface. Everything is drawn through Skia (Avalonia's render backend):
/// no WebView, no HTML/CSS. Pipeline stages: downsample+blur backdrop, tint, rounded refraction,
/// top edge highlight, optional pointer lighting.
/// </summary>
public class GlassCard : Decorator
{
    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<GlassCard, double>(nameof(CornerRadius), 18);

    public static readonly StyledProperty<double> BlurAmountProperty =
        AvaloniaProperty.Register<GlassCard, double>(nameof(BlurAmount), 1.0);

    public static readonly StyledProperty<double> TintOpacityProperty =
        AvaloniaProperty.Register<GlassCard, double>(nameof(TintOpacity), 0.42);

    public static readonly StyledProperty<Color> TintColorProperty =
        AvaloniaProperty.Register<GlassCard, Color>(nameof(TintColor), Color.FromArgb(255, 22, 30, 52));

    public static readonly StyledProperty<double> RefractionProperty =
        AvaloniaProperty.Register<GlassCard, double>(nameof(Refraction), 10);

    public static readonly StyledProperty<double> HighlightProperty =
        AvaloniaProperty.Register<GlassCard, double>(nameof(Highlight), 0.9);

    public static readonly StyledProperty<Thickness> PaddingProperty =
        AvaloniaProperty.Register<GlassCard, Thickness>(nameof(Padding), default);

    public Thickness Padding { get => GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    public double CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public double BlurAmount { get => GetValue(BlurAmountProperty); set => SetValue(BlurAmountProperty, value); }
    public double TintOpacity { get => GetValue(TintOpacityProperty); set => SetValue(TintOpacityProperty, value); }
    public Color TintColor { get => GetValue(TintColorProperty); set => SetValue(TintColorProperty, value); }
    public double Refraction { get => GetValue(RefractionProperty); set => SetValue(RefractionProperty, value); }
    public double Highlight { get => GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }

    private Point _pointer = new(-1000, -1000);
    private bool _pointerInside;

    public GlassCard()
    {
        IsHitTestVisible = true;
        ClipToBounds = false;
        AddHandler(PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerEnteredEvent, (_, _) => { _pointerInside = true; InvalidateVisual(); }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerExitedEvent, (_, _) => { _pointerInside = false; InvalidateVisual(); }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!PerformanceState.AnimationsEnabled) return;
        _pointer = e.GetPosition(this);
        _pointerInside = true;
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var child = Child;
        if (child is null) return new Size(padding.Left + padding.Right, padding.Top + padding.Bottom);
        var inner = new Size(
            Math.Max(0, availableSize.Width - padding.Left - padding.Right),
            Math.Max(0, availableSize.Height - padding.Top - padding.Bottom));
        child.Measure(inner);
        return new Size(child.DesiredSize.Width + padding.Left + padding.Right,
            child.DesiredSize.Height + padding.Top + padding.Bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var padding = Padding;
        Child?.Arrange(new Rect(
            padding.Left, padding.Top,
            Math.Max(0, finalSize.Width - padding.Left - padding.Right),
            Math.Max(0, finalSize.Height - padding.Top - padding.Bottom)));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Bounds.Size;
        if (size.Width < 4 || size.Height < 4) return;

        var tier = PerformanceState.Tier;
        if (tier == GlassTier.Off)
        {
            context.FillRectangle(new SolidColorBrush(TintColor, TintOpacity + 0.2), new Rect(size));
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        var scaling = topLevel?.RenderScaling ?? 1.0;
        var origin = this.TranslatePoint(new Point(0, 0), topLevel as Visual) ?? new Point(0, 0);
        var windowSize = topLevel?.ClientSize ?? new Size(1280, 800);
        if (windowSize.Width < 10) windowSize = new Size(1280, 800);

        var backdropWidth = (int)Math.Round(windowSize.Width * scaling);
        var backdropHeight = (int)Math.Round(windowSize.Height * scaling);
        GlassBackdrop.Ensure(backdropWidth, backdropHeight);

        var op = new GlassDrawOperation
        {
            Bounds = new Rect(size),
            CornerRadius = (float)(CornerRadius * scaling),
            Origin = new Point(origin.X * scaling, origin.Y * scaling),
            WindowSize = new Size(windowSize.Width * scaling, windowSize.Height * scaling),
            Tint = TintColor,
            TintOpacity = (float)TintOpacity,
            Tier = tier,
            BlurAmount = (float)BlurAmount,
            Refraction = (float)(Refraction * scaling),
            Highlight = (float)Highlight,
            Pointer = _pointerInside ? new Point(_pointer.X * scaling, _pointer.Y * scaling) : null,
            PointerLighting = _pointerInside && PerformanceState.PointerLighting,
            CardId = GetHashCode()
        };
        context.Custom(op);
    }

    private sealed class GlassDrawOperation : ICustomDrawOperation
    {
        public Rect Bounds { get; init; }
        public float CornerRadius { get; init; }
        public Point Origin { get; init; }
        public Size WindowSize { get; init; }
        public Color Tint { get; init; }
        public float TintOpacity { get; init; }
        public GlassTier Tier { get; init; }
        public float BlurAmount { get; init; }
        public float Refraction { get; init; }
        public float Highlight { get; init; }
        public Point? Pointer { get; init; }
        public bool PointerLighting { get; init; }
        public int CardId { get; init; }

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null) return;
            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            if (canvas is null) return;

            var w = (float)Bounds.Width;
            var h = (float)Bounds.Height;
            var rect = new SKRect(0, 0, w, h);
            var rrect = new SKRoundRect(rect, CornerRadius);

            var sharp = GlassBackdrop.Sharp;
            var blurred = GlassBackdrop.Blurred;
            var bw = Math.Max(1, GlassBackdrop.Width);
            var bh = Math.Max(1, GlassBackdrop.Height);
            var sx = bw / (float)Math.Max(1, WindowSize.Width);
            var sy = bh / (float)Math.Max(1, WindowSize.Height);

            var srcRect = new SKRect(
                (float)Origin.X * sx,
                (float)Origin.Y * sy,
                (float)(Origin.X + Bounds.Width) * sx,
                (float)(Origin.Y + Bounds.Height) * sy);

            canvas.Save();
            canvas.ClipRoundRect(rrect, SKClipOperation.Intersect, antialias: true);

            // Stage 1: backdrop (pre-blurred) + optional extra live blur for the high tier.
            if (blurred is not null && (Tier is GlassTier.HighPerformance or GlassTier.Balanced))
            {
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    FilterQuality = SKFilterQuality.Low,
                    ImageFilter = Tier == GlassTier.HighPerformance
                        ? SKImageFilter.CreateBlur(10f * BlurAmount, 10f * BlurAmount, SKShaderTileMode.Clamp)
                        : SKImageFilter.CreateBlur(4f * BlurAmount, 4f * BlurAmount, SKShaderTileMode.Clamp)
                };
                canvas.DrawImage(blurred, srcRect, rect, paint);
            }
            else if (sharp is not null && Tier == GlassTier.LowPower)
            {
                using var paint = new SKPaint { Color = SKColors.White.WithAlpha(40) };
                canvas.DrawImage(sharp, srcRect, rect, paint);
            }

            // Stage 2: tint overlay
            using (var tintPaint = new SKPaint
                   {
                       Color = new SKColor(Tint.R, Tint.G, Tint.B, (byte)Math.Clamp(TintOpacity * 255 * 1.15f, 0, 255))
                   })
            {
                canvas.DrawRect(rect, tintPaint);
            }

            // Vertical luminance gradient: darker at the bottom, airy at the top.
            using (var grad = new SKPaint())
            {
                grad.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(0, h),
                    new[] { new SKColor(255, 255, 255, 26), new SKColor(0, 0, 0, 40) },
                    null, SKShaderTileMode.Clamp);
                canvas.DrawRect(rect, grad);
            }

            // Stage 3: rounded refraction (bright inner rim, bent colour near edges)
            if (Tier is GlassTier.HighPerformance or GlassTier.Balanced && Refraction > 0)
            {
                using var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
                rim.StrokeWidth = Math.Max(1f, Refraction * 0.35f);
                rim.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(w, h),
                    new[]
                    {
                        new SKColor(180, 220, 255, 70),
                        new SKColor(120, 180, 255, 24),
                        new SKColor(180, 140, 255, 40)
                    },
                    null, SKShaderTileMode.Clamp);
                rim.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Refraction * 0.5f);
                canvas.DrawRoundRect(new SKRoundRect(
                    new SKRect(Refraction * 0.5f, Refraction * 0.5f, w - Refraction * 0.5f, h - Refraction * 0.5f),
                    Math.Max(0, CornerRadius - Refraction * 0.3f)), rim);
            }

            canvas.Restore();

            // Stage 4: edge highlight (1px crisp border + bright top arc)
            using (var border = new SKPaint
                   {
                       IsAntialias = true,
                       Style = SKPaintStyle.Stroke,
                       StrokeWidth = 1f
                   })
            {
                border.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(w, h),
                    new[]
                    {
                        new SKColor(255, 255, 255, (byte)(170 * Highlight)),
                        new SKColor(160, 200, 255, (byte)(60 * Highlight)),
                        new SKColor(255, 255, 255, (byte)(25 * Highlight))
                    },
                    new[] { 0f, 0.45f, 1f },
                    SKShaderTileMode.Clamp);
                canvas.DrawRoundRect(rrect, border);
            }

            // Top specular arc
            using (var spec = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f })
            {
                spec.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(w * 0.1f, 0), new SKPoint(w * 0.9f, 0),
                    new[]
                    {
                        new SKColor(255, 255, 255, 0),
                        new SKColor(255, 255, 255, (byte)(200 * Highlight)),
                        new SKColor(255, 255, 255, 0)
                    },
                    null, SKShaderTileMode.Clamp);
                canvas.DrawLine(CornerRadius * 1.4f, 1f, w - CornerRadius * 1.4f, 1f, spec);
            }

            // Stage 5: optional pointer lighting
            if (PointerLighting && Pointer is { } p)
            {
                using var light = new SKPaint();
                light.Shader = SKShader.CreateRadialGradient(
                    new SKPoint((float)p.X, (float)p.Y), Math.Max(w, h) * 0.55f,
                    new[]
                    {
                        new SKColor(140, 190, 255, 46),
                        new SKColor(140, 190, 255, 10),
                        new SKColor(140, 190, 255, 0)
                    },
                    new[] { 0f, 0.4f, 1f },
                    SKShaderTileMode.Clamp);
                light.BlendMode = SKBlendMode.Plus;
                canvas.DrawRoundRect(rrect, light);
            }
        }
    }
}

