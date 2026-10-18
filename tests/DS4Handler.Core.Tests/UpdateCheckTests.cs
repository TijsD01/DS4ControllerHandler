using DS4Handler.Core.Updates;

namespace DS4Handler.Core.Tests;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, "")]
    [InlineData("v1.2.3", 1, 2, 3, "")]
    [InlineData("1.2", 1, 2, 0, "")]
    [InlineData("1.2.3.0", 1, 2, 3, "")]
    [InlineData("1.1.0-beta1", 1, 1, 0, "beta1")]
    [InlineData("1.0.2+abc123", 1, 0, 2, "")]
    [InlineData("0.0.0-dev.12+abc", 0, 0, 0, "dev.12")]
    public void Parses(string text, int major, int minor, int patch, string suffix)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var v));
        Assert.Equal(new ReleaseVersion(major, minor, patch, suffix), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("realease1")]
    [InlineData("1")]
    [InlineData("1.x.3")]
    public void RejectsNonVersions(string? text) => Assert.False(ReleaseVersion.TryParse(text, out _));

    [Theory]
    [InlineData("1.0.3", "1.0.2")]
    [InlineData("1.1.0", "1.0.99")]
    [InlineData("2.0.0", "1.9.9")]
    [InlineData("1.0.10", "1.0.9")]
    [InlineData("1.1.0", "1.1.0-beta2")]
    [InlineData("1.1.0-beta2", "1.1.0-beta1")]
    [InlineData("0.0.0-dev.10", "0.0.0-dev.9")]
    [InlineData("1.0.2", "0.0.0-dev.40")]
    public void Orders(string newer, string older)
    {
        ReleaseVersion.TryParse(newer, out var a);
        ReleaseVersion.TryParse(older, out var b);
        Assert.True(a > b);
        Assert.True(b < a);
    }

    [Fact]
    public void EqualVersionsAreNotNewer()
    {
        ReleaseVersion.TryParse("1.0.2", out var a);
        ReleaseVersion.TryParse("v1.0.2+sha", out var b);
        Assert.Equal(0, a.CompareTo(b));
    }
}

public class ReleaseNotesTests
{
    [Fact]
    public void TakesTheNieuwSectionFromTheReleaseText()
    {
        var body = """
            ## Nieuw in deze versie

            - De app meldt zelf als er een nieuwe versie is
            * **Profielen** exporteren en [importeren](https://example.com)

            ## Downloaden

            - Dit hoort er niet bij

            ## What's Changed
            * Add updater by @TijsD01 in https://github.com/TijsD01/DS4ControllerHandler/pull/4
            """;
        Assert.Equal(new[] { "De app meldt zelf als er een nieuwe versie is", "Profielen exporteren en importeren" },
            ReleaseNotes.FromReleaseBody(body));
    }

    [Fact]
    public void FallsBackToGeneratedNotesWithoutAuthorAndLink()
    {
        var body = "## Downloaden\r\nKlik hier.\r\n\r\n## What's Changed\r\n* Add updater by @TijsD01 in https://github.com/x/y/pull/4\r\n* Fix bot by @dependabot[bot] in https://github.com/x/y/pull/5\r\n";
        Assert.Equal(new[] { "Add updater", "Fix bot" }, ReleaseNotes.FromReleaseBody(body));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Gewoon tekst zonder lijstjes.")]
    public void NothingToShow(string? body) => Assert.Empty(ReleaseNotes.FromReleaseBody(body));

    [Fact]
    public void ChangelogGivesTheTopSectionIncludingSubheadings()
    {
        var changelog = """
            # Wat is er nieuw

            Uitleg bovenaan.

            ## 1.1.0

            ### Nieuw
            - Updates vanuit de app
            ### Opgelost
            - Installer houdt je profielen

            ## 1.0.2
            - Eerste versie
            """;
        Assert.Equal(new[] { "Updates vanuit de app", "Installer houdt je profielen" }, ReleaseNotes.NewestFromChangelog(changelog));
    }
}

public class UpdateCheckTests
{
    private const string Feed = """
        [
          { "tag_name": "v1.2.0-beta1", "html_url": "https://github.com/TijsD01/DS4ControllerHandler/releases/tag/v1.2.0-beta1",
            "draft": false, "prerelease": true, "body": "## Nieuw in deze versie\n- Beta-ding",
            "assets": [ { "name": "DS4Handler-Setup-1.2.0-beta1.exe", "size": 10,
                          "browser_download_url": "https://github.com/TijsD01/DS4ControllerHandler/releases/download/v1.2.0-beta1/DS4Handler-Setup-1.2.0-beta1.exe" } ] },
          { "tag_name": "v9.9.9", "draft": true, "prerelease": false, "body": "",
            "assets": [ { "name": "DS4Handler-Setup-9.9.9.exe", "size": 10,
                          "browser_download_url": "https://github.com/TijsD01/DS4ControllerHandler/releases/download/v9.9.9/DS4Handler-Setup-9.9.9.exe" } ] },
          { "tag_name": "v1.1.0", "html_url": "https://github.com/TijsD01/DS4ControllerHandler/releases/tag/v1.1.0",
            "draft": false, "prerelease": false, "body": "## Nieuw in deze versie\n- Updates vanuit de app\n- Profielen blijven bewaard",
            "assets": [
              { "name": "DS4Handler-1.1.0-portable.exe", "size": 5,
                "browser_download_url": "https://github.com/TijsD01/DS4ControllerHandler/releases/download/v1.1.0/DS4Handler-1.1.0-portable.exe" },
              { "name": "DS4Handler-Setup-1.1.0.exe", "size": 69684672,
                "browser_download_url": "https://github.com/TijsD01/DS4ControllerHandler/releases/download/v1.1.0/DS4Handler-Setup-1.1.0.exe" } ] },
          { "tag_name": "v1.0.5", "draft": false, "prerelease": false, "body": "## Nieuw in deze versie\n- Kleine fix",
            "assets": [ { "name": "DS4Handler-Setup-1.0.5.exe", "size": 10,
                          "browser_download_url": "https://github.com/TijsD01/DS4ControllerHandler/releases/download/v1.0.5/DS4Handler-Setup-1.0.5.exe" } ] },
          { "tag_name": "evil", "draft": false, "prerelease": false, "body": "",
            "assets": [ { "name": "DS4Handler-Setup-50.0.0.exe", "size": 10,
                          "browser_download_url": "https://evil.example.com/DS4Handler-Setup-50.0.0.exe" } ] },
          { "tag_name": "realease1", "draft": false, "prerelease": false, "body": "Eerste release",
            "assets": [ { "name": "DS4Handler-Setup-1.0.2.exe", "size": 10,
                          "browser_download_url": "https://github.com/TijsD01/DS4ControllerHandler/releases/download/realease1/DS4Handler-Setup-1.0.2.exe" } ] },
          { "tag_name": "nosetup", "draft": false, "prerelease": false, "body": "", "assets": [] }
        ]
        """;

    private static ReleaseVersion V(string s) => ReleaseVersion.TryParse(s, out var v) ? v : throw new ArgumentException(s);

    [Fact]
    public void ParsesStableReleasesWithASetupFromThisRepo()
    {
        var releases = UpdateCheck.ParseReleases(Feed);
        Assert.Equal(new[] { "1.1.0", "1.0.5", "1.0.2" }, releases.Select(r => r.Version.ToString()));
        var latest = releases[0];
        Assert.Equal("DS4Handler-Setup-1.1.0.exe", latest.SetupName);
        Assert.Equal(69684672, latest.SetupSize);
        Assert.Equal(new[] { "Updates vanuit de app", "Profielen blijven bewaard" }, latest.Notes);
        Assert.Equal("realease1", releases[2].Tag);
    }

    [Fact]
    public void PrereleasesOnlyWhenAskedFor()
    {
        var releases = UpdateCheck.ParseReleases(Feed, includePrereleases: true);
        Assert.Contains(releases, r => r.Version.ToString() == "1.2.0-beta1");
    }

    [Fact]
    public void FindsTheNewestAndCollectsEverythingSinceTheRunningVersion()
    {
        var update = UpdateCheck.FindUpdate(V("1.0.2"), UpdateCheck.ParseReleases(Feed));
        Assert.NotNull(update);
        Assert.Equal("1.1.0", update!.Latest.Version.ToString());
        Assert.Equal(new[] { "1.1.0", "1.0.5" }, update.Changes.Select(c => c.Version.ToString()));
        Assert.Equal(new[] { "Updates vanuit de app", "Profielen blijven bewaard", "Kleine fix" }, update.AllNotes);
    }

    [Theory]
    [InlineData("1.1.0")]
    [InlineData("1.1.1")]
    public void NoUpdateWhenUpToDate(string current) =>
        Assert.Null(UpdateCheck.FindUpdate(V(current), UpdateCheck.ParseReleases(Feed)));

    [Fact]
    public void DevBuildsAreOfferedTheRelease()
    {
        var update = UpdateCheck.FindUpdate(V("0.0.0-dev.7"), UpdateCheck.ParseReleases(Feed));
        Assert.Equal("1.1.0", update!.Latest.Version.ToString());
    }

    [Theory]
    [InlineData("https://github.com/TijsD01/DS4ControllerHandler/releases/download/v1/DS4Handler-Setup-1.0.0.exe", true)]
    [InlineData("http://github.com/TijsD01/DS4ControllerHandler/releases/download/v1/x.exe", false)]
    [InlineData("https://github.com/someone/else/releases/download/v1/x.exe", false)]
    [InlineData("https://github.com.evil.com/TijsD01/DS4ControllerHandler/releases/download/v1/x.exe", false)]
    [InlineData("not a url", false)]
    public void OnlyTrustsThisRepositorysDownloads(string url, bool trusted) =>
        Assert.Equal(trusted, UpdateCheck.IsTrustedDownload(url));

    [Fact]
    public void BadJsonGivesNothingInsteadOfCrashingOnShape()
    {
        Assert.Empty(UpdateCheck.ParseReleases("{\"message\":\"API rate limit exceeded\"}"));
    }
}

public class ChangelogFileTests
{
    [Fact]
    public void TheRepositoryChangelogHasNewsAtTheTop()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CHANGELOG.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var notes = ReleaseNotes.NewestFromChangelog(File.ReadAllText(Path.Combine(dir!.FullName, "CHANGELOG.md")));
        Assert.NotEmpty(notes);
    }
}
