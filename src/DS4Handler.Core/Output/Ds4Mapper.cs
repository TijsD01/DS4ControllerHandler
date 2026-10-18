using DS4Handler.Core.Input;

namespace DS4Handler.Core.Output;

/// <summary>Converts calibrated input into a virtual DualShock 4 report.</summary>
public static class Ds4Mapper
{
    /// <summary>Trigger value above which the digital L2/R2 bit is set.</summary>
    public const float DigitalTriggerThreshold = 0.1f;

    public static Ds4OutputReport Map(CalibratedState s)
    {
        var b = Ds4OutputButtons.None;
        if ((s.Buttons & Buttons.Cross) != 0) b |= Ds4OutputButtons.Cross;
        if ((s.Buttons & Buttons.Circle) != 0) b |= Ds4OutputButtons.Circle;
        if ((s.Buttons & Buttons.Square) != 0) b |= Ds4OutputButtons.Square;
        if ((s.Buttons & Buttons.Triangle) != 0) b |= Ds4OutputButtons.Triangle;
        if ((s.Buttons & Buttons.L1) != 0) b |= Ds4OutputButtons.ShoulderLeft;
        if ((s.Buttons & Buttons.R1) != 0) b |= Ds4OutputButtons.ShoulderRight;
        if ((s.Buttons & Buttons.L3) != 0) b |= Ds4OutputButtons.ThumbLeft;
        if ((s.Buttons & Buttons.R3) != 0) b |= Ds4OutputButtons.ThumbRight;
        if ((s.Buttons & Buttons.Share) != 0) b |= Ds4OutputButtons.Share;
        if ((s.Buttons & Buttons.Options) != 0) b |= Ds4OutputButtons.Options;
        if (s.L2 > DigitalTriggerThreshold) b |= Ds4OutputButtons.TriggerLeft;
        if (s.R2 > DigitalTriggerThreshold) b |= Ds4OutputButtons.TriggerRight;

        var special = Ds4SpecialButtons.None;
        if ((s.Buttons & Buttons.PS) != 0) special |= Ds4SpecialButtons.Ps;
        if ((s.Buttons & Buttons.TouchpadClick) != 0) special |= Ds4SpecialButtons.Touchpad;

        return new Ds4OutputReport(
            StickToByte(s.LeftX), StickToByte(-s.LeftY),
            StickToByte(s.RightX), StickToByte(-s.RightY),
            XboxMapper.TriggerToByte(s.L2), XboxMapper.TriggerToByte(s.R2),
            b, DPadFrom(s.Buttons), special);
    }

    /// <summary>-1..1 to 0..255 with 0 → 128, +1 → 255, -1 → 0. NaN maps to 128.</summary>
    public static byte StickToByte(float v)
    {
        if (float.IsNaN(v)) return 128;
        v = Math.Clamp(v, -1f, 1f);
        return v >= 0 ? (byte)MathF.Round(128f + v * 127f) : (byte)MathF.Round(128f + v * 128f);
    }

    public static Ds4DPad DPadFrom(Buttons buttons)
    {
        bool up = (buttons & Buttons.DpadUp) != 0, down = (buttons & Buttons.DpadDown) != 0;
        bool left = (buttons & Buttons.DpadLeft) != 0, right = (buttons & Buttons.DpadRight) != 0;
        if (up && down) up = down = false;
        if (left && right) left = right = false;
        return (up, down, left, right) switch
        {
            (true, _, true, _) => Ds4DPad.NorthWest,
            (true, _, _, true) => Ds4DPad.NorthEast,
            (true, _, _, _) => Ds4DPad.North,
            (_, true, true, _) => Ds4DPad.SouthWest,
            (_, true, _, true) => Ds4DPad.SouthEast,
            (_, true, _, _) => Ds4DPad.South,
            (_, _, true, _) => Ds4DPad.West,
            (_, _, _, true) => Ds4DPad.East,
            _ => Ds4DPad.None,
        };
    }
}
