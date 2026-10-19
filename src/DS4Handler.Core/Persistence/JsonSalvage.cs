using System.Text.Json;
using System.Text.RegularExpressions;

namespace DS4Handler.Core.Persistence;

/// <summary>
/// Gets single values out of a JSON file that no longer parses (truncated write, hand edit gone wrong),
/// so the few values that must not be lost survive the switch to default settings. Best effort: only
/// top-level-looking properties written the way System.Text.Json writes them are found.
/// </summary>
public static class JsonSalvage
{
    /// <summary>
    /// The strings of the array property <paramref name="name"/>. A cut-off array gives the complete
    /// strings before the cut; a damaged array (missing ']', anything but strings and commas) gives the strings
    /// before the damage. Empty when the property is not found. One pass over the text.
    /// </summary>
    public static List<string> StringArray(string? json, string name)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(json)) return result;
        var start = Regex.Match(json, "\"" + Regex.Escape(name) + @"""\s*:\s*\[", RegexOptions.CultureInvariant);
        if (!start.Success) return result;
        int i = start.Index + start.Length;
        while (true)
        {
            i = SkipWhitespace(json, i);
            if (i >= json.Length || json[i] != '"') return result; // ']', end of text or damage
            int close = IndexOfStringEnd(json, i + 1);
            if (close < 0) return result; // cut off inside the string
            var raw = json[(i + 1)..close];
            i = SkipWhitespace(json, close + 1);
            // Only a string followed by ',' or ']' (or the end of a cut-off file) is an array element;
            // "x": means the ']' is missing and this is already the next property.
            if (i < json.Length && json[i] != ',' && json[i] != ']') return result;
            var value = Unescape(raw);
            if (!string.IsNullOrWhiteSpace(value) && !result.Contains(value, StringComparer.OrdinalIgnoreCase)) result.Add(value);
            if (i >= json.Length || json[i] == ']') return result;
            i++; // ','
        }
    }

    /// <summary>The boolean property <paramref name="name"/>; null when it is not found.</summary>
    public static bool? Bool(string? json, string name)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var m = Regex.Match(json, "\"" + Regex.Escape(name) + @"""\s*:\s*(true|false)\b", RegexOptions.CultureInvariant);
        return m.Success ? m.Groups[1].Value == "true" : null;
    }

    private static int SkipWhitespace(string json, int i)
    {
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        return i;
    }

    /// <summary>Index of the '"' closing the string whose text starts at <paramref name="from"/>; -1 when it never closes.</summary>
    private static int IndexOfStringEnd(string json, int from)
    {
        for (int i = from; i < json.Length; i++)
        {
            if (json[i] == '\\') i++;
            else if (json[i] == '"') return i;
        }
        return -1;
    }

    private static string Unescape(string raw)
    {
        try { return JsonSerializer.Deserialize<string>("\"" + raw + "\"") ?? ""; }
        catch (JsonException) { return ""; }
    }
}
