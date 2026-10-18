using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;
using DS4Handler.Core.Profiles;

namespace DS4Handler.Core.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ds4handler-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static CalibrationProfile Sample(string id = "AA:BB:CC:DD:EE:FF") => new()
    {
        ControllerId = id,
        Model = ControllerModel.DualSense,
        LeftStick = new StickCalibrationData
        {
            CenterX = 0.05f,
            CenterY = -0.03f,
            NoiseRadius = 0.01f,
            RestSpread = 0.02f,
            OuterRadii = new[] { 0.98f, 1.0f, 0f, 0.95f },
            RestSamples = 1234,
        },
        RightStick = new StickCalibrationData { CenterX = -0.1f, OuterRadii = new[] { 0.9f } },
        L2 = new TriggerCalibrationData { Rest = 1f, EndStop = 255f, PressCount = 3 },
        R2 = new TriggerCalibrationData
        {
            Rest = 2.5f,
            EndStop = 201f,
            EndStopLearned = true,
            PressCount = 42,
            RecentPeaks = new[] { 200f, 201.5f, 202f, 150f },
        },
        UpdatedUtc = new DateTime(2026, 10, 18, 12, 30, 0, DateTimeKind.Utc),
    };

    private static void AssertSameStick(StickCalibrationData e, StickCalibrationData a)
    {
        Assert.Equal(e.CenterX, a.CenterX);
        Assert.Equal(e.CenterY, a.CenterY);
        Assert.Equal(e.NoiseRadius, a.NoiseRadius);
        Assert.Equal(e.RestSpread, a.RestSpread);
        Assert.Equal(e.OuterRadii, a.OuterRadii);
        Assert.Equal(e.RestSamples, a.RestSamples);
    }

    private static void AssertSameTrigger(TriggerCalibrationData e, TriggerCalibrationData a)
    {
        Assert.Equal(e.Rest, a.Rest);
        Assert.Equal(e.EndStop, a.EndStop);
        Assert.Equal(e.EndStopLearned, a.EndStopLearned);
        Assert.Equal(e.PressCount, a.PressCount);
        Assert.Equal(e.RecentPeaks, a.RecentPeaks);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEverything()
    {
        var store = new ProfileStore(_dir);
        var p = Sample();
        store.Save(p);

        var loaded = store.Load(p.ControllerId);
        Assert.NotNull(loaded);
        Assert.Equal(p.Version, loaded!.Version);
        Assert.Equal(p.ControllerId, loaded.ControllerId);
        Assert.Equal(p.Model, loaded.Model);
        Assert.Equal(p.UpdatedUtc, loaded.UpdatedUtc);
        AssertSameStick(p.LeftStick, loaded.LeftStick);
        AssertSameStick(p.RightStick, loaded.RightStick);
        AssertSameTrigger(p.L2, loaded.L2);
        AssertSameTrigger(p.R2, loaded.R2);
    }

    [Fact]
    public void Save_WritesIndentedJsonWithStringEnums_AndSafeFileName()
    {
        var store = new ProfileStore(_dir);
        store.Save(Sample("AA:BB:CC:DD:EE:FF"));

        var files = Directory.GetFiles(_dir);
        var file = Assert.Single(files);
        Assert.DoesNotContain(':', Path.GetFileName(file));
        Assert.EndsWith(".json", file);

        var json = File.ReadAllText(file);
        Assert.Contains("\"DualSense\"", json);
        Assert.Contains("\n", json);
    }

    [Fact]
    public void Save_Overwrites_AndLeavesNoTempFiles()
    {
        var store = new ProfileStore(_dir);
        store.Save(Sample());
        store.Save(Sample() with { R2 = new TriggerCalibrationData { EndStop = 180f, EndStopLearned = true } });

        Assert.Single(Directory.GetFiles(_dir));
        Assert.Equal(180f, store.Load("AA:BB:CC:DD:EE:FF")!.R2.EndStop);
    }

    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF")]
    [InlineData("../../etc/passwd")]
    [InlineData("con")]
    [InlineData("serial/with\\slashes*?")]
    public void UnusualIds_StayInsideTheDirectory(string id)
    {
        var store = new ProfileStore(_dir);
        store.Save(Sample(id));
        var file = Assert.Single(Directory.GetFiles(_dir));
        Assert.Equal(Path.GetFullPath(_dir), Path.GetDirectoryName(Path.GetFullPath(file)));
        Assert.Equal(id, store.Load(id)!.ControllerId);
    }

    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF", "aa-bb-cc-dd-ee-ff")]
    [InlineData("con", "_con")]
    [InlineData("COM1", "_com1")]
    [InlineData("", "unknown")]
    [InlineData("a b/c", "a_b_c")]
    public void SanitizeId_ProducesSafeNames(string id, string expected)
    {
        Assert.Equal(expected, ProfileStore.SanitizeId(id));
    }

    [Fact]
    public void Load_MissingProfile_ReturnsNull()
    {
        Assert.Null(new ProfileStore(_dir).Load("00:11:22:33:44:55"));
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"Model\": \"NotAController\"}")]
    public void Load_CorruptFile_ReturnsNull(string content)
    {
        var store = new ProfileStore(_dir);
        store.Save(Sample());
        File.WriteAllText(Directory.GetFiles(_dir).Single(), content);

        Assert.Null(store.Load("AA:BB:CC:DD:EE:FF"));
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void LoadAll_ReturnsEveryValidProfile_AndSkipsCorruptOnes()
    {
        var store = new ProfileStore(_dir);
        Assert.Empty(store.LoadAll()); // directory does not exist yet

        store.Save(Sample("AA:AA:AA:AA:AA:AA"));
        store.Save(Sample("BB:BB:BB:BB:BB:BB") with { Model = ControllerModel.DualShock4 });
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{{{");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "ignored");

        var all = store.LoadAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, p => p.ControllerId == "AA:AA:AA:AA:AA:AA" && p.Model == ControllerModel.DualSense);
        Assert.Contains(all, p => p.ControllerId == "BB:BB:BB:BB:BB:BB" && p.Model == ControllerModel.DualShock4);
    }

    [Fact]
    public void Delete_RemovesProfile()
    {
        var store = new ProfileStore(_dir);
        store.Save(Sample());

        Assert.True(store.Delete("AA:BB:CC:DD:EE:FF"));
        Assert.Null(store.Load("AA:BB:CC:DD:EE:FF"));
        Assert.False(store.Delete("AA:BB:CC:DD:EE:FF"));
        Assert.False(new ProfileStore(Path.Combine(_dir, "missing")).Delete("x"));
    }

    [Fact]
    public void DefaultDirectory_IsUnderApplicationData()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DS4ControllerHandler", "profiles");
        Assert.Equal(expected, ProfileStore.DefaultDirectory);
    }

    [Fact]
    public void Load_normalizes_null_sections_so_import_does_not_throw()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "pad.json"),
            "{\"ControllerId\":\"pad\",\"Model\":\"DualShock4\",\"L2\":null,\"R2\":null,\"LeftStick\":null,\"RightStick\":{\"OuterRadii\":null}}");
        var store = new ProfileStore(_dir);
        var loaded = store.Load("pad");
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.L2);
        Assert.NotNull(loaded.LeftStick);
        Assert.NotNull(loaded.RightStick.OuterRadii);
        var c = new ControllerCalibration("pad", ControllerModel.DualShock4);
        Assert.Null(Record.Exception(() => c.Import(loaded)));
    }

    [Fact]
    public void Load_of_json_null_returns_null()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "pad.json"), "null");
        Assert.Null(new ProfileStore(_dir).Load("pad"));
    }
}
