using System.Buffers.Binary;
using DS4Handler.App.Engine;
using DS4Handler.Core.Input;

namespace DS4Handler.Core.Tests.AppLinked;

public class OutputReportsTests
{
    // CRC the pad checks: seeded with the 0xA2 header byte, over bytes 0..73.
    private static uint BluetoothCrc(byte[] buf) =>
        ~Crc32.Compute(buf.AsSpan(0, 74), Crc32.Compute(new byte[] { 0xA2 }));

    [Fact]
    public void Crc32MatchesStandardCheckValue() =>
        Assert.Equal(0xCBF43926u, ~Crc32.Compute("123456789"u8));

    [Fact]
    public void Ds4BluetoothReportLayoutAndCrc()
    {
        var buf = new byte[78];
        OutputReports.Write(buf, ControllerModel.DualShock4, ConnectionType.Bluetooth,
            small: 0x11, large: 0x22, red: 0x00, green: 0x30, blue: 0xFF, sequence: 0, btPollIntervalMs: 4);

        Assert.Equal(0x11, buf[0]);
        Assert.Equal(0xC4, buf[1]);
        Assert.Equal(0x07, buf[3]);
        Assert.Equal(0x11, buf[6]);
        Assert.Equal(0x22, buf[7]);
        Assert.Equal(new byte[] { 0x00, 0x30, 0xFF }, buf[8..11]);
        Assert.Equal(BluetoothCrc(buf), BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(74)));
    }

    [Fact]
    public void DualSenseBluetoothReportLayoutAndCrc()
    {
        var buf = new byte[78];
        OutputReports.Write(buf, ControllerModel.DualSense, ConnectionType.Bluetooth,
            small: 0x11, large: 0x22, red: 0x00, green: 0x30, blue: 0xFF, sequence: 5, btPollIntervalMs: 4);

        Assert.Equal(0x31, buf[0]);
        Assert.Equal(0x50, buf[1]);
        Assert.Equal(0x10, buf[2]);
        Assert.Equal(0x11, buf[5]);
        Assert.Equal(0x22, buf[6]);
        Assert.Equal(0x04, buf[41]);
        Assert.Equal(BluetoothCrc(buf), BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(74)));
    }
}
