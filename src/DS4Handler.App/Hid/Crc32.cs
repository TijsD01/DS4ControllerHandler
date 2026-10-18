namespace DS4Handler.App.Hid;

/// <summary>Standard CRC-32 (IEEE 802.3, reflected, poly 0xEDB88320) used by Sony Bluetooth output reports.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data, uint seed = 0xFFFFFFFFu)
    {
        uint crc = seed;
        foreach (byte b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    /// <summary>
    /// CRC of a Bluetooth output report as the controller expects it: the HID transaction header
    /// byte 0xA2 (DATA | OUTPUT) followed by the report bytes.
    /// </summary>
    public static uint ForBluetoothOutput(ReadOnlySpan<byte> report)
    {
        uint crc = Compute(stackalloc byte[] { 0xA2 });
        return ~Compute(report, crc);
    }
}
