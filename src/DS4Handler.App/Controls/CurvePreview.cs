using System.Windows;
using System.Windows.Media;
using DS4Handler.Core.Tuning;

namespace DS4Handler.App.Controls;

/// <summary>
/// Response curve of one stick or trigger section: input (how far you push, left to right) against
/// output (what the game gets, bottom to top). Grey band: the extra deadzone; dashed diagonal: 1 to 1;
/// accent line: the real response, computed with <see cref="InputShaper.ShapeResponse(float, float, float, float, float, float)"/>,
/// the same function the engine uses, so the preview cannot drift from the output. Redraws whenever
/// a value changes (bind the values to the sliders for a live preview).
/// </summary>
public sealed class CurvePreview : FrameworkElement
{
    private const double Pad = 10;

    private sealed class Palette
    {
        public readonly Brush Plate = ThemeBrushes.Get("InsetBrush", 0x0A, 0x10, 0x20);
        public readonly Pen PlatePen = ThemeBrushes.Pen(ThemeBrushes.Get("InsetBorderBrush", 0x26, 0x32, 0x4A), 1);
        public readonly Brush Band = ThemeBrushes.Get("DeadzoneBandBrush", 0x1A, 0x23, 0x36);
        public readonly Pen DiagonalPen = ThemeBrushes.Pen(ThemeBrushes.Get("InsetBorderBrush", 0x26, 0x32, 0x4A), 1,
            ThemeBrushes.Freeze(new DashStyle(new double[] { 3, 4 }, 0)));
        public readonly Pen CurvePen;

        public Palette()
        {
            var pen = new Pen(ThemeBrushes.Get("AccentBrush", 0x4C, 0x8D, 0xFF), 2.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            CurvePen = ThemeBrushes.Freeze(pen);
        }
    }

    private static Palette? _palette;
    private static Palette P => _palette ??= new Palette();

    public static readonly DependencyProperty InnerDeadzoneProperty = Register(nameof(InnerDeadzone), 0.0);
    public static readonly DependencyProperty OuterDeadzoneProperty = Register(nameof(OuterDeadzone), 0.0);
    public static readonly DependencyProperty AntiDeadzoneProperty = Register(nameof(AntiDeadzone), 0.0);
    public static readonly DependencyProperty CurveProperty = Register(nameof(Curve), 1.0);
    public static readonly DependencyProperty SensitivityProperty = Register(nameof(Sensitivity), 1.0);

    /// <summary>Extra deadzone, 0..0.9.</summary>
    public double InnerDeadzone { get => (double)GetValue(InnerDeadzoneProperty); set => SetValue(InnerDeadzoneProperty, value); }

    /// <summary>Full output this much before the end ("Eindpunt"), 0..0.5.</summary>
    public double OuterDeadzone { get => (double)GetValue(OuterDeadzoneProperty); set => SetValue(OuterDeadzoneProperty, value); }

    /// <summary>Anti-deadzone, 0..0.9.</summary>
    public double AntiDeadzone { get => (double)GetValue(AntiDeadzoneProperty); set => SetValue(AntiDeadzoneProperty, value); }

    /// <summary>Curve exponent; 1 = straight.</summary>
    public double Curve { get => (double)GetValue(CurveProperty); set => SetValue(CurveProperty, value); }

    /// <summary>Sensitivity multiplier (sticks only; leave 1 for triggers).</summary>
    public double Sensitivity { get => (double)GetValue(SensitivityProperty); set => SetValue(SensitivityProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 150 : Math.Min(availableSize.Width, 150);
        double h = double.IsInfinity(availableSize.Height) ? w : availableSize.Height;
        double s = Math.Min(w, h);
        return new Size(s, s);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var p = P;
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Pad * 3) return;
        dc.DrawRoundedRectangle(p.Plate, p.PlatePen, new Rect(0.5, 0.5, size - 1, size - 1), 6, 6);

        double g = size - 2 * Pad; // graph side
        Point Map(double x, double y) => new(Pad + x * g, Pad + (1 - y) * g);

        float inner = (float)Math.Clamp(InnerDeadzone, 0, 1);
        float outer = (float)Math.Clamp(OuterDeadzone, 0, 1);
        float anti = (float)Math.Clamp(AntiDeadzone, 0, 1);
        float curve = (float)Curve, sensitivity = (float)Sensitivity;

        if (inner > 0) dc.DrawRectangle(p.Band, null, new Rect(Pad, Pad, inner * g, g));
        dc.DrawLine(p.DiagonalPen, Map(0, 0), Map(1, 1));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(Map(0, 0), false, false);
            if (inner > 0) ctx.LineTo(Map(inner, 0), true, true);
            int steps = Math.Max(48, (int)g);
            for (int i = 0; i <= steps; i++)
            {
                float x = inner + (1 - inner) * i / steps;
                if (i == 0) x = MathF.BitIncrement(inner); // just outside the deadzone: shows an anti-deadzone jump
                float y = InputShaper.ShapeResponse(x, inner, outer, anti, curve, sensitivity);
                ctx.LineTo(Map(x, y), true, true);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, p.CurvePen, geometry);
    }

    private static DependencyProperty Register(string name, double def) => DependencyProperty.Register(
        name, typeof(double), typeof(CurvePreview), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));
}
