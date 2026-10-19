using DS4Handler.HidHide;

namespace DS4Handler.Core.Tests;

/// <summary>Every combination of the inputs of <see cref="HidHideCleanup.Plan"/>, checked against the rules it promises.</summary>
public class HidHideCleanupMatrixTests
{
    private const string Mine1 = @"HID\VID_054C&PID_09CC&MI_03\7&1234&0&0000";
    private const string Mine2 = @"HID\VID_054C&PID_0CE6\9&FF&0&0000";
    private const string Users = @"HID\VID_046D&PID_C52B\6&AA&0&0001";
    private const string Exe = @"\Device\HarddiskVolume3\Program Files\DS4 Handler\DS4Handler.exe";
    private const string OtherApp = @"\Device\HarddiskVolume3\Games\Tool.exe";

    public static IEnumerable<object[]> Matrix()
    {
        foreach (var ownedOnList in new[] { 0, 1, 2 })        // how many of the 2 owned ids are still hidden
        foreach (var othersHidden in new[] { false, true })
        foreach (var exeOnList in new[] { false, true })
        foreach (var inverted in new[] { false, true })
        foreach (var active in new[] { false, true })
        foreach (var activatedByApp in new[] { false, true })
        foreach (var passExe in new[] { false, true })
            yield return new object[] { ownedOnList, othersHidden, exeOnList, inverted, active, activatedByApp, passExe };
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void DecisionTable(int ownedOnList, bool othersHidden, bool exeOnList, bool inverted, bool active, bool activatedByApp, bool passExe)
    {
        var black = new List<string>();
        if (othersHidden) black.Add(Users);
        if (ownedOnList >= 1) black.Add(Mine1.ToLowerInvariant()); // HidHide may store another casing
        if (ownedOnList >= 2) black.Add(Mine2);
        var white = new List<string> { OtherApp };
        if (exeOnList) white.Add(Exe.ToUpperInvariant());
        var blackBefore = black.ToArray();
        var whiteBefore = white.ToArray();

        var plan = HidHideCleanup.Plan(black, white, active, inverted, new[] { Mine1, Mine2 }, passExe ? Exe : null, activatedByApp);

        // Inputs are not mutated.
        Assert.Equal(blackBefore, black);
        Assert.Equal(whiteBefore, white);

        // Hide list: owned ids gone, everything else kept in order.
        Assert.Equal(othersHidden ? new[] { Users } : Array.Empty<string>(), plan.Blacklist);
        Assert.Equal(ownedOnList, plan.Unhidden.Count);
        Assert.Equal(2 - ownedOnList, plan.AlreadyVisible.Count);
        Assert.Equal(new[] { Mine1, Mine2 }.OrderBy(x => x), plan.Unhidden.Concat(plan.AlreadyVisible).OrderBy(x => x));
        Assert.Equal(ownedOnList > 0, plan.BlacklistChanged);
        Assert.Equal(othersHidden ? 1 : 0, plan.OthersStillHidden);

        // Allow list: only our own exe, only when not inverted and a path was given.
        bool removeExe = exeOnList && !inverted && passExe;
        Assert.Equal(removeExe, plan.WhitelistChanged);
        Assert.Equal(removeExe ? new[] { OtherApp } : whiteBefore, plan.Whitelist);

        // Switch: only when the app turned it on, it is on, and nothing stays hidden.
        Assert.Equal(activatedByApp && active && !othersHidden, plan.Deactivate);
    }

    [Fact]
    public void Deactivates_EvenWhenNoOwnedIdsAreLeft()
    {
        // The app turned HidHide on, its pads were already unhidden on exit: uninstall still turns it off.
        var plan = HidHideCleanup.Plan(Array.Empty<string>(), Array.Empty<string>(), active: true, whitelistInverted: false,
            Array.Empty<string>(), null, activatedByApp: true);
        Assert.True(plan.Deactivate);
        Assert.False(plan.BlacklistChanged);
    }

    [Fact]
    public void OnlyExactIdsAreRemoved_NotPrefixesOrSubstrings()
    {
        var longer = Mine1 + "0";
        var plan = HidHideCleanup.Plan(new[] { longer, Mine1[..^1] }, Array.Empty<string>(), true, false, new[] { Mine1 }, null, true);
        Assert.Equal(new[] { longer, Mine1[..^1] }, plan.Blacklist);
        Assert.Equal(new[] { Mine1 }, plan.AlreadyVisible);
        Assert.False(plan.Deactivate);
    }

    [Fact]
    public void DuplicateEntriesOnTheHideList_AreAllRemoved()
    {
        var plan = HidHideCleanup.Plan(new[] { Mine1, Users, Mine1.ToLowerInvariant(), Mine1 }, Array.Empty<string>(), true, false,
            new[] { Mine1 }, null, true);
        Assert.Equal(new[] { Users }, plan.Blacklist);
        Assert.Equal(new[] { Mine1 }, plan.Unhidden);
    }

    [Fact]
    public void DuplicateExeEntries_AreAllRemoved_OtherPathsKept()
    {
        var plan = HidHideCleanup.Plan(Array.Empty<string>(), new[] { Exe, OtherApp, Exe.ToLowerInvariant() }, false, false,
            Array.Empty<string>(), Exe, false);
        Assert.Equal(new[] { OtherApp }, plan.Whitelist);
        Assert.True(plan.WhitelistChanged);
    }

    [Fact]
    public void ExeNotOnTheAllowList_NoChange()
    {
        var plan = HidHideCleanup.Plan(Array.Empty<string>(), new[] { OtherApp }, false, false, Array.Empty<string>(), Exe, false);
        Assert.False(plan.WhitelistChanged);
        Assert.Equal(new[] { OtherApp }, plan.Whitelist);
    }

    [Theory]
    [InlineData(@"C:\Program Files\DS4 Handler\DS4Handler.exe")] // DOS path never matches the NT paths HidHide stores
    [InlineData(@"\Device\HarddiskVolume4\Program Files\DS4 Handler\DS4Handler.exe")] // other volume
    [InlineData(@"\Device\HarddiskVolume3\Program Files\DS4 Handler\DS4Handler.exe ")] // trailing space is a different path
    public void DifferentPathSpelling_IsNotRemoved(string exe)
    {
        var plan = HidHideCleanup.Plan(Array.Empty<string>(), new[] { Exe }, false, false, Array.Empty<string>(), exe, false);
        Assert.False(plan.WhitelistChanged);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankExePath_IsIgnored(string exe)
    {
        var plan = HidHideCleanup.Plan(Array.Empty<string>(), new[] { "   ", OtherApp }, false, false, Array.Empty<string>(), exe, false);
        Assert.False(plan.WhitelistChanged);
    }

    [Fact]
    public void EmptyStringsOnTheHideList_CountAsHidden_SoTheSwitchStaysOn()
    {
        // Decode never returns empty entries; documents what Plan does if a caller passes one.
        var plan = HidHideCleanup.Plan(new[] { Mine1, "" }, Array.Empty<string>(), true, false, new[] { Mine1 }, null, true);
        Assert.Equal(new[] { "" }, plan.Blacklist);
        Assert.False(plan.Deactivate);
    }

    [Fact]
    public void Result_EncodesToWhatTheDriverWouldStore()
    {
        // What CleanUp writes back: decode(encode(plan)) equals the plan, and our ids are not in it.
        var current = MultiString.Encode(new[] { Users, Mine1, Mine2 });
        var plan = HidHideCleanup.Plan(MultiString.Decode(current), Array.Empty<string>(), true, false, new[] { Mine2.ToLowerInvariant(), Mine1 }, null, true);
        var written = MultiString.Decode(MultiString.Encode(plan.Blacklist));
        Assert.Equal(new[] { Users }, written);
    }

    [Fact]
    public void RemovingTheOnlyEntry_EncodesAsAnEmptyMultiString()
    {
        var plan = HidHideCleanup.Plan(new[] { Mine1 }, new[] { Exe }, true, false, new[] { Mine1 }, Exe, true);
        Assert.Equal(new byte[] { 0, 0 }, MultiString.Encode(plan.Blacklist));
        Assert.Equal(new byte[] { 0, 0 }, MultiString.Encode(plan.Whitelist));
        Assert.Empty(MultiString.Decode(MultiString.Encode(plan.Blacklist)));
        Assert.True(plan.Deactivate);
    }

    [Fact]
    public void LazyInputs_AreEnumeratedOnce()
    {
        int blackReads = 0, whiteReads = 0, ownedReads = 0;
        IEnumerable<string> Black() { blackReads++; yield return Mine1; }
        IEnumerable<string> White() { whiteReads++; yield return Exe; }
        IEnumerable<string> Owned() { ownedReads++; yield return Mine1; }
        var plan = HidHideCleanup.Plan(Black(), White(), true, false, Owned(), Exe, true);
        Assert.Equal((1, 1, 1), (blackReads, whiteReads, ownedReads));
        Assert.True(plan.Deactivate);
    }
}

public class MultiStringRemoveTests
{
    [Fact]
    public void RemoveAll_IsCaseInsensitive_AndRemovesEveryCopy()
    {
        var list = new List<string> { "A", "b", "a", "C" };
        Assert.True(MultiString.RemoveAll(list, "A"));
        Assert.Equal(new[] { "b", "C" }, list);
        Assert.False(MultiString.RemoveAll(list, "A"));
    }

    [Fact]
    public void RemoveAll_UsesOrdinalNotCultureCasing()
    {
        // Turkish dotted/dotless i must not match "I" through culture rules.
        var list = new List<string> { "HID\\İ", "HID\\ı" };
        Assert.False(MultiString.RemoveAll(list, "HID\\I"));
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void AddThenRemove_RoundTripsThroughTheDriverFormat()
    {
        var list = MultiString.Decode(MultiString.Encode(new[] { "X", "Y" }));
        Assert.True(MultiString.AddIfMissing(list, "Z"));
        Assert.False(MultiString.AddIfMissing(list, "z"));
        Assert.True(MultiString.RemoveAll(list, "x"));
        Assert.Equal(new[] { "Y", "Z" }, MultiString.Decode(MultiString.Encode(list)));
    }

    [Fact]
    public void Encode_SkipsBlankEntries_SoTheListIsNotCutShort()
    {
        var bytes = MultiString.Encode(new[] { "A", "", "  ", "B" });
        Assert.Equal(new[] { "A", "B" }, MultiString.Decode(bytes));
    }
}
