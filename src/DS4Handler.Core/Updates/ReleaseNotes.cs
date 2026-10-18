using System.Text.RegularExpressions;

namespace DS4Handler.Core.Updates;

/// <summary>
/// Pulls the "what's new" list out of Markdown: the release text on GitHub and CHANGELOG.md.
/// Only bullet lines are kept, without Markdown markup, so the app can show them as plain text.
/// </summary>
public static partial class ReleaseNotes
{
    /// <summary>Heading of the section the release workflow writes from CHANGELOG.md.</summary>
    public const string NewSectionHeading = "Nieuw in deze versie";

    [GeneratedRegex(@"^\s{0,3}[-*+]\s+(.+?)\s*$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex Heading();

    // GitHub's generated notes: "* Fix thing by @user in https://github.com/.../pull/3"
    [GeneratedRegex(@"\s+by\s+@[\w-]+(?:\[bot\])?\s+in\s+https?://\S+$")]
    private static partial Regex GeneratedSuffix();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(\*\*|__|`)")]
    private static partial Regex Emphasis();

    /// <summary>
    /// The new features listed in a release text: the bullets under "Nieuw in deze versie", or
    /// else the bullets of GitHub's generated "What's Changed" list. Empty when neither is there.
    /// </summary>
    public static IReadOnlyList<string> FromReleaseBody(string? body)
    {
        var sections = Sections(body);
        var nieuw = sections.FirstOrDefault(s => s.Title.StartsWith("Nieuw", StringComparison.OrdinalIgnoreCase));
        if (nieuw != null && nieuw.Bullets.Count > 0) return nieuw.Bullets;
        var changed = sections.FirstOrDefault(s => s.Title.StartsWith("What's Changed", StringComparison.OrdinalIgnoreCase));
        return changed?.Bullets ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    /// <summary>
    /// The bullets of the first version section in CHANGELOG.md ("## 1.1.0", "## Volgende versie"),
    /// which is what the newest release contains.
    /// </summary>
    public static IReadOnlyList<string> NewestFromChangelog(string? changelog) =>
        Sections(changelog).FirstOrDefault(s => s.Level == 2)?.Bullets ?? (IReadOnlyList<string>)Array.Empty<string>();

    private sealed record Section(int Level, string Title, List<string> Bullets);

    private static List<Section> Sections(string? markdown)
    {
        var sections = new List<Section>();
        if (string.IsNullOrEmpty(markdown)) return sections;
        Section? current = null;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var h = Heading().Match(raw);
            // "###" and deeper are sub-headings: their bullets belong to the section above.
            if (h.Success && h.Groups[1].Value.Length <= 2)
            {
                current = new Section(h.Groups[1].Value.Length, h.Groups[2].Value.Trim(), new List<string>());
                sections.Add(current);
                continue;
            }
            if (current == null || h.Success) continue;
            var b = Bullet().Match(raw);
            if (!b.Success) continue;
            var text = Clean(b.Groups[1].Value);
            if (text.Length > 0) current.Bullets.Add(text);
        }
        return sections;
    }

    private static string Clean(string text)
    {
        text = GeneratedSuffix().Replace(text, "");
        text = Link().Replace(text, "$1");
        text = Emphasis().Replace(text, "");
        return text.Trim();
    }
}
