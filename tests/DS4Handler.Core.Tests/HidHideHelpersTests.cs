using DS4Handler.HidHide;

namespace DS4Handler.Core.Tests;

public class HidHideHelpersTests
{
    [Fact]
    public void MultiString_EmptyList_RoundTrips()
    {
        var bytes = MultiString.Encode(Array.Empty<string>());
        Assert.Equal(new byte[] { 0, 0 }, bytes);
        Assert.Empty(MultiString.Decode(bytes));
    }

    [Fact]
    public void MultiString_RoundTripsUnicodeAndTerminators()
    {
        var items = new[] { @"\Device\HarddiskVolume3\Prögram Files\アプリ.exe", "HID\\VID_054C" };
        var bytes = MultiString.Encode(items);
        Assert.Equal(0, bytes[^1]); Assert.Equal(0, bytes[^2]); Assert.Equal(0, bytes[^3]); Assert.Equal(0, bytes[^4]);
        Assert.Equal(items, MultiString.Decode(bytes));
    }

    [Fact]
    public void MultiString_Decode_ToleratesMissingTerminator()
    {
        var raw = System.Text.Encoding.Unicode.GetBytes("abc");
        Assert.Equal(new[] { "abc" }, MultiString.Decode(raw));
    }

    [Fact]
    public void UsbPath_ConvertsToInstanceId()
        => Assert.Equal(@"HID\VID_054C&PID_09CC&MI_03\7&1234&0&0000",
            MultiString.InstanceIdFromHidPath(@"\\?\hid#vid_054c&pid_09cc&mi_03#7&1234&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}"));

    [Fact]
    public void BluetoothPath_ConvertsToInstanceId()
        => Assert.Equal(@"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&09CC\8&2A3B&0&0000",
            MultiString.InstanceIdFromHidPath(@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&09cc#8&2a3b&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}"));

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData(@"\\?\usb#vid_054c&pid_09cc#abc#{guid}")]
    [InlineData(@"\\?\hid#only")]
    [InlineData(@"\\?\hid##x#{g}")]
    public void GarbagePaths_ReturnNull(string path) => Assert.Null(MultiString.InstanceIdFromHidPath(path));

    [Fact]
    public void NullPath_ReturnsNull() => Assert.Null(HidHideClient.InstanceIdFromHidPath(null!));

    [Fact]
    public void AddAndRemove_AreIdempotentAndCaseInsensitive()
    {
        var list = new List<string> { @"HID\A" };
        Assert.False(MultiString.AddIfMissing(list, @"hid\a"));
        Assert.True(MultiString.AddIfMissing(list, @"HID\B"));
        Assert.False(MultiString.AddIfMissing(list, @"HID\B"));
        Assert.Equal(2, list.Count);
        Assert.True(MultiString.RemoveAll(list, @"hid\b"));
        Assert.False(MultiString.RemoveAll(list, @"HID\B"));
        Assert.Single(list);
    }

    [Fact]
    public void IoctlCodes_MatchDriver()
    {
        Assert.Equal(0x80016000u, HidHideClient.IOCTL_GET_WHITELIST);
        Assert.Equal(0x80016004u, HidHideClient.IOCTL_SET_WHITELIST);
        Assert.Equal(0x80016008u, HidHideClient.IOCTL_GET_BLACKLIST);
        Assert.Equal(0x8001600Cu, HidHideClient.IOCTL_SET_BLACKLIST);
        Assert.Equal(0x80016010u, HidHideClient.IOCTL_GET_ACTIVE);
        Assert.Equal(0x80016014u, HidHideClient.IOCTL_SET_ACTIVE);
        Assert.Equal(0x80016018u, HidHideClient.IOCTL_GET_WLINVERSE);
    }

    [Fact]
    public void NotWindows_IsInstalledFalse()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(HidHideClient.IsInstalled()); Assert.Equal(HidHideStatus.NotInstalled, HidHideClient.GetStatus()); }
    }

    [Theory]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", true)]
    [InlineData(@"c:\x\DOTNET.EXE", true)]
    [InlineData(@"C:\x\DS4Handler.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SharedHostGuard(string? path, bool expected) => Assert.Equal(expected, HidHideClient.IsSharedHostExecutable(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("relative.exe")]
    public void DosPathToNtPath_BadInput_ReturnsNull(string? p) => Assert.Null(HidHideClient.DosPathToNtPath(p));

    [Fact]
    public void Encode_SkipsBlankEntries()
        => Assert.Equal(new[] { "a" }, MultiString.Decode(MultiString.Encode(new[] { "", "  ", "a" })));
}
