using DS4Handler.Core.Mapping;
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
/// the deadzone edge (no jump) and is full at 1 - outer.
/// Radial: circle, scaled along the radius.
/// Square: the edge is the square max(|x|,|y|) = dz, still scaled along the radius from where the ray
/// leaves that square, so a full push reaches full output in every direction (the inner deadzone is
/// capped so diagonals keep a live range, see <see cref="MaxInnerDeadzone"/>).
/// Axial: each axis on its own (see below).</item>
/// <item>Response curve on the magnitude (<see cref="XboxMapper.ApplyResponseCurve"/> semantics).</item>
/// <item>Sensitivity on the magnitude, clamped to 1.</item>
/// <item>Anti-deadzone: magnitude (0,1] maps onto (anti,1]. Last of the magnitude steps, so the curve and
/// the sensitivity never shrink the jump that cancels a game's own deadzone.</item>
/// <item>Square output: elliptical grid mapping of the disc onto the square, so diagonals reach (±1,±1).
/// Close to identity near the middle; near the rim a small overscale gives the corner a few degrees of
/// width (see <see cref="ToSquareOutput"/>).</item>
/// <item>Invert X / Y, then clamp both axes to [-1, 1].</item>
/// </list>
/// Axial does steps 1–4 per axis, so one axis never pulls on the other (each axis only grows while the
/// stick moves outwards, and a game's per-axis deadzone is cancelled per axis). The deadzone + curve part
/// is divided by the length the same mapping gives for a full push in that direction, so a full push
/// reaches exactly 1 in every direction (diagonals included); sensitivity and anti-deadzone then work on
/// each axis and may push a diagonal slightly past the circle (clamped per axis).
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
    private const float RimSnapSquared = 0.99999f;

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
        if (t.SquareOutput) (x, y) = ToSquareOutput(x, y);
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
                return ShapeAxial(x, y, t);
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
        float mag = MagnitudeSteps(MathF.Min(len, 1f), t.Curve, t.Sensitivity, t.AntiDeadzone);
        float k = mag / len;
        return (dx * k, dy * k);
    }

    /// <summary>
    /// Axial: per-axis deadzone and curve, normalised by the full-push length in this direction, then
    /// per-axis sensitivity and anti-deadzone. Every step is monotonic per axis and the normalisation is
    /// constant along a ray, so an axis never drops while the stick moves outwards.
    /// </summary>
    private static (float X, float Y) ShapeAxial(float x, float y, StickTuning t)
    {
        float ax = MathF.Abs(x), ay = MathF.Abs(y);
        float dz = t.InnerDeadzone;
        if (ax <= dz && ay <= dz) return (0f, 0f);
        float span = MathF.Max(1f - t.OuterDeadzone - dz, MinSpan);
        float r = MathF.Sqrt(x * x + y * y);
        // Length of a full push (r = 1) in this direction after the same per-axis mapping.
        float fc = AxialAxis(ax / r, dz, span, t.Curve), fs = AxialAxis(ay / r, dz, span, t.Curve);
        float full = MathF.Sqrt(fc * fc + fs * fs);
        if (!(full > 0f)) return (0f, 0f); // only when the sanitize cap was bypassed
        float nx = AxialOut(AxialAxis(ax, dz, span, t.Curve) / full, t);
        float ny = AxialOut(AxialAxis(ay, dz, span, t.Curve) / full, t);
        return (MathF.CopySign(nx, x), MathF.CopySign(ny, y));
    }

    private static float AxialAxis(float v, float dz, float span, float curve) =>
        v <= dz ? 0f : XboxMapper.ApplyResponseCurve(MathF.Min((v - dz) / span, 1f), curve);

    private static float AxialOut(float n, StickTuning t)
    {
        if (!(n > 0f)) return 0f;
        n = MathF.Min(n * t.Sensitivity, 1f);
        return MathF.Min(t.AntiDeadzone + (1f - t.AntiDeadzone) * n, 1f);
    }

    /// <summary>
    /// Square output as the stick pipeline uses it: <see cref="DiscToSquare"/> plus a gentle overscale
    /// near the rim (1 + <see cref="CornerOverscale"/>·r⁸, about +3% at r = 0.9 and +6% at the rim) so
    /// pushes within roughly 3° of a diagonal reach the corner (±1, ±1) instead of only an exact 45°.
    /// Small movements are unchanged (+0.02% at r = 0.5) and the output stays monotonic along a ray.
    /// </summary>
    public static (float X, float Y) ToSquareOutput(float u, float v)
    {
        if (float.IsNaN(u) || float.IsNaN(v)) return (0f, 0f);
        float r = MathF.Min(MathF.Sqrt(u * u + v * v), 1f);
        var (x, y) = DiscToSquare(u, v);
        float r4 = r * r * r * r;
        float k = 1f + CornerOverscale * r4 * r4;
        return (Math.Clamp(x * k, -1f, 1f), Math.Clamp(y * k, -1f, 1f));
    }

    /// <summary>Overscale at the rim used by <see cref="ToSquareOutput"/>.</summary>
    public const float CornerOverscale = 0.06f;

    /// <summary>
    /// Elliptical grid mapping (Fong): the unit disc onto the unit square, so (0.707, 0.707) becomes (1, 1)
    /// while straight pushes and small movements stay (nearly) the same. Points on or outside the rim
    /// (r ≥ 0.999995, which absorbs float rounding of a calibrated full push) are mapped from the rim
    /// point in their direction, so they land on the square's edge.
    /// </summary>
    public static (float X, float Y) DiscToSquare(float u, float v)
    {
        if (float.IsNaN(u) || float.IsNaN(v)) return (0f, 0f);
        float r2 = u * u + v * v;
        if (r2 >= RimSnapSquared)
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
        return ShapeResponse(v, t.InnerDeadzone, t.OuterDeadzone, t.AntiDeadzone, t.Curve);
    }

    /// <summary>
    /// The one-dimensional response on a push of 0..1: inner deadzone, outer deadzone (full at
    /// 1 - outer), curve, sensitivity (clamped to 1) and anti-deadzone, clamped to [0, 1]. This is
    /// exactly what <see cref="ShapeTrigger"/> does, and what <see cref="ShapeStick"/> gives for a
    /// straight push along one axis with any <see cref="DeadzoneShape"/> (before square output and
    /// inverting). Pure; the curve preview in the app draws it, so preview and output cannot drift apart.
    /// NaN reads as released.
    /// </summary>
    public static float ShapeResponse(float v, float inner, float outer, float anti, float curve, float sensitivity = 1f)
    {
        if (!(v > inner)) return 0f; // also NaN
        float span = MathF.Max(1f - outer - inner, MinSpan);
        float mag = MathF.Min((v - inner) / span, 1f);
        return Math.Clamp(MagnitudeSteps(mag, curve, sensitivity, anti), 0f, 1f);
    }

    /// <summary><see cref="ShapeResponse(float, float, float, float, float, float)"/> with one stick's settings (neutral settings return the input).</summary>
    public static float ShapeResponse(float v, StickTuning t) =>
        HasMagnitudeStep(t) ? ShapeResponse(v, t.InnerDeadzone, t.OuterDeadzone, t.AntiDeadzone, t.Curve, t.Sensitivity) : v;

    /// <summary>Same as <see cref="ShapeTrigger"/>.</summary>
    public static float ShapeResponse(float v, TriggerTuning t) => ShapeTrigger(v, t);

    /// <summary>Curve, sensitivity (clamped to 1), then anti-deadzone on a magnitude 0..1.</summary>
    private static float MagnitudeSteps(float mag, float curve, float sensitivity, float anti)
    {
        mag = XboxMapper.ApplyResponseCurve(mag, curve);
        mag = MathF.Min(mag * sensitivity, 1f);
        return anti + (1f - anti) * mag;
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
            Remap = tuning.Remap ?? ButtonRemap.Identity,
        };
        return result == tuning ? tuning : result;
    }

    public static StickTuning Sanitize(StickTuning? t)
    {
        if (t is null) return new StickTuning();
        var shape = Enum.IsDefined(t.Shape) ? t.Shape : DeadzoneShape.Radial;
        float inner = Clamp(t.InnerDeadzone, 0f, 0f, MaxInnerDeadzone(shape, 0f));
        float outer = MathF.Min(Clamp(t.OuterDeadzone, 0f, 0f, MaxOuterDeadzone), 1f - MinSpan - inner * DiagonalFactor(shape));
        var result = t with
        {
            Shape = shape,
            InnerDeadzone = inner,
            OuterDeadzone = MathF.Max(outer, 0f),
            AntiDeadzone = Clamp(t.AntiDeadzone, 0f, 0f, MaxAntiDeadzone),
            Curve = Clamp(t.Curve, 1f, MinCurve, MaxCurve),
            Sensitivity = Clamp(t.Sensitivity, 1f, MinSensitivity, MaxSensitivity),
            FilterResponse = Clamp(t.FilterResponse, 0f, 0f, 1f),
        };
        return result == t ? t : result;
    }

    /// <summary>
    /// Largest stick inner deadzone <see cref="Sanitize(StickTuning)"/> keeps for this shape and outer
    /// deadzone, for limiting a slider. Radial: 1 - <see cref="MinSpan"/> - outer. Square and Axial:
    /// (1 - <see cref="MinSpan"/> - outer) / √2, because on a diagonal the square / cross edge sits √2
    /// further out; above it full diagonals would stop reaching full output (or die at 0.707). Never
    /// above <see cref="MaxStickInnerDeadzone"/>.
    /// </summary>
    public static float MaxInnerDeadzone(DeadzoneShape shape, float outerDeadzone)
    {
        float outer = Clamp(outerDeadzone, 0f, 0f, MaxOuterDeadzone);
        return Math.Clamp((1f - MinSpan - outer) / DiagonalFactor(shape), 0f, MaxStickInnerDeadzone);
    }

    /// <summary>
    /// Largest trigger inner deadzone <see cref="Sanitize(TriggerTuning)"/> keeps with this outer deadzone
    /// without shrinking the outer one: 1 - <see cref="MinSpan"/> - outer, never above
    /// <see cref="MaxTriggerInnerDeadzone"/>. For limiting a slider.
    /// </summary>
    public static float MaxTriggerInner(float outerDeadzone)
    {
        float outer = Clamp(outerDeadzone, 0f, 0f, MaxOuterDeadzone);
        return Math.Clamp(1f - MinSpan - outer, 0f, MaxTriggerInnerDeadzone);
    }

    private static float DiagonalFactor(DeadzoneShape shape) => shape is DeadzoneShape.Square or DeadzoneShape.Axial ? Sqrt2 : 1f;

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
