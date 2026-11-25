using System.Buffers.Binary;
using DS4Handler.Core.Input;
using DS4Handler.Core.Output;

namespace DS4Handler.App.Engine;

/// <summary>
/// Builds the HID output reports that drive rumble (and the DS4 light bar) into a reusable buffer.
/// Layouts follow the Linux hid-sony / hid-playstation drivers and DS4Windows.
/// </summary>
internal static class OutputReports
{
    /// <summary>Light bar colour (0xRRGGBB) a DS4 gets while this app handles it, unless the user picks another (matches the app accent).</summary>
    public const int LightBarDefault = 0x0030FF;

    /// <summary>"#RRGGBB" from the settings as 0xRRGGBB; null or garbage gives the default.</summary>
    public static int ParseLightBar(string? hex) =>
        hex is ['#', .. var digits] && digits.Length == 6
        && int.TryParse(digits, System.Globalization.NumberStyles.AllowHexSpecifier, null, out int rgb)
            ? rgb
            : LightBarDefault;

    /// <summary>
    /// The light bar to show for a colour a game sent to the virtual DS4. A game that only sets rumble
    /// still sends a full report with the colour bytes left at zero, so black means "no colour set" and
    /// keeps <paramref name="chosen"/>; otherwise the first rumble would switch the light bar off.
    /// </summary>
    public static int LightBarFromGame(byte red, byte green, byte blue, int chosen) =>
        (red | green | blue) == 0 ? chosen : (red << 16) | (green << 8) | blue;

    private static readonly TimeSpan StartupWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StartupResend = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan KeepAliveResend = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long the output writer waits before sending the same report again. A pad can drop reports
    /// (right after connecting, or later on a Bluetooth hiccup or a pad that resets its light bar), and
    /// nothing would send them again because the values did not change: so resend quickly just after
    /// connecting, and once a second after that.
    /// </summary>
    public static TimeSpan ResendInterval(TimeSpan sinceStart) =>
        sinceStart < StartupWindow ? StartupResend : KeepAliveResend;

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
    /// <param name="btPollIntervalMs">DS4 over Bluetooth only: report interval the pad should send at (1..16 ms).</param>
    public static void Write(Span<byte> buffer, ControllerModel model, ConnectionType connection, byte small, byte large,
        byte red, byte green, byte blue, byte sequence, int btPollIntervalMs = 1)
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
                buffer[1] = Ds4BluetoothOutput.ControlByte(btPollIntervalMs); // HID + CRC, poll interval
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
