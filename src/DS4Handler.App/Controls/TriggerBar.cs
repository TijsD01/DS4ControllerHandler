using System.Windows;
using System.Windows.Media;

namespace DS4Handler.App.Controls;

/// <summary>
/// Draws one trigger as two horizontal bars: on top how far the trigger physically goes (raw), with
/// the learned end stop marked; below what the game gets after calibration.
/// </summary>
public sealed class TriggerBar : FrameworkElement
{
    private static readonly Brush TrackBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x28, 0x2D, 0x38)));
    private static readonly Brush RawBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x7D, 0x86, 0x98)));
    private static readonly Brush OutBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)));
    private static readonly Brush FullBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x3C, 0xCB, 0x82)));
    private static readonly Pen EndStopPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xA5, 0x24))), 2.5));
    private static readonly Brush RestBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x60, 0xF5, 0xA5, 0x24)));

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
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, 34);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w <= 4) return;

        // Raw bar (thin, top).
        var rawTrack = new Rect(0, 2, w, 10);
        dc.DrawRoundedRectangle(TrackBrush, null, rawTrack, 5, 5);
        if (Rest > 0.005) dc.DrawRoundedRectangle(RestBrush, null, new Rect(0, 2, w * Math.Clamp(Rest, 0, 1), 10), 5, 5);
        double raw = Math.Clamp(Raw, 0, 1);
        if (raw > 0.005) dc.DrawRoundedRectangle(RawBrush, null, new Rect(0, 2, Math.Max(10, w * raw), 10), 5, 5);
        double stop = Math.Clamp(EndStop, 0, 1) * w;
        dc.DrawLine(EndStopPen, new Point(Math.Min(stop, w - 1.25), 0), new Point(Math.Min(stop, w - 1.25), 14));

        // Output bar (thick, bottom).
        var outTrack = new Rect(0, 18, w, 14);
        dc.DrawRoundedRectangle(TrackBrush, null, outTrack, 7, 7);
        double output = Math.Clamp(Output, 0, 1);
        if (output > 0.005)
            dc.DrawRoundedRectangle(output >= 0.995 ? FullBrush : OutBrush, null, new Rect(0, 18, Math.Max(14, w * output), 14), 7, 7);
    }

    private static DependencyProperty Register(string name, double def) => DependencyProperty.Register(
        name, typeof(double), typeof(TriggerBar), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
