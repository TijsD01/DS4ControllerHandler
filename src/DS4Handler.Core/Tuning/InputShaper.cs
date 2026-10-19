using DS4Handler.Core.Output;

namespace DS4Handler.Core.Tuning;

/// <summary>
/// Applies the user's <see cref="InputTuning"/> to calibrated input. Pure, allocation free and safe to
/// call per report. Neutral settings return the input bit for bit unchanged.
/// </summary>
/// <remarks>
/// Stick order (on the calibrated -1..1 position, inside the unit circle):
/// <list type="number">
/// <item>Inner + outer deadzone by <see cref="StickTuning.Shape"/>, rescaled so the output starts at 0 on
/// the deadzone edge (no jump) and is full at 1 - outer. Radial: circle, scaled along the radius.
/// Square: the edge is the square max(|x|,|y|) = dz, still scaled along the radius from that edge, so a
/// full push reaches full output in every direction. Axial: each axis on its own.</item>
/// <item>Response curve on the magnitude (<see cref="XboxMapper.ApplyResponseCurve"/> semantics).</item>
/// <item>Sensitivity on the magnitude, clamped to 1.</item>
/// <item>Anti-deadzone: magnitude (0,1] maps onto (anti,1]. Last of the magnitude steps, so the curve and
/// the sensitivity never shrink the jump that cancels a game's own deadzone.</item>
/// <item>Square output: elliptical grid mapping of the disc onto the square, so diagonals reach (±1,±1).
/// Smooth and close to identity near the middle (no extra diagonal gain for small movements).</item>
/// <item>Invert X / Y, then clamp both axes to [-1, 1].</item>
/// </list>
/// Triggers: inner deadzone, outer deadzone (full at 1 - outer), curve, anti-deadzone, clamp to [0, 1].
/// </remarks>
public static class InputShaper
{
    public const float MaxStickInnerDeadzone = 0.9f;
    public const float MaxOuterDeadzone = 0.5f;
    public const float MaxAntiDeadzone = 0.9f;
    public const float MinCurve = 0.2f;
    public const float MaxCurve = 5f;
    public const float MinSensitivity = 0.1f;
    public const float MaxSensitivity = 3f;
    public const float MaxTriggerInnerDeadzone = 0.9f;

    /// <summary>The live range between inner and outer deadzone never gets narrower than this.</summary>
    public const float MinSpan = 0.05f;

    public const int MinBluetoothPollIntervalMs = 1;
    public const int MaxBluetoothPollIntervalMs = 16;

    /// <summary>Allowed <see cref="InputTuning.OutputRateHz"/> values; 0 = no limit.</summary>
    public static ReadOnlySpan<int> OutputRates => new[] { 0, 125, 250, 500, 1000 };

    private const float Sqrt2 = 1.41421356f;

    /// <summary>True when the stick settings change nothing.</summary>
    public static bool IsNeutral(StickTuning t) =>
        !HasMagnitudeStep(t) && !t.SquareOutput && !t.InvertX && !t.InvertY;

    /// <summary>True when the trigger settings change nothing.</summary>
    public static bool IsNeutral(TriggerTuning t) =>
        t.InnerDeadzone == 0f && t.OuterDeadzone == 0f && t.AntiDeadzone == 0f && t.Curve == 1f;

    private static bool HasMagnitudeStep(StickTuning t) =>
        t.InnerDeadzone != 0f || t.OuterDeadzone != 0f || t.AntiDeadzone != 0f || t.Curve != 1f || t.Sensitivity != 1f;

    /// <summary>
    /// Shapes one calibrated stick position. Expects sanitized settings (see <see cref="Sanitize"/>);
    /// NaN input reads as centered.
    /// </summary>
    public static (float X, float Y) ShapeStick(float x, float y, StickTuning t)
    {
        if (IsNeutral(t)) return (x, y);
        if (float.IsNaN(x)) x = 0f;
        if (float.IsNaN(y)) y = 0f;

        if (HasMagnitudeStep(t)) (x, y) = ShapeMagnitude(x, y, t);
        if (t.SquareOutput) (x, y) = DiscToSquare(x, y);
        if (t.InvertX) x = -x;
        if (t.InvertY) y = -y;
        return (Math.Clamp(x, -1f, 1f), Math.Clamp(y, -1f, 1f));
    }

    /// <summary>Deadzones by shape, then curve, sensitivity and anti-deadzone on the magnitude.</summary>
    private static (float X, float Y) ShapeMagnitude(float x, float y, StickTuning t)
    {
        float dz = t.InnerDeadzone, outer = t.OuterDeadzone;
        float ax = MathF.Abs(x), ay = MathF.Abs(y);

        // Step 1: a deadzone-free vector (dx, dy) whose length is the normalised push 0..1.
        float dx, dy;
        switch (t.Shape)
        {
            case DeadzoneShape.Axial:
            {
                float span = MathF.Max(1f - outer - dz, MinSpan);
                dx = ax <= dz ? 0f : MathF.CopySign(MathF.Min((ax - dz) / span, 1f), x);
                dy = ay <= dz ? 0f : MathF.CopySign(MathF.Min((ay - dz) / span, 1f), y);
                break;
            }
            case DeadzoneShape.Square:
            {
                float m = MathF.Max(ax, ay);
                if (m <= dz) return (0f, 0f);
                float r = MathF.Sqrt(x * x + y * y);
                // Where the ray through (x, y) leaves the square deadzone: dz / max(|cos|, |sin|).
                float edge = dz * r / m;
                float span = MathF.Max(1f - outer - edge, MinSpan);
                float scaled = MathF.Min((r - edge) / span, 1f);
                dx = x / r * scaled;
                dy = y / r * scaled;
                break;
            }
            default:
            {
                float r = MathF.Sqrt(x * x + y * y);
                if (r <= dz) return (0f, 0f);
                float span = MathF.Max(1f - outer - dz, MinSpan);
                float scaled = MathF.Min((r - dz) / span, 1f);
                dx = x / r * scaled;
                dy = y / r * scaled;
                break;
            }
        }

        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (!(len > 0f)) return (0f, 0f);

        // Step 2: curve, sensitivity, anti-deadzone on the length; the direction stays.
        float mag = MathF.Min(len, 1f);
        mag = XboxMapper.ApplyResponseCurve(mag, t.Curve);
        mag = MathF.Min(mag * t.Sensitivity, 1f);
        mag = t.AntiDeadzone + (1f - t.AntiDeadzone) * mag;
        float k = mag / len;
        return (dx * k, dy * k);
    }

    /// <summary>
    /// Elliptical grid mapping (Fong): the unit disc onto the unit square, so (0.707, 0.707) becomes (1, 1)
    /// while straight pushes and small movements stay (nearly) the same.
    /// </summary>
    public static (float X, float Y) DiscToSquare(float u, float v)
    {
        if (float.IsNaN(u) || float.IsNaN(v)) return (0f, 0f);
        float r2 = u * u + v * v;
        if (r2 > 1f)
        {
            float r = MathF.Sqrt(r2);
            u /= r;
            v /= r;
        }
        float uu = u * u, vv = v * v;
        float tu = 2f * Sqrt2 * u, tv = 2f * Sqrt2 * v;
        float x = 0.5f * (MathF.Sqrt(MathF.Max(0f, 2f + tu + uu - vv)) - MathF.Sqrt(MathF.Max(0f, 2f - tu + uu - vv)));
        float y = 0.5f * (MathF.Sqrt(MathF.Max(0f, 2f + tv - uu + vv)) - MathF.Sqrt(MathF.Max(0f, 2f - tv - uu + vv)));
        return (Math.Clamp(x, -1f, 1f), Math.Clamp(y, -1f, 1f));
    }

    /// <summary>
    /// Shapes one calibrated trigger value (0..1). Expects sanitized settings; NaN reads as released.
    /// </summary>
    public static float ShapeTrigger(float v, TriggerTuning t)
    {
        if (IsNeutral(t)) return v;
        if (!(v > t.InnerDeadzone)) return 0f; // also NaN
        float span = MathF.Max(1f - t.OuterDeadzone - t.InnerDeadzone, MinSpan);
        float mag = MathF.Min((v - t.InnerDeadzone) / span, 1f);
        mag = XboxMapper.ApplyResponseCurve(mag, t.Curve);
        mag = t.AntiDeadzone + (1f - t.AntiDeadzone) * mag;
        return Math.Clamp(mag, 0f, 1f);
    }

    /// <summary>
    /// Brings every value into its documented range: NaN / infinity and null sections become the
    /// default, an unknown shape becomes <see cref="DeadzoneShape.Radial"/>, the outer deadzone shrinks so
    /// at least <see cref="MinSpan"/> stays live, the poll interval is clamped to 1..16 ms and the
    /// output rate snaps to the nearest allowed value (any positive value keeps a limit).
    /// Returns the same instance when nothing had to change.
    /// </summary>
    public static InputTuning Sanitize(InputTuning? tuning)
    {
        if (tuning is null) return InputTuning.Default;
        var result = new InputTuning
        {
            LeftStick = Sanitize(tuning.LeftStick),
            RightStick = Sanitize(tuning.RightStick),
            L2 = Sanitize(tuning.L2),
            R2 = Sanitize(tuning.R2),
            BluetoothPollIntervalMs = Math.Clamp(tuning.BluetoothPollIntervalMs, MinBluetoothPollIntervalMs, MaxBluetoothPollIntervalMs),
            OutputRateHz = SnapOutputRate(tuning.OutputRateHz),
        };
        return result == tuning ? tuning : result;
    }

    public static StickTuning Sanitize(StickTuning? t)
    {
        if (t is null) return new StickTuning();
        float inner = Clamp(t.InnerDeadzone, 0f, 0f, MaxStickInnerDeadzone);
        float outer = MathF.Min(Clamp(t.OuterDeadzone, 0f, 0f, MaxOuterDeadzone), 1f - MinSpan - inner);
        var result = t with
        {
            Shape = Enum.IsDefined(t.Shape) ? t.Shape : DeadzoneShape.Radial,
            InnerDeadzone = inner,
            OuterDeadzone = MathF.Max(outer, 0f),
            AntiDeadzone = Clamp(t.AntiDeadzone, 0f, 0f, MaxAntiDeadzone),
            Curve = Clamp(t.Curve, 1f, MinCurve, MaxCurve),
            Sensitivity = Clamp(t.Sensitivity, 1f, MinSensitivity, MaxSensitivity),
        };
        return result == t ? t : result;
    }

    public static TriggerTuning Sanitize(TriggerTuning? t)
    {
        if (t is null) return new TriggerTuning();
        float inner = Clamp(t.InnerDeadzone, 0f, 0f, MaxTriggerInnerDeadzone);
        float outer = MathF.Min(Clamp(t.OuterDeadzone, 0f, 0f, MaxOuterDeadzone), 1f - MinSpan - inner);
        var result = t with
        {
            InnerDeadzone = inner,
            OuterDeadzone = MathF.Max(outer, 0f),
            AntiDeadzone = Clamp(t.AntiDeadzone, 0f, 0f, MaxAntiDeadzone),
            Curve = Clamp(t.Curve, 1f, MinCurve, MaxCurve),
        };
        return result == t ? t : result;
    }

    /// <summary>0 or less = no limit; otherwise the nearest of 125, 250, 500, 1000.</summary>
    public static int SnapOutputRate(int hz)
    {
        if (hz <= 0) return 0;
        int best = 125;
        foreach (int rate in OutputRates)
        {
            if (rate == 0) continue;
            if (Math.Abs(rate - hz) < Math.Abs(best - hz)) best = rate;
        }
        return best;
    }

    private static float Clamp(float value, float fallback, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
