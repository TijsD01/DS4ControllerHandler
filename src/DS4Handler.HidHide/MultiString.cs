using System.Text;

namespace DS4Handler.HidHide;

/// <summary>REG_MULTI_SZ style UTF-16LE buffers and HID path conversion (pure, platform independent).</summary>
public static class MultiString
{
    /// <summary>Encodes strings as UTF-16LE, each null terminated, plus a final extra null terminator.</summary>
    public static byte[] Encode(IEnumerable<string> items)
    {
        var sb = new StringBuilder();
        foreach (var s in items)
        {
            if (string.IsNullOrEmpty(s)) continue; // an empty entry would terminate the list
            sb.Append(s).Append('\0');
        }
        sb.Append('\0');
        return Encoding.Unicode.GetBytes(sb.ToString());
    }

    /// <summary>Decodes a multi-string buffer; stops at the terminating empty string. Tolerates a missing terminator.</summary>
    public static List<string> Decode(ReadOnlySpan<byte> data)
    {
        var result = new List<string>();
        int chars = data.Length / 2;
        var sb = new StringBuilder();
        for (int i = 0; i < chars; i++)
        {
            char c = (char)(data[2 * i] | (data[2 * i + 1] << 8));
            if (c == '\0')
            {
                if (sb.Length == 0) break;
                result.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }

    /// <summary>
    /// Converts a HID device interface path to a device instance id, e.g.
    /// <c>\\?\hid#vid_054c&amp;pid_09cc&amp;mi_03#7&amp;1234&amp;0&amp;0000#{guid}</c> to
    /// <c>HID\VID_054C&amp;PID_09CC&amp;MI_03\7&amp;1234&amp;0&amp;0000</c>. Returns null for anything else.
    /// </summary>
    public static string? InstanceIdFromHidPath(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return null;
        var p = devicePath.Trim();
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal))
            p = p[4..];
        var parts = p.Split('#');
        if (parts.Length < 3) return null;
        if (!parts[0].Equals("hid", StringComparison.OrdinalIgnoreCase)) return null;
        if (parts[1].Length == 0 || parts[2].Length == 0) return null;
        return ("HID\\" + parts[1] + "\\" + parts[2]).ToUpperInvariant();
    }

    /// <summary>Adds <paramref name="item"/> if absent (case-insensitive). Returns true if the list changed.</summary>
    internal static bool AddIfMissing(List<string> list, string item)
    {
        if (list.Any(x => x.Equals(item, StringComparison.OrdinalIgnoreCase))) return false;
        list.Add(item);
        return true;
    }

    /// <summary>Removes all case-insensitive matches. Returns true if the list changed.</summary>
    internal static bool RemoveAll(List<string> list, string item)
        => list.RemoveAll(x => x.Equals(item, StringComparison.OrdinalIgnoreCase)) > 0;
}
