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
    private static readonly Regex QuotedString = new(@"""((?:[^""\\]|\\.)*)""", RegexOptions.CultureInvariant);

    /// <summary>
    /// The strings of the array property <paramref name="name"/>. A cut-off array gives the complete
    /// strings before the cut. Empty when the property is not found.
    /// </summary>
    public static List<string> StringArray(string? json, string name)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(json)) return result;
        var start = Regex.Match(json, "\"" + Regex.Escape(name) + @"""\s*:\s*\[", RegexOptions.CultureInvariant);
        if (!start.Success) return result;
        int from = start.Index + start.Length;
        int end = IndexOfArrayEnd(json, from);
        var body = json[from..end];
        foreach (Match m in QuotedString.Matches(body))
        {
            var value = Unescape(m.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(value) && !result.Contains(value, StringComparer.OrdinalIgnoreCase)) result.Add(value);
        }
        return result;
    }

    /// <summary>The boolean property <paramref name="name"/>; null when it is not found.</summary>
    public static bool? Bool(string? json, string name)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var m = Regex.Match(json, "\"" + Regex.Escape(name) + @"""\s*:\s*(true|false)\b", RegexOptions.CultureInvariant);
        return m.Success ? m.Groups[1].Value == "true" : null;
    }

    /// <summary>Index of the ']' closing the array that starts at <paramref name="from"/> (skipping strings), or the text end.</summary>
    private static int IndexOfArrayEnd(string json, int from)
    {
        bool inString = false;
        for (int i = from; i < json.Length; i++)
        {
            char c = json[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
            }
            else if (c == '"') inString = true;
            else if (c == ']') return i;
        }
        return json.Length;
    }

    private static string Unescape(string raw)
    {
        try { return JsonSerializer.Deserialize<string>("\"" + raw + "\"") ?? ""; }
        catch (JsonException) { return ""; }
    }
}
