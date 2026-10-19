using System.Windows;
using System.Windows.Media;

namespace DS4Handler.App.Controls;

/// <summary>
/// Draws one trigger as two horizontal bars: on top how far the trigger physically goes (raw), with
/// the learned end stop marked; below what the game gets after calibration.
/// </summary>
public sealed class TriggerBar : FrameworkElement
{
    private static readonly Brush TrackBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x23, 0x27, 0x30)));
    private static readonly Brush RawBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x86)));
    private static readonly Brush OutBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)));
    private static readonly Brush FullBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x3C, 0xCB, 0x82)));
    private static readonly Brush EndStopBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xA5, 0x24)));
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
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, 26);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w <= 4) return;

        // Raw bar (6 px, top), with the learned rest value and the end stop tick.
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 4, w, 6), 1, 1);
        if (Rest > 0.005) dc.DrawRoundedRectangle(RestBrush, null, new Rect(0, 4, w * Math.Clamp(Rest, 0, 1), 6), 1, 1);
        double raw = Math.Clamp(Raw, 0, 1);
        if (raw > 0.005) dc.DrawRoundedRectangle(RawBrush, null, new Rect(0, 4, Math.Max(2, w * raw), 6), 1, 1);
        double stop = Math.Min(Math.Round(Math.Clamp(EndStop, 0, 1) * w), w - 2);
        dc.DrawRectangle(EndStopBrush, null, new Rect(stop, 0, 2, 14));

        // Output bar (12 px, bottom): accent, green at full strength.
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 14, w, 12), 2, 2);
        double output = Math.Clamp(Output, 0, 1);
        if (output > 0.005)
            dc.DrawRoundedRectangle(output >= 0.995 ? FullBrush : OutBrush, null, new Rect(0, 14, Math.Max(4, w * output), 12), 2, 2);
    }

    private static DependencyProperty Register(string name, double def) => DependencyProperty.Register(
        name, typeof(double), typeof(TriggerBar), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
