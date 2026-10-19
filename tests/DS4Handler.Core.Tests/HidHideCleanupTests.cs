using DS4Handler.HidHide;

namespace DS4Handler.Core.Tests;

public class HidHideCleanupTests
{
    private const string Exe = @"\Device\HarddiskVolume3\Program Files\DS4 Handler\DS4Handler.exe";
    private const string OtherApp = @"\Device\HarddiskVolume3\Games\Tool.exe";

    [Fact]
    public void RemovesOnlyOwnedIds_AndOwnExe()
    {
        var plan = HidHideCleanup.Plan(
            blacklist: new[] { @"HID\MINE", @"HID\USERS" }, whitelist: new[] { OtherApp, Exe.ToUpperInvariant() },
            active: true, whitelistInverted: false, ownedIds: new[] { @"hid\mine", @"HID\GONE" }, exeNtPath: Exe, activatedByApp: true);

        Assert.Equal(new[] { @"HID\USERS" }, plan.Blacklist);
        Assert.Equal(new[] { @"hid\mine" }, plan.Unhidden);
        Assert.Equal(new[] { @"HID\GONE" }, plan.AlreadyVisible);
        Assert.Equal(new[] { OtherApp }, plan.Whitelist);
        Assert.True(plan.BlacklistChanged);
        Assert.True(plan.WhitelistChanged);
        Assert.Equal(1, plan.OthersStillHidden);
        Assert.False(plan.Deactivate); // another device still relies on the switch
    }

    [Fact]
    public void Deactivates_WhenAppTurnedItOnAndNothingStaysHidden()
    {
        var plan = HidHideCleanup.Plan(new[] { @"HID\MINE" }, new[] { Exe }, active: true, whitelistInverted: false,
            new[] { @"HID\MINE" }, Exe, activatedByApp: true);
        Assert.Empty(plan.Blacklist);
        Assert.True(plan.Deactivate);
    }

    [Theory]
    [InlineData(false, true)]  // the user turned it on: leave it
    [InlineData(true, false)]  // already off
    public void LeavesSwitchAlone(bool activatedByApp, bool active)
    {
        var plan = HidHideCleanup.Plan(new[] { @"HID\MINE" }, Array.Empty<string>(), active, false, new[] { @"HID\MINE" }, Exe, activatedByApp);
        Assert.False(plan.Deactivate);
    }

    [Fact]
    public void InvertedWhitelist_IsLeftAlone()
    {
        var plan = HidHideCleanup.Plan(Array.Empty<string>(), new[] { Exe }, true, whitelistInverted: true, Array.Empty<string>(), Exe, false);
        Assert.False(plan.WhitelistChanged);
        Assert.Equal(new[] { Exe }, plan.Whitelist);
    }

    [Fact]
    public void NothingOwnedAndNoExe_ChangesNothing()
    {
        var plan = HidHideCleanup.Plan(new[] { @"HID\USERS" }, new[] { OtherApp }, true, false, new[] { "", " " }, null, false);
        Assert.False(plan.BlacklistChanged);
        Assert.False(plan.WhitelistChanged);
        Assert.False(plan.Deactivate);
        Assert.Empty(plan.Unhidden);
        Assert.Empty(plan.AlreadyVisible);
    }

    [Fact]
    public void DuplicateOwnedIds_CountOnce()
    {
        var plan = HidHideCleanup.Plan(new[] { @"HID\A" }, Array.Empty<string>(), false, false, new[] { @"HID\A", @"hid\a" }, null, false);
        Assert.Single(plan.Unhidden);
        Assert.Empty(plan.AlreadyVisible);
    }
}
