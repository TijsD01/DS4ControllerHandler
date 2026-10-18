using DS4Handler.Core.Calibration;
using DS4Handler.Core.Profiles;

namespace DS4Handler.App.Engine;

/// <summary>Slower-changing facts about what was learned, derived from an exported profile.</summary>
public sealed record StickSummary(
    float CenterX,
    float CenterY,
    float Deadzone,
    int Releases,
    // Smallest trusted reach (fraction of the factory range) over the learned directions; null when none yet.
    float? WeakestReach,
    // How many of the directions have a trusted reach.
    int LearnedDirections,
    int TotalDirections,
    // Typical jitter amplitude as a fraction of the full output (about the +- range of the shaking).
    float Jitter,
    // How strongly the jitter filter would work for this stick (0 = not needed, 1 = fully).
    float JitterFilterStrength,
    bool JitterFilterEnabled);

public sealed record TriggerSummary(float Rest, float EndStop, bool EndStopLearned, PlateauKind EndStopKind, int PressCount);

public sealed record CalibrationSummary(StickSummary Left, StickSummary Right, TriggerSummary L2, TriggerSummary R2)
{
    private static readonly StickCalibrationOptions StickDefaults = new();

    internal static CalibrationSummary From(CalibrationProfile p, StickCalibrator left, StickCalibrator right) => new(
        Stick(p.LeftStick, left),
        Stick(p.RightStick, right),
        Trigger(p.L2),
        Trigger(p.R2));

    private static StickSummary Stick(StickCalibrationData d, StickCalibrator live)
    {
        float deadzone = live.Deadzone;
        float? weakest = null;
        int learned = 0;
        var radii = d.OuterRadii ?? Array.Empty<float>();
        foreach (float r in radii)
        {
            if (!(r >= StickDefaults.MinTrustedOuterRadius)) continue;
            learned++;
            if (weakest is null || r < weakest) weakest = r;
        }
        int total = radii.Length > 0 ? radii.Length : StickDefaults.OuterSectors;
        // Learned jitter is a standard deviation; uniform +-a shaking has a std of a / sqrt(3).
        float jitter = live.LearnedJitterOutput * MathF.Sqrt(3f);
        bool enabled = live.JitterFilterEnabled;
        var filter = new StickJitterFilter(StickDefaults);
        float strength = filter.StrengthFor(live.LearnedJitter);
        return new StickSummary(d.CenterX, d.CenterY, deadzone, d.Releases, weakest, learned, total, jitter, strength, enabled);
    }

    private static TriggerSummary Trigger(TriggerCalibrationData d) => new(d.Rest, d.EndStop, d.EndStopLearned, d.EndStopKind, d.PressCount);
}
