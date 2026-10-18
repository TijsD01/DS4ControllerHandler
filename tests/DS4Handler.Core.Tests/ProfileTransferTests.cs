using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;
using DS4Handler.Core.Profiles;

namespace DS4Handler.Core.Tests;

public class ProfileTransferTests
{
    private static CalibrationProfile Sample() => new()
    {
        ControllerId = "ds4-a4538512abcd",
        Model = ControllerModel.DualShock4,
        LeftStick = new StickCalibrationData { CenterX = 0.12f, CenterY = -0.08f, OuterRadii = new[] { 0.9f, 1f }, Releases = 7 },
        R2 = new TriggerCalibrationData { EndStop = 200f, EndStopLearned = true, PressCount = 9, RecentPeaks = new[] { 199f, 200f } },
        UpdatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Theory]
    [InlineData("ds4-a4538512abcd", "A4:53:85:12:AB:CD")]
    [InlineData("dualsense-0011223344ff", "00:11:22:33:44:FF")]
    [InlineData("DS4-A4:53:85:12:AB:CD", "A4:53:85:12:AB:CD")]
    public void MacFromId_FormatsMac(string id, string? expected)
    {
        Assert.Equal(expected, ControllerIds.MacFromId(id));
    }

    [Theory]
    [InlineData("ds4-path-0123456789abcdef")]
    [InlineData("ds4-12345")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ds4-a4538512abzz")]
    public void MacFromId_ReturnsNullWithoutMac(string? id)
    {
        Assert.Null(ControllerIds.MacFromId(id));
    }

    [Fact]
    public void PathFallback_IsDetected()
    {
        Assert.True(ControllerIds.IsPathFallback("ds4-path-0123456789abcdef"));
        Assert.False(ControllerIds.IsPathFallback("ds4-a4538512abcd"));
    }

    [Fact]
    public void ModelFromId_UsesPrefix()
    {
        Assert.Equal(ControllerModel.DualShock4, ControllerIds.ModelFromId("ds4-a4538512abcd"));
        Assert.Equal(ControllerModel.DualSense, ControllerIds.ModelFromId("dualsense-a4538512abcd"));
        Assert.Null(ControllerIds.ModelFromId("xbox-1"));
    }

    [Fact]
    public void CopyFor_ChangesOnlyIdentityAndIsDeep()
    {
        var source = Sample();
        var copy = ProfileTransfer.CopyFor(source, "dualsense-001122334455", ControllerModel.DualSense);

        Assert.Equal("dualsense-001122334455", copy.ControllerId);
        Assert.Equal(ControllerModel.DualSense, copy.Model);
        Assert.Equal(200f, copy.R2.EndStop);
        Assert.True(copy.R2.EndStopLearned);
        Assert.Equal(0.12f, copy.LeftStick.CenterX);
        Assert.Equal(source.LeftStick.OuterRadii, copy.LeftStick.OuterRadii);
        Assert.NotSame(source.LeftStick.OuterRadii, copy.LeftStick.OuterRadii);
        Assert.NotSame(source.R2.RecentPeaks, copy.R2.RecentPeaks);
        Assert.Equal("ds4-a4538512abcd", source.ControllerId); // source untouched
    }

    [Fact]
    public void CopyFor_LoadsIntoCalibration()
    {
        var copy = ProfileTransfer.CopyFor(Sample(), "ds4-001122334455", ControllerModel.DualShock4);
        var calibration = new ControllerCalibration("ds4-001122334455", ControllerModel.DualShock4);
        calibration.Import(copy);
        Assert.Equal(200f, calibration.R2.EndStop);
        Assert.True(calibration.R2.EndStopLearned);
    }

    [Fact]
    public void SerializeDeserialize_RoundTripsWithName()
    {
        var json = ProfileTransfer.Serialize(Sample(), "  Controller van Sem ");
        var imported = ProfileTransfer.Deserialize(json);
        Assert.Equal("Controller van Sem", imported.Name);
        Assert.Equal("ds4-a4538512abcd", imported.Profile.ControllerId);
        Assert.Equal(ControllerModel.DualShock4, imported.Profile.Model);
        Assert.Equal(200f, imported.Profile.R2.EndStop);
        Assert.Equal(new[] { 0.9f, 1f }, imported.Profile.LeftStick.OuterRadii);
    }

    [Fact]
    public void Deserialize_AcceptsPlainProfileFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ds4handler-transfer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProfileStore(dir);
            store.Save(Sample());
            var file = Directory.GetFiles(dir, "*.json").Single();
            var imported = ProfileTransfer.Deserialize(File.ReadAllText(file));
            Assert.Null(imported.Name);
            Assert.Equal("ds4-a4538512abcd", imported.Profile.ControllerId);
            Assert.Equal(200f, imported.Profile.R2.EndStop);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Deserialize_NullSectionsBecomeDefaults()
    {
        var imported = ProfileTransfer.Deserialize("""
            { "Format": "DS4Handler.Profile", "Version": 1, "Profile": { "ControllerId": "ds4-a4538512abcd", "Model": "DualShock4", "L2": null, "LeftStick": null } }
            """);
        Assert.NotNull(imported.Profile.L2);
        Assert.NotNull(imported.Profile.LeftStick);
        Assert.Equal(255f, imported.Profile.L2.EndStop);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "Format": "Other", "Version": 1, "Profile": { "ControllerId": "x" } }""")]
    [InlineData("""{ "Format": "DS4Handler.Profile", "Version": 99, "Profile": { "ControllerId": "x" } }""")]
    [InlineData("""{ "Format": "DS4Handler.Profile", "Version": 1 }""")]
    [InlineData("""{ "ControllerId": "" }""")]
    [InlineData("""{ "ControllerId": "ds4-a4538512abcd", "Model": 7 }""")]
    [InlineData("""{ "ControllerId": "ds4-a4538512abcd", "Model": "Xbox" }""")]
    public void Deserialize_RejectsBadFiles(string json)
    {
        Assert.Throws<FormatException>(() => ProfileTransfer.Deserialize(json));
    }

    private static string PlainProfile(string id, string model) =>
        "{ \"ControllerId\": \"" + id + "\", \"Model\": \"" + model + "\" }";

    [Theory]
    [InlineData("ds4-a4538512abcd", "DualShock4")]
    [InlineData("dualsense-a4538512abcd", "DualSense")]
    [InlineData("ds4-path-0123456789abcdef", "DualShock4")]
    [InlineData("dualsense-path-0123456789abcdef", "DualSense")]
    public void Deserialize_AcceptsCanonicalIds(string id, string model)
    {
        Assert.Equal(id, ProfileTransfer.Deserialize(PlainProfile(id, model)).Profile.ControllerId);
    }

    [Fact]
    public void Deserialize_LowercasesAndTrimsId()
    {
        var imported = ProfileTransfer.Deserialize(PlainProfile("  DS4-A4538512ABCD ", "DualShock4"));
        Assert.Equal("ds4-a4538512abcd", imported.Profile.ControllerId);
    }

    [Theory]
    [InlineData("ds4-../../evil", "DualShock4")]           // path tricks
    [InlineData("ds4-a4:53:85:12:ab:cd", "DualShock4")]    // not canonical (colons)
    [InlineData("ds4-a4538512abc", "DualShock4")]          // 11 digits
    [InlineData("ds4-a4538512abcde", "DualShock4")]        // 13 digits
    [InlineData("ds4-a4538512abzz", "DualShock4")]         // not hex
    [InlineData("xbox-a4538512abcd", "DualShock4")]        // unknown prefix
    [InlineData("ds4-path-0123", "DualShock4")]            // short path hash
    [InlineData("dualsense-path-0123456789abcdef0", "DualSense")] // too long
    [InlineData("ds4-a4538512abcd", "DualSense")]          // model does not match the id
    [InlineData("dualsense-a4538512abcd", "DualShock4")]   // model does not match the id
    [InlineData("CON", "DualShock4")]                      // reserved name
    public void Deserialize_RejectsNonCanonicalIds(string id, string model)
    {
        Assert.Throws<FormatException>(() => ProfileTransfer.Deserialize(PlainProfile(id, model)));
    }

    [Fact]
    public void Deserialize_CutsVeryLongNames()
    {
        var json = ProfileTransfer.Serialize(Sample(), new string('x', 500));
        Assert.Equal(60, ProfileTransfer.Deserialize(json).Name!.Length);
    }

    [Theory]
    [InlineData("ds4-a4538512abcd", true)]
    [InlineData("ds4-path-0123456789abcdef", true)]
    [InlineData("DS4-A4538512ABCD", false)]
    [InlineData("ds4-a4538512abcd ", false)]
    [InlineData(null, false)]
    public void IsCanonical_MatchesOnlyAppIds(string? id, bool expected)
    {
        Assert.Equal(expected, ControllerIds.IsCanonical(id));
    }
}
