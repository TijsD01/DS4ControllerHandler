using System.Windows;
using System.Windows.Media;
using DS4Handler.App.ViewModels;

namespace DS4Handler.App.Controls;

/// <summary>
/// Round status mark with a sign inside: a check when ok, "!" for a warning, a cross for a problem,
/// three dots while checking and a dash when off. Same colours as the status dots (<see cref="LevelBrushConverter"/>).
/// </summary>
public sealed class StatusMark : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(string), typeof(StatusMark),
        new FrameworkPropertyMetadata(ViewModels.Level.Off, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly LevelBrushConverter Brushes = new();

    // Signs drawn in a 16×16 box; geometry is parsed once and frozen.
    private static readonly Geometry Check = Frozen("M4.6,8.3 L7,10.6 L11.4,5.6");
    private static readonly Geometry Cross = Frozen("M5.5,5.5 L10.5,10.5 M10.5,5.5 L5.5,10.5");
    private static readonly Geometry Bang = Frozen("M8,4.4 L8,8.9");
    private static readonly Geometry Dash = Frozen("M5,8 L11,8");

    public StatusMark()
    {
        Width = 16;
        Height = 16;
        VerticalAlignment = VerticalAlignment.Center;
        SnapsToDevicePixels = true;
    }

    public string Level
    {
        get => (string)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var fill = (Brush)Brushes.Convert(Level, typeof(Brush), null!, null!);
        // The page background colour as ink: dark on the bright fills reads at 7:1 or more; white would not.
        var ink = ThemeBrushes.Get("BgBrush", 0x0F, 0x17, 0x2A);
        double s = Math.Min(ActualWidth, ActualHeight) / 16;
        dc.PushTransform(new ScaleTransform(s, s));
        dc.DrawEllipse(fill, null, new Point(8, 8), 8, 8);

        var pen = new Pen(ink, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        switch (Level)
        {
            case ViewModels.Level.Ok: dc.DrawGeometry(null, pen, Check); break;
            case ViewModels.Level.Bad: dc.DrawGeometry(null, pen, Cross); break;
            case ViewModels.Level.Warn:
                dc.DrawGeometry(null, pen, Bang);
                dc.DrawEllipse(ink, null, new Point(8, 11.5), 1.1, 1.1);
                break;
            case ViewModels.Level.Learning:
                for (int i = -1; i <= 1; i++) dc.DrawEllipse(ink, null, new Point(8 + i * 3.2, 8), 1.1, 1.1);
                break;
            default: dc.DrawGeometry(null, pen, Dash); break;
        }
        dc.Pop();
    }

    private static Geometry Frozen(string data) => ThemeBrushes.Freeze(Geometry.Parse(data));
}
