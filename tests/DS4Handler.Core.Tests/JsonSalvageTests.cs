using System.Text.Json;
using DS4Handler.Core.Persistence;

namespace DS4Handler.Core.Tests;

public class JsonSalvageTests
{
    // Written the way System.Text.Json writes settings.json: '&' as &, '\' as \\.
    private static readonly string Valid = JsonSerializer.Serialize(new
    {
        StartWithWindows = true,
        HiddenInstanceIds = new[] { @"HID\VID_054C&PID_09CC&MI_03\7&1234&0&0000", @"HID\VID_054C&PID_05C4\8&AB&0&0000" },
        HidHideActivatedByApp = true,
    }, new JsonSerializerOptions { WriteIndented = true });

    [Fact]
    public void FindsEscapedIdsInIntactJson()
    {
        Assert.Contains("\\u0026", Valid);
        Assert.Equal(new[] { @"HID\VID_054C&PID_09CC&MI_03\7&1234&0&0000", @"HID\VID_054C&PID_05C4\8&AB&0&0000" },
            JsonSalvage.StringArray(Valid, "HiddenInstanceIds"));
        Assert.True(JsonSalvage.Bool(Valid, "HidHideActivatedByApp"));
    }

    [Fact]
    public void TruncatedInsideSecondId_KeepsTheFirst()
    {
        var cut = Valid[..(Valid.IndexOf("PID_05C4", StringComparison.Ordinal))];
        Assert.Equal(new[] { @"HID\VID_054C&PID_09CC&MI_03\7&1234&0&0000" }, JsonSalvage.StringArray(cut, "HiddenInstanceIds"));
        Assert.Null(JsonSalvage.Bool(cut, "HidHideActivatedByApp"));
    }

    [Fact]
    public void BrokenElsewhere_StillFindsValues()
    {
        var broken = "{ \"Controllers\": { \"x\": { oops ,, }, \"HiddenInstanceIds\": [\"HID\\\\A\", \"hid\\\\a\", \"\", \"HID\\\\B\"], \"HidHideActivatedByApp\": false";
        Assert.Equal(new[] { @"HID\A", @"HID\B" }, JsonSalvage.StringArray(broken, "HiddenInstanceIds"));
        Assert.False(JsonSalvage.Bool(broken, "HidHideActivatedByApp"));
    }

    [Fact]
    public void StopsAtTheEndOfTheArray()
    {
        var json = "{\"HiddenInstanceIds\": [\"a]b\"], \"Other\": [\"not-an-id\"]}";
        Assert.Equal(new[] { "a]b" }, JsonSalvage.StringArray(json, "HiddenInstanceIds"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\0\0\0garbage")]
    [InlineData("{\"HiddenInstanceIds\": null}")]
    public void NothingFound_GivesEmpty(string? text)
    {
        Assert.Empty(JsonSalvage.StringArray(text, "HiddenInstanceIds"));
        Assert.Null(JsonSalvage.Bool(text, "HidHideActivatedByApp"));
    }
}
