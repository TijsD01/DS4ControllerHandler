using System.Text.Json;
using System.Text.RegularExpressions;

namespace DS4Handler.Core.Updates;

/// <summary>One release on GitHub that has a setup exe.</summary>
public sealed record ReleaseInfo(
    ReleaseVersion Version,
    string Tag,
    string PageUrl,
    string SetupName,
    string SetupUrl,
    long SetupSize,
    IReadOnlyList<string> Notes);

/// <summary>A newer version than the one running, with what changed since.</summary>
public sealed record UpdateInfo(ReleaseInfo Latest, IReadOnlyList<(ReleaseVersion Version, IReadOnlyList<string> Notes)> Changes)
{
    /// <summary>All new features since the running version, newest version first.</summary>
    public IReadOnlyList<string> AllNotes => Changes.SelectMany(c => c.Notes).Distinct().ToList();
}

/// <summary>Reads the GitHub releases list and decides whether there is something newer.</summary>
public static partial class UpdateCheck
{
    public const string Owner = "TijsD01";
    public const string Repo = "DS4ControllerHandler";
    public static readonly string ReleasesApiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=30";
    public static readonly string ReleasesPageUrl = $"https://github.com/{Owner}/{Repo}/releases/latest";

    // The version comes from the setup's file name, so a tag like "realease1" works too.
    [GeneratedRegex(@"^DS4Handler-Setup-(.+)\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex SetupName();

    /// <summary>
    /// Parses the JSON of GET /repos/{owner}/{repo}/releases. Drafts, releases without a setup exe
    /// and assets outside github.com are skipped. Pre-releases only when <paramref name="includePrereleases"/>.
    /// </summary>
    public static IReadOnlyList<ReleaseInfo> ParseReleases(string json, bool includePrereleases = false)
    {
        var result = new List<ReleaseInfo>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
        foreach (var rel in doc.RootElement.EnumerateArray())
        {
            if (Bool(rel, "draft")) continue;
            bool pre = Bool(rel, "prerelease");
            if (pre && !includePrereleases) continue;
            if (!rel.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Str(asset, "name");
                var url = Str(asset, "browser_download_url");
                var m = SetupName().Match(name);
                if (!m.Success || !ReleaseVersion.TryParse(m.Groups[1].Value, out var version)) continue;
                if (version.IsPrerelease && !includePrereleases && !pre) continue; // e.g. a dev build attached by hand
                if (!IsTrustedDownload(url)) continue;
                long size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                result.Add(new ReleaseInfo(version, Str(rel, "tag_name"), Str(rel, "html_url"), name, url, size,
                    ReleaseNotes.FromReleaseBody(Str(rel, "body"))));
                break;
            }
        }
        return result;
    }

    /// <summary>The newest release above <paramref name="current"/>, or null when the app is up to date.</summary>
    public static UpdateInfo? FindUpdate(ReleaseVersion current, IEnumerable<ReleaseInfo> releases)
    {
        var newer = releases.Where(r => r.Version > current).OrderByDescending(r => r.Version).ToList();
        if (newer.Count == 0) return null;
        return new UpdateInfo(newer[0], newer.Select(r => (r.Version, r.Notes)).ToList());
    }

    /// <summary>Only download setups from this repository's own release files.</summary>
    public static bool IsTrustedDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith($"/{Owner}/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase);

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
