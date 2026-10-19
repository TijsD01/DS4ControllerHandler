using System.Text.RegularExpressions;
using DS4Handler.Core.Updates;

namespace DS4Handler.Core.Tests;

/// <summary>
/// CHANGELOG.md as the release workflow (.github/workflows/release.yml) and the app read it: the top "## " section
/// must be the release being made, and both readers must agree on its bullets.
/// </summary>
public class ReleaseChangelogTests
{
    private static string RepoFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, name))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, name);
    }

    /// <summary>The PowerShell loop of the "Release-tekst" step, line for line (PowerShell -match is case-insensitive).</summary>
    private static string WorkflowNotes(IEnumerable<string> lines)
    {
        var notes = new List<string>();
        bool inside = false;
        foreach (var line in lines)
        {
            if (Regex.IsMatch(line, @"^\s{0,3}##\s", RegexOptions.IgnoreCase)) { if (inside) break; inside = true; continue; }
            if (Regex.IsMatch(line, @"^\s{0,3}#\s", RegexOptions.IgnoreCase)) { if (inside) break; continue; }
            if (inside) notes.Add(line);
        }
        var nieuw = string.Join("\n", notes).Trim();
        return nieuw.Length > 0 ? nieuw : "- Verbeteringen en reparaties.";
    }

    [Fact]
    public void WorkflowStillUsesTheSimulatedExtraction()
    {
        // If this fails the workflow changed: update WorkflowNotes above to match it.
        var yml = File.ReadAllText(RepoFile(Path.Combine(".github", "workflows", "release.yml")));
        Assert.Contains("foreach ($line in Get-Content CHANGELOG.md -Encoding utf8) {", yml);
        Assert.Contains(@"if ($line -match '^\s{0,3}##\s') { if ($inside) { break }; $inside = $true; continue }", yml);
        Assert.Contains(@"if ($line -match '^\s{0,3}#\s') { if ($inside) { break }; continue }", yml);
        Assert.Contains("if ($inside) { $notes += $line }", yml);
    }

    private static List<(string Heading, int Line)> VersionHeadings(string[] lines) =>
        lines.Select((l, i) => (l.Trim(), i)).Where(x => Regex.IsMatch(lines[x.i], @"^\s{0,3}##\s")).ToList();

    [Fact]
    public void TopSection_IsTheHighestVersion_And_1_2_1_IsAboveAllOlderOnes()
    {
        var headings = VersionHeadings(File.ReadAllLines(RepoFile("CHANGELOG.md")));
        var versions = headings.Select(h => Version.TryParse(h.Heading[2..].Trim(), out var v) ? v : null).ToList();
        var top = versions[0];
        if (top != null) Assert.All(versions.Where(v => v != null), v => Assert.True(v <= top, $"{v} is above the top section {top}"));
        int i121 = headings.FindIndex(h => h.Heading == "## 1.2.1");
        int i120 = headings.FindIndex(h => h.Heading == "## 1.2.0");
        Assert.True(i121 >= 0 && i120 == i121 + 1, "## 1.2.1 must directly precede ## 1.2.0");
    }

    [Fact]
    public void WorkflowAndApp_PickTheSameTopBullets()
    {
        var text = File.ReadAllText(RepoFile("CHANGELOG.md"));
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var workflow = WorkflowNotes(lines);
        Assert.NotEqual("- Verbeteringen en reparaties.", workflow);
        var headings = VersionHeadings(lines);
        Assert.DoesNotContain(headings[1].Heading[2..].Trim(), workflow); // nothing of the second section

        var app = ReleaseNotes.NewestFromChangelog(text);
        Assert.NotEmpty(app);
        // The release body the workflow writes is read back by the app (FromReleaseBody) to the same list.
        var body = $"## {ReleaseNotes.NewSectionHeading}\n\n{workflow}\n\n## Downloaden\n\n**[DS4Handler-Setup-x.exe](https://example.invalid/x)** is de makkelijkste keuze.\n";
        Assert.Equal(app, ReleaseNotes.FromReleaseBody(body));
    }

    [Fact]
    public void Section_1_2_1_ParsesToItsFiveBullets()
    {
        // Feed the workflow and the app only the 1.2.1 section onwards, as it was when 1.2.1 was released.
        var lines = File.ReadAllLines(RepoFile("CHANGELOG.md"));
        var from = Array.FindIndex(lines, l => l.Trim() == "## 1.2.1");
        Assert.True(from >= 0);
        var text = "# Wijzigingen\n\n" + string.Join("\n", lines[from..]);
        var workflow = WorkflowNotes(text.Split('\n'));
        Assert.DoesNotContain("Nieuw ontwerp", workflow); // first bullet of 1.2.0
        var app = ReleaseNotes.NewestFromChangelog(text);
        Assert.Equal(5, app.Count);
        Assert.StartsWith("Verwijderen maakt je controllers nu ook weer zichtbaar", app[0]);
        Assert.StartsWith("De app start niet meer met een foutmelding", app[^1]);
        Assert.Equal(app, ReleaseNotes.FromReleaseBody($"## {ReleaseNotes.NewSectionHeading}\n\n{workflow}\n"));
    }

    [Fact]
    public void Simulation_StopsAtTheNextSection_AndSkipsTheIntro()
    {
        var md = "# Wijzigingen\n\nIntro.\n- not a note\n\n## 9.9.9\n\n- a\n- b\n\n## 9.9.8\n\n- old\n";
        Assert.Equal("- a\n- b", WorkflowNotes(md.Split('\n')));
        Assert.Equal(new[] { "a", "b" }, ReleaseNotes.NewestFromChangelog(md));
    }

    [Fact]
    public void Simulation_EmptyTopSection_FallsBack()
    {
        Assert.Equal("- Verbeteringen en reparaties.", WorkflowNotes("# X\n\n## 1.0.0\n\n## 0.9\n- old".Split('\n')));
    }

    [Fact]
    public void ChangelogHasNoUnicodeReplacementCharacters()
    {
        // The workflow reads it as UTF-8 ("geïnstalleerd"); a wrongly saved file would show as garbage on GitHub.
        var text = File.ReadAllText(RepoFile("CHANGELOG.md"));
        Assert.DoesNotContain('�', text);
        Assert.Contains("geïnstalleerd", text);
    }
}
