using DS4Handler.Core.Input;
using DS4Handler.Core.Output;

namespace DS4Handler.Core.Tests;

public class Ds4MapperTests
{
    private static CalibratedState S(float lx = 0, float ly = 0, float rx = 0, float ry = 0, float l2 = 0, float r2 = 0, Buttons b = Buttons.None) =>
        new(lx, ly, rx, ry, l2, r2, b);

    [Fact]
    public void Neutral_input_is_centered()
    {
        var r = Ds4Mapper.Map(S());
        Assert.Equal(new Ds4OutputReport(128, 128, 128, 128, 0, 0, Ds4OutputButtons.None, Ds4DPad.None, Ds4SpecialButtons.None), r);
    }

    [Fact]
    public void Sticks_map_with_inverted_Y()
    {
        var r = Ds4Mapper.Map(S(lx: 1, ly: 1, rx: -1, ry: -1));
        Assert.Equal((byte)255, r.LeftX);
        Assert.Equal((byte)0, r.LeftY);   // up
        Assert.Equal((byte)0, r.RightX);
        Assert.Equal((byte)255, r.RightY); // down
    }

    [Fact]
    public void Stick_out_of_range_and_nan_are_safe()
    {
        Assert.Equal((byte)255, Ds4Mapper.StickToByte(5f));
        Assert.Equal((byte)0, Ds4Mapper.StickToByte(-5f));
        Assert.Equal((byte)128, Ds4Mapper.StickToByte(float.NaN));
    }

    [Fact]
    public void Triggers_map_and_set_digital_bits_above_threshold()
    {
        var r = Ds4Mapper.Map(S(l2: 1f, r2: 0.5f));
        Assert.Equal((byte)255, r.L2);
        Assert.Equal((byte)128, r.R2);
        Assert.Equal(Ds4OutputButtons.TriggerLeft | Ds4OutputButtons.TriggerRight, r.Buttons);

        var low = Ds4Mapper.Map(S(l2: 0.05f, r2: 0.1f));
        Assert.Equal(Ds4OutputButtons.None, low.Buttons);
    }

    [Fact]
    public void Buttons_map()
    {
        var all = Buttons.Cross | Buttons.Circle | Buttons.Square | Buttons.Triangle | Buttons.L1 | Buttons.R1
            | Buttons.L3 | Buttons.R3 | Buttons.Share | Buttons.Options | Buttons.PS | Buttons.TouchpadClick;
        var r = Ds4Mapper.Map(S(b: all));
        Assert.Equal(Ds4OutputButtons.Cross | Ds4OutputButtons.Circle | Ds4OutputButtons.Square | Ds4OutputButtons.Triangle
            | Ds4OutputButtons.ShoulderLeft | Ds4OutputButtons.ShoulderRight | Ds4OutputButtons.ThumbLeft | Ds4OutputButtons.ThumbRight
            | Ds4OutputButtons.Share | Ds4OutputButtons.Options, r.Buttons);
        Assert.Equal(Ds4SpecialButtons.Ps | Ds4SpecialButtons.Touchpad, r.Special);
    }

    [Theory]
    [InlineData(Buttons.DpadUp, Ds4DPad.North)]
    [InlineData(Buttons.DpadUp | Buttons.DpadRight, Ds4DPad.NorthEast)]
    [InlineData(Buttons.DpadRight, Ds4DPad.East)]
    [InlineData(Buttons.DpadDown | Buttons.DpadRight, Ds4DPad.SouthEast)]
    [InlineData(Buttons.DpadDown, Ds4DPad.South)]
    [InlineData(Buttons.DpadDown | Buttons.DpadLeft, Ds4DPad.SouthWest)]
    [InlineData(Buttons.DpadLeft, Ds4DPad.West)]
    [InlineData(Buttons.DpadUp | Buttons.DpadLeft, Ds4DPad.NorthWest)]
    [InlineData(Buttons.None, Ds4DPad.None)]
    [InlineData(Buttons.DpadUp | Buttons.DpadDown, Ds4DPad.None)]
    [InlineData(Buttons.DpadLeft | Buttons.DpadRight, Ds4DPad.None)]
    [InlineData(Buttons.DpadUp | Buttons.DpadDown | Buttons.DpadLeft, Ds4DPad.West)]
    public void DPad_maps_to_hat(Buttons input, Ds4DPad expected) =>
        Assert.Equal(expected, Ds4Mapper.Map(S(b: input)).DPad);

    [Fact]
    public void Enum_values_match_ViGEm()
    {
        Assert.Equal(8, (int)Ds4DPad.None);
        Assert.Equal(0, (int)Ds4DPad.North);
        Assert.Equal(7, (int)Ds4DPad.NorthWest);
        Assert.Equal(32768, (int)Ds4OutputButtons.ThumbRight);
        Assert.Equal(16, (int)Ds4OutputButtons.Square);
        Assert.Equal(2, (int)Ds4SpecialButtons.Touchpad);
    }
}
