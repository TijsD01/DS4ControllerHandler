using System.Buffers.Binary;
using DS4Handler.Core.Input;

namespace DS4Handler.App.Engine;

/// <summary>
/// Builds the HID output reports that drive rumble (and the DS4 light bar) into a reusable buffer.
/// Layouts follow the Linux hid-sony / hid-playstation drivers and DS4Windows.
/// </summary>
internal static class OutputReports
{
    /// <summary>Light bar colour a DS4 gets while this app handles it (matches the app accent).</summary>
    public static readonly (byte R, byte G, byte B) LightBar = (0x00, 0x30, 0xFF);

    /// <summary>Bytes the report itself needs; the write buffer is at least this long.</summary>
    public static int RequiredLength(ControllerModel model, ConnectionType connection) => (model, connection) switch
    {
        (ControllerModel.DualShock4, ConnectionType.Usb) => 32,
        (ControllerModel.DualSense, ConnectionType.Usb) => 48,
        _ => 78,
    };

    /// <summary>
    /// Writes the report into <paramref name="buffer"/> (cleared first; Windows wants exactly the
    /// device's output report length, so the buffer may be longer than the report).
    /// </summary>
    /// <param name="small">Right / high-frequency motor (Xbox "small motor").</param>
    /// <param name="large">Left / low-frequency motor (Xbox "large motor").</param>
    /// <param name="sequence">Rolling counter, only used by DualSense over Bluetooth.</param>
    /// <param name="red">Light bar colour (DS4 only; a game talking to a virtual DS4 may change it).</param>
    public static void Write(Span<byte> buffer, ControllerModel model, ConnectionType connection, byte small, byte large,
        byte red, byte green, byte blue, byte sequence)
    {
        buffer.Clear();
        switch (model, connection)
        {
            case (ControllerModel.DualShock4, ConnectionType.Usb):
                buffer[0] = 0x05;
                buffer[1] = 0xFF;          // enable rumble + light bar + flash
                buffer[4] = small;
                buffer[5] = large;
                buffer[6] = red;
                buffer[7] = green;
                buffer[8] = blue;
                break;

            case (ControllerModel.DualShock4, ConnectionType.Bluetooth):
                // Report 0x11, 78 bytes, CRC-32 over 0xA2 + bytes 0..73 stored at 74..77.
                buffer[0] = 0x11;
                buffer[1] = 0xC0;          // HID + CRC, poll interval 0
                buffer[3] = 0x07;          // rumble + light bar + flash
                buffer[6] = small;
                buffer[7] = large;
                buffer[8] = red;
                buffer[9] = green;
                buffer[10] = blue;
                BinaryPrimitives.WriteUInt32LittleEndian(buffer[74..], Crc32.ForBluetoothOutput(buffer[..74]));
                break;

            case (ControllerModel.DualSense, ConnectionType.Usb):
                // Report 0x02 + common block. valid_flag0: compatible vibration + haptics select;
                // valid_flag2 bit 2: COMPATIBLE_VIBRATION2 (needed on firmware 0x0224 and newer).
                buffer[0] = 0x02;
                buffer[1] = 0x03;
                buffer[3] = small;         // motor right
                buffer[4] = large;         // motor left
                buffer[1 + 38] = 0x04;
                break;

            default:
                // DualSense Bluetooth: report 0x31, seq/tag header, same common block, CRC at 74..77.
                buffer[0] = 0x31;
                buffer[1] = (byte)((sequence & 0x0F) << 4);
                buffer[2] = 0x10;          // DS_OUTPUT_TAG
                buffer[3] = 0x03;
                buffer[5] = small;
                buffer[6] = large;
                buffer[3 + 38] = 0x04;
                BinaryPrimitives.WriteUInt32LittleEndian(buffer[74..], Crc32.ForBluetoothOutput(buffer[..74]));
                break;
        }
    }
}
