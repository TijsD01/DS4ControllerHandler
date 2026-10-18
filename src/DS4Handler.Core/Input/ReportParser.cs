namespace DS4Handler.Core.Input;

/// <summary>
/// Parses HID input reports from DualShock 4 and DualSense controllers.
/// Offsets follow the layouts DS4Windows and the Linux hid-playstation driver use.
/// </summary>
public static class ReportParser
{
    public const int SonyVendorId = 0x054C;
    public const int Ds4V1ProductId = 0x05C4;
    public const int Ds4V2ProductId = 0x09CC;
    public const int Ds4WirelessAdapterProductId = 0x0BA0;
    public const int DualSenseProductId = 0x0CE6;
    public const int DualSenseEdgeProductId = 0x0DF2;

    public static ControllerModel? ModelForProductId(int productId) => productId switch
    {
        Ds4V1ProductId or Ds4V2ProductId or Ds4WirelessAdapterProductId => ControllerModel.DualShock4,
        DualSenseProductId or DualSenseEdgeProductId => ControllerModel.DualSense,
        _ => null,
    };

    /// <summary>
    /// Parses one input report (including the report id in byte 0).
    /// Returns false for reports that carry no input (for example the reduced
    /// Bluetooth report a DS4 sends before it is switched to full mode).
    /// <paramref name="knownConnection"/> lets the caller say the device is on Bluetooth, so the reduced 0x01 report is labelled correctly.
    /// </summary>
    public static bool TryParse(ControllerModel model, ReadOnlySpan<byte> report, out RawControllerState state, out ConnectionType connection, ConnectionType? knownConnection = null)
    {
        state = default;
        connection = ConnectionType.Usb;
        if (report.Length < 2) return false;

        int offset;
        switch (model, report[0])
        {
            case (ControllerModel.DualShock4, 0x01) when report.Length >= 10:
                offset = 1;
                // A DS4 on Bluetooth sends this reduced report (USB layout) until switched to full mode.
                connection = knownConnection == ConnectionType.Bluetooth ? ConnectionType.Bluetooth : ConnectionType.Usb;
                break;
            case (ControllerModel.DualShock4, 0x11) when report.Length >= 12:
                offset = 3;
                connection = ConnectionType.Bluetooth;
                break;
            case (ControllerModel.DualSense, 0x01) when report.Length >= 11:
                offset = 1;
                connection = ConnectionType.Usb;
                break;
            case (ControllerModel.DualSense, 0x31) when report.Length >= 12:
                offset = 2;
                connection = ConnectionType.Bluetooth;
                break;
            default:
                return false;
        }

        var d = report[offset..];
        state = model == ControllerModel.DualShock4 ? ParseDs4(d) : ParseDualSense(d);
        return true;
    }

    private static RawControllerState ParseDs4(ReadOnlySpan<byte> d)
    {
        // 0 LX, 1 LY, 2 RX, 3 RY, 4 dpad+face, 5 shoulder/menu, 6 PS/touch, 7 L2, 8 R2
        var buttons = FaceAndDpad(d[4]) | Shoulders(d[5]);
        if ((d[6] & 0x01) != 0) buttons |= Buttons.PS;
        if ((d[6] & 0x02) != 0) buttons |= Buttons.TouchpadClick;

        byte? battery = null;
        if (d.Length > 29)
        {
            // Byte 29 (relative): low nibble level 0..10 (0..11 when charging), bit 4 cable.
            int level = d[29] & 0x0F;
            bool cable = (d[29] & 0x10) != 0;
            int max = cable ? 11 : 10;
            battery = (byte)Math.Clamp(level * 100 / max, 0, 100);
        }

        return new RawControllerState(d[0], d[1], d[2], d[3], d[7], d[8], buttons, battery);
    }

    private static RawControllerState ParseDualSense(ReadOnlySpan<byte> d)
    {
        // 0 LX, 1 LY, 2 RX, 3 RY, 4 L2, 5 R2, 6 counter, 7 dpad+face, 8 shoulder/menu, 9 PS/touch/mute
        var buttons = FaceAndDpad(d[7]) | Shoulders(d[8]);
        if ((d[9] & 0x01) != 0) buttons |= Buttons.PS;
        if ((d[9] & 0x02) != 0) buttons |= Buttons.TouchpadClick;
        if ((d[9] & 0x04) != 0) buttons |= Buttons.Mute;

        byte? battery = null;
        if (d.Length > 52)
        {
            int level = d[52] & 0x0F; // 0..10
            battery = (byte)Math.Clamp(level * 10, 0, 100);
        }

        return new RawControllerState(d[0], d[1], d[2], d[3], d[4], d[5], buttons, battery);
    }

    private static Buttons FaceAndDpad(byte b)
    {
        var buttons = (b & 0x0F) switch
        {
            0 => Buttons.DpadUp,
            1 => Buttons.DpadUp | Buttons.DpadRight,
            2 => Buttons.DpadRight,
            3 => Buttons.DpadRight | Buttons.DpadDown,
            4 => Buttons.DpadDown,
            5 => Buttons.DpadDown | Buttons.DpadLeft,
            6 => Buttons.DpadLeft,
            7 => Buttons.DpadLeft | Buttons.DpadUp,
            _ => Buttons.None, // 8 = released
        };
        if ((b & 0x10) != 0) buttons |= Buttons.Square;
        if ((b & 0x20) != 0) buttons |= Buttons.Cross;
        if ((b & 0x40) != 0) buttons |= Buttons.Circle;
        if ((b & 0x80) != 0) buttons |= Buttons.Triangle;
        return buttons;
    }

    private static Buttons Shoulders(byte b)
    {
        var buttons = Buttons.None;
        if ((b & 0x01) != 0) buttons |= Buttons.L1;
        if ((b & 0x02) != 0) buttons |= Buttons.R1;
        if ((b & 0x04) != 0) buttons |= Buttons.L2Digital;
        if ((b & 0x08) != 0) buttons |= Buttons.R2Digital;
        if ((b & 0x10) != 0) buttons |= Buttons.Share;
        if ((b & 0x20) != 0) buttons |= Buttons.Options;
        if ((b & 0x40) != 0) buttons |= Buttons.L3;
        if ((b & 0x80) != 0) buttons |= Buttons.R3;
        return buttons;
    }
}
