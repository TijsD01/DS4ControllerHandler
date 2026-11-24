using Avalonia;
using Avalonia.Media;

namespace DS4Handler.Mac.Controls;

/// <summary>Brushes from App.axaml for the controls that draw themselves, so they use the same tokens as the XAML.</summary>
internal static class ThemeBrush
{
    public static IBrush Get(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Magenta;
}
