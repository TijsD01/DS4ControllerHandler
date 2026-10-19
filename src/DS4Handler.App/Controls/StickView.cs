using System.Windows;
using System.Windows.Media;

namespace DS4Handler.App.Controls;

/// <summary>
/// Draws one analog stick on a square plate: the factory range (circular gate), the learned deadzone
/// around the learned rest point, the raw position (hollow grey ring) and what the game gets (solid accent dot).
/// Rendering uses frozen, shared brushes and pens; only changed values cause a redraw.
/// </summary>
public sealed class StickView : FrameworkElement
{
    private static readonly Brush PlateBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x11, 0x14, 0x1A)));
    private static readonly Brush GuidedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x16, 0x20, 0x3A)));
    private static readonly Pen PlatePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x2B, 0x30, 0x3B))), 1));
    private static readonly Pen GuidedPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF))), 1));
    private static readonly Pen CrossPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x2B))), 1));
    private static readonly Pen GatePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x2B, 0x30, 0x3B))), 1));
    private static readonly Brush DeadzoneBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x33, 0xF5, 0xA5, 0x24)));
    private static readonly Pen DeadzonePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xA5, 0x24))), 1));
    private static readonly Pen RawPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xA3, 0xAB, 0xBA))), 1.5));
    private static readonly Brush OutBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)));

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
        // Square: as wide as allowed (176 when unconstrained), never taller than the available height.
        double w = double.IsInfinity(availableSize.Width) ? 176 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? w : availableSize.Height;
        double s = Math.Min(w, h);
        return new Size(s, s);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 16) return;
        var mid = new Point(ActualWidth / 2, ActualHeight / 2);
        double half = size / 2;
        double r = half - 8;

        // Plate (square, radius 4) with crosshair and circular gate. Half-pixel offsets keep 1 px lines crisp.
        var plate = new Rect(mid.X - half + 0.5, mid.Y - half + 0.5, size - 1, size - 1);
        dc.DrawRoundedRectangle(Guided ? GuidedBrush : PlateBrush, Guided ? GuidedPen : PlatePen, plate, 4, 4);
        double cx = Math.Floor(mid.X) + 0.5, cy = Math.Floor(mid.Y) + 0.5;
        dc.DrawLine(CrossPen, new Point(mid.X - r, cy), new Point(mid.X + r, cy));
        dc.DrawLine(CrossPen, new Point(cx, mid.Y - r), new Point(cx, mid.Y + r));
        dc.DrawEllipse(null, GatePen, mid, r, r);

        var center = ToPoint(mid, r, CenterX, CenterY);
        double dz = Math.Clamp(Deadzone, 0, 1) * r;
        if (dz > 0.5) dc.DrawEllipse(DeadzoneBrush, DeadzonePen, center, dz, dz);

        var raw = ToPoint(mid, r, RawX, RawY);
        dc.DrawEllipse(null, RawPen, raw, 6, 6);

        var output = ToPoint(mid, r, OutX, OutY);
        dc.DrawEllipse(OutBrush, null, output, 5, 5);
    }

    private static Point ToPoint(Point mid, double r, double x, double y) =>
        new(mid.X + Math.Clamp(x, -1.1, 1.1) * r, mid.Y - Math.Clamp(y, -1.1, 1.1) * r);

    private static DependencyProperty Register(string name) => DependencyProperty.Register(
        name, typeof(double), typeof(StickView), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
