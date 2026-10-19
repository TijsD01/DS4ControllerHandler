using System.Windows;
using System.Windows.Media;

namespace DS4Handler.App.Controls;

/// <summary>
/// Draws one analog stick on a rounded inset square: the factory range (outer circle), a crosshair,
/// the learned deadzone as a dashed orange ring around the learned rest point, the raw position (grey
/// dot) and what the game gets (accent dot). Brushes and pens are frozen and shared; only changed
/// values cause a redraw.
/// </summary>
public sealed class StickView : FrameworkElement
{
    private sealed class Palette
    {
        public readonly Brush Plate = ThemeBrushes.Get("InsetBrush", 0x0A, 0x10, 0x20);
        public readonly Brush GuidedPlate = ThemeBrushes.Get("InfoStripBrush", 0x13, 0x25, 0x4A);
        public readonly Pen PlatePen;
        public readonly Pen GuidedPen;
        public readonly Pen GatePen;
        public readonly Pen CrossPen;
        public readonly Pen DeadzonePen;
        public readonly Brush Raw = ThemeBrushes.Get("RawBrush", 0x6B, 0x7C, 0x96);
        public readonly Brush Output = ThemeBrushes.Get("AccentBrush", 0x4C, 0x8D, 0xFF);

        public Palette()
        {
            PlatePen = ThemeBrushes.Pen(ThemeBrushes.Get("InsetBorderBrush", 0x26, 0x32, 0x4A), 1);
            GuidedPen = ThemeBrushes.Pen(Output, 1);
            GatePen = ThemeBrushes.Pen(ThemeBrushes.Get("GateBrush", 0x2C, 0x3A, 0x55), 1);
            CrossPen = ThemeBrushes.Pen(ThemeBrushes.Get("CrosshairBrush", 0x1E, 0x29, 0x40), 1);
            DeadzonePen = ThemeBrushes.Pen(ThemeBrushes.Get("WarnBrush", 0xF5, 0xA5, 0x24), 1,
                ThemeBrushes.Freeze(new DashStyle(new double[] { 3, 3 }, 0)));
        }
    }

    private static Palette? _palette;
    private static Palette P => _palette ??= new Palette();

    public static readonly DependencyProperty RawXProperty = Register(nameof(RawX));
    public static readonly DependencyProperty RawYProperty = Register(nameof(RawY));
    public static readonly DependencyProperty OutXProperty = Register(nameof(OutX));
    public static readonly DependencyProperty OutYProperty = Register(nameof(OutY));
    public static readonly DependencyProperty CenterXProperty = Register(nameof(CenterX));
    public static readonly DependencyProperty CenterYProperty = Register(nameof(CenterY));
    public static readonly DependencyProperty DeadzoneProperty = Register(nameof(Deadzone));
    public static readonly DependencyProperty GuidedProperty = DependencyProperty.Register(
        nameof(Guided), typeof(bool), typeof(StickView), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Raw position, -1..1, up positive.</summary>
    public double RawX { get => (double)GetValue(RawXProperty); set => SetValue(RawXProperty, value); }
    public double RawY { get => (double)GetValue(RawYProperty); set => SetValue(RawYProperty, value); }

    /// <summary>Calibrated position sent to the game, -1..1, up positive.</summary>
    public double OutX { get => (double)GetValue(OutXProperty); set => SetValue(OutXProperty, value); }
    public double OutY { get => (double)GetValue(OutYProperty); set => SetValue(OutYProperty, value); }

    /// <summary>Learned rest point, -1..1, up positive.</summary>
    public double CenterX { get => (double)GetValue(CenterXProperty); set => SetValue(CenterXProperty, value); }
    public double CenterY { get => (double)GetValue(CenterYProperty); set => SetValue(CenterYProperty, value); }

    /// <summary>Deadzone radius, 0..1.</summary>
    public double Deadzone { get => (double)GetValue(DeadzoneProperty); set => SetValue(DeadzoneProperty, value); }

    /// <summary>The guided re-centre is running (the plate is tinted blue with an accent border).</summary>
    public bool Guided { get => (bool)GetValue(GuidedProperty); set => SetValue(GuidedProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Square: as wide as allowed (200 when unconstrained), never taller than the available height.
        double w = double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? w : availableSize.Height;
        double s = Math.Min(w, h);
        return new Size(s, s);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var p = P;
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 24) return;
        var mid = new Point(ActualWidth / 2, ActualHeight / 2);
        double half = size / 2;
        double r = half * 0.86; // outer circle, as in the design (86 of 100)

        // Plate (radius 8) with crosshair and circular gate. Half-pixel offsets keep 1 px lines crisp.
        var plate = new Rect(mid.X - half + 0.5, mid.Y - half + 0.5, size - 1, size - 1);
        dc.DrawRoundedRectangle(Guided ? p.GuidedPlate : p.Plate, Guided ? p.GuidedPen : p.PlatePen, plate, 8, 8);
        double cx = Math.Floor(mid.X) + 0.5, cy = Math.Floor(mid.Y) + 0.5;
        dc.DrawLine(p.CrossPen, new Point(mid.X - r, cy), new Point(mid.X + r, cy));
        dc.DrawLine(p.CrossPen, new Point(cx, mid.Y - r), new Point(cx, mid.Y + r));
        dc.DrawEllipse(null, p.GatePen, mid, r, r);

        var center = ToPoint(mid, r, CenterX, CenterY);
        double dz = Math.Clamp(Deadzone, 0, 1) * r;
        if (dz > 0.5) dc.DrawEllipse(null, p.DeadzonePen, center, Math.Max(dz, 3), Math.Max(dz, 3));

        dc.DrawEllipse(p.Raw, null, ToPoint(mid, r, RawX, RawY), 5, 5);
        dc.DrawEllipse(p.Output, null, ToPoint(mid, r, OutX, OutY), 6, 6);
    }

    private static Point ToPoint(Point mid, double r, double x, double y) =>
        new(mid.X + Math.Clamp(x, -1.1, 1.1) * r, mid.Y - Math.Clamp(y, -1.1, 1.1) * r);

    private static DependencyProperty Register(string name) => DependencyProperty.Register(
        name, typeof(double), typeof(StickView), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
}
