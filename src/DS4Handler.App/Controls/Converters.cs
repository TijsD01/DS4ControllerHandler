using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DS4Handler.App.ViewModels;

namespace DS4Handler.App.Controls;

/// <summary>
/// Maps a <see cref="Level"/> string to a theme brush. Without parameter: the dot colour.
/// ConverterParameter "text": a colour that is readable as text (≥ 4.5:1 on cards).
/// ConverterParameter "bg": a soft background for that level.
/// </summary>
public sealed class LevelBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string mode = parameter as string ?? "";
        string key = (value as string, mode) switch
        {
            (Level.Ok, "text") => "OkTextBrush",
            (Level.Ok, "bg") => "OkStripBrush",
            (Level.Ok, _) => "OkBrush",
            (Level.Warn or Level.Learning, "bg") => "WarnStripBrush",
            (Level.Warn or Level.Learning, _) => "WarnBrush",
            (Level.Bad, "text") => "DangerTextBrush",
            (Level.Bad, "bg") => "BadStripBrush",
            (Level.Bad, _) => "BadBrush",
            (Level.Info, "bg") => "InfoStripBrush",
            (Level.Info, _) => "LinkBrush",
            (_, "text") => "TextDimBrush",
            (_, "bg") => "CardBrush",
            _ => "OffBrush",
        };
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// For responsive layouts: an available width (usually a panel's ActualWidth) becomes a column count
/// for a UniformGrid. 2 when the width is at least ConverterParameter (pixels, default 1000), else 1.
/// </summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double threshold = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : 1000;
        return value is double width && width >= threshold ? 2 : 1;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Maps a <see cref="Level"/> string to an icon glyph (Segoe Fluent Icons).</summary>
public sealed class LevelIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string) switch
    {
        Level.Ok => "",       // check
        Level.Warn => "",     // warning
        Level.Bad => "",      // error
        Level.Learning => "", // processing
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>true → Visible; with ConverterParameter "invert" the other way round.</summary>
public sealed class BoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool v = value is true;
        if (parameter as string == "invert") v = !v;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Collapsed for null or empty strings; with ConverterParameter "invert" only visible for empty ones.</summary>
public sealed class TextVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool visible = !string.IsNullOrEmpty(value as string);
        if (parameter as string == "invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Maps a <see cref="Level"/> string to a short Dutch status word ("in orde", "leert", ...).</summary>
public sealed class LevelWordConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string) switch
    {
        Level.Ok => "in orde",
        Level.Warn => "let op",
        Level.Bad => "probleem",
        Level.Learning => "leert",
        Level.Info => "info",
        _ => "uit",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
