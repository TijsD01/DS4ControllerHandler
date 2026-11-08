using System.Runtime.InteropServices;
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

    /// <summary>All sizes of the icon; WPF picks the right one for the title bar and the taskbar. Null (WPF default) if it can't load.</summary>
    public static BitmapFrame? ForWindow()
    {
        try
        {
            // OnLoad decodes now, so a broken resource fails here instead of later during rendering.
            return BitmapFrame.Create(Source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex)
        {
            Log.Warn("App-icoon kon niet geladen worden, standaardicoon wordt gebruikt.", ex);
            return null;
        }
    }

    /// <summary>
    /// The small-icon size at the primary monitor's current DPI (the notification area lives on the primary taskbar).
    /// Not SystemInformation.SmallIconSize: in this PerMonitorV2 app that is the startup system DPI and never changes.
    /// </summary>
    public static int TrayIconSize()
    {
        try
        {
            var monitor = MonitorFromPoint(default, MonitorDefaultToPrimary); // the primary monitor always contains (0,0)
            if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint dpi, out _) == 0
                && GetSystemMetricsForDpi(SmCxSmIcon, dpi) is > 0 and var size)
                return size;
        }
        catch (Exception ex)
        {
            Log.Warn("DPI van het hoofdscherm onbekend.", ex);
        }
        return System.Windows.Forms.SystemInformation.SmallIconSize.Width;
    }

    /// <summary>The tray frame for <paramref name="size"/>; falls back to the Windows application icon so startup never fails on it.</summary>
    public static System.Drawing.Icon CreateTrayIcon(int size)
    {
        try
        {
            var resource = Application.GetResourceStream(Source) ?? throw new InvalidOperationException("Resource ontbreekt: " + Source);
            using var stream = resource.Stream;
            return new System.Drawing.Icon(stream, size, size);
        }
        catch (Exception ex)
        {
            Log.Warn("Tray-icoon kon niet geladen worden, standaardicoon wordt gebruikt.", ex);
            return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone(); // clone: the caller disposes it
        }
    }

    private const uint MonitorDefaultToPrimary = 1;
    private const int MdtEffectiveDpi = 0;
    private const int SmCxSmIcon = 49;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
