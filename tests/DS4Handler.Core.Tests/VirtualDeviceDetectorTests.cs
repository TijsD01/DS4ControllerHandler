using DS4Handler.Core.Input;

namespace DS4Handler.Core.Tests;

public class VirtualDeviceDetectorTests
{
    [Theory]
    [InlineData(@"Nefarius\ViGEmBus\Gen1")]
    [InlineData(@"nefarius\vigembus\gen1")]
    [InlineData(@"ROOT\ViGEmBus")]
    public void ViGEm_bus_ids_are_virtual(string id) => Assert.True(VirtualDeviceDetector.IsVirtualBusHardwareId(id));

    [Theory]
    [InlineData(@"USB\VID_054C&PID_09CC&REV_0100")]
    [InlineData(@"BTHENUM\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&09cc")]
    [InlineData(@"USB\ROOT_HUB30")]
    [InlineData("")]
    [InlineData(null)]
    public void Real_device_ids_are_not_virtual(string? id) => Assert.False(VirtualDeviceDetector.IsVirtualBusHardwareId(id));

    [Fact]
    public void Any_parent_on_the_bus_marks_the_device_virtual()
    {
        Assert.True(VirtualDeviceDetector.AnyVirtualBus(new[] { @"USB\VID_054C&PID_05C4", @"Nefarius\ViGEmBus\Gen1" }));
        Assert.False(VirtualDeviceDetector.AnyVirtualBus(new[] { @"USB\VID_054C&PID_05C4", @"USB\ROOT_HUB30" }));
        Assert.False(VirtualDeviceDetector.AnyVirtualBus(Array.Empty<string>()));
    }
}
