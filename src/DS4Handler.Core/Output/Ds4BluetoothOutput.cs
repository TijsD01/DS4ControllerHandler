namespace DS4Handler.Core.Output;

/// <summary>Pieces of the DualShock 4 Bluetooth output report (0x11) that do not need the HID stack.</summary>
public static class Ds4BluetoothOutput
{
    /// <summary>
    /// Byte 1 of report 0x11: 0xC0 (HID + CRC flags) with the report interval in milliseconds in the low
    /// 6 bits. 1 ms is written as 0 (both mean 1 ms, and 0 is what the app always sent); the interval is
    /// clamped to 1..16.
    /// </summary>
    public static byte ControlByte(int pollIntervalMs)
    {
        int interval = Math.Clamp(pollIntervalMs, 1, 16);
        return (byte)(0xC0 | (interval == 1 ? 0 : interval));
    }
}
