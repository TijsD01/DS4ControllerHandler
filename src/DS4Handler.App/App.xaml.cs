using System.Windows;
using System.Windows.Threading;
using DS4Handler.App.Infrastructure;
using DS4Handler.App.Services;
using DS4Handler.App.ViewModels;
using DS4Handler.App.Views;

namespace DS4Handler.App;

/// <summary>
/// Startup and shutdown: single instance, tray icon, the main window and the UI poll timer.
/// The engine runs on its own threads; the UI only polls snapshots (~30 Hz while the window is
/// visible, once per second while it is hidden in the tray).
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\DS4Handler.SingleInstance";
    private const string ShowEventName = @"Local\DS4Handler.Show";
    private static readonly TimeSpan FastTick = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan SlowTick = TimeSpan.FromSeconds(1);

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private AppHost? _host;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private DispatcherTimer? _timer;
    private bool _quitting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--unhide", StringComparer.OrdinalIgnoreCase))
        {
            // Started by the uninstaller after it killed the app: the normal exit never ran, so give back
            // the controllers this app hid. Runs before the single-instance check; no window, no tray.
            var store = new SettingsStore();
            try { new HidHideService(store).UnhideAllAsync().Wait(TimeSpan.FromSeconds(10)); }
            catch (Exception ex) { Log.Warn("Controllers zichtbaar maken (verwijderen) mislukt", ex); }
            store.Save();
            Log.Info("Controllers zichtbaar gemaakt (verwijderen).");
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Already running: bring that window to the front instead of starting twice.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(ShowWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Onverwachte fout (achtergrond)", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Onverwachte fout (taak)", args.Exception);
            args.SetObserved();
        };

        ConfigureToolTips();
        Log.Info("Gestart.");

        _host = new AppHost();
        _viewModel = new MainViewModel(_host, new Dialogs(() => _window));
        _window = new MainWindow(_viewModel, AppIcon.Render(64));
        _window.QuitRequested += (_, _) => Quit();
        _window.HiddenToTray += (_, _) => OnHiddenToTray();
        _window.IsVisibleChanged += (_, _) => UpdateTimerSpeed();
        _window.StateChanged += (_, _) => UpdateTimerSpeed();

        _tray = new TrayIcon(
            show: ShowWindow,
            setLearning: enabled => _viewModel.SetLearningAll(enabled),
            isLearning: () => _viewModel.AllLearning,
            quit: Quit);

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = SlowTick };
        _timer.Tick += OnTick;
        _timer.Start();

        _host.Start();

        // Keep the Run entry pointing at this exe (the app may have been moved or updated).
        if (_host.SettingsStore.Read(s => s.StartWithWindows)) _host.SetStartWithWindows(true);

        bool startHidden = _host.SettingsStore.Read(s => s.StartMinimized)
                           || e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        if (!startHidden) ShowWindow();
    }

    /// <summary>Tooltips are the help system of this app: show them quickly and keep them up while reading.</summary>
    private static void ConfigureToolTips()
    {
        try
        {
            System.Windows.Controls.ToolTipService.ShowDurationProperty.OverrideMetadata(
                typeof(DependencyObject), new FrameworkPropertyMetadata(60_000));
            System.Windows.Controls.ToolTipService.InitialShowDelayProperty.OverrideMetadata(
                typeof(DependencyObject), new FrameworkPropertyMetadata(350));
        }
        catch (Exception ex)
        {
            Log.Warn("Tooltip-instellingen aanpassen mislukt", ex);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_viewModel == null || _window == null || _tray == null) return;
        try
        {
            _viewModel.Tick(_window.IsShowing);
            _tray.Text = _viewModel.TrayText;
        }
        catch (Exception ex)
        {
            Log.Error("Fout bij bijwerken van het venster", ex);
        }
    }

    private void UpdateTimerSpeed()
    {
        if (_timer == null || _window == null) return;
        var wanted = _window.IsShowing ? FastTick : SlowTick;
        if (_timer.Interval != wanted) _timer.Interval = wanted;
    }

    private void ShowWindow()
    {
        if (_quitting || _window == null) return;
        _window.ShowAndActivate();
        UpdateTimerSpeed();
    }

    private void OnHiddenToTray()
    {
        if (_host == null || _tray == null) return;
        if (_host.SettingsStore.Read(s => s.TrayHintShown)) return;
        _host.SettingsStore.Update(s => s.TrayHintShown = true);
        _tray.ShowBalloon("DS4 Handler draait door",
            "Je controllers blijven werken. Klik op het icoon in het systeemvak om het venster weer te openen, of kies daar Afsluiten.");
    }

    private void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _timer?.Stop();
        if (_window != null)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        _tray?.Dispose();
        _tray = null;
        ShutdownHost();
        Shutdown();
    }

    private void ShutdownHost()
    {
        try { _host?.Dispose(); }
        catch (Exception ex) { Log.Error("Afsluiten ging niet netjes", ex); }
        _host = null;
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows logs off or shuts down: save profiles and unplug the virtual pads.
        base.OnSessionEnding(e);
        Quit();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        ShutdownHost();
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
        }
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Onverwachte fout", e.Exception);
        e.Handled = true;
        MessageBox.Show(
            "Er ging iets mis: " + e.Exception.Message + "\n\nDe app draait door. Meer details staan in het logbestand:\n" + Log.FilePath,
            "DS4 Handler", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
