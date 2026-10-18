using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;

namespace DS4Handler.Core.Profiles;

/// <summary>Everything learned about one physical controller, saved between sessions.</summary>
public sealed record CalibrationProfile
{
    public int Version { get; init; } = 1;

    /// <summary>Stable id of the controller, usually its MAC address or serial number.</summary>
    public string ControllerId { get; init; } = "";

    public ControllerModel Model { get; init; }

    public StickCalibrationData LeftStick { get; init; } = new();
    public StickCalibrationData RightStick { get; init; } = new();
    public TriggerCalibrationData L2 { get; init; } = new();
    public TriggerCalibrationData R2 { get; init; } = new();

    public DateTime UpdatedUtc { get; init; }
}
