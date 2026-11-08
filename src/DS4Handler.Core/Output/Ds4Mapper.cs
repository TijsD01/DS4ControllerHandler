using DS4Handler.Core.Input;

namespace DS4Handler.Core.Output;

/// <summary>Converts calibrated input into a virtual DualShock 4 report.</summary>
public static class Ds4Mapper
{
    /// <summary>Trigger value above which the digital L2/R2 bit is set.</summary>
    public const float DigitalTriggerThreshold = 0.1f;

    public static Ds4OutputReport Map(in CalibratedState s)
    {
        uint b = (uint)s.Buttons; // bit positions are hard-coded: HotPathEquivalenceTests guards them if Buttons/Ds4OutputButtons change
        // Face buttons move to ViGEm's order; L1/R1, Share/Options and L3/R3 all shift up by 4 bits.
        uint face = ((b & 0x1) << 5)   // Cross → 32
                  | ((b & 0x2) << 5)   // Circle → 64
                  | ((b & 0x4) << 2)   // Square → 16
                  | ((b & 0x8) << 4)   // Triangle → 128
                  | ((b & 0xF30) << 4);
        if (s.L2 > DigitalTriggerThreshold) face |= (uint)Ds4OutputButtons.TriggerLeft;
        if (s.R2 > DigitalTriggerThreshold) face |= (uint)Ds4OutputButtons.TriggerRight;
        var special = (Ds4SpecialButtons)((b >> 12) & 0x3); // PS → 1, touchpad click → 2

        return new Ds4OutputReport(
            StickToByte(s.LeftX), StickToByte(-s.LeftY),
            StickToByte(s.RightX), StickToByte(-s.RightY),
            XboxMapper.TriggerToByte(s.L2), XboxMapper.TriggerToByte(s.R2),
            (Ds4OutputButtons)face, DPadFrom(s.Buttons), special);
    }

    /// <summary>-1..1 to 0..255 with 0 → 128, +1 → 255, -1 → 0. NaN maps to 128.</summary>
    public static byte StickToByte(float v)
    {
        if (float.IsNaN(v)) return 128;
        v = Math.Clamp(v, -1f, 1f);
        return v >= 0 ? (byte)MathF.Round(128f + v * 127f) : (byte)MathF.Round(128f + v * 128f);
    }

    /// <summary>Hat direction for the four d-pad bits; a table lookup (runs per report).</summary>
    public static Ds4DPad DPadFrom(Buttons buttons) => DPadTable[((uint)buttons >> 15) & 0xF];

    // Index: bit 0 up, 1 down, 2 left, 3 right (Buttons.DpadUp..DpadRight shifted down by 15).
    private static readonly Ds4DPad[] DPadTable = BuildDPadTable();

    private static Ds4DPad[] BuildDPadTable()
    {
        var table = new Ds4DPad[16];
        for (int i = 0; i < 16; i++) table[i] = DPadSlow((Buttons)((uint)i << 15));
        return table;
    }

    private static Ds4DPad DPadSlow(Buttons buttons)
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
