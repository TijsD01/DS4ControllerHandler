using DS4Handler.Core.Mapping;

namespace DS4Handler.Core.Tests;

public class KeyEdgeTrackerTests
{
    private readonly List<string> _events = new();
    private readonly KeyEdgeTracker _tracker = new();

    private void Press(ushort k) => _events.Add($"press {k:X}");
    private void Release(ushort k) => _events.Add($"release {k:X}");

    private void Update(uint held, ButtonRemap remap) => _tracker.Update(held, remap, Press, Release);

    private static uint Bit(PadControl c) => 1u << (int)c;

    private static readonly ButtonRemap SquareSpace = ButtonRemap.Identity.With(PadControl.Square, RemapTarget.ToKey(0x39));

    [Fact]
    public void PressThenRelease_OneEachEdge()
    {
        Update(Bit(PadControl.Square), SquareSpace);
        Update(0, SquareSpace);
        Assert.Equal(new[] { "press 39", "release 39" }, _events);
    }

    [Fact]
    public void HeldAcrossReports_NoRepeat()
    {
        for (int i = 0; i < 5; i++) Update(Bit(PadControl.Square), SquareSpace);
        Assert.Equal(new[] { "press 39" }, _events);
    }

    [Fact]
    public void RemapChangedWhileHeld_ReleasesOldScanCodePressesNew()
    {
        Update(Bit(PadControl.Square), SquareSpace);
        _events.Clear();
        var squareA = ButtonRemap.Identity.With(PadControl.Square, RemapTarget.ToKey(0x1E));
        Update(Bit(PadControl.Square), squareA);
        Assert.Equal(new[] { "release 39", "press 1E" }, _events);
    }

    [Fact]
    public void RemapToPadWhileHeld_ReleasesKey()
    {
        Update(Bit(PadControl.Square), SquareSpace);
        _events.Clear();
        var squareToCross = ButtonRemap.Identity.With(PadControl.Square, RemapTarget.ToPad(PadControl.Cross));
        // The remapper reports no key source for a pad target.
        Update(0, squareToCross);
        Assert.Equal(new[] { "release 39" }, _events);
    }

    [Fact]
    public void ReleaseAll_ReleasesEveryHeldKey_ThenNothing()
    {
        var remap = SquareSpace.With(PadControl.Cross, RemapTarget.ToKey(0xE01D));
        Update(Bit(PadControl.Square) | Bit(PadControl.Cross), remap);
        _events.Clear();
        _tracker.ReleaseAll(Release);
        Assert.Equal(2, _events.Count);
        Assert.Contains("release 39", _events);
        Assert.Contains("release E01D", _events);
        _events.Clear();
        _tracker.ReleaseAll(Release);
        Update(0, remap);
        Assert.Empty(_events);
    }

    [Fact]
    public void TwoSourcesSameKey_ReleaseOnlyWhenBothUp()
    {
        var remap = SquareSpace.With(PadControl.Cross, RemapTarget.ToKey(0x39));
        Update(Bit(PadControl.Square), remap);
        Update(Bit(PadControl.Square) | Bit(PadControl.Cross), remap);
        Update(Bit(PadControl.Cross), remap);
        Assert.Equal(new[] { "press 39" }, _events);
        Update(0, remap);
        Assert.Equal(new[] { "press 39", "release 39" }, _events);
    }
}
