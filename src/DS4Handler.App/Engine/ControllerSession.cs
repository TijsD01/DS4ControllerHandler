using System.Diagnostics;
using DS4Handler.App.Infrastructure;
using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;
using DS4Handler.Core.Output;
using DS4Handler.Core.Profiles;
using DS4Handler.Core.Tuning;
using HidSharp;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace DS4Handler.App.Engine;

/// <summary>Which virtual controller games get.</summary>
public enum OutputMode
{
    /// <summary>Virtual Xbox 360 controller: works in almost every PC game.</summary>
    Xbox360,

    /// <summary>Virtual DualShock 4: for games with native PlayStation support (PlayStation button icons).</summary>
    DualShock4,
}

/// <summary>State of the virtual controller that belongs to a session.</summary>
public enum VirtualPadState
{
    Starting,
    Active,
    DriverMissing,
    Failed,
}

/// <summary>
/// One physical controller: reads its HID reports on a dedicated high-priority thread, runs the
/// adaptive calibration and feeds the result into a virtual Xbox 360 or DualShock 4 controller (ViGEm).
/// </summary>
/// <remarks>
/// Hot path (per input report, 250–1000 per second): read into a reused buffer, parse into structs,
/// calibrate under an uncontended lock, map and submit to ViGEm only when the output changed.
/// Nothing allocates and nothing is pushed to the UI; the UI copies <see cref="ControllerSnapshot"/>
/// at its own pace.
/// </remarks>
public sealed class ControllerSession : IDisposable
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);
    private const int ReadTimeoutMs = 1000;
    private const int WriteTimeoutMs = 200;
    private static readonly int LightbarDefault =
        (OutputReports.LightBar.R << 16) | (OutputReports.LightBar.G << 8) | OutputReports.LightBar.B;

    private readonly HidDevice _device;
    private readonly object _sync = new();          // guards the calibration and the snapshot fields
    private readonly CancellationTokenSource _cts = new();

    private HidStream? _stream;
    private Thread? _readThread;
    private Thread? _rumbleThread;
    private readonly AutoResetEvent _rumbleSignal = new(false);
    private int _rumbleWanted;                       // (large << 8) | small, written by the ViGEm callback
    private int _lightbarWanted = LightbarDefault;   // 0xRRGGBB, set by a virtual DS4's game
    private byte[] _outputBuffer = Array.Empty<byte>();
    private byte _outputSequence;

    private ControllerCalibration? _calibration;
    private ProfileStore? _store;
    private bool _profileLoaded;                     // never overwrite a saved profile we could not read
    private readonly object _saveGate = new();       // serialises saving with switching / replacing profiles
    private string? _appliedProfileId;               // profile the calibration belongs to (under _saveGate)
    private bool _appliedBlocked;
    private long _savedFingerprint;
    private Timer? _saveTimer;
    private int _tuningSavePending;                  // 1 while a tuning save is queued on the thread pool

    // Virtual pad. Swapped under _padLock (also taken per report, uncontended) so a switch never
    // races a submit on a target that is being unplugged.
    private readonly object _padLock = new();
    private ViGEmClient? _vigem;
    private IXbox360Controller? _xbox;
    private IDualShock4Controller? _ds4;
    private XboxReport _lastXbox;
    private Ds4OutputReport _lastDs4;
    private bool _hasSubmitted;
    private long _lastSubmitTimestamp;              // Stopwatch ticks of the last submit, for the output rate limit
    private int _desiredMode;                       // OutputMode the user wants; applied by a worker
    private volatile bool _reconnectAttempted;      // one automatic retry after the driver refused input

    // Snapshot fields (under _sync).
    private RawControllerState _raw;
    private CalibratedState _output;
    private bool _hasInput;
    private long _reports;
    private long _lastReportTimestamp;

    private int _disposed;

    public ControllerSession(HidDevice device, ControllerModel model, ConnectionType connection)
    {
        _device = device;
        Model = model;
        Connection = connection;
        DevicePath = device.DevicePath;
    }

    /// <summary>Stable profile id (MAC based); set by <see cref="Open"/>.</summary>
    public string Id { get; private set; } = "";

    /// <summary>Bluetooth MAC address ("A4:53:85:12:AB:CD"), when known.</summary>
    public string? Mac { get; private set; }

    /// <summary>False when no MAC could be read and the id hangs on the USB port.</summary>
    public bool HasStableId { get; private set; }

    private string? _profileId;

    /// <summary>
    /// The saved profile this controller learns into: its own id (default) or another controller's
    /// profile it is linked to. Set by the manager before <see cref="Start"/>; changed with <see cref="ApplyProfile"/>.
    /// </summary>
    public string ProfileId
    {
        get => Volatile.Read(ref _profileId) ?? Id;
        internal set => Volatile.Write(ref _profileId, value);
    }

    /// <summary>
    /// True when the profile is already used by another connected controller: this one then runs on
    /// factory values and never saves, so two controllers never write the same file.
    /// </summary>
    public bool ProfileBlocked
    {
        get => Volatile.Read(ref _profileBlocked);
        internal set => Volatile.Write(ref _profileBlocked, value);
    }

    private bool _profileBlocked;

    public string DevicePath { get; }
    public ControllerModel Model { get; }

    /// <summary>From the HID descriptor (report sizes), so it is known before the first report.</summary>
    public ConnectionType Connection { get; }

    public string DisplayName => Model == ControllerModel.DualShock4 ? "DualShock 4" : "DualSense";

    public VirtualPadState VirtualState { get; private set; } = VirtualPadState.Starting;

    /// <summary>The kind of virtual controller games get (or would get once the driver is there).</summary>
    public OutputMode OutputMode { get; private set; }

    /// <summary>Why the virtual controller failed, for display (Dutch); null when fine.</summary>
    public string? VirtualError { get; private set; }

    /// <summary>Raised on the read thread when the controller is gone (unplugged, link lost).</summary>
    public event EventHandler? Ended;

    public bool LearningEnabled
    {
        get { lock (_sync) return _calibration?.LearningEnabled ?? false; }
        set { lock (_sync) if (_calibration != null) _calibration.LearningEnabled = value; }
    }

    /// <summary>Stick jitter filter on/off for both sticks (see <see cref="StickCalibrator.JitterFilterEnabled"/>).</summary>
    public bool JitterFilterEnabled
    {
        get { lock (_sync) return _calibration?.JitterFilterEnabled ?? true; }
        set { lock (_sync) if (_calibration != null) _calibration.JitterFilterEnabled = value; }
    }

    /// <summary>The user tuning currently in effect (from the profile). Thread-safe snapshot.</summary>
    public InputTuning Tuning
    {
        get { lock (_sync) return _calibration?.Tuning ?? InputTuning.Default; }
    }

    /// <summary>True when the poll rate of the controller itself can be set (DualShock 4 over Bluetooth).</summary>
    public bool SupportsBluetoothPollRate => Model == ControllerModel.DualShock4 && Connection == ConnectionType.Bluetooth;

    /// <summary>False when changes are used live but not saved (profile in use elsewhere, or unreadable).</summary>
    public bool CanSaveTuning => _profileLoaded && !ProfileBlocked;

    private int _tuningGeneration;                   // bumped under _sync whenever the calibration / tuning is swapped

    /// <summary>
    /// Changes whenever the tuning is replaced from elsewhere: a profile switch (link, unlink, blocked
    /// profile released) or a copied / imported profile. Read it <em>before</em> <see cref="Tuning"/> and
    /// pass it to <see cref="ApplyTuning"/>, so values shown for one profile never land in another.
    /// </summary>
    public int TuningGeneration => Volatile.Read(ref _tuningGeneration);

    /// <summary>
    /// Uses new tuning at once (sanitized) and saves it into the profile, unless the tuning was replaced
    /// since <paramref name="expectedGeneration"/> was read (see <see cref="TuningGeneration"/>): then
    /// nothing changes and the result is false, and the caller should reload <see cref="Tuning"/>.
    /// Returns at once: the save runs on the thread pool (quick repeated calls coalesce into one save of
    /// the latest values) and a new Bluetooth poll interval is sent by the output writer. When
    /// <see cref="CanSaveTuning"/> is false the tuning is used live but not saved.
    /// </summary>
    public bool ApplyTuning(InputTuning tuning, int expectedGeneration)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        lock (_sync)
        {
            // Checked under the same lock that swaps the calibration, so it can never land on another profile.
            if (_calibration == null || _tuningGeneration != expectedGeneration) return false;
            _calibration.Tuning = tuning;
        }
        WakeOutputWriter(); // the poll interval is part of every Bluetooth output report
        if (!CanSaveTuning || Volatile.Read(ref _disposed) != 0) return true;
        if (Interlocked.Exchange(ref _tuningSavePending, 1) != 0) return true; // a queued save picks up these values
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Volatile.Write(ref _tuningSavePending, 0);
            if (Volatile.Read(ref _disposed) != 0) return; // Dispose saves on its own
            SaveProfile(force: true);
        });
        return true;
    }

    private void WakeOutputWriter()
    {
        try { _rumbleSignal.Set(); } catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Opens the device and reads its identity. Blocking I/O: call from a worker thread.
    /// Throws when the device cannot be opened (for example held exclusively by another program).
    /// </summary>
    public void Open()
    {
        if (!_device.TryOpen(new OpenConfiguration(), out DeviceStream deviceStream, out Exception? error)
            || deviceStream is not HidStream stream)
            throw new IOException($"{DisplayName} kon niet worden geopend; mogelijk gebruikt een ander programma hem.", error);
        _stream = stream;
        stream.ReadTimeout = ReadTimeoutMs;
        stream.WriteTimeout = WriteTimeoutMs;

        var identity = ControllerIdentity.Read(_device, stream, Model, Connection);
        Id = identity.Id;
        Mac = identity.Mac;
        HasStableId = identity.IsStable;
    }

    /// <summary>
    /// Loads the profile, switches Bluetooth pads to full reports, plugs in the virtual controller
    /// and starts the read thread. Blocking I/O: call from a worker thread, after <see cref="Open"/>.
    /// </summary>
    public void Start(ProfileStore store, ViGEmClient? vigem, bool learningEnabled, OutputMode outputMode, bool jitterFilterEnabled = true)
    {
        var stream = _stream ?? throw new InvalidOperationException("Open() first.");
        _store = store;
        _vigem = vigem;
        OutputMode = outputMode;
        _desiredMode = (int)outputMode;

        var calibration = new ControllerCalibration(ProfileId, Model)
        {
            LearningEnabled = learningEnabled,
            JitterFilterEnabled = jitterFilterEnabled,
        };
        (_profileLoaded, _savedFingerprint) = LoadInto(store, calibration);
        _appliedProfileId = ProfileId;
        _appliedBlocked = ProfileBlocked;
        lock (_sync)
        {
            _calibration = calibration;
            _tuningGeneration++; // a view model made before Start showed defaults
        }

        if (Connection == ConnectionType.Bluetooth) RequestFullReports();

        int outLength = OutputReports.RequiredLength(Model, Connection);
        try { outLength = Math.Max(outLength, _device.GetMaxOutputReportLength()); } catch { }
        _outputBuffer = new byte[outLength];

        lock (_padLock) ConnectVirtualPad();

        _saveTimer = new Timer(_ => SaveProfile(), null, SaveInterval, SaveInterval);

        _rumbleThread = new Thread(RumbleLoop) { IsBackground = true, Name = $"Rumble {Id}" };
        _rumbleThread.Start();
        _rumbleSignal.Set(); // first write sets the DS4 light bar, so the user sees the pad is handled

        _readThread = new Thread(() => ReadLoop(stream))
        {
            IsBackground = true,
            Name = $"HID {Id}",
            Priority = ThreadPriority.Highest,
        };
        _readThread.Start();
    }

    /// <summary>Copies the latest values; cheap and allocation free, meant for ~30 Hz UI polling.</summary>
    public void CopySnapshot(ref ControllerSnapshot s)
    {
        lock (_sync)
        {
            s.HasInput = _hasInput;
            s.Raw = _raw;
            s.Output = _output;
            s.Connection = Connection;
            s.Reports = _reports;
            s.LastReportTimestamp = _lastReportTimestamp;
            var c = _calibration;
            if (c == null) return;
            s.LearningEnabled = c.LearningEnabled;
            s.Left.CenterX = c.LeftStick.CenterX;
            s.Left.CenterY = c.LeftStick.CenterY;
            s.Left.Deadzone = c.LeftStick.Deadzone;
            s.Left.GuidedInProgress = c.LeftStick.GuidedInProgress;
            s.Right.CenterX = c.RightStick.CenterX;
            s.Right.CenterY = c.RightStick.CenterY;
            s.Right.Deadzone = c.RightStick.Deadzone;
            s.Right.GuidedInProgress = c.RightStick.GuidedInProgress;
            CopyTrigger(c.L2, ref s.L2);
            CopyTrigger(c.R2, ref s.R2);
        }

        static void CopyTrigger(TriggerCalibrator t, ref TriggerLive live)
        {
            live.Rest = t.Rest;
            live.EndStop = t.EndStop;
            live.EndStopLearned = t.EndStopLearned;
            live.EndStopKind = t.EndStopKind;
            live.PressCount = t.PressCount;
            live.GuidedInProgress = t.GuidedInProgress;
            live.GuidedPressesRemaining = t.GuidedPressesRemaining;
        }
    }

    /// <summary>
    /// Starts the guided end-stop assistant for one trigger: the user presses it fully a few times and
    /// that becomes the new 100%. Works even when automatic learning is off.
    /// </summary>
    public void StartGuidedEndStop(bool rightTrigger)
    {
        lock (_sync)
        {
            if (_calibration == null) return;
            (rightTrigger ? _calibration.R2 : _calibration.L2).StartGuidedEndStop();
        }
    }

    /// <summary>Starts the guided re-centre on both sticks: the user lets go and the rest position is measured anew.</summary>
    public void StartGuidedCenter()
    {
        lock (_sync)
        {
            if (_calibration == null) return;
            _calibration.LeftStick.StartGuidedCenter();
            _calibration.RightStick.StartGuidedCenter();
        }
    }

    public void CancelGuidedCenter()
    {
        lock (_sync)
        {
            if (_calibration == null) return;
            _calibration.LeftStick.CancelGuided();
            _calibration.RightStick.CancelGuided();
        }
    }

    public void CancelGuidedEndStop(bool rightTrigger)
    {
        lock (_sync)
        {
            if (_calibration == null) return;
            (rightTrigger ? _calibration.R2 : _calibration.L2).CancelGuided();
        }
    }

    /// <summary>What was learned, for the plain-language explanation. Allocates; call a few times per second at most.</summary>
    public CalibrationSummary? GetSummary()
    {
        lock (_sync)
        {
            var c = _calibration;
            return c == null ? null : CalibrationSummary.From(c.Export(), c.LeftStick, c.RightStick);
        }
    }

    /// <summary>Forgets everything learned for this controller and saves the clean profile. The user's tuning stays.</summary>
    public void ResetCalibration()
    {
        lock (_sync) _calibration?.Reset();
        SaveProfile(force: true);
    }

    /// <summary>Saves the profile when something changed (and only when the saved one was read first).</summary>
    public void SaveProfile() => SaveProfile(force: false);

    /// <summary>
    /// Loads the profile <see cref="ProfileId"/> into <paramref name="calibration"/>. Returns whether
    /// saving is allowed later (false when the file could not be read, or the profile is in use elsewhere).
    /// </summary>
    private (bool Loaded, long Fingerprint) LoadInto(ProfileStore store, ControllerCalibration calibration)
    {
        if (ProfileBlocked)
        {
            Log.Warn($"Profiel {ProfileId} is al in gebruik door een andere controller; {Id} draait zonder opslaan.");
            return (false, 0);
        }
        try
        {
            // Strict: a file that exists but cannot be read throws, so it is never overwritten.
            var profile = store.LoadStrict(ProfileId);
            if (profile != null && profile.Model == Model) calibration.Import(profile);
            return (true, profile != null ? Fingerprint(calibration.Export()) : 0);
        }
        catch (Exception ex)
        {
            // Keep running with factory values but never overwrite the file we could not read.
            Log.Warn($"Profiel {ProfileId} laden mislukt; er wordt niet over het bestand heen geschreven.", ex);
            return (false, 0);
        }
    }

    /// <summary>
    /// Brings the calibration in line with <see cref="ProfileId"/> and <see cref="ProfileBlocked"/> as
    /// reserved by the manager: saves the current profile and continues with the reserved one (a
    /// shared profile, back to its own, or its own again after it was blocked). The learning and
    /// filter switches carry over. Calls are serialised and always apply the latest reservation; a
    /// call that finds nothing changed does nothing. Blocking file I/O: call from a worker.
    /// </summary>
    internal void ApplyProfile()
    {
        var store = _store;
        if (store == null || Volatile.Read(ref _disposed) != 0) return;
        lock (_saveGate) ApplyProfileCore(store);
    }

    /// <summary>Must be called under _saveGate.</summary>
    private void ApplyProfileCore(ProfileStore store)
    {
        string target = ProfileId;
        bool blocked = ProfileBlocked;
        if (string.Equals(target, _appliedProfileId, StringComparison.OrdinalIgnoreCase) && blocked == _appliedBlocked) return;

        // From here on, tuning the UI read for the old profile is refused (it would be lost after the save
        // below, or land in the new profile). Bumped again at the swap for a UI that re-read in between.
        lock (_sync) _tuningGeneration++;
        SaveProfileCore(force: false); // the old profile, under its own id
        bool learning = LearningEnabled, jitter = JitterFilterEnabled;
        var calibration = new ControllerCalibration(target, Model) { LearningEnabled = learning, JitterFilterEnabled = jitter };
        var (loaded, fingerprint) = LoadInto(store, calibration);
        lock (_sync)
        {
            _calibration = calibration;
            _tuningGeneration++;
        }
        _profileLoaded = loaded;
        Interlocked.Exchange(ref _savedFingerprint, fingerprint);
        _appliedProfileId = target;
        _appliedBlocked = blocked;
        WakeOutputWriter(); // the new profile may have another Bluetooth poll interval
        Log.Info(blocked ? $"{Id} draait zonder opslaan (profiel {target} in gebruik)." : $"{Id} gebruikt nu profiel {target}.");
    }

    /// <summary>
    /// Replaces what was learned with a copied or imported profile and saves it. Returns false when this
    /// controller may not save (its profile is in use elsewhere, or could not be read).
    /// </summary>
    public bool ReplaceCalibration(CalibrationProfile profile)
    {
        var store = _store;
        if (store == null) return false;
        lock (_saveGate)
        {
            ApplyProfileCore(store); // never write into a profile we are switching away from
            if (!_profileLoaded) return false;
            lock (_sync)
            {
                _calibration?.Import(profile);
                _tuningGeneration++;
            }
            SaveProfileCore(force: true);
            WakeOutputWriter();
            return true;
        }
    }

    private void SaveProfile(bool force)
    {
        lock (_saveGate) SaveProfileCore(force);
    }

    private void SaveProfileCore(bool force)
    {
        var store = _store;
        if (store == null || !_profileLoaded) return;
        try
        {
            CalibrationProfile profile;
            lock (_sync)
            {
                if (_calibration == null) return;
                profile = _calibration.Export();
            }
            long fingerprint = Fingerprint(profile);
            if (!force && fingerprint == Interlocked.Read(ref _savedFingerprint)) return;
            store.Save(profile);
            Interlocked.Exchange(ref _savedFingerprint, fingerprint);
        }
        catch (Exception ex)
        {
            Log.Warn($"Profiel opslaan mislukt voor {Id}", ex);
        }
    }

    /// <summary>
    /// A DS4 on Bluetooth only sends the reduced 0x01 report until feature report 0x05 is read; the
    /// DualSense does the same for its 0x31 report. Failures are harmless, so they are only logged.
    /// </summary>
    private void RequestFullReports()
    {
        var stream = _stream;
        if (stream == null) return;
        try
        {
            int length = 41;
            try { length = Math.Max(41, _device.GetMaxFeatureReportLength()); } catch { }
            var buffer = new byte[length];
            buffer[0] = 0x05;
            stream.GetFeature(buffer);
        }
        catch (Exception ex)
        {
            Log.Info($"Volledige Bluetooth-rapporten aanvragen voor {Id} gaf: {ex.Message}");
        }
    }

    /// <summary>
    /// Asks for the other kind of virtual controller. Returns at once; a worker unplugs the current
    /// target and plugs in the new one. Quick repeated clicks always end in the latest choice.
    /// </summary>
    public void RequestOutputMode(OutputMode mode)
    {
        Volatile.Write(ref _desiredMode, (int)mode);
        _reconnectAttempted = false;
        QueueApplyOutputMode();
    }

    private void QueueApplyOutputMode() => ThreadPool.QueueUserWorkItem(_ =>
    {
        try { ApplyDesiredOutputMode(); }
        catch (Exception ex) { Log.Error($"Uitvoer wisselen voor {Id} mislukt", ex); }
    });

    /// <summary>Brings the virtual pad in line with the latest requested mode. Blocking driver calls.</summary>
    private void ApplyDesiredOutputMode()
    {
        OutputMode mode;
        lock (_padLock)
        {
            // Re-checked under the lock: Dispose takes the same lock, so no target is made on a closed client.
            if (Volatile.Read(ref _disposed) != 0) return;
            mode = (OutputMode)Volatile.Read(ref _desiredMode);
            if (mode == OutputMode && VirtualState is VirtualPadState.Active) return;
            DisconnectVirtualPad();
            OutputMode = mode;
            Volatile.Write(ref _lightbarWanted, LightbarDefault);
            Volatile.Write(ref _rumbleWanted, 0);
            ConnectVirtualPad();
        }
        try { _rumbleSignal.Set(); } catch (ObjectDisposedException) { } // stop old rumble, restore our light bar
        Log.Info($"{Id}: uitvoer is nu {mode}.");
    }

    /// <summary>Must be called under _padLock.</summary>
    private void ConnectVirtualPad()
    {
        _hasSubmitted = false;
        VirtualError = null;
        var vigem = _vigem;
        if (vigem == null)
        {
            VirtualState = VirtualPadState.DriverMissing;
            return;
        }

        try
        {
            if (OutputMode == OutputMode.DualShock4)
            {
                var ds4 = vigem.CreateDualShock4Controller();
                try
                {
                    ds4.AutoSubmitReport = false;
#pragma warning disable CS0618 // FeedbackReceived is marked obsolete but is the simple, working way to get rumble
                    ds4.FeedbackReceived += OnDs4FeedbackReceived;
#pragma warning restore CS0618
                    ds4.Connect();
                }
                catch
                {
                    ReleaseDs4(ds4);
                    throw;
                }
                _ds4 = ds4;
            }
            else
            {
                var xbox = vigem.CreateXbox360Controller();
                try
                {
                    xbox.AutoSubmitReport = false;
                    xbox.FeedbackReceived += OnXboxFeedbackReceived;
                    xbox.Connect();
                }
                catch
                {
                    ReleaseXbox(xbox);
                    throw;
                }
                _xbox = xbox;
            }
            VirtualState = VirtualPadState.Active;
        }
        catch (Exception ex)
        {
            Log.Error($"Virtuele {OutputMode}-controller voor {Id} maken mislukt", ex);
            VirtualError = ex.Message;
            VirtualState = VirtualPadState.Failed;
        }
    }

    /// <summary>Must be called under _padLock.</summary>
    private void DisconnectVirtualPad()
    {
        var xbox = _xbox;
        var ds4 = _ds4;
        _xbox = null;
        _ds4 = null;
        if (xbox != null) ReleaseXbox(xbox);
        if (ds4 != null) ReleaseDs4(ds4);
        VirtualState = VirtualPadState.Starting;
    }

    private void ReleaseXbox(IXbox360Controller xbox)
    {
        try { xbox.FeedbackReceived -= OnXboxFeedbackReceived; } catch { }
        try { xbox.Disconnect(); } catch { }
        try { (xbox as IDisposable)?.Dispose(); } catch { }
    }

    private void ReleaseDs4(IDualShock4Controller ds4)
    {
#pragma warning disable CS0618
        try { ds4.FeedbackReceived -= OnDs4FeedbackReceived; } catch { }
#pragma warning restore CS0618
        try { ds4.Disconnect(); } catch { }
        try { ds4.Dispose(); } catch { }
    }

    private void ReadLoop(HidStream stream)
    {
        int length = 64;
        try { length = Math.Max(64, _device.GetMaxInputReportLength()); } catch { }
        var buffer = new byte[length];
        var model = Model;
        var connection = Connection;
        int unparsed = 0;
        var token = _cts.Token;
        // Report interval for the stick jitter filter: measured on arrival and smoothed, because
        // reports can arrive in small bursts. Starts at the USB rate.
        long previousArrival = 0;
        double interval = 1.0 / 250;
        double tickSeconds = 1.0 / Stopwatch.Frequency;

        try
        {
            while (!token.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = stream.Read(buffer, 0, buffer.Length);
                }
                catch (TimeoutException)
                {
                    continue; // a pad always streams; a quiet second is not an error
                }
                if (read <= 0) continue;

                if (!ReportParser.TryParse(model, new ReadOnlySpan<byte>(buffer, 0, read), out var raw, out _, connection))
                {
                    // A Bluetooth pad that keeps sending reduced reports missed the full-mode switch: ask again now and then.
                    if (connection == ConnectionType.Bluetooth && ++unparsed % 500 == 0) RequestFullReports();
                    continue;
                }

                long arrival = Stopwatch.GetTimestamp();
                if (previousArrival != 0)
                {
                    double seconds = (arrival - previousArrival) * tickSeconds;
                    if (seconds > 0 && seconds < 0.05) interval += (seconds - interval) * 0.02;
                }
                previousArrival = arrival;

                CalibratedState output;
                int outputRate;
                lock (_sync)
                {
                    var calibration = _calibration!;
                    output = calibration.Process(raw, interval);
                    outputRate = calibration.Tuning.OutputRateHz;
                    _raw = raw;
                    _output = output;
                    _hasInput = true;
                    _reports++;
                    _lastReportTimestamp = arrival;
                }

                SubmitVirtual(output, arrival, outputRate);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
        {
            // Unplugged, Bluetooth link lost or stream closed by Dispose.
        }
        catch (Exception ex)
        {
            Log.Error($"Leesfout bij {Id}", ex);
        }
        finally
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                try { Ended?.Invoke(this, EventArgs.Empty); } catch (Exception ex) { Log.Error("Ended-handler faalde", ex); }
            }
        }
    }

    /// <summary>
    /// Hands one calibrated state to the virtual pad. Only changes go to the driver (games keep the last
    /// state). With <paramref name="outputRate"/> above 0, an analog-only change that comes sooner than
    /// 1/rate after the previous submit waits: it is compared against the last <em>submitted</em> report,
    /// so the pad's next report (they stream continuously) still sends it. Digital edges (buttons, d-pad,
    /// a trigger leaving or reaching 0) always go out at once, so short taps are never swallowed.
    /// </summary>
    private void SubmitVirtual(in CalibratedState output, long arrival, int outputRate)
    {
        lock (_padLock)
        {
            try
            {
                if (_xbox is { } xbox)
                {
                    var r = XboxMapper.Map(output);
                    if (_hasSubmitted && r == _lastXbox) return;
                    if (TooSoon(arrival, outputRate) && !ReportEdges.HasDigitalEdge(_lastXbox, r)) return;
                    xbox.SetAxisValue(Xbox360Axis.LeftThumbX, r.LeftThumbX);
                    xbox.SetAxisValue(Xbox360Axis.LeftThumbY, r.LeftThumbY);
                    xbox.SetAxisValue(Xbox360Axis.RightThumbX, r.RightThumbX);
                    xbox.SetAxisValue(Xbox360Axis.RightThumbY, r.RightThumbY);
                    xbox.SetSliderValue(Xbox360Slider.LeftTrigger, r.LeftTrigger);
                    xbox.SetSliderValue(Xbox360Slider.RightTrigger, r.RightTrigger);
                    // XboxButtons uses the native XUSB bit values, so the whole word is set at once.
                    xbox.SetButtonsFull((ushort)r.Buttons);
                    xbox.SubmitReport();
                    _lastXbox = r;
                    _hasSubmitted = true;
                    _lastSubmitTimestamp = arrival;
                }
                else if (_ds4 is { } ds4)
                {
                    var r = Ds4Mapper.Map(output);
                    if (_hasSubmitted && r == _lastDs4) return;
                    if (TooSoon(arrival, outputRate) && !ReportEdges.HasDigitalEdge(_lastDs4, r)) return;
                    // The ref properties write straight into ViGEm's native report: no lookups, no allocations.
                    ds4.LeftThumbX = r.LeftX;
                    ds4.LeftThumbY = r.LeftY;
                    ds4.RightThumbX = r.RightX;
                    ds4.RightThumbY = r.RightY;
                    ds4.LeftTrigger = r.L2;
                    ds4.RightTrigger = r.R2;
                    // Ds4Mapper uses ViGEm's numeric values: button bits, d-pad in the low nibble, special byte.
                    ds4.SetButtonsFull((ushort)((ushort)r.Buttons | (ushort)r.DPad));
                    ds4.SetSpecialButtonsFull((byte)r.Special);
                    ds4.SubmitReport();
                    _lastDs4 = r;
                    _hasSubmitted = true;
                    _lastSubmitTimestamp = arrival;
                }
            }
            catch (Exception ex)
            {
                if (VirtualState != VirtualPadState.Failed) Log.Error($"Virtuele controller {Id} weigert input", ex);
                VirtualError = ex.Message;
                DisconnectVirtualPad();
                VirtualState = VirtualPadState.Failed;
                if (!_reconnectAttempted)
                {
                    // Try once to plug a fresh target in (for example after the driver was restarted).
                    _reconnectAttempted = true;
                    _ = Task.Delay(2000).ContinueWith(_ => QueueApplyOutputMode(), TaskScheduler.Default);
                }
            }
        }
    }

    /// <summary>
    /// True when the output rate limit says to wait. Allows 1/8 of the interval early, so reports that
    /// arrive with a little jitter at exactly the limit rate are not halved. Must be called under _padLock.
    /// </summary>
    private bool TooSoon(long arrival, int outputRate)
    {
        if (outputRate <= 0 || !_hasSubmitted) return false;
        long minTicks = Stopwatch.Frequency / outputRate;
        return arrival - _lastSubmitTimestamp < minTicks - (minTicks >> 3);
    }

    // Run on ViGEm's notification thread: only record the latest values and wake the writer.
    private void OnXboxFeedbackReceived(object sender, Xbox360FeedbackReceivedEventArgs e)
    {
        Volatile.Write(ref _rumbleWanted, (e.LargeMotor << 8) | e.SmallMotor);
        _rumbleSignal.Set();
    }

    private void OnDs4FeedbackReceived(object sender, DualShock4FeedbackReceivedEventArgs e)
    {
        Volatile.Write(ref _rumbleWanted, (e.LargeMotor << 8) | e.SmallMotor);
        if (e.LightbarColor is { } c) Volatile.Write(ref _lightbarWanted, (c.Red << 16) | (c.Green << 8) | c.Blue);
        _rumbleSignal.Set();
    }

    /// <summary>Sends the newest motor values; a slow write never blocks ViGEm or the input thread.</summary>
    private void RumbleLoop()
    {
        long sent = -1;
        var token = _cts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                _rumbleSignal.WaitOne(1000);
                if (token.IsCancellationRequested) break;
                int rumble = Volatile.Read(ref _rumbleWanted);
                int light = Volatile.Read(ref _lightbarWanted);
                int poll = WantedPollInterval();
                long wanted = ((long)poll << 40) | ((long)light << 16) | (uint)rumble;
                if (wanted == sent) continue;
                if (WriteOutput((byte)(rumble & 0xFF), (byte)(rumble >> 8), light, poll)) sent = wanted;
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"Trilfunctie {Id} gestopt", ex);
        }
    }

    /// <summary>The Bluetooth report interval from the tuning (DS4 over Bluetooth); 1 for everything else.</summary>
    private int WantedPollInterval()
    {
        if (!SupportsBluetoothPollRate) return 1;
        lock (_sync) return _calibration?.Tuning.BluetoothPollIntervalMs ?? 1;
    }

    private bool WriteOutput(byte small, byte large, int lightbar, int pollIntervalMs)
    {
        var stream = _stream;
        if (stream == null) return false;
        try
        {
            lock (_outputBuffer)
            {
                OutputReports.Write(_outputBuffer, Model, Connection, small, large,
                    (byte)(lightbar >> 16), (byte)(lightbar >> 8), (byte)lightbar, _outputSequence++, pollIntervalMs);
                stream.Write(_outputBuffer, 0, _outputBuffer.Length);
            }
            return true;
        }
        catch
        {
            // Rumble is best effort; a failed write must never stop input.
            return false;
        }
    }

    /// <summary>Hash of what matters in a profile (learned values and tuning), to skip saving when nothing changed.</summary>
    private static long Fingerprint(CalibrationProfile p)
    {
        var h = new HashCode();
        AddStick(ref h, p.LeftStick);
        AddStick(ref h, p.RightStick);
        AddTrigger(ref h, p.L2);
        AddTrigger(ref h, p.R2);
        AddTuning(ref h, p.Tuning ?? InputTuning.Default);
        return (uint)h.ToHashCode() | (1L << 40); // never 0, which means "no saved file yet"

        static void AddStick(ref HashCode h, StickCalibrationData d)
        {
            h.Add(d.CenterX); h.Add(d.CenterY); h.Add(d.NoiseRadius); h.Add(d.RestSpread);
            h.Add(d.Releases); h.Add(d.RestSamples);
            foreach (float r in d.OuterRadii ?? Array.Empty<float>()) h.Add(r);
        }

        static void AddTrigger(ref HashCode h, TriggerCalibrationData d)
        {
            h.Add(d.Rest); h.Add(d.EndStop); h.Add(d.EndStopLearned); h.Add(d.PressCount);
            foreach (float r in d.RecentPeaks ?? Array.Empty<float>()) h.Add(r);
        }

        static void AddTuning(ref HashCode h, InputTuning t)
        {
            // Records hash all their fields, sections included.
            h.Add(t.LeftStick); h.Add(t.RightStick); h.Add(t.L2); h.Add(t.R2);
            h.Add(t.BluetoothPollIntervalMs); h.Add(t.OutputRateHz);
            h.Add(t.Remap);
        }
    }

    /// <summary>Stops reading, stops the motors, saves the profile and unplugs the virtual controller.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _cts.Cancel();
        _saveTimer?.Dispose();
        _rumbleSignal.Set();
        if (_rumbleThread != null && _rumbleThread != Thread.CurrentThread) _rumbleThread.Join(500);
        if (_outputBuffer.Length > 0) WriteOutput(0, 0, LightbarDefault, 1); // never leave the motors running; poll interval back to 1 ms

        try { _stream?.Dispose(); } catch { }
        if (_readThread != null && _readThread != Thread.CurrentThread) _readThread.Join(2000);

        SaveProfile();

        lock (_padLock) DisconnectVirtualPad();

        _rumbleSignal.Dispose();
        _cts.Dispose();
    }
}
