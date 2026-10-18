using DS4Handler.Core.Input;
using DS4Handler.Core.Output;

namespace DS4Handler.Core.Tests;

public class XboxMapperTests
{
    private static CalibratedState State(Buttons buttons = Buttons.None, float lx = 0, float ly = 0, float rx = 0, float ry = 0, float l2 = 0, float r2 = 0)
        => new(lx, ly, rx, ry, l2, r2, buttons);

    [Theory]
    [InlineData(Buttons.Cross, XboxButtons.A)]
    [InlineData(Buttons.Circle, XboxButtons.B)]
    [InlineData(Buttons.Square, XboxButtons.X)]
    [InlineData(Buttons.Triangle, XboxButtons.Y)]
    [InlineData(Buttons.L1, XboxButtons.LeftShoulder)]
    [InlineData(Buttons.R1, XboxButtons.RightShoulder)]
    [InlineData(Buttons.L3, XboxButtons.LeftThumb)]
    [InlineData(Buttons.R3, XboxButtons.RightThumb)]
    [InlineData(Buttons.Share, XboxButtons.Back)]
    [InlineData(Buttons.Options, XboxButtons.Start)]
    [InlineData(Buttons.PS, XboxButtons.Guide)]
    [InlineData(Buttons.DpadUp, XboxButtons.Up)]
    [InlineData(Buttons.DpadDown, XboxButtons.Down)]
    [InlineData(Buttons.DpadLeft, XboxButtons.Left)]
    [InlineData(Buttons.DpadRight, XboxButtons.Right)]
    [InlineData(Buttons.L2Digital | Buttons.R2Digital | Buttons.TouchpadClick | Buttons.Mute, XboxButtons.None)]
    public void Buttons_Map(Buttons input, XboxButtons expected)
    {
        Assert.Equal(expected, XboxMapper.Map(State(input)).Buttons);
    }

    [Fact]
    public void CombinedButtons_Map()
    {
        var r = XboxMapper.Map(State(Buttons.Cross | Buttons.R1 | Buttons.DpadUp | Buttons.DpadLeft));
        Assert.Equal(XboxButtons.A | XboxButtons.RightShoulder | XboxButtons.Up | XboxButtons.Left, r.Buttons);
    }

    [Fact]
    public void ButtonBits_MatchViGEm()
    {
        Assert.Equal(0x0001, (int)XboxButtons.Up);
        Assert.Equal(0x0400, (int)XboxButtons.Guide);
        Assert.Equal(0x1000, (int)XboxButtons.A);
        Assert.Equal(0x8000, (int)XboxButtons.Y);
    }

    [Theory]
    [InlineData(0f, (short)0)]
    [InlineData(1f, short.MaxValue)]
    [InlineData(-1f, short.MinValue)]
    [InlineData(2f, short.MaxValue)]
    [InlineData(-5f, short.MinValue)]
    [InlineData(0.5f, (short)16384)]
    [InlineData(-0.5f, (short)-16384)]
    [InlineData(float.NaN, (short)0)]
    public void Sticks_ScaleToShort(float v, short expected)
    {
        var r = XboxMapper.Map(State(lx: v, ly: v, rx: v, ry: v));
        Assert.Equal(expected, r.LeftThumbX);
        Assert.Equal(expected, r.LeftThumbY);
        Assert.Equal(expected, r.RightThumbX);
        Assert.Equal(expected, r.RightThumbY);
    }

    [Fact]
    public void StickAxes_StayIndependentAndUpStaysPositive()
    {
        var r = XboxMapper.Map(State(lx: 1, ly: -1, rx: 0, ry: 1));
        Assert.Equal(short.MaxValue, r.LeftThumbX);
        Assert.Equal(short.MinValue, r.LeftThumbY);
        Assert.Equal(0, r.RightThumbX);
        Assert.Equal(short.MaxValue, r.RightThumbY);
    }

    [Theory]
    [InlineData(0f, (byte)0)]
    [InlineData(1f, (byte)255)]
    [InlineData(0.5f, (byte)128)]
    [InlineData(-0.2f, (byte)0)]
    [InlineData(1.7f, (byte)255)]
    [InlineData(0.998f, (byte)254)]
    [InlineData(float.NaN, (byte)0)]
    public void Triggers_ScaleToByte(float v, byte expected)
    {
        var r = XboxMapper.Map(State(l2: v, r2: v));
        Assert.Equal(expected, r.LeftTrigger);
        Assert.Equal(expected, r.RightTrigger);
    }

    [Fact]
    public void Triggers_AreIndependent()
    {
        var r = XboxMapper.Map(State(l2: 0, r2: 1));
        Assert.Equal(0, r.LeftTrigger);
        Assert.Equal(255, r.RightTrigger);
    }

    [Theory]
    [InlineData(0.5f, 1f, 0.5f)]
    [InlineData(0.5f, 2f, 0.25f)]
    [InlineData(-0.5f, 2f, -0.25f)]
    [InlineData(0.25f, 0.5f, 0.5f)]
    [InlineData(-0.25f, 0.5f, -0.5f)]
    [InlineData(1f, 3f, 1f)]
    [InlineData(-1f, 3f, -1f)]
    [InlineData(0f, 2f, 0f)]
    [InlineData(0.5f, 0f, 0.5f)]   // invalid exponent leaves value unchanged
    [InlineData(0.5f, -1f, 0.5f)]
    public void ResponseCurve_PreservesSign(float value, float exponent, float expected)
    {
        Assert.Equal(expected, XboxMapper.ApplyResponseCurve(value, exponent), 5);
    }
}
