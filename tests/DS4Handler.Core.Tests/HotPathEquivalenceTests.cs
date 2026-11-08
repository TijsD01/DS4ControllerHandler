using DS4Handler.Core.Input;
using DS4Handler.Core.Output;

namespace DS4Handler.Core.Tests;

/// <summary>
/// The per-report button mapping was made faster (shifts and a table, no loops). These tests pin it to the plain
/// reference logic it replaced, over every possible input, so speed never changes behaviour.
/// </summary>
public class HotPathEquivalenceTests
{
    private const int ButtonBits = 19; // Buttons uses bits 0..18

    [Fact]
    public void XboxButtons_MatchReference_ForEveryCombination()
    {
        for (uint bits = 0; bits < 1u << ButtonBits; bits++)
        {
            var s = new CalibratedState(0, 0, 0, 0, 0, 0, (Buttons)bits);
            Assert.Equal(ReferenceXboxButtons((Buttons)bits), XboxMapper.Map(s).Buttons);
        }
    }

    [Fact]
    public void Ds4Buttons_MatchReference_ForEveryCombination()
    {
        for (uint bits = 0; bits < 1u << ButtonBits; bits++)
        {
            var s = new CalibratedState(0, 0, 0, 0, 0, 0, (Buttons)bits);
            var r = Ds4Mapper.Map(s);
            Assert.Equal(ReferenceDPad((Buttons)bits), r.DPad);
            Assert.Equal(ReferenceDs4Buttons((Buttons)bits), r.Buttons);
            Assert.Equal(ReferenceSpecial((Buttons)bits), r.Special);
        }
    }

    [Fact]
    public void Ds4Mapper_DigitalTriggerBits_StillFollowTheAnalogValue()
    {
        var r = Ds4Mapper.Map(new CalibratedState(0, 0, 0, 0, 0.5f, 0.05f, Buttons.None));
        Assert.Equal(Ds4OutputButtons.TriggerLeft, r.Buttons);
    }

    // ---- Reference logic: the readable versions the fast code replaced. ----

    private static XboxButtons ReferenceXboxButtons(Buttons b)
    {
        (Buttons From, XboxButtons To)[] map =
        {
            (Buttons.Cross, XboxButtons.A), (Buttons.Circle, XboxButtons.B),
            (Buttons.Square, XboxButtons.X), (Buttons.Triangle, XboxButtons.Y),
            (Buttons.L1, XboxButtons.LeftShoulder), (Buttons.R1, XboxButtons.RightShoulder),
            (Buttons.L3, XboxButtons.LeftThumb), (Buttons.R3, XboxButtons.RightThumb),
            (Buttons.Share, XboxButtons.Back), (Buttons.Options, XboxButtons.Start),
            (Buttons.PS, XboxButtons.Guide),
            (Buttons.DpadUp, XboxButtons.Up), (Buttons.DpadDown, XboxButtons.Down),
            (Buttons.DpadLeft, XboxButtons.Left), (Buttons.DpadRight, XboxButtons.Right),
        };
        var result = XboxButtons.None;
        foreach (var (from, to) in map)
            if ((b & from) != 0) result |= to;
        return result;
    }

    private static Ds4OutputButtons ReferenceDs4Buttons(Buttons s)
    {
        var b = Ds4OutputButtons.None;
        if ((s & Buttons.Cross) != 0) b |= Ds4OutputButtons.Cross;
        if ((s & Buttons.Circle) != 0) b |= Ds4OutputButtons.Circle;
        if ((s & Buttons.Square) != 0) b |= Ds4OutputButtons.Square;
        if ((s & Buttons.Triangle) != 0) b |= Ds4OutputButtons.Triangle;
        if ((s & Buttons.L1) != 0) b |= Ds4OutputButtons.ShoulderLeft;
        if ((s & Buttons.R1) != 0) b |= Ds4OutputButtons.ShoulderRight;
        if ((s & Buttons.L3) != 0) b |= Ds4OutputButtons.ThumbLeft;
        if ((s & Buttons.R3) != 0) b |= Ds4OutputButtons.ThumbRight;
        if ((s & Buttons.Share) != 0) b |= Ds4OutputButtons.Share;
        if ((s & Buttons.Options) != 0) b |= Ds4OutputButtons.Options;
        return b;
    }

    private static Ds4SpecialButtons ReferenceSpecial(Buttons s)
    {
        var special = Ds4SpecialButtons.None;
        if ((s & Buttons.PS) != 0) special |= Ds4SpecialButtons.Ps;
        if ((s & Buttons.TouchpadClick) != 0) special |= Ds4SpecialButtons.Touchpad;
        return special;
    }

    private static Ds4DPad ReferenceDPad(Buttons buttons)
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
