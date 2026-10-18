using DS4Handler.Core.Input;

namespace DS4Handler.Core.Tests;

public class ReportParserTests
{
    // Builds a report: id byte, then `offset - 1` filler bytes, then the payload laid out relative to the offset.
    private static byte[] Ds4Report(byte id, int offset, byte lx, byte ly, byte rx, byte ry, byte face, byte shoulders, byte ps, byte l2, byte r2, int length = 64)
    {
        var r = new byte[length];
        r[0] = id;
        var p = new byte[] { lx, ly, rx, ry, face, shoulders, ps, l2, r2 };
        p.CopyTo(r, offset);
        return r;
    }

    private static byte[] DualSenseReport(byte id, int offset, byte lx, byte ly, byte rx, byte ry, byte l2, byte r2, byte face, byte shoulders, byte ps, int length = 64)
    {
        var r = new byte[length];
        r[0] = id;
        var p = new byte[] { lx, ly, rx, ry, l2, r2, 0x5A /* counter */, face, shoulders, ps };
        p.CopyTo(r, offset);
        return r;
    }

    [Fact]
    public void Ds4Usb_ParsesSticksTriggersAndButtons()
    {
        var report = Ds4Report(0x01, 1, 10, 20, 30, 40, 0x28 /* Cross + dpad 8 */, 0x01 /* L1 */, 0x01 /* PS */, 77, 200);
        Assert.True(ReportParser.TryParse(ControllerModel.DualShock4, report, out var s, out var conn));
        Assert.Equal(ConnectionType.Usb, conn);
        Assert.Equal(new RawControllerState(10, 20, 30, 40, 77, 200, Buttons.Cross | Buttons.L1 | Buttons.PS, s.BatteryPercent), s);
    }

    [Fact]
    public void Ds4Bluetooth_UsesOffsetThree()
    {
        var report = Ds4Report(0x11, 3, 11, 22, 33, 44, 0x08, 0x02 /* R1 */, 0x02 /* touch */, 5, 250, length: 78);
        report[1] = 0xC0; // BT header bytes must not be read as sticks
        report[2] = 0x00;
        Assert.True(ReportParser.TryParse(ControllerModel.DualShock4, report, out var s, out var conn));
        Assert.Equal(ConnectionType.Bluetooth, conn);
        Assert.Equal((byte)11, s.LeftX);
        Assert.Equal((byte)22, s.LeftY);
        Assert.Equal((byte)33, s.RightX);
        Assert.Equal((byte)44, s.RightY);
        Assert.Equal((byte)5, s.L2);
        Assert.Equal((byte)250, s.R2);
        Assert.Equal(Buttons.R1 | Buttons.TouchpadClick, s.Buttons);
    }

    [Fact]
    public void Ds4ReducedReport_WithBluetoothHint_ReportsBluetoothButKeepsUsbLayout()
    {
        var report = Ds4Report(0x01, 1, 10, 20, 30, 40, 0x28, 0, 0, 77, 200);
        Assert.True(ReportParser.TryParse(ControllerModel.DualShock4, report, out var s, out var conn, ConnectionType.Bluetooth));
        Assert.Equal(ConnectionType.Bluetooth, conn);
        Assert.Equal((byte)10, s.LeftX);
        Assert.Equal((byte)200, s.R2);
        Assert.Equal(Buttons.Cross, s.Buttons);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ConnectionType.Usb)]
    public void Ds4ReducedReport_WithoutBluetoothHint_ReportsUsb(ConnectionType? hint)
    {
        var report = Ds4Report(0x01, 1, 10, 20, 30, 40, 0x08, 0, 0, 0, 0);
        Assert.True(ReportParser.TryParse(ControllerModel.DualShock4, report, out _, out var conn, hint));
        Assert.Equal(ConnectionType.Usb, conn);
    }

    [Fact]
    public void Ds4FullBluetoothReport_IgnoresHint()
    {
        var report = Ds4Report(0x11, 3, 1, 2, 3, 4, 0x08, 0, 0, 0, 0, length: 78);
        Assert.True(ReportParser.TryParse(ControllerModel.DualShock4, report, out _, out var conn, ConnectionType.Usb));
        Assert.Equal(ConnectionType.Bluetooth, conn);
    }

    [Fact]
    public void Ds4Usb_ReadsBattery()
    {
        var report = Ds4Report(0x01, 1, 128, 128, 128, 128, 0x08, 0, 0, 0, 0);
        report[30] = 0x05; // level 5 of 10, no cable
        Assert.True(ReportParser.TryParse(ControllerModel.DualShock4, report, out var s, out _));
        Assert.Equal((byte)50, s.BatteryPercent);
    }

    [Fact]
    public void DualSenseUsb_ParsesSticksTriggersAndButtons()
    {
        var report = DualSenseReport(0x01, 1, 1, 2, 3, 4, 60, 190, 0x88 /* Triangle + released */, 0x30 /* Share+Options */, 0x05 /* PS + Mute */);
        Assert.True(ReportParser.TryParse(ControllerModel.DualSense, report, out var s, out var conn));
        Assert.Equal(ConnectionType.Usb, conn);
        Assert.Equal((byte)1, s.LeftX);
        Assert.Equal((byte)2, s.LeftY);
        Assert.Equal((byte)3, s.RightX);
        Assert.Equal((byte)4, s.RightY);
        Assert.Equal((byte)60, s.L2);
        Assert.Equal((byte)190, s.R2);
        Assert.Equal(Buttons.Triangle | Buttons.Share | Buttons.Options | Buttons.PS | Buttons.Mute, s.Buttons);
    }

    [Fact]
    public void DualSenseBluetooth_UsesOffsetTwo()
    {
        var report = DualSenseReport(0x31, 2, 9, 8, 7, 6, 5, 255, 0x18 /* Square */, 0xC0 /* L3+R3 */, 0x00, length: 78);
        report[1] = 0x99; // sequence byte
        Assert.True(ReportParser.TryParse(ControllerModel.DualSense, report, out var s, out var conn));
        Assert.Equal(ConnectionType.Bluetooth, conn);
        Assert.Equal(new RawControllerState(9, 8, 7, 6, 5, 255, Buttons.Square | Buttons.L3 | Buttons.R3, s.BatteryPercent), s);
    }

    [Theory]
    [InlineData(0, Buttons.DpadUp)]
    [InlineData(1, Buttons.DpadUp | Buttons.DpadRight)]
    [InlineData(2, Buttons.DpadRight)]
    [InlineData(3, Buttons.DpadRight | Buttons.DpadDown)]
    [InlineData(4, Buttons.DpadDown)]
    [InlineData(5, Buttons.DpadDown | Buttons.DpadLeft)]
    [InlineData(6, Buttons.DpadLeft)]
    [InlineData(7, Buttons.DpadLeft | Buttons.DpadUp)]
    [InlineData(8, Buttons.None)]
    [InlineData(15, Buttons.None)]
    public void DpadHat_DecodesAllDirections(int hat, Buttons expected)
    {
        var ds4 = Ds4Report(0x01, 1, 128, 128, 128, 128, (byte)hat, 0, 0, 0, 0);
        Assert.True(ReportParser.TryParse(ControllerModel.DualShock4, ds4, out var s4, out _));
        Assert.Equal(expected, s4.Buttons);

        var ds = DualSenseReport(0x01, 1, 128, 128, 128, 128, 0, 0, (byte)hat, 0, 0);
        Assert.True(ReportParser.TryParse(ControllerModel.DualSense, ds, out var s5, out _));
        Assert.Equal(expected, s5.Buttons);
    }

    [Theory]
    [InlineData(0x18, 0x00, Buttons.Square)]
    [InlineData(0x28, 0x00, Buttons.Cross)]
    [InlineData(0x48, 0x00, Buttons.Circle)]
    [InlineData(0x88, 0x00, Buttons.Triangle)]
    [InlineData(0x08, 0x01, Buttons.L1)]
    [InlineData(0x08, 0x02, Buttons.R1)]
    [InlineData(0x08, 0x04, Buttons.L2Digital)]
    [InlineData(0x08, 0x08, Buttons.R2Digital)]
    [InlineData(0x08, 0x10, Buttons.Share)]
    [InlineData(0x08, 0x20, Buttons.Options)]
    [InlineData(0x08, 0x40, Buttons.L3)]
    [InlineData(0x08, 0x80, Buttons.R3)]
    [InlineData(0xF2, 0xFF, Buttons.Square | Buttons.Cross | Buttons.Circle | Buttons.Triangle | Buttons.DpadRight
        | Buttons.L1 | Buttons.R1 | Buttons.L2Digital | Buttons.R2Digital | Buttons.Share | Buttons.Options | Buttons.L3 | Buttons.R3)]
    public void FaceAndShoulderButtons_Decode(int face, int shoulders, Buttons expected)
    {
        var ds4 = Ds4Report(0x01, 1, 128, 128, 128, 128, (byte)face, (byte)shoulders, 0, 0, 0);
        Assert.True(ReportParser.TryParse(ControllerModel.DualShock4, ds4, out var s4, out _));
        Assert.Equal(expected, s4.Buttons);

        var ds = DualSenseReport(0x31, 2, 128, 128, 128, 128, 0, 0, (byte)face, (byte)shoulders, 0);
        Assert.True(ReportParser.TryParse(ControllerModel.DualSense, ds, out var s5, out _));
        Assert.Equal(expected, s5.Buttons);
    }

    [Fact]
    public void Ds4_PsCounterBitsAreIgnored()
    {
        // Upper six bits of byte 7 are a frame counter, not buttons.
        var report = Ds4Report(0x01, 1, 128, 128, 128, 128, 0x08, 0, 0xFC, 0, 0);
        Assert.True(ReportParser.TryParse(ControllerModel.DualShock4, report, out var s, out _));
        Assert.Equal(Buttons.None, s.Buttons);
    }

    [Theory]
    [InlineData(ControllerModel.DualShock4, new byte[] { })]
    [InlineData(ControllerModel.DualShock4, new byte[] { 0x01 })]
    [InlineData(ControllerModel.DualShock4, new byte[] { 0x01, 128, 128, 128, 128, 8, 0, 0, 0 })]           // 9 bytes, needs 10
    [InlineData(ControllerModel.DualShock4, new byte[] { 0x11, 0xC0, 0, 128, 128, 128, 128, 8, 0, 0, 0 })] // 11 bytes, needs 12
    [InlineData(ControllerModel.DualSense, new byte[] { 0x01, 128, 128, 128, 128, 0, 0, 0, 8, 0 })]         // 10 bytes, needs 11
    [InlineData(ControllerModel.DualSense, new byte[] { 0x31, 0, 128, 128, 128, 128, 0, 0, 0, 8, 0 })]     // 11 bytes, needs 12
    public void ShortReports_ReturnFalse(ControllerModel model, byte[] report)
    {
        Assert.False(ReportParser.TryParse(model, report, out var s, out _));
        Assert.Equal(default, s);
    }

    [Theory]
    [InlineData(ControllerModel.DualShock4, 0x31)] // DualSense BT id on a DS4
    [InlineData(ControllerModel.DualShock4, 0x05)]
    [InlineData(ControllerModel.DualSense, 0x11)]  // DS4 BT id on a DualSense
    [InlineData(ControllerModel.DualSense, 0x02)]
    public void UnknownReportIds_ReturnFalse(ControllerModel model, int id)
    {
        var report = new byte[78];
        report[0] = (byte)id;
        Assert.False(ReportParser.TryParse(model, report, out _, out _));
    }

    [Fact]
    public void MinimumLengthReports_Parse()
    {
        var ds4Usb = new byte[10]; ds4Usb[0] = 0x01; ds4Usb[9] = 99;
        Assert.True(ReportParser.TryParse(ControllerModel.DualShock4, ds4Usb, out var a, out _));
        Assert.Equal((byte)99, a.R2);
        Assert.Null(a.BatteryPercent);

        var dsBt = new byte[12]; dsBt[0] = 0x31; dsBt[7] = 123;
        Assert.True(ReportParser.TryParse(ControllerModel.DualSense, dsBt, out var b, out _));
        Assert.Equal((byte)123, b.R2);
        Assert.Null(b.BatteryPercent);
    }

    [Theory]
    [InlineData(0x05C4, ControllerModel.DualShock4)]
    [InlineData(0x09CC, ControllerModel.DualShock4)]
    [InlineData(0x0BA0, ControllerModel.DualShock4)]
    [InlineData(0x0CE6, ControllerModel.DualSense)]
    [InlineData(0x0DF2, ControllerModel.DualSense)]
    public void ProductId_MapsToModel(int pid, ControllerModel expected)
    {
        Assert.Equal(expected, ReportParser.ModelForProductId(pid));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x028E)] // Xbox 360 pad
    [InlineData(0x0268)] // DualShock 3
    public void UnknownProductId_ReturnsNull(int pid)
    {
        Assert.Null(ReportParser.ModelForProductId(pid));
    }

    [Fact]
    public void DualSenseSimpleReport_WithBluetoothHint_IsRejected()
    {
        // Over Bluetooth a DualSense sends a reduced 0x01 report with a different layout until it is switched to full mode.
        var report = DualSenseReport(0x01, 1, 1, 2, 3, 4, 60, 190, 0x08, 0, 0, length: 10 + 64);
        Assert.False(ReportParser.TryParse(ControllerModel.DualSense, report, out _, out _, ConnectionType.Bluetooth));
        Assert.True(ReportParser.TryParse(ControllerModel.DualSense, report, out _, out var conn, ConnectionType.Usb));
        Assert.Equal(ConnectionType.Usb, conn);
    }
}
