using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DS4Handler.Core.Persistence;

namespace DS4Handler.Core.Tests;

/// <summary>Edge cases for <see cref="JsonSalvage"/>: what a damaged settings.json can look like in practice.</summary>
public class JsonSalvageEdgeTests
{
    private static readonly string[] Ids =
    {
        @"HID\VID_054C&PID_09CC&MI_03\7&1234&0&0000",
        @"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&09CC\8&2A3B&0&0000",
        @"HID\VID_054C&PID_0CE6\9&FF&0&0000",
    };

    private static string Settings(bool indented, object? controllers = null) => JsonSerializer.Serialize(new
    {
        StartWithWindows = false,
        LastRunVersion = "1.2.0",
        Controllers = controllers ?? new Dictionary<string, object>
        {
            ["054C-09CC-AB"] = new { LearningEnabled = true, Name = "Controller van Sem", LastSeenUtc = DateTime.UnixEpoch },
        },
        HiddenInstanceIds = Ids,
        HidHideActivatedByApp = true,
    }, new JsonSerializerOptions { WriteIndented = indented });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IntactFile_IndentedOrCompact_GivesAllIds(bool indented)
    {
        var json = Settings(indented);
        Assert.Equal(Ids, JsonSalvage.StringArray(json, "HiddenInstanceIds"));
        Assert.True(JsonSalvage.Bool(json, "HidHideActivatedByApp"));
    }

    [Fact]
    public void EveryTruncationPoint_GivesAPrefixOfTheIds_AndNeverThrows()
    {
        // A write cut off at any byte: the salvaged ids are always complete ids, in order, never a partial one.
        var json = Settings(indented: true);
        int arrayStart = json.IndexOf("\"HiddenInstanceIds\"", StringComparison.Ordinal);
        for (int cut = 0; cut <= json.Length; cut++)
        {
            var text = json[..cut];
            var got = JsonSalvage.StringArray(text, "HiddenInstanceIds");
            Assert.True(got.Count <= Ids.Length);
            Assert.Equal(Ids.Take(got.Count), got);
            if (cut <= arrayStart) Assert.Empty(got);
            var flag = JsonSalvage.Bool(text, "HidHideActivatedByApp");
            Assert.True(flag is null or true);
        }
        Assert.Equal(Ids, JsonSalvage.StringArray(json[..(json.LastIndexOf(']') + 1)], "HiddenInstanceIds"));
    }

    [Fact]
    public void Utf8BomAtTheStart_StillFound()
    {
        var json = "\uFEFF" + Settings(indented: true);
        Assert.Equal(Ids, JsonSalvage.StringArray(json, "HiddenInstanceIds"));
        Assert.True(JsonSalvage.Bool(json, "HidHideActivatedByApp"));
    }

    [Fact]
    public void IdsWithQuotesBackslashesAndUnicodeEscapes_AreUnescaped()
    {
        var odd = new[] { "A\"B", @"C\D\\E", "naïve ü", "tab\there", "slash/ok" };
        var json = JsonSerializer.Serialize(new { HiddenInstanceIds = odd }); // STJ escapes " \ non-ASCII and control chars
        Assert.Equal(odd, JsonSalvage.StringArray(json, "HiddenInstanceIds"));
    }

    [Fact]
    public void HandWrittenEscapes_AreUnescaped()
    {
        var json = "{\"HiddenInstanceIds\":[\"HID\\u005CVID_1\\u00262\", \"HID\\/X\"]";
        Assert.Equal(new[] { "HID\\VID_1&2", "HID/X" }, JsonSalvage.StringArray(json, "HiddenInstanceIds"));
    }

    [Fact]
    public void InvalidEscape_DropsOnlyThatString()
    {
        var json = "{\"HiddenInstanceIds\":[\"good\", \"bad\\q\", \"also good\"]}";
        Assert.Equal(new[] { "good", "also good" }, JsonSalvage.StringArray(json, "HiddenInstanceIds"));
    }

    [Fact]
    public void Duplicates_DifferingOnlyInCase_KeepTheFirst()
    {
        var json = "{\"HiddenInstanceIds\":[\"HID\\\\x\", \"hid\\\\X\", \"HID\\\\Y\", \"HID\\\\x\"]}";
        Assert.Equal(new[] { @"HID\x", @"HID\Y" }, JsonSalvage.StringArray(json, "HiddenInstanceIds"));
    }

    [Fact]
    public void WhitespaceOnlyEntries_AreDropped()
    {
        var json = "{\"HiddenInstanceIds\":[\" \", \"\\t\", \"\", \"A\"]}";
        Assert.Equal(new[] { "A" }, JsonSalvage.StringArray(json, "HiddenInstanceIds"));
    }

    [Fact]
    public void PropertyName_IsCaseSensitive_LikeTheSerializer()
    {
        // System.Text.Json (default options, as SettingsStore uses) ignores "hiddeninstanceids", so salvage does too.
        var json = "{\"hiddeninstanceids\":[\"A\"], \"hidhideactivatedbyapp\": true}";
        Assert.Empty(JsonSalvage.StringArray(json, "HiddenInstanceIds"));
        Assert.Null(JsonSalvage.Bool(json, "HidHideActivatedByApp"));
    }

    [Fact]
    public void LongerPropertyNameWithSameSuffix_IsNotConfused()
    {
        var json = "{\"OldHiddenInstanceIds\":[\"OLD\"], \"HidHideActivatedByAppX\": true, \"HiddenInstanceIds\":[\"NEW\"]}";
        Assert.Equal(new[] { "NEW" }, JsonSalvage.StringArray(json, "HiddenInstanceIds"));
        Assert.Null(JsonSalvage.Bool(json, "HidHideActivatedByApp"));
    }

    [Fact]
    public void PropertyNameInsideAStringValue_IsNotMatched()
    {
        // A controller named after the property: STJ escapes the quotes, so "\"HiddenInstanceIds\": [" never matches.
        var controllers = new Dictionary<string, object> { ["X"] = new { Name = "\"HiddenInstanceIds\": [\"FAKE\"], \"HidHideActivatedByApp\": false" } };
        var json = Settings(indented: true, controllers);
        Assert.Equal(Ids, JsonSalvage.StringArray(json, "HiddenInstanceIds"));
        Assert.True(JsonSalvage.Bool(json, "HidHideActivatedByApp"));
    }

    [Theory]
    [InlineData("{\"HiddenInstanceIds\": \"HID\\\\A\"}")]           // string instead of array
    [InlineData("{\"HiddenInstanceIds\": 42}")]                     // number
    [InlineData("{\"HiddenInstanceIds\": {\"a\": \"HID\\\\A\"}}")]  // object
    [InlineData("{\"HiddenInstanceIds\": []}")]                      // empty
    [InlineData("{\"HiddenInstanceIds\": [1, 2, true, null]}")]      // no strings
    public void WrongTypes_GiveEmpty(string json) => Assert.Empty(JsonSalvage.StringArray(json, "HiddenInstanceIds"));

    [Theory]
    [InlineData("{\"HidHideActivatedByApp\": \"true\"}")]
    [InlineData("{\"HidHideActivatedByApp\": 1}")]
    [InlineData("{\"HidHideActivatedByApp\": null}")]
    [InlineData("{\"HidHideActivatedByApp\": True}")]
    [InlineData("{\"HidHideActivatedByApp\": truest}")]
    [InlineData("{\"HidHideActivatedByApp\": tru")]
    public void Bool_WrongTypesOrTruncated_GiveNull(string json) => Assert.Null(JsonSalvage.Bool(json, "HidHideActivatedByApp"));

    [Theory]
    [InlineData("{\"HidHideActivatedByApp\":true}", true)]
    [InlineData("{\"HidHideActivatedByApp\" :\r\n\tfalse }", false)]
    [InlineData("{\"HidHideActivatedByApp\": true", true)]  // truncated right after the value
    public void Bool_Whitespace(string json, bool expected) => Assert.Equal(expected, JsonSalvage.Bool(json, "HidHideActivatedByApp"));

    [Fact]
    public void NestedArray_StopsAtTheFirstClose()
    {
        // Not something the app writes; only checks it does not run on into the rest of the file.
        var json = "{\"HiddenInstanceIds\": [[\"A\"], \"B\"], \"X\": [\"C\"]}";
        Assert.Equal(new[] { "A" }, JsonSalvage.StringArray(json, "HiddenInstanceIds"));
    }

    [Fact]
    public void EscapedQuoteAndBracketInsideAnId_DoNotEndTheArray()
    {
        var json = "{\"HiddenInstanceIds\": [\"a\\\"]b\", \"c\"], \"Other\": [\"d\"]}";
        Assert.Equal(new[] { "a\"]b", "c" }, JsonSalvage.StringArray(json, "HiddenInstanceIds"));
    }

    [Fact]
    public void Utf16TextReadAsGarbage_DoesNotThrow()
    {
        // A file whose encoding was misdetected: every other char is a NUL.
        var bytes = Encoding.Unicode.GetBytes(Settings(indented: true));
        var garbled = Encoding.Latin1.GetString(bytes);
        Assert.Empty(JsonSalvage.StringArray(garbled, "HiddenInstanceIds"));
        Assert.Null(JsonSalvage.Bool(garbled, "HidHideActivatedByApp"));
    }

    [Fact]
    public void RandomGarbage_NeverThrows()
    {
        var rng = new Random(1234);
        const string alphabet = "\"\\[]{}:, \n\tHiddenInstanceIdsHidHideActivatedByApptruefalseu0123456789&";
        for (int round = 0; round < 2000; round++)
        {
            var chars = new char[rng.Next(0, 200)];
            for (int i = 0; i < chars.Length; i++) chars[i] = alphabet[rng.Next(alphabet.Length)];
            var text = (rng.Next(2) == 0 ? "{\"HiddenInstanceIds\": [" : "") + new string(chars);
            var ids = JsonSalvage.StringArray(text, "HiddenInstanceIds");
            Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
            JsonSalvage.Bool(text, "HidHideActivatedByApp");
        }
    }

    [Fact]
    public void HugeFile_IsHandledQuickly()
    {
        // A settings.json with many controllers (and a big hand-pasted blob) must not stall startup.
        var controllers = Enumerable.Range(0, 5000).ToDictionary(i => $"054C-09CC-{i:X8}", i => (object)new { Name = new string('x', 100) });
        var json = Settings(indented: true, controllers);
        var cut = json[..^10]; // damaged at the end
        var sw = Stopwatch.StartNew();
        Assert.Equal(Ids, JsonSalvage.StringArray(cut, "HiddenInstanceIds"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed}");
    }
}
