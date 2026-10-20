using System.Text.Json.Serialization;
using DS4Handler.Core.Mapping;

namespace DS4Handler.Core.Tuning;

/// <summary>Shape of the extra stick deadzone the user picks on top of the learned one.</summary>
public enum DeadzoneShape
{
    /// <summary>Round: the stick must leave a circle before it counts. Smooth in every direction.</summary>
    Radial,

    /// <summary>Square: both axes must be inside the deadzone for the stick to count as centered (max(|x|,|y|)).</summary>
    Square,

    /// <summary>Cross / axial: each axis has its own deadzone, so a small sideways wobble does not leak into a straight push.</summary>
    Axial,
}

/// <summary>
/// User settings for one stick, applied after the adaptive calibration (drift, learned deadzone,
/// reach and jitter filter). All distances are fractions of the calibrated output range (0..1).
/// Defaults change nothing: output stays exactly what the calibration gives.
/// </summary>
public sealed record StickTuning
{
    public DeadzoneShape Shape { get; init; } = DeadzoneShape.Radial;

    /// <summary>Extra deadzone on top of the learned one (0 = only the learned deadzone). Range 0..0.9.</summary>
    public float InnerDeadzone { get; init; }

    /// <summary>Full output is reached this much before the edge (0 = at the edge). Range 0..0.5.</summary>
    public float OuterDeadzone { get; init; }

    /// <summary>Output jumps to at least this as soon as the stick leaves the deadzone, to cancel a game's own deadzone. Range 0..0.9.</summary>
    public float AntiDeadzone { get; init; }

    /// <summary>Response curve exponent: 1 = linear, above 1 = finer near the middle, below 1 = faster off the middle. Range 0.2..5.</summary>
    public float Curve { get; init; } = 1f;

    /// <summary>Output multiplier, clamped to the full range afterwards. Range 0.1..3.</summary>
    public float Sensitivity { get; init; } = 1f;

    /// <summary>Maps the round stick range onto a square, so diagonals reach full on both axes.</summary>
    public bool SquareOutput { get; init; }

    public bool InvertX { get; init; }
    public bool InvertY { get; init; }
}

/// <summary>User settings for one trigger, applied after the adaptive trigger calibration. Defaults change nothing.</summary>
public sealed record TriggerTuning
{
    /// <summary>Pressing less than this reads as 0. Range 0..0.9.</summary>
    public float InnerDeadzone { get; init; }

    /// <summary>Pressing more than 1 - this reads as full. Range 0..0.5 (inner + outer stays below 1).</summary>
    public float OuterDeadzone { get; init; }

    /// <summary>Output jumps to at least this as soon as the trigger leaves its deadzone. Range 0..0.9.</summary>
    public float AntiDeadzone { get; init; }

    /// <summary>Response curve exponent: 1 = linear. Range 0.2..5.</summary>
    public float Curve { get; init; } = 1f;
}

/// <summary>
/// Everything the user can tune for one controller profile. Saved in the profile
/// (<see cref="Profiles.CalibrationProfile.Tuning"/>), so it follows the controller, a linked profile
/// and an exported profile. Resetting the calibration keeps it. Read tolerantly from JSON
/// (<see cref="InputTuningJsonConverter"/>): a bad field falls back to its default.
/// </summary>
[JsonConverter(typeof(InputTuningJsonConverter))]
public sealed record InputTuning
{
    public StickTuning LeftStick { get; init; } = new();
    public StickTuning RightStick { get; init; } = new();
    public TriggerTuning L2 { get; init; } = new();
    public TriggerTuning R2 { get; init; } = new();

    /// <summary>
    /// DualShock 4 over Bluetooth only: interval in milliseconds the controller sends its reports at
    /// (1 = 1000 per second, 2 = 500, 4 = 250, 8 = 125, 16 = 62). Written into the low 6 bits of byte 1
    /// of output report 0x11; 0 and 1 both mean 1 ms. Over USB the controller decides its own rate.
    /// Range 1..16.
    /// </summary>
    public int BluetoothPollIntervalMs { get; init; } = 1;

    /// <summary>
    /// Highest rate (per second) at which the virtual controller is updated. 0 = every report (no
    /// limit, lowest latency). Allowed: 0, 125, 250, 500, 1000.
    /// </summary>
    public int OutputRateHz { get; init; }

    public ButtonRemap Remap { get; init; } = ButtonRemap.Identity;

    public static InputTuning Default { get; } = new();
}
