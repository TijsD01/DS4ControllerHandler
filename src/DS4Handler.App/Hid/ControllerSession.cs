using System.Security.Cryptography;
using System.Text;
using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;
using DS4Handler.Core.Output;
using DS4Handler.Core.Profiles;
using HidSharp;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace DS4Handler.App.Hid;

/// <summary>Latest values of one controller, swapped atomically so the UI can read without locking.</summary>
public sealed record ControllerSnapshot(
    RawControllerState Raw,
    CalibratedState Calibrated,
    XboxReport Xbox,
    ConnectionType Connection,
    DateTime TimestampUtc);

/// <summary>Learned calibration values for display.</summary>
public readonly record struct CalibrationInfo(
    float LeftCenterX, float LeftCenterY, float LeftDeadzone,
    float RightCenterX, float RightCenterY, float RightDeadzone,
    float L2Rest, float L2EndStop, bool L2Learned, int L2Presses,
    float R2Rest, float R2EndStop, bool R2Learned, int R2Presses,
    bool LearningEnabled);

/// <summary>
/// One physical controller: reads its HID reports on a background thread, runs the adaptive
/// calibration, and feeds the result into a virtual Xbox 360 controller (ViGEm).
///
/// Note: the real DS4/DualSense stays visible to games next to the virtual Xbox pad, so some games
/// see two controllers (double input). DS4Windows solves this with HidHide; install HidHide and hide
/// the Sony controller (whitelisting this app) to avoid it. There is no HidHide integration here.
/// </summary>
public sealed class ControllerSession : IDisposable
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);

    private readonly HidDevice _device;
    private readonly ProfileStore _store;
    private readonly ViGEmClient? _vigem;
    private readonly object _calibrationLock = new();
    private readonly object _writeLock = new();
    private readonly CancellationTokenSource _cts = new();

    private HidStream? _stream;
    private Thread? _thread;
    private IXbox360Controller? _xbox;
    private System.Threading.Timer? _saveTimer;
    private ConnectionType _connection;
    private byte _outputSequence;
    private int _maxOutputLength;
    private int _disposed;

    public ControllerSession(HidDevice device, ControllerModel model, ProfileStore store, ViGEmClient? vigem, bool learningEnabled)
    {
        _device = device;
        _store = store;
        _vigem = vigem;
        Model = model;
        DevicePath = device.DevicePath;
        Id = BuildId(device, model);
        Calibration = new ControllerCalibration(Id, model) { LearningEnabled = learningEnabled };
        _connection = GuessConnection(device);
    }

    public string Id { get; }
    public string DevicePath { get; }
    public ControllerModel Model { get; }
    public ControllerCalibration Calibration { get; }
    public ConnectionType Connection => _connection;

    public string DisplayName => Model switch
    {
        ControllerModel.DualShock4 => "DualShock 4",
        _ => "DualSense",
    };

    /// <summary>Latest input, or null until the first report arrived.</summary>
    public ControllerSnapshot? Latest { get; private set; }

    /// <summary>Human-readable status of the virtual controller (Dutch).</summary>
    public string VirtualStatus { get; private set; } = "Niet verbonden";

    /// <summary>Raised (on the read thread) when the controller is gone or the session stopped.</summary>
    public event EventHandler? Ended;

    public bool LearningEnabled
    {
        get { lock (_calibrationLock) return Calibration.LearningEnabled; }
        set { lock (_calibrationLock) Calibration.LearningEnabled = value; }
    }

    /// <summary>Opens the device, loads the saved profile and starts reading. Throws if the device cannot be opened.</summary>
    public void Start()
    {
        if (!_device.TryOpen(out HidStream stream))
            throw new IOException($"Kan {DisplayName} niet openen (in gebruik door een ander programma?).");
        _stream = stream;
        _stream.ReadTimeout = 1000;
        try { _maxOutputLength = _device.GetMaxOutputReportLength(); } catch { _maxOutputLength = 0; }

        LoadProfile();
        EnableFullReports();
        CreateVirtualController();

        // Give the DS4 light bar our colour so the user sees it is being handled.
        SendRumble(0, 0);

        _saveTimer = new System.Threading.Timer(_ => SaveProfile(), null, SaveInterval, SaveInterval);
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = $"HID {DisplayName} {Id}" };
        _thread.Start();
    }

    public CalibrationInfo GetCalibrationInfo()
    {
        lock (_calibrationLock)
        {
            var c = Calibration;
            return new CalibrationInfo(
                c.LeftStick.CenterX, c.LeftStick.CenterY, c.LeftStick.Deadzone,
                c.RightStick.CenterX, c.RightStick.CenterY, c.RightStick.Deadzone,
                c.L2.Rest, c.L2.EndStop, c.L2.EndStopLearned, c.L2.PressCount,
                c.R2.Rest, c.R2.EndStop, c.R2.EndStopLearned, c.R2.PressCount,
                c.LearningEnabled);
        }
    }

    /// <summary>Forgets everything learned for this controller and saves the clean profile.</summary>
    public void ResetCalibration()
    {
        lock (_calibrationLock) Calibration.Reset();
        SaveProfile();
    }

    public void SaveProfile()
    {
        try
        {
            CalibrationProfile profile;
            lock (_calibrationLock) profile = Calibration.Export();
            _store.Save(profile);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Profiel opslaan mislukt voor {Id}: {ex.Message}");
        }
    }

    private void LoadProfile()
    {
        try
        {
            var profile = _store.Load(Id);
            if (profile != null && profile.Model == Model)
            {
                lock (_calibrationLock) Calibration.Import(profile);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Profiel laden mislukt voor {Id}: {ex.Message}");
        }
    }

    /// <summary>
    /// A DS4 over Bluetooth only sends a reduced 0x01 report (no triggers/sticks precision) until
    /// feature report 0x05 is read; after that it sends full 0x11 reports. The DualSense does the
    /// same for its 0x31 report. Failures are harmless, so they are ignored.
    /// </summary>
    private void EnableFullReports()
    {
        if (_connection != ConnectionType.Bluetooth || _stream == null) return;
        try
        {
            int length = 41;
            try { length = Math.Max(41, _device.GetMaxFeatureReportLength()); } catch { }
            var buffer = new byte[length];
            buffer[0] = 0x05;
            _stream.GetFeature(buffer);
        }
        catch
        {
            // Ignore: some stacks refuse the request but still deliver full reports.
        }
    }

    private void CreateVirtualController()
    {
        if (_vigem == null)
        {
            VirtualStatus = "ViGEmBus ontbreekt – alleen meten, geen virtuele controller";
            return;
        }
        try
        {
            var xbox = _vigem.CreateXbox360Controller();
            xbox.AutoSubmitReport = false;
            xbox.FeedbackReceived += OnFeedbackReceived;
            xbox.Connect();
            _xbox = xbox;
            VirtualStatus = "Virtuele Xbox 360-controller actief";
        }
        catch (Exception ex)
        {
            _xbox = null;
            VirtualStatus = $"Virtuele controller mislukt: {ex.Message}";
        }
    }

    private void ReadLoop()
    {
        var stream = _stream!;
        int length = 64;
        try { length = Math.Max(64, _device.GetMaxInputReportLength()); } catch { }
        var buffer = new byte[length];

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = stream.Read(buffer, 0, buffer.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }
                if (read <= 0) continue;

                if (!ReportParser.TryParse(Model, buffer.AsSpan(0, read), out var raw, out var connection))
                    continue;
                _connection = connection;

                CalibratedState calibrated;
                lock (_calibrationLock) calibrated = Calibration.Process(raw);
                var xboxReport = XboxMapper.Map(calibrated);

                Submit(xboxReport);
                Latest = new ControllerSnapshot(raw, calibrated, xboxReport, connection, DateTime.UtcNow);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Unplugged, Bluetooth link lost or stream closed by Dispose.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Leesfout {Id}: {ex}");
        }
        finally
        {
            Ended?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Submit(in XboxReport r)
    {
        var xbox = _xbox;
        if (xbox == null) return;
        try
        {
            xbox.SetAxisValue(Xbox360Axis.LeftThumbX, r.LeftThumbX);
            xbox.SetAxisValue(Xbox360Axis.LeftThumbY, r.LeftThumbY);
            xbox.SetAxisValue(Xbox360Axis.RightThumbX, r.RightThumbX);
            xbox.SetAxisValue(Xbox360Axis.RightThumbY, r.RightThumbY);
            xbox.SetSliderValue(Xbox360Slider.LeftTrigger, r.LeftTrigger);
            xbox.SetSliderValue(Xbox360Slider.RightTrigger, r.RightTrigger);
            // XboxButtons uses the native XUSB bit values, so the whole word can be set at once.
            xbox.SetButtonsFull((ushort)r.Buttons);
            xbox.SubmitReport();
        }
        catch (Exception ex)
        {
            VirtualStatus = $"Fout bij virtuele controller: {ex.Message}";
        }
    }

    private void OnFeedbackReceived(object sender, Xbox360FeedbackReceivedEventArgs e)
        => SendRumble(e.SmallMotor, e.LargeMotor);

    private void SendRumble(byte small, byte large, bool force = false)
    {
        var stream = _stream;
        if (stream == null || (!force && Volatile.Read(ref _disposed) != 0)) return;
        try
        {
            lock (_writeLock)
            {
                var report = OutputReports.Build(Model, _connection, small, large, _outputSequence++, _maxOutputLength);
                stream.Write(report);
            }
        }
        catch
        {
            // Rumble is best effort; a failed write must never stop input.
        }
    }

    private static ConnectionType GuessConnection(HidDevice device)
    {
        // USB reports are 64 bytes; over Bluetooth the descriptor allows much larger reports.
        try { return device.GetMaxInputReportLength() > 64 ? ConnectionType.Bluetooth : ConnectionType.Usb; }
        catch { return ConnectionType.Usb; }
    }

    private static string BuildId(HidDevice device, ControllerModel model)
    {
        string prefix = model == ControllerModel.DualShock4 ? "ds4" : "dualsense";
        string? serial = null;
        try { serial = device.GetSerialNumber(); } catch { }

        if (!string.IsNullOrWhiteSpace(serial))
        {
            // Over Bluetooth this is the MAC address ("a4:53:85:..."); keep only safe filename characters.
            var clean = new string(serial.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (clean.Length > 0) return $"{prefix}-{clean}";
        }

        // No serial (common for a DS4 on USB): fall back to a stable hash of the device path.
        // This is tied to the USB port, so the same controller on another port gets its own profile.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(device.DevicePath.ToLowerInvariant()));
        return $"{prefix}-path-{Convert.ToHexString(hash, 0, 8).ToLowerInvariant()}";
    }

    /// <summary>Stops reading, saves the profile and unplugs the virtual controller.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _cts.Cancel();
        _saveTimer?.Dispose();
        SendRumble(0, 0, force: true); // never leave the motors running
        try { _stream?.Dispose(); } catch { }
        if (_thread != null && _thread != Thread.CurrentThread) _thread.Join(2000);

        SaveProfile();

        var xbox = _xbox;
        _xbox = null;
        if (xbox != null)
        {
            try { xbox.FeedbackReceived -= OnFeedbackReceived; } catch { }
            try { xbox.Disconnect(); } catch { }
        }
        _cts.Dispose();
    }
}
