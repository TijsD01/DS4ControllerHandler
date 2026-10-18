using System.Buffers.Binary;
using DS4Handler.Core.Input;

namespace DS4Handler.App.Hid;

/// <summary>
/// Builds the HID output reports that drive rumble (and the DS4 light bar).
/// Layouts follow the Linux hid-sony / hid-playstation drivers and DS4Windows.
/// </summary>
internal static class OutputReports
{
    /// <summary>Light bar colour we give a DS4 that is handled by this app.</summary>
    public static readonly (byte R, byte G, byte B) LightBar = (0x00, 0x30, 0xFF);

    /// <param name="small">Right / high-frequency motor (Xbox "small motor").</param>
    /// <param name="large">Left / low-frequency motor (Xbox "large motor").</param>
    /// <param name="sequence">Rolling counter, only used by DualSense over Bluetooth.</param>
    public static byte[] Build(ControllerModel model, ConnectionType connection, byte small, byte large, byte sequence, int maxOutputLength)
    {
        byte[] report = (model, connection) switch
        {
            (ControllerModel.DualShock4, ConnectionType.Usb) => Ds4Usb(small, large),
            (ControllerModel.DualShock4, ConnectionType.Bluetooth) => Ds4Bluetooth(small, large),
            (ControllerModel.DualSense, ConnectionType.Usb) => DualSenseUsb(small, large),
            _ => DualSenseBluetooth(small, large, sequence),
        };

        // Windows wants output buffers of exactly the device's output report length.
        if (maxOutputLength > report.Length)
        {
            var padded = new byte[maxOutputLength];
            report.CopyTo(padded, 0);
            report = padded;
        }
        return report;
    }

    private static byte[] Ds4Usb(byte small, byte large)
    {
        var b = new byte[32];
        b[0] = 0x05;
        b[1] = 0xFF;          // enable rumble + light bar + flash
        b[4] = small;
        b[5] = large;
        b[6] = LightBar.R;
        b[7] = LightBar.G;
        b[8] = LightBar.B;
        return b;
    }

    // Not verified on hardware: report 0x11, 78 bytes, CRC-32 over 0xA2 + bytes 0..73 stored at 74..77.
    private static byte[] Ds4Bluetooth(byte small, byte large)
    {
        var b = new byte[78];
        b[0] = 0x11;
        b[1] = 0xC0;          // HID + CRC, poll interval 0
        b[3] = 0x07;          // rumble + light bar + flash
        b[6] = small;
        b[7] = large;
        b[8] = LightBar.R;
        b[9] = LightBar.G;
        b[10] = LightBar.B;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(74), Crc32.ForBluetoothOutput(b.AsSpan(0, 74)));
        return b;
    }

    private static byte[] DualSenseUsb(byte small, byte large)
    {
        var b = new byte[48];
        b[0] = 0x02;
        b[1] = 0x03;          // valid_flag0: compatible vibration + haptics select
        b[3] = small;         // motor right
        b[4] = large;         // motor left
        return b;
    }

    // Not verified on hardware: report 0x31, seq/tag header, same common block, CRC at 74..77.
    private static byte[] DualSenseBluetooth(byte small, byte large, byte sequence)
    {
        var b = new byte[78];
        b[0] = 0x31;
        b[1] = (byte)((sequence & 0x0F) << 4);
        b[2] = 0x10;          // DS_OUTPUT_TAG
        b[3] = 0x03;
        b[5] = small;
        b[6] = large;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(74), Crc32.ForBluetoothOutput(b.AsSpan(0, 74)));
        return b;
    }
}
