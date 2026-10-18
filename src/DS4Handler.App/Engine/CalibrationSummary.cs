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
    int TotalDirections);

public sealed record TriggerSummary(float Rest, float EndStop, bool EndStopLearned, PlateauKind EndStopKind, int PressCount);

public sealed record CalibrationSummary(StickSummary Left, StickSummary Right, TriggerSummary L2, TriggerSummary R2)
{
    private static readonly StickCalibrationOptions StickDefaults = new();

    internal static CalibrationSummary From(CalibrationProfile p, float leftDeadzone, float rightDeadzone) => new(
        Stick(p.LeftStick, leftDeadzone),
        Stick(p.RightStick, rightDeadzone),
        Trigger(p.L2),
        Trigger(p.R2));

    private static StickSummary Stick(StickCalibrationData d, float deadzone)
    {
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
        return new StickSummary(d.CenterX, d.CenterY, deadzone, d.Releases, weakest, learned, total);
    }

    private static TriggerSummary Trigger(TriggerCalibrationData d) => new(d.Rest, d.EndStop, d.EndStopLearned, d.EndStopKind, d.PressCount);
}
