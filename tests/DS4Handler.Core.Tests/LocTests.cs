using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DS4Handler.Core.Tests;

/// <summary>Every UI text key the app uses exists in Dutch and English, with the same {0} placeholders.</summary>
public class LocTests
{
    private static readonly string AppDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DS4Handler.App"));
    private static readonly Regex Placeholder = new(@"\{(\d+)[^}]*\}");

    private static Dictionary<string, string> Read(string file) =>
        XDocument.Load(Path.Combine(AppDir, "Lang", file)).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    public static IEnumerable<object[]> Pairs() =>
        Directory.GetFiles(Path.Combine(AppDir, "Lang"), "*.en.resx")
            .Select(en => new object[] { Path.GetFileName(en).Replace(".en.resx", ".resx") });

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Dutch_and_English_have_the_same_keys_and_placeholders(string dutchFile)
    {
        var nl = Read(dutchFile);
        var en = Read(dutchFile.Replace(".resx", ".en.resx"));
        Assert.Empty(nl.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(nl.Keys));
        foreach (var (key, text) in nl)
        {
            var a = Placeholder.Matches(text).Select(m => m.Groups[1].Value).Distinct().Order();
            var b = Placeholder.Matches(en[key]).Select(m => m.Groups[1].Value).Distinct().Order();
            Assert.True(a.SequenceEqual(b), $"{key}: placeholders differ between Dutch and English");
            Assert.False(string.IsNullOrWhiteSpace(en[key]), $"{key}: English text is empty");
        }
    }

    [Fact]
    public void Every_key_used_in_the_app_exists()
    {
        var keys = Directory.GetFiles(Path.Combine(AppDir, "Lang"), "*.resx")
            .Where(f => !f.EndsWith(".en.resx")).SelectMany(f => Read(Path.GetFileName(f)).Keys).ToHashSet();
        var used = new Regex(@"\{l:Loc (\w+)\}|Loc\.(?:Get|Format)\(""(\w+)""");
        var missing = Directory.GetFiles(AppDir, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => used.Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)))
            .Where(u => !keys.Contains(u.Key))
            .Select(u => $"{u.File}: {u.Key}")
            .ToList();
        Assert.Empty(missing);
    }
}
