using DS4Handler.Core.Input;

namespace DS4Handler.Core.Output;

/// <summary>Maps calibrated PlayStation input to an Xbox 360 report.</summary>
public static class XboxMapper
{
    private static readonly (Buttons From, XboxButtons To)[] ButtonMap =
    {
        (Buttons.Cross, XboxButtons.A),
        (Buttons.Circle, XboxButtons.B),
        (Buttons.Square, XboxButtons.X),
        (Buttons.Triangle, XboxButtons.Y),
        (Buttons.L1, XboxButtons.LeftShoulder),
        (Buttons.R1, XboxButtons.RightShoulder),
        (Buttons.L3, XboxButtons.LeftThumb),
        (Buttons.R3, XboxButtons.RightThumb),
        (Buttons.Share, XboxButtons.Back),
        (Buttons.Options, XboxButtons.Start),
        (Buttons.PS, XboxButtons.Guide),
        (Buttons.DpadUp, XboxButtons.Up),
        (Buttons.DpadDown, XboxButtons.Down),
        (Buttons.DpadLeft, XboxButtons.Left),
        (Buttons.DpadRight, XboxButtons.Right),
    };

    public static XboxReport Map(CalibratedState s)
    {
        var buttons = XboxButtons.None;
        foreach (var (from, to) in ButtonMap)
        {
            if ((s.Buttons & from) != 0) buttons |= to;
        }

        return new XboxReport(
            StickToShort(s.LeftX),
            StickToShort(s.LeftY),
            StickToShort(s.RightX),
            StickToShort(s.RightY),
            TriggerToByte(s.L2),
            TriggerToByte(s.R2),
            buttons);
    }

    /// <summary>-1..1 to -32768..32767 (+1 → 32767, -1 → -32768, 0 → 0). NaN maps to 0.</summary>
    public static short StickToShort(float v)
    {
        if (float.IsNaN(v)) return 0;
        v = Math.Clamp(v, -1f, 1f);
        return v >= 0
            ? (short)MathF.Round(v * short.MaxValue)
            : (short)MathF.Round(v * -(float)short.MinValue);
    }

    /// <summary>0..1 to 0..255, rounded and clamped. NaN maps to 0.</summary>
    public static byte TriggerToByte(float v)
    {
        if (float.IsNaN(v)) return 0;
        return (byte)MathF.Round(Math.Clamp(v, 0f, 1f) * 255f);
    }

    /// <summary>
    /// Sign-preserving power curve: sign(v) * |v|^exponent. Exponent 1 is linear,
    /// above 1 is softer near the center, below 1 is more sensitive near the center.
    /// Non-positive or NaN exponents leave the value unchanged.
    /// </summary>
    public static float ApplyResponseCurve(float value, float exponent)
    {
        if (float.IsNaN(value)) return 0f;
        if (!(exponent > 0f) || exponent == 1f) return value;
        float magnitude = MathF.Pow(Math.Min(MathF.Abs(value), 1f), exponent);
        return value < 0 ? -magnitude : magnitude;
    }
}
