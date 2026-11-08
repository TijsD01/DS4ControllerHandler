using DS4Handler.Core.Input;

namespace DS4Handler.Core.Output;

/// <summary>Maps calibrated PlayStation input to an Xbox 360 report.</summary>
public static class XboxMapper
{
    public static XboxReport Map(in CalibratedState s) => new(
        StickToShort(s.LeftX),
        StickToShort(s.LeftY),
        StickToShort(s.RightX),
        StickToShort(s.RightY),
        TriggerToByte(s.L2),
        TriggerToByte(s.R2),
        MapButtons(s.Buttons));

    /// <summary>
    /// PlayStation buttons to XUSB bits with shifts and masks, no loop (runs per report).
    /// Cross/Circle/Square/Triangle → A/B/X/Y, L1/R1 → shoulders, Share → Back, Options → Start,
    /// L3/R3 → thumbs, PS → Guide, d-pad → d-pad. L2/R2 digital, touchpad and mute have no bit.
    /// </summary>
    public static XboxButtons MapButtons(Buttons buttons)
    {
        uint b = (uint)buttons;
        uint x = ((b & 0xF) << 12)          // Cross, Circle, Square, Triangle → A, B, X, Y
               | (((b >> 4) & 0x3) << 8)    // L1, R1 → LeftShoulder, RightShoulder
               | (((b >> 8) & 0x1) << 5)    // Share → Back
               | (((b >> 9) & 0x1) << 4)    // Options → Start
               | (((b >> 10) & 0x3) << 6)   // L3, R3 → LeftThumb, RightThumb
               | (((b >> 12) & 0x1) << 10)  // PS → Guide
               | ((b >> 15) & 0xF);         // Up, Down, Left, Right
        return (XboxButtons)x;
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
