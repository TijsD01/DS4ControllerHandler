using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DS4Handler.App.Infrastructure;

/// <summary>Draws the app icon (accent tile with a gamepad) in code, so no binary asset is needed.</summary>
public static class AppIcon
{
    private static readonly Geometry Pad = Geometry.Parse(
        "M7,7 C4.2,7 2.6,9.4 2.1,12.6 L1.4,17.2 C1.1,19.3 2.2,21 3.9,21 C5.1,21 5.9,20.2 6.7,19 L7.7,17.6 " +
        "C8.1,17.1 8.6,16.8 9.2,16.8 L14.8,16.8 C15.4,16.8 15.9,17.1 16.3,17.6 L17.3,19 C18.1,20.2 18.9,21 20.1,21 " +
        "C21.8,21 22.9,19.3 22.6,17.2 L21.9,12.6 C21.4,9.4 19.8,7 17,7 Z");

    /// <summary>Renders the icon at <paramref name="size"/> pixels.</summary>
    public static BitmapSource Render(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double s = size;
            var tile = new LinearGradientBrush(Color.FromRgb(0x5A, 0x98, 0xFF), Color.FromRgb(0x2F, 0x5F, 0xE0), 45);
            dc.DrawRoundedRectangle(tile, null, new Rect(0, 0, s, s), s * 0.22, s * 0.22);

            dc.PushTransform(new TranslateTransform(s * 0.12, s * 0.06));
            dc.PushTransform(new ScaleTransform(s * 0.76 / 24, s * 0.76 / 24));
            dc.DrawGeometry(Brushes.White, null, Pad);
            var dark = new SolidColorBrush(Color.FromRgb(0x2F, 0x5F, 0xE0));
            // D-pad and buttons.
            dc.DrawRectangle(dark, null, new Rect(5.2, 11.0, 4.0, 1.4));
            dc.DrawRectangle(dark, null, new Rect(6.5, 9.7, 1.4, 4.0));
            dc.DrawEllipse(dark, null, new Point(16.8, 10.6), 0.95, 0.95);
            dc.DrawEllipse(dark, null, new Point(18.6, 12.4), 0.95, 0.95);
            dc.DrawEllipse(dark, null, new Point(15.0, 12.4), 0.95, 0.95);
            dc.DrawEllipse(dark, null, new Point(16.8, 14.2), 0.95, 0.95);
            dc.Pop();
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The same picture as a Win32 icon for the tray.</summary>
    public static System.Drawing.Icon CreateTrayIcon()
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Render(32)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var bitmap = new System.Drawing.Bitmap(stream);
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var temp = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)temp.Clone(); // owns its own copy, so the handle can be freed
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
