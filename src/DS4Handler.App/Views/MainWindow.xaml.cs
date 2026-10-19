using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DS4Handler.App.ViewModels;

namespace DS4Handler.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel, ImageSource icon)
    {
        InitializeComponent();
        DataContext = viewModel;
        Icon = icon;
        SourceInitialized += (_, _) => UseDarkTitleBar();
    }

    /// <summary>Set by the app when it really shuts down; until then closing only hides the window.</summary>
    public bool AllowClose { get; set; }

    /// <summary>The user clicked "Afsluiten".</summary>
    public event EventHandler? QuitRequested;

    /// <summary>The window was closed to the tray.</summary>
    public event EventHandler? HiddenToTray;

    /// <summary>True while live values are worth updating.</summary>
    public bool IsShowing => IsVisible && WindowState != WindowState.Minimized;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            // Keep running in the tray, like DS4Windows: the controllers keep working.
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
        }
        base.OnClosing(e);
    }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;  // bring to front even when another app has focus
        Topmost = false;
        Focus();
    }

    private void OnQuitClick(object sender, RoutedEventArgs e) => QuitRequested?.Invoke(this, EventArgs.Empty);

    private void UseDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int on = 1;
            // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 on Windows 11 / 10 20H1+, 19 on older Windows 10 builds.
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
        catch
        {
            // Cosmetic only.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
