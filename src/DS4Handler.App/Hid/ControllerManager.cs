using DS4Handler.Core.Input;
using DS4Handler.Core.Profiles;
using HidSharp;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace DS4Handler.App.Hid;

/// <summary>
/// Finds Sony controllers, keeps one <see cref="ControllerSession"/> per connected device and
/// follows hot-plug events, like DS4Windows does.
/// </summary>
public sealed class ControllerManager : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ControllerSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ProfileStore _store;
    private readonly ViGEmClient? _vigem;
    private bool _learningEnabled = true;
    private bool _disposed;

    public ControllerManager()
    {
        _store = new ProfileStore(ProfileStore.DefaultDirectory);
        try
        {
            _vigem = new ViGEmClient();
        }
        catch (VigemBusNotFoundException)
        {
            ViGEmError = "De ViGEmBus-driver is niet geïnstalleerd. Zonder deze driver kan er geen virtuele " +
                         "Xbox-controller worden gemaakt; de app laat dan alleen de waarden zien.\n\n" +
                         "Download ViGEmBus via https://github.com/nefarius/ViGEmBus/releases en start de app opnieuw.";
        }
        catch (Exception ex)
        {
            ViGEmError = $"ViGEmBus kon niet worden geopend: {ex.Message}";
        }
    }

    /// <summary>Set when no virtual controllers can be created (Dutch, for display).</summary>
    public string? ViGEmError { get; }

    public string ProfileDirectory => ProfileStore.DefaultDirectory;

    /// <summary>Raised from a background thread whenever a controller connects or disconnects.</summary>
    public event EventHandler? SessionsChanged;

    public IReadOnlyList<ControllerSession> Sessions
    {
        get { lock (_lock) return _sessions.Values.ToList(); }
    }

    /// <summary>Learning switch for all controllers; new controllers start with this value.</summary>
    public bool LearningEnabled
    {
        get => _learningEnabled;
        set
        {
            _learningEnabled = value;
            foreach (var s in Sessions) s.LearningEnabled = value;
        }
    }

    public void Start()
    {
        DeviceList.Local.Changed += OnDeviceListChanged;
        Refresh();
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e)
        => ThreadPool.QueueUserWorkItem(_ => Refresh());

    /// <summary>Opens sessions for new controllers and closes sessions for removed ones.</summary>
    public void Refresh()
    {
        var removed = new List<ControllerSession>();
        bool changed = false;

        lock (_lock)
        {
            if (_disposed) return;

            var present = new Dictionary<string, (HidDevice Device, ControllerModel Model)>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in DeviceList.Local.GetHidDevices(ReportParser.SonyVendorId))
            {
                var model = ReportParser.ModelForProductId(device.ProductID);
                if (model == null) continue;
                int inputLength;
                try { inputLength = device.GetMaxInputReportLength(); } catch { continue; }
                if (inputLength <= 0) continue; // not the gamepad collection
                present[device.DevicePath] = (device, model.Value);
            }

            foreach (var path in _sessions.Keys.Where(p => !present.ContainsKey(p)).ToList())
            {
                removed.Add(_sessions[path]);
                _sessions.Remove(path);
                changed = true;
            }

            foreach (var (path, info) in present)
            {
                if (_sessions.ContainsKey(path)) continue;
                var session = new ControllerSession(info.Device, info.Model, _store, _vigem, _learningEnabled);
                try
                {
                    session.Start();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Controller openen mislukt: {ex.Message}");
                    session.Dispose();
                    continue;
                }
                session.Ended += OnSessionEnded;
                _sessions[path] = session;
                changed = true;
            }
        }

        // Dispose outside the lock: it joins the read thread, which may be raising Ended.
        foreach (var s in removed)
        {
            s.Ended -= OnSessionEnded;
            s.Dispose();
        }
        if (changed) SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionEnded(object? sender, EventArgs e)
    {
        if (sender is not ControllerSession session) return;
        // Raised on the session's own read thread; hand off so Dispose can join it.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool removed;
            lock (_lock)
            {
                removed = _sessions.TryGetValue(session.DevicePath, out var current) && current == session
                          && _sessions.Remove(session.DevicePath);
            }
            session.Ended -= OnSessionEnded;
            session.Dispose();
            if (removed) SessionsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void SaveAll()
    {
        foreach (var s in Sessions) s.SaveProfile();
    }

    public void Dispose()
    {
        List<ControllerSession> sessions;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            sessions = _sessions.Values.ToList();
            _sessions.Clear();
        }
        DeviceList.Local.Changed -= OnDeviceListChanged;
        foreach (var s in sessions)
        {
            s.Ended -= OnSessionEnded;
            s.Dispose(); // saves the profile and unplugs the virtual controller
        }
        _vigem?.Dispose();
    }
}
