using DS4Handler.Core.Input;
using DS4Handler.Core.Mapping;

namespace DS4Handler.Core.Tests;

public class ButtonRemapperTests
{
    private static CalibratedState Press(Buttons b, float l2 = 0f, float r2 = 0f) =>
        new(0f, 0f, 0f, 0f, l2, r2, b);

    private static ButtonRemap Map(params (PadControl Src, RemapTarget Dst)[] entries)
    {
        var r = ButtonRemap.Identity;
        foreach (var (src, dst) in entries) r = r.With(src, dst);
        return r;
    }

    [Fact]
    public void Identity_ReturnsInputUnchanged()
    {
        var rng = new Random(42);
        for (int i = 0; i < 1000; i++)
        {
            var s = new CalibratedState(
                (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1),
                (float)rng.NextDouble(), (float)rng.NextDouble(),
                (Buttons)(rng.Next() & ((1 << 19) - 1)));
            var o = ButtonRemapper.Apply(s, ButtonRemap.Identity, out var k);
            Assert.Equal(s, o);
            Assert.Equal(0u, k);
        }
    }

    [Fact]
    public void ButtonToButton_MovesBit()
    {
        var r = Map((PadControl.L1, RemapTarget.ToPad(PadControl.Cross)));
        var o = ButtonRemapper.Apply(Press(Buttons.L1), r, out _);
        Assert.Equal(Buttons.Cross, o.Buttons);
    }

    [Fact]
    public void SwapCrossAndCircle()
    {
        var r = Map((PadControl.Cross, RemapTarget.ToPad(PadControl.Circle)),
                    (PadControl.Circle, RemapTarget.ToPad(PadControl.Cross)));
        Assert.Equal(Buttons.Circle, ButtonRemapper.Apply(Press(Buttons.Cross), r, out _).Buttons);
        Assert.Equal(Buttons.Cross, ButtonRemapper.Apply(Press(Buttons.Circle), r, out _).Buttons);
    }

    [Fact]
    public void ButtonToTrigger_FullWhileHeld()
    {
        var r = Map((PadControl.Cross, RemapTarget.ToPad(PadControl.R2)));
        var o = ButtonRemapper.Apply(Press(Buttons.Cross), r, out _);
        Assert.Equal(1f, o.R2);
        Assert.False(o.Buttons.HasFlag(Buttons.Cross));
    }

    [Fact]
    public void TriggerToButton_Threshold()
    {
        var r = Map((PadControl.R2, RemapTarget.ToPad(PadControl.Square)));
        var below = ButtonRemapper.Apply(Press(Buttons.None, r2: 0.49f), r, out _);
        Assert.False(below.Buttons.HasFlag(Buttons.Square));
        Assert.Equal(0f, below.R2);
        var at = ButtonRemapper.Apply(Press(Buttons.None, r2: 0.5f), r, out _);
        Assert.True(at.Buttons.HasFlag(Buttons.Square));
        Assert.Equal(0f, at.R2);
    }

    [Fact]
    public void TriggerSwap_KeepsAnalog()
    {
        var r = Map((PadControl.L2, RemapTarget.ToPad(PadControl.R2)),
                    (PadControl.R2, RemapTarget.ToPad(PadControl.L2)));
        var o = ButtonRemapper.Apply(Press(Buttons.None, 0.3f, 0.8f), r, out _);
        Assert.Equal(0.8f, o.L2);
        Assert.Equal(0.3f, o.R2);
    }

    [Fact]
    public void TwoSourcesOneTrigger_TakesMax()
    {
        var r = Map((PadControl.Cross, RemapTarget.ToPad(PadControl.R2)));
        Assert.Equal(1f, ButtonRemapper.Apply(Press(Buttons.Cross, r2: 0.4f), r, out _).R2);
        Assert.Equal(0.4f, ButtonRemapper.Apply(Press(Buttons.None, r2: 0.4f), r, out _).R2);
    }

    [Fact]
    public void KeyTarget_SetsBit_RemovesPadOutput()
    {
        var r = Map((PadControl.Square, RemapTarget.ToKey(0x39)));
        var o = ButtonRemapper.Apply(Press(Buttons.Square), r, out var k);
        Assert.Equal(1u << (int)PadControl.Square, k);
        Assert.False(o.Buttons.HasFlag(Buttons.Square));
    }

    [Fact]
    public void KeyTarget_OtherSourceStillProducesPad()
    {
        var r = Map((PadControl.Square, RemapTarget.ToKey(0x39)),
                    (PadControl.Circle, RemapTarget.ToPad(PadControl.Square)));
        var o = ButtonRemapper.Apply(Press(Buttons.Square | Buttons.Circle), r, out var k);
        Assert.True(o.Buttons.HasFlag(Buttons.Square));
        Assert.Equal(1u << (int)PadControl.Square, k);
    }

    [Fact]
    public void Disabled_ProducesNothing()
    {
        var r = Map((PadControl.PS, RemapTarget.Disabled));
        var o = ButtonRemapper.Apply(Press(Buttons.PS), r, out var k);
        Assert.False(o.Buttons.HasFlag(Buttons.PS));
        Assert.Equal(0u, k);
    }

    [Fact]
    public void SwapSticks_SwapsAxes()
    {
        var r = ButtonRemap.Identity.WithSwapSticks(true);
        var o = ButtonRemapper.Apply(new CalibratedState(0.1f, 0.2f, 0.3f, 0.4f, 0f, 0f, Buttons.None), r, out _);
        Assert.Equal((0.3f, 0.4f, 0.1f, 0.2f), (o.LeftX, o.LeftY, o.RightX, o.RightY));
    }

    [Fact]
    public void DigitalTriggerBits_FollowRemappedTriggers()
    {
        var r = Map((PadControl.L2, RemapTarget.Disabled), (PadControl.Cross, RemapTarget.ToPad(PadControl.R2)));
        // Input digital bits alone produce nothing; L2 is disabled so its analog is dropped.
        var o = ButtonRemapper.Apply(Press(Buttons.L2Digital | Buttons.R2Digital, l2: 0.7f), r, out _);
        Assert.Equal(0f, o.L2);
        Assert.Equal(0f, o.R2);
        Assert.False(o.Buttons.HasFlag(Buttons.L2Digital));
        Assert.False(o.Buttons.HasFlag(Buttons.R2Digital));

        o = ButtonRemapper.Apply(Press(Buttons.Cross), r, out _);
        Assert.True(o.Buttons.HasFlag(Buttons.R2Digital));
        Assert.False(o.Buttons.HasFlag(Buttons.L2Digital));
    }

    [Fact]
    public void With_PadToSelf_IsSame()
    {
        var r = ButtonRemap.Identity.With(PadControl.Cross, RemapTarget.ToPad(PadControl.Cross));
        Assert.Equal(RemapTarget.Same, r[PadControl.Cross]);
        Assert.True(r.IsIdentity);
    }

    [Fact]
    public void With_ToMute_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ButtonRemap.Identity.With(PadControl.Cross, RemapTarget.ToPad(PadControl.Mute)));
    }

    [Fact]
    public void Equality_ByValue()
    {
        var a = Map((PadControl.Cross, RemapTarget.ToKey(0x39)), (PadControl.L2, RemapTarget.ToPad(PadControl.R2))).WithSwapSticks(true);
        var b = ButtonRemap.Identity.WithSwapSticks(true)
            .With(PadControl.L2, RemapTarget.ToPad(PadControl.R2))
            .With(PadControl.Cross, RemapTarget.ToKey(0x39));
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.Equals(b.WithSwapSticks(false)));
    }

    [Fact]
    public void Apply_DoesNotAllocate()
    {
        var r = Map((PadControl.Cross, RemapTarget.ToPad(PadControl.R2)),
                    (PadControl.Square, RemapTarget.ToKey(0x39)),
                    (PadControl.L2, RemapTarget.ToPad(PadControl.Circle))).WithSwapSticks(true);
        var s = Press(Buttons.Cross | Buttons.Square, 0.6f, 0.2f);
        ButtonRemapper.Apply(s, r, out _); // warm up JIT
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) ButtonRemapper.Apply(s, r, out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
