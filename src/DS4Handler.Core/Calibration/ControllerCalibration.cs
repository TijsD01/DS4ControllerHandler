using DS4Handler.Core.Input;
using DS4Handler.Core.Profiles;

namespace DS4Handler.Core.Calibration;

/// <summary>
/// The adaptive calibration for one physical controller: both sticks and both triggers,
/// each learning from normal play. Feed it every raw report; it returns calibrated input.
/// </summary>
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

    /// <summary>Learns from one raw report and returns the calibrated state.</summary>
    public CalibratedState Process(in RawControllerState raw)
    {
        var (lx, ly) = LeftStick.Process(raw.LeftX, raw.LeftY);
        var (rx, ry) = RightStick.Process(raw.RightX, raw.RightY);
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
        LeftStick.Import(profile.LeftStick);
        RightStick.Import(profile.RightStick);
        L2.Import(profile.L2);
        R2.Import(profile.R2);
    }
}
