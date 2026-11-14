using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GlassCraft.App.Services;

namespace GlassCraft.App.Controls;

/// <summary>
/// Flat Minecraft-style card: dark panel, 1px border, subtle top highlight and bottom shadow.
/// The previous liquid-glass pipeline was removed on request.
/// </summary>
public class GlassCard : Decorator
{
    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<GlassCard, double>(nameof(CornerRadius), 4);

    public static readonly StyledProperty<double> TintOpacityProperty =
        AvaloniaProperty.Register<GlassCard, double>(nameof(TintOpacity), 0.94);

    public static readonly StyledProperty<Color> TintColorProperty =
        AvaloniaProperty.Register<GlassCard, Color>(nameof(TintColor), Color.FromArgb(255, 34, 34, 34));

    public static readonly StyledProperty<bool> ElevatedProperty =
        AvaloniaProperty.Register<GlassCard, bool>(nameof(Elevated), true);

    public static readonly StyledProperty<Thickness> PaddingProperty =
        AvaloniaProperty.Register<GlassCard, Thickness>(nameof(Padding), default);

    public double CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public double TintOpacity { get => GetValue(TintOpacityProperty); set => SetValue(TintOpacityProperty, value); }
    public Color TintColor { get => GetValue(TintColorProperty); set => SetValue(TintColorProperty, value); }
    public bool Elevated { get => GetValue(ElevatedProperty); set => SetValue(ElevatedProperty, value); }
    public Thickness Padding { get => GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    public GlassCard()
    {
        ClipToBounds = false;
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
        if (tier == GlassTier.Off) return;

        var rect = new Rect(size);
        var radius = CornerRadius;
        context.DrawRectangle(new SolidColorBrush(TintColor, TintOpacity), null, new RoundedRect(rect, radius));

        var border = new Pen(new SolidColorBrush(Color.FromArgb(255, 58, 58, 58)), 1);
        context.DrawRectangle(null, border, new RoundedRect(new Rect(0, 0, size.Width - 1, size.Height - 1), radius));

        if (tier == GlassTier.HighPerformance)
        {
            var highlight = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 1);
            context.DrawLine(highlight, new Point(radius + 1, 1), new Point(size.Width - radius - 1, 1));
        }
        if (tier is GlassTier.HighPerformance or GlassTier.Balanced)
        {
            var shadow = new Pen(new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), 1);
            context.DrawLine(shadow, new Point(radius + 1, size.Height - 1.5),
                new Point(size.Width - radius - 1, size.Height - 1.5));
        }
    }
}
