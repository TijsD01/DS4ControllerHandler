using System.Windows;
using System.Windows.Media;

namespace DS4Handler.App.Controls;

/// <summary>
/// Theme colours for the controls that draw themselves (StickView, TriggerBar, CurvePreview), read
/// once from Themes/Theme.xaml so those controls use the same tokens as the XAML. The fallback is only
/// used when no application resources exist (designer). Everything returned is frozen.
/// </summary>
internal static class ThemeBrushes
{
    public static Brush Get(string key, byte r, byte g, byte b)
    {
        if (Application.Current?.TryFindResource(key) is Brush found)
            return found.IsFrozen ? found : Freeze(found.Clone());
        return Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));
    }

    public static Pen Pen(Brush brush, double thickness, DashStyle? dash = null)
    {
        var pen = new Pen(brush, thickness);
        if (dash != null) pen.DashStyle = dash;
        return Freeze(pen);
    }

    public static T Freeze<T>(T f) where T : Freezable
    {
        if (f.CanFreeze) f.Freeze();
        return f;
    }
}
