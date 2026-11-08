using System.Windows;
using System.Windows.Media.Imaging;

namespace DS4Handler.App.Infrastructure;

/// <summary>
/// The app icon (Assets/Logo/app.ico, built by make_icon.py). The same file is the exe icon
/// (ApplicationIcon in the .csproj), so the taskbar, Explorer, shortcuts and the tray all match.
/// </summary>
public static class AppIcon
{
    private static readonly Uri Source = new("pack://application:,,,/Assets/Logo/app.ico");

    /// <summary>All sizes of the icon; WPF picks the right one for the title bar and the taskbar.</summary>
    public static BitmapFrame ForWindow() => BitmapFrame.Create(Source);

    /// <summary>The tray size for this screen's DPI, so it stays sharp.</summary>
    public static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = Application.GetResourceStream(Source).Stream;
        return new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }
}
