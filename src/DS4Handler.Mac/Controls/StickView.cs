using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace DS4Handler.Mac.Controls;

/// <summary>
/// One analog stick on a rounded inset square: the factory range (circle), a crosshair, the learned
/// deadzone as a dashed orange ring around the learned rest point, the raw position (grey dot) and the
/// calibrated position (accent dot). Port of the Windows StickView; values come from code, and only a
/// change causes a redraw.
/// </summary>
public sealed class StickView : Control
{
    private sealed class Palette
    {
        public readonly IBrush Plate = ThemeBrush.Get("InsetBrush");
        public readonly IBrush GuidedPlate = ThemeBrush.Get("InfoStripBrush");
        public readonly IBrush Raw = ThemeBrush.Get("RawBrush");
        public readonly IBrush Output = ThemeBrush.Get("AccentBrush");
        public readonly IPen PlatePen = new ImmutablePen(ThemeBrush.Get("InsetBorderBrush").ToImmutable(), 1);
        public readonly IPen GuidedPen = new ImmutablePen(ThemeBrush.Get("AccentBrush").ToImmutable(), 1);
        public readonly IPen GatePen = new ImmutablePen(ThemeBrush.Get("GateBrush").ToImmutable(), 1);
        public readonly IPen CrossPen = new ImmutablePen(ThemeBrush.Get("CrosshairBrush").ToImmutable(), 1);
        public readonly IPen DeadzonePen = new ImmutablePen(ThemeBrush.Get("WarnBrush").ToImmutable(), 1, new ImmutableDashStyle(new double[] { 3, 3 }, 0));
    }

    private static Palette? _palette;
    private static Palette P => _palette ??= new Palette();

    private (double RawX, double RawY, double OutX, double OutY, double CenterX, double CenterY, double Deadzone, bool Guided) _v;

    /// <summary>Positions are -1..1 with up positive; deadzone is a radius 0..1; guided tints the plate blue.</summary>
    public void Set(double rawX, double rawY, double outX, double outY, double centerX, double centerY, double deadzone, bool guided)
    {
        var v = (rawX, rawY, outX, outY, centerX, centerY, deadzone, guided);
        if (v == _v) return;
        _v = v;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 180 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? w : availableSize.Height;
        double s = Math.Min(w, h);
        return new Size(s, s);
    }

    public override void Render(DrawingContext dc)
    {
        var p = P;
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 24) return;
        var mid = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double half = size / 2;
        double r = half * 0.86;

        // Half-pixel offsets keep 1 px lines crisp.
        var plate = new Rect(mid.X - half + 0.5, mid.Y - half + 0.5, size - 1, size - 1);
        dc.DrawRectangle(_v.Guided ? p.GuidedPlate : p.Plate, _v.Guided ? p.GuidedPen : p.PlatePen, plate, 8, 8);
        double cx = Math.Floor(mid.X) + 0.5, cy = Math.Floor(mid.Y) + 0.5;
        dc.DrawLine(p.CrossPen, new Point(mid.X - r, cy), new Point(mid.X + r, cy));
        dc.DrawLine(p.CrossPen, new Point(cx, mid.Y - r), new Point(cx, mid.Y + r));
        dc.DrawEllipse(null, p.GatePen, mid, r, r);

        double dz = Math.Clamp(_v.Deadzone, 0, 1) * r;
        if (dz > 0.5) dc.DrawEllipse(null, p.DeadzonePen, ToPoint(mid, r, _v.CenterX, _v.CenterY), Math.Max(dz, 3), Math.Max(dz, 3));

        dc.DrawEllipse(p.Raw, null, ToPoint(mid, r, _v.RawX, _v.RawY), 5, 5);
        dc.DrawEllipse(p.Output, null, ToPoint(mid, r, _v.OutX, _v.OutY), 6, 6);
    }

    private static Point ToPoint(Point mid, double r, double x, double y) =>
        new(mid.X + Math.Clamp(x, -1.1, 1.1) * r, mid.Y - Math.Clamp(y, -1.1, 1.1) * r);
}
