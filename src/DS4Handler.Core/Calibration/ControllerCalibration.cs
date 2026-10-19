using DS4Handler.Core.Input;
using DS4Handler.Core.Profiles;
using DS4Handler.Core.Tuning;

namespace DS4Handler.Core.Calibration;

/// <summary>
/// The adaptive calibration for one physical controller: both sticks and both triggers,
/// each learning from normal play. Feed it every raw report; it returns calibrated input.
/// </summary>
/// <remarks>
/// The calibrators are not thread-safe. The caller must serialize <see cref="Process"/>,
/// <see cref="Export"/>, <see cref="Import"/> and <see cref="Reset"/> (for example with a lock).
/// </remarks>
public sealed class ControllerCalibration
{
    public ControllerCalibration(
        string controllerId,
        ControllerModel model,
        StickCalibrationOptions? stickOptions = null,
        TriggerCalibrationOptions? triggerOptions = null)
    {
        ControllerId = controllerId;
        Model = model;
        LeftStick = new StickCalibrator(stickOptions);
        RightStick = new StickCalibrator(stickOptions);
        L2 = new TriggerCalibrator(triggerOptions);
        R2 = new TriggerCalibrator(triggerOptions);
    }

    public string ControllerId { get; }
    public ControllerModel Model { get; }

    public StickCalibrator LeftStick { get; }
    public StickCalibrator RightStick { get; }
    public TriggerCalibrator L2 { get; }
    public TriggerCalibrator R2 { get; }

    /// <summary>
    /// User tuning applied after calibration (see <see cref="InputTuning"/>). Part of the profile;
    /// <see cref="Reset"/> keeps it. TODO(senior): apply in Process, export/import, sanitize.
    /// </summary>
    public InputTuning Tuning { get; set; } = InputTuning.Default;

    /// <summary>Turns learning on or off for all four inputs at once.</summary>
    public bool LearningEnabled
    {
        get => LeftStick.LearningEnabled;
        set
        {
            LeftStick.LearningEnabled = value;
            RightStick.LearningEnabled = value;
            L2.LearningEnabled = value;
            R2.LearningEnabled = value;
        }
    }

    /// <summary>
    /// Turns the stick jitter filter on or off for both sticks at once
    /// (see <see cref="StickCalibrator.JitterFilterEnabled"/>).
    /// </summary>
    public bool JitterFilterEnabled
    {
        get => LeftStick.JitterFilterEnabled;
        set
        {
            LeftStick.JitterFilterEnabled = value;
            RightStick.JitterFilterEnabled = value;
        }
    }

    /// <summary>Learns from one raw report and returns the calibrated state (assumes 250 reports per second).</summary>
    public CalibratedState Process(in RawControllerState raw)
    {
        var (lx, ly) = LeftStick.Process(raw.LeftX, raw.LeftY);
        var (rx, ry) = RightStick.Process(raw.RightX, raw.RightY);
        float l2 = L2.Process(raw.L2);
        float r2 = R2.Process(raw.R2);
        return new CalibratedState(lx, ly, rx, ry, l2, r2, raw.Buttons);
    }

    /// <summary>
    /// Learns from one raw report and returns the calibrated state. <paramref name="dtSeconds"/> is
    /// the time since the previous report; it tunes the stick jitter filter to the real report rate
    /// (USB about 250 per second, Bluetooth up to about 1000).
    /// </summary>
    public CalibratedState Process(in RawControllerState raw, double dtSeconds)
    {
        var (lx, ly) = LeftStick.Process(raw.LeftX, raw.LeftY, dtSeconds);
        var (rx, ry) = RightStick.Process(raw.RightX, raw.RightY, dtSeconds);
        float l2 = L2.Process(raw.L2);
        float r2 = R2.Process(raw.R2);
        return new CalibratedState(lx, ly, rx, ry, l2, r2, raw.Buttons);
    }

    public void Reset()
    {
        LeftStick.Reset();
        RightStick.Reset();
        L2.Reset();
        R2.Reset();
    }

    public CalibrationProfile Export() => new()
    {
        ControllerId = ControllerId,
        Model = Model,
        LeftStick = LeftStick.Export(),
        RightStick = RightStick.Export(),
        L2 = L2.Export(),
        R2 = R2.Export(),
        UpdatedUtc = DateTime.UtcNow,
    };

    public void Import(CalibrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        // Sections can be null when the profile came from hand-edited or damaged JSON: treat them as defaults.
        LeftStick.Import(profile.LeftStick ?? new());
        RightStick.Import(profile.RightStick ?? new());
        L2.Import(profile.L2 ?? new());
        R2.Import(profile.R2 ?? new());
    }
}
