using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DS4Handler.App.ViewModels;

namespace DS4Handler.App.Controls;

/// <summary>Maps a <see cref="Level"/> string to a brush. ConverterParameter "bg" gives the soft background.</summary>
public sealed class LevelBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool bg = parameter as string == "bg";
        string key = (value as string) switch
        {
            Level.Ok => bg ? "SuccessSoftBrush" : "SuccessBrush",
            Level.Warn => bg ? "WarningSoftBrush" : "WarningBrush",
            Level.Bad => bg ? "DangerSoftBrush" : "DangerBrush",
            Level.Learning => bg ? "AccentSoftBrush" : "AccentHoverBrush",
            _ => bg ? "Surface2Brush" : "TextDimBrush",
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
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
        _ => "uit",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
