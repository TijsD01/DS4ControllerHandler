using System.Security.Cryptography;
using System.Text;
using DS4Handler.App.Infrastructure;
using DS4Handler.Core.Input;
using HidSharp;

namespace DS4Handler.App.Engine;

/// <summary>
/// Works out a stable id for a physical controller, so the same pad gets the same profile on USB
/// and on Bluetooth. The id is "ds4-" or "dualsense-" followed by the 12 lowercase hex digits of the
/// controller's Bluetooth MAC address (the same format older versions used for Bluetooth pads).
/// </summary>
internal static class ControllerIdentity
{
    public readonly record struct Result(string Id, string? Mac, bool IsStable);

    public static Result Read(HidDevice device, HidStream stream, ControllerModel model, ConnectionType connection)
    {
        string prefix = model == ControllerModel.DualShock4 ? "ds4" : "dualsense";
        string? mac = null;

        if (model == ControllerModel.DualSense)
        {
            // Feature 0x09 (pairing info, 20 bytes) carries the MAC at bytes 1..6, little endian; works on USB and BT.
            mac = MacFromFeature(device, stream, 0x09, 20) ?? MacFromSerial(device);
        }
        else if (connection == ConnectionType.Usb)
        {
            // DS4 on USB: feature 0x12 (16 bytes), MAC at bytes 1..6, little endian. The HID serial is empty.
            mac = MacFromFeature(device, stream, 0x12, 16) ?? MacFromSerial(device);
        }
        else
        {
            // DS4 on Bluetooth: Windows reports the MAC as the HID serial number.
            mac = MacFromSerial(device) ?? MacFromFeature(device, stream, 0x12, 16);
        }

        if (mac != null) return new Result($"{prefix}-{mac}", FormatMac(mac), true);

        // Last resort: tied to the USB port / pairing, so the pad may get a different profile elsewhere.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(device.DevicePath.ToLowerInvariant()));
        Log.Warn($"Geen MAC-adres gevonden voor {device.DevicePath}; profiel hangt aan deze poort.");
        return new Result($"{prefix}-path-{Convert.ToHexString(hash, 0, 8).ToLowerInvariant()}", null, false);
    }

    /// <summary>"a4538512abcd" → "A4:53:85:12:AB:CD".</summary>
    public static string FormatMac(string hex12)
    {
        var sb = new StringBuilder(17);
        for (int i = 0; i < 12; i += 2)
        {
            if (i > 0) sb.Append(':');
            sb.Append(char.ToUpperInvariant(hex12[i])).Append(char.ToUpperInvariant(hex12[i + 1]));
        }
        return sb.ToString();
    }

    private static string? MacFromFeature(HidDevice device, HidStream stream, byte reportId, int exactLength)
    {
        int max = 0;
        try { max = device.GetMaxFeatureReportLength(); } catch { }

        // Windows can be picky about the buffer size: try the report's own size first, then the device maximum.
        if (TryFeature(stream, reportId, exactLength) is { } mac) return mac;
        if (max > exactLength && TryFeature(stream, reportId, max) is { } mac2) return mac2;
        return null;
    }

    private static string? TryFeature(HidStream stream, byte reportId, int length)
    {
        try
        {
            var buffer = new byte[length];
            buffer[0] = reportId;
            stream.GetFeature(buffer);
            return buffer[0] == reportId ? HexFromBytesReversed(buffer.AsSpan(1, 6)) : null;
        }
        catch (Exception ex)
        {
            Log.Info($"Feature 0x{reportId:X2} ({length} bytes) lezen lukte niet: {ex.Message}");
            return null;
        }
    }

    private static string? MacFromSerial(HidDevice device)
    {
        string? serial = null;
        try { serial = device.GetSerialNumber(); } catch { }
        if (string.IsNullOrWhiteSpace(serial)) return null;

        var sb = new StringBuilder(12);
        foreach (char c in serial)
        {
            if (char.IsAsciiHexDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c is not (':' or '-' or ' ')) return null; // not a MAC
        }
        return IsUsableMac(sb.ToString()) ? sb.ToString() : null;
    }

    private static string? HexFromBytesReversed(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(12);
        for (int i = bytes.Length - 1; i >= 0; i--) sb.Append(bytes[i].ToString("x2"));
        var hex = sb.ToString();
        return IsUsableMac(hex) ? hex : null;
    }

    private static bool IsUsableMac(string hex) =>
        hex.Length == 12 && hex != "000000000000" && hex != "ffffffffffff";
}
