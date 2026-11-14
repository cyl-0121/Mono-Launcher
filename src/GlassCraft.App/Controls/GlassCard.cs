using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GlassCraft.App.Services;

namespace GlassCraft.App.Controls;

/// <summary>Flat white card with a light border, matching the PCL-style light theme.</summary>
public class GlassCard : Decorator
{
    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<GlassCard, double>(nameof(CornerRadius), 8);

    public static readonly StyledProperty<double> TintOpacityProperty =
        AvaloniaProperty.Register<GlassCard, double>(nameof(TintOpacity), 1.0);

    public static readonly StyledProperty<Color> TintColorProperty =
        AvaloniaProperty.Register<GlassCard, Color>(nameof(TintColor), Color.FromArgb(255, 13, 13, 13));

    public static readonly StyledProperty<Color> BorderColorProperty =
        AvaloniaProperty.Register<GlassCard, Color>(nameof(BorderColor), Color.FromArgb(255, 38, 38, 38));

    public static readonly StyledProperty<bool> ElevatedProperty =
        AvaloniaProperty.Register<GlassCard, bool>(nameof(Elevated), true);

    public static readonly StyledProperty<Thickness> PaddingProperty =
        AvaloniaProperty.Register<GlassCard, Thickness>(nameof(Padding), default);

    public double CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public double TintOpacity { get => GetValue(TintOpacityProperty); set => SetValue(TintOpacityProperty, value); }
    public Color TintColor { get => GetValue(TintColorProperty); set => SetValue(TintColorProperty, value); }
    public Color BorderColor { get => GetValue(BorderColorProperty); set => SetValue(BorderColorProperty, value); }
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

        if (tier != GlassTier.LowPower)
        {
            var border = new Pen(new SolidColorBrush(BorderColor), 1);
            context.DrawRectangle(null, border, new RoundedRect(new Rect(0.5, 0.5, size.Width - 1, size.Height - 1), radius));
        }
    }
}
