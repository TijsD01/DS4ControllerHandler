using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace DS4Handler.Mac.Controls;

/// <summary>
/// One trigger as an inset track: a thin grey line for how far it is physically pressed (raw), a thicker
/// accent bar for the calibrated value, and orange marks for the learned rest point and end stop.
/// Port of the Windows TriggerBar.
/// </summary>
public sealed class TriggerBar : Control
{
    private const double TrackHeight = 14;

    private sealed class Palette
    {
        public readonly IBrush Track = ThemeBrush.Get("InsetBrush");
        public readonly IPen TrackPen = new ImmutablePen(ThemeBrush.Get("InsetBorderBrush").ToImmutable(), 1);
        public readonly IBrush Raw = ThemeBrush.Get("RawBrush");
        public readonly IBrush Output = ThemeBrush.Get("AccentBrush");
        public readonly IBrush Mark = ThemeBrush.Get("WarnBrush");
    }

    private static Palette? _palette;
    private static Palette P => _palette ??= new Palette();

    private (double Raw, double Output, double RestPoint, double EndStop) _v = (0, 0, 0, 1);

    /// <summary>All values are fractions 0..1 of the factory range (output: of the calibrated range).</summary>
    public void Set(double raw, double output, double rest, double endStop)
    {
        var v = (raw, output, rest, endStop);
        if (v == _v) return;
        _v = v;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, TrackHeight);

    public override void Render(DrawingContext dc)
    {
        var p = P;
        double w = Bounds.Width;
        if (w <= 8) return;

        dc.DrawRectangle(p.Track, p.TrackPen, new Rect(0.5, 0.5, w - 1, TrackHeight - 1), 4, 4);
        using (dc.PushClip(new RoundedRect(new Rect(1, 1, w - 2, TrackHeight - 2), 3)))
        {
            double inner = w - 2;
            double raw = Math.Clamp(_v.Raw, 0, 1);
            if (raw > 0.003) dc.DrawRectangle(p.Raw, null, new Rect(1, 2, Math.Max(2, inner * raw), 3));
            double output = Math.Clamp(_v.Output, 0, 1);
            if (output > 0.003) dc.DrawRectangle(p.Output, null, new Rect(1, TrackHeight - 2 - 6, Math.Max(2, inner * output), 6), 1, 1);

            dc.DrawRectangle(p.Mark, null, new Rect(Marker(_v.RestPoint, inner), 0, 2, TrackHeight));
            dc.DrawRectangle(p.Mark, null, new Rect(Marker(_v.EndStop, inner), 0, 2, TrackHeight));
        }
    }

    private static double Marker(double v, double inner) => 1 + Math.Min(Math.Round(Math.Clamp(v, 0, 1) * inner), inner - 2);
}
