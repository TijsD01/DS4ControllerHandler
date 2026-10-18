using DS4Handler.App.Infrastructure;
using DS4Handler.Core.Input;
using DS4Handler.Core.Profiles;
using HidSharp;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace DS4Handler.App.Engine;

/// <summary>Whether virtual Xbox controllers can be made.</summary>
public enum VirtualDriverState
{
    Unknown,
    Ready,
    Missing,
    Failed,
}

/// <summary>
/// Finds DualShock 4 / DualSense controllers, keeps one <see cref="ControllerSession"/> per physical
/// pad and follows hot-plug events. All device I/O runs on worker threads, never under the state
/// lock and never on the UI thread. The UI polls <see cref="Version"/> and <see cref="Sessions"/>.
/// </summary>
public sealed class ControllerManager : IDisposable
{
    private static readonly TimeSpan RescanAfterError = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan RetryBlocked = TimeSpan.FromSeconds(5);

    private readonly object _lock = new();       // guards the collections below; held only briefly
    private readonly object _scanGate = new();   // one scan at a time; I/O happens under this, not _lock
    private readonly Dictionary<string, ControllerSession> _sessions = new(StringComparer.OrdinalIgnoreCase); // by device path
    private readonly Dictionary<string, DateTime> _blockedUntil = new(StringComparer.OrdinalIgnoreCase);     // paths that failed to open
    private readonly HashSet<string> _duplicates = new(StringComparer.OrdinalIgnoreCase);                     // second interface of an open pad
    private readonly ProfileStore _store;
    private readonly Func<string, (bool Learning, OutputMode Output)> _optionsFor;
    private ViGEmClient? _vigem;
    private ControllerSession[] _sessionList = Array.Empty<ControllerSession>();
    private int _scanRequested;
    private int _version;
    private bool _started;
    private bool _disposed;
    private Timer? _rescanTimer;

    /// <param name="optionsFor">Per controller id (from the settings): whether learning starts on, and which virtual pad to make.</param>
    public ControllerManager(ProfileStore store, Func<string, (bool Learning, OutputMode Output)> optionsFor)
    {
        _store = store;
        _optionsFor = optionsFor;
    }

    public VirtualDriverState DriverState { get; private set; } = VirtualDriverState.Unknown;

    /// <summary>Technical reason when the ViGEm client failed (other than "not installed").</summary>
    public string? DriverError { get; private set; }

    /// <summary>Changes whenever a controller is added or removed, or the blocked list changes.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>The running sessions; a new array is published on every change, so it is safe to iterate.</summary>
    public IReadOnlyList<ControllerSession> Sessions => Volatile.Read(ref _sessionList);

    /// <summary>Controllers that are plugged in but could not be opened (another program holds them).</summary>
    public int BlockedCount { get { lock (_lock) return _blockedUntil.Count; } }

    /// <summary>Raised on a worker thread after a session started (used for HidHide).</summary>
    public event Action<ControllerSession>? SessionStarted;

    /// <summary>Connects to ViGEmBus and starts watching for controllers. Returns immediately.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_started || _disposed) return;
            _started = true;
        }
        _rescanTimer = new Timer(_ => RequestScan(), null, Timeout.Infinite, Timeout.Infinite);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                ConnectDriver();
                DeviceList.Local.Changed += OnDeviceListChanged;
                RequestScan();
            }
            catch (Exception ex)
            {
                Log.Error("Starten van de controllerdetectie mislukt", ex);
            }
        });
    }

    private void ConnectDriver()
    {
        try
        {
            _vigem = new ViGEmClient();
            DriverState = VirtualDriverState.Ready;
        }
        catch (VigemBusNotFoundException)
        {
            DriverState = VirtualDriverState.Missing;
            Log.Warn("ViGEmBus is niet geïnstalleerd.");
        }
        catch (Exception ex)
        {
            DriverError = ex.Message;
            DriverState = VirtualDriverState.Failed;
            Log.Error("ViGEmBus openen mislukt", ex);
        }
        Bump();
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e) => RequestScan();

    /// <summary>Asks for a scan; requests that arrive during a scan are folded into one extra pass.</summary>
    public void RequestScan()
    {
        if (Interlocked.Exchange(ref _scanRequested, 1) == 1) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                lock (_scanGate)
                {
                    Interlocked.Exchange(ref _scanRequested, 0);
                    Scan();
                }
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _scanRequested, 0);
                Log.Error("Controllers zoeken mislukt", ex);
            }
        });
    }

    private void ScheduleRescan(TimeSpan delay)
    {
        try { _rescanTimer?.Change(delay, Timeout.InfiniteTimeSpan); } catch (ObjectDisposedException) { }
    }

    /// <summary>Opens sessions for new controllers and closes sessions for removed ones. Runs under _scanGate.</summary>
    private void Scan()
    {
        var present = new Dictionary<string, (HidDevice Device, ControllerModel Model, ConnectionType Connection)>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in DeviceList.Local.GetHidDevices(ReportParser.SonyVendorId))
        {
            var model = ReportParser.ModelForProductId(device.ProductID);
            if (model == null) continue;
            int inputLength;
            try { inputLength = device.GetMaxInputReportLength(); } catch { continue; }
            if (inputLength <= 0) continue; // not the gamepad collection
            // USB reports are 64 bytes; over Bluetooth the descriptor is larger (DS4 547, DualSense 78).
            var connection = inputLength > 64 ? ConnectionType.Bluetooth : ConnectionType.Usb;
            present[device.DevicePath] = (device, model.Value, connection);
        }

        var removed = new List<ControllerSession>();
        bool changed = false;
        lock (_lock)
        {
            if (_disposed) return;
            foreach (var path in _sessions.Keys.ToList())
            {
                if (present.ContainsKey(path)) continue;
                removed.Add(_sessions[path]);
                _sessions.Remove(path);
                changed = true;
            }
            foreach (var path in _blockedUntil.Keys.ToList())
                if (!present.ContainsKey(path)) { _blockedUntil.Remove(path); changed = true; }
            _duplicates.RemoveWhere(p => !present.ContainsKey(p));
            if (changed) Publish();
        }
        foreach (var s in removed) DisposeSession(s);
        if (removed.Count > 0) lock (_lock) _duplicates.Clear(); // the other interface of that pad may now be used

        bool retryLater = false;
        foreach (var (path, info) in present)
        {
            lock (_lock)
            {
                if (_disposed) return;
                if (_sessions.ContainsKey(path) || _duplicates.Contains(path)) continue;
                if (_blockedUntil.TryGetValue(path, out var until) && until > DateTime.UtcNow) { retryLater = true; continue; }
            }

            var session = new ControllerSession(info.Device, info.Model, info.Connection);
            try
            {
                session.Open();
            }
            catch (Exception ex)
            {
                bool firstFailure;
                lock (_lock) firstFailure = !_blockedUntil.ContainsKey(path);
                if (firstFailure) Log.Warn($"Controller openen mislukt ({path}); wordt elke paar seconden opnieuw geprobeerd", ex);
                session.Dispose(); // nothing was loaded, so nothing is saved
                lock (_lock) { _blockedUntil[path] = DateTime.UtcNow + RetryBlocked; if (firstFailure) Bump(); }
                retryLater = true;
                continue;
            }

            bool duplicate;
            lock (_lock)
            {
                duplicate = _sessions.Values.Any(s => string.Equals(s.Id, session.Id, StringComparison.OrdinalIgnoreCase));
                if (duplicate) _duplicates.Add(path);
                if (_blockedUntil.Remove(path)) Bump();
            }
            if (duplicate)
            {
                // Same pad on cable while still paired over Bluetooth: keep the session we already have.
                Log.Info($"{session.Id} is al verbonden; tweede verbinding ({info.Connection}) wordt genegeerd.");
                session.Dispose();
                continue;
            }

            session.Ended += OnSessionEnded; // before Start, so an instant read error is not missed
            try
            {
                var options = _optionsFor(session.Id);
                session.Start(_store, _vigem, options.Learning, options.Output);
            }
            catch (Exception ex)
            {
                Log.Error($"Controller {session.Id} starten mislukt", ex);
                session.Ended -= OnSessionEnded;
                session.Dispose();
                lock (_lock) { _blockedUntil[path] = DateTime.UtcNow + RetryBlocked; Bump(); }
                retryLater = true;
                continue;
            }

            lock (_lock)
            {
                if (_disposed)
                {
                    DisposeSession(session);
                    return;
                }
                _sessions[path] = session;
                Publish();
            }
            Log.Info($"{session.DisplayName} verbonden: {session.Id} via {session.Connection}");
            try { SessionStarted?.Invoke(session); } catch (Exception ex) { Log.Error("SessionStarted-handler faalde", ex); }
        }

        if (retryLater) ScheduleRescan(RetryBlocked);
    }

    private void OnSessionEnded(object? sender, EventArgs e)
    {
        if (sender is not ControllerSession session) return;
        // Raised on the session's own read thread; hand off so Dispose can join it.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                bool removed;
                lock (_lock)
                {
                    removed = _sessions.TryGetValue(session.DevicePath, out var current) && current == session
                              && _sessions.Remove(session.DevicePath);
                    if (removed)
                    {
                        _duplicates.Clear();
                        Publish();
                    }
                }
                DisposeSession(session);
                if (removed)
                {
                    Log.Info($"{session.Id} verbroken.");
                    // The device may still be listed (read error, Bluetooth hiccup): look again shortly.
                    ScheduleRescan(RescanAfterError);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Afhandelen van verbroken controller mislukt", ex);
            }
        });
    }

    private void DisposeSession(ControllerSession session)
    {
        session.Ended -= OnSessionEnded;
        try { session.Dispose(); } catch (Exception ex) { Log.Error($"Sessie {session.Id} sluiten mislukt", ex); }
    }

    /// <summary>Must be called under _lock.</summary>
    private void Publish()
    {
        Volatile.Write(ref _sessionList, _sessions.Values.ToArray());
        Bump();
    }

    private void Bump() => Interlocked.Increment(ref _version);

    public void SaveAll()
    {
        foreach (var s in Sessions) s.SaveProfile();
    }

    /// <summary>Saves all profiles, unplugs the virtual controllers and stops watching.</summary>
    public void Dispose()
    {
        ControllerSession[] sessions;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            sessions = _sessions.Values.ToArray();
            _sessions.Clear();
            Publish();
        }
        DeviceList.Local.Changed -= OnDeviceListChanged;
        _rescanTimer?.Dispose();
        foreach (var s in sessions) DisposeSession(s);
        try { _vigem?.Dispose(); } catch { }
    }
}
