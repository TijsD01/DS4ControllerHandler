using System.Windows;
using System.Windows.Media;

namespace DS4Handler.App.Controls;

/// <summary>
/// Draws one trigger as an inset track: a thin grey line on top for how far the trigger physically
/// goes (raw), a thicker accent bar below for what the game gets, and orange markers for the learned
/// rest point and end stop.
/// </summary>
public sealed class TriggerBar : FrameworkElement
{
    private const double TrackHeight = 14;

    private sealed class Palette
    {
        public readonly Brush Track = ThemeBrushes.Get("InsetBrush", 0x0A, 0x10, 0x20);
        public readonly Pen TrackPen = ThemeBrushes.Pen(ThemeBrushes.Get("InsetBorderBrush", 0x26, 0x32, 0x4A), 1);
        public readonly Brush Raw = ThemeBrushes.Get("RawBrush", 0x6B, 0x7C, 0x96);
        public readonly Brush Output = ThemeBrushes.Get("AccentBrush", 0x4C, 0x8D, 0xFF);
        public readonly Brush Mark = ThemeBrushes.Get("WarnBrush", 0xF5, 0xA5, 0x24);
    }

    private static Palette? _palette;
    private static Palette P => _palette ??= new Palette();

    public static readonly DependencyProperty RawProperty = Register(nameof(Raw), 0.0);
    public static readonly DependencyProperty OutputProperty = Register(nameof(Output), 0.0);
    public static readonly DependencyProperty EndStopProperty = Register(nameof(EndStop), 1.0);
    public static readonly DependencyProperty RestProperty = Register(nameof(Rest), 0.0);

    /// <summary>Raw trigger position, 0..1 of the factory range.</summary>
    public double Raw { get => (double)GetValue(RawProperty); set => SetValue(RawProperty, value); }

    /// <summary>Calibrated value sent to the game, 0..1.</summary>
    public double Output { get => (double)GetValue(OutputProperty); set => SetValue(OutputProperty, value); }

    /// <summary>Learned end stop, 0..1 of the factory range.</summary>
    public double EndStop { get => (double)GetValue(EndStopProperty); set => SetValue(EndStopProperty, value); }

    /// <summary>Learned rest value, 0..1 of the factory range.</summary>
    public double Rest { get => (double)GetValue(RestProperty); set => SetValue(RestProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, TrackHeight);

    protected override void OnRender(DrawingContext dc)
    {
        var p = P;
        double w = ActualWidth;
        if (w <= 8) return;

        var track = new Rect(0.5, 0.5, w - 1, TrackHeight - 1);
        dc.DrawRoundedRectangle(p.Track, p.TrackPen, track, 4, 4);
        dc.PushClip(new RectangleGeometry(new Rect(1, 1, w - 2, TrackHeight - 2), 3, 3));

        double inner = w - 2;
        double raw = Math.Clamp(Raw, 0, 1);
        if (raw > 0.003) dc.DrawRectangle(p.Raw, null, new Rect(1, 2, Math.Max(2, inner * raw), 3));
        double output = Math.Clamp(Output, 0, 1);
        if (output > 0.003) dc.DrawRoundedRectangle(p.Output, null, new Rect(1, TrackHeight - 2 - 6, Math.Max(2, inner * output), 6), 1, 1);

        // Rest point and end stop.
        dc.DrawRectangle(p.Mark, null, new Rect(Marker(Rest, inner), 0, 2, TrackHeight));
        dc.DrawRectangle(p.Mark, null, new Rect(Marker(EndStop, inner), 0, 2, TrackHeight));
        dc.Pop();
    }

    private static double Marker(double v, double inner) => 1 + Math.Min(Math.Round(Math.Clamp(v, 0, 1) * inner), inner - 2);

    private static DependencyProperty Register(string name, double def) => DependencyProperty.Register(
        name, typeof(double), typeof(TriggerBar), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));
}
