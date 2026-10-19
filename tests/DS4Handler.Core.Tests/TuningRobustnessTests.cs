using DS4Handler.Core.Input;
using DS4Handler.Core.Output;
using DS4Handler.Core.Profiles;
using DS4Handler.Core.Tuning;

namespace DS4Handler.Core.Tests;

/// <summary>Bad hand-edited tuning never costs the learned calibration; unreadable files are never mistaken for missing ones.</summary>
public sealed class TuningRobustnessTests : IDisposable
{
    private const string Id = "ds4-a4538512abcd";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ds4handler-robust-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string PathOf() => Path.Combine(_dir, ProfileStore.SanitizeId(Id) + ".json");

    private static string ProfileJson(string tuning) => $$"""
        {
          "Version": 1,
          "ControllerId": "{{Id}}",
          "Model": "DualShock4",
          "LeftStick": { "CenterX": 0.05, "CenterY": -0.02, "OuterRadii": [0.9, 1.0] },
          "RightStick": { "CenterX": 0, "CenterY": 0, "OuterRadii": [] },
          "L2": { "Rest": 0, "EndStop": 255 },
          "R2": { "Rest": 0, "EndStop": 200, "EndStopLearned": true, "PressCount": 12 },
          "Tuning": {{tuning}}
        }
        """;

    private CalibrationProfile LoadWith(string tuning)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathOf(), ProfileJson(tuning));
        var store = new ProfileStore(_dir);
        var p = store.LoadStrict(Id);
        Assert.NotNull(p);
        Assert.Equal(p!.Tuning, store.Load(Id)!.Tuning); // the tolerant Load agrees
        // The calibration survives.
        Assert.Equal(200f, p.R2.EndStop);
        Assert.True(p.R2.EndStopLearned);
        Assert.Equal(12, p.R2.PressCount);
        Assert.Equal(0.05f, p.LeftStick.CenterX);
        return p;
    }

    [Theory]
    [InlineData("\"Hexagon\"")]
    [InlineData("null")]
    [InlineData("\"1\"")]
    [InlineData("\"Square,Axial\"")]
    [InlineData("7")]
    [InlineData("true")]
    [InlineData("{}")]
    public void BadShape_FallsBackToRadial_KeepsTheRest(string shape)
    {
        var p = LoadWith($$"""{ "LeftStick": { "Shape": {{shape}}, "InnerDeadzone": 0.2 } }""");
        Assert.Equal(DeadzoneShape.Radial, p.Tuning.LeftStick.Shape);
        Assert.Equal(0.2f, p.Tuning.LeftStick.InnerDeadzone);
    }

    [Theory]
    [InlineData("\"Square\"", DeadzoneShape.Square)]
    [InlineData("\"axial\"", DeadzoneShape.Axial)]
    [InlineData("1", DeadzoneShape.Square)]
    public void GoodShapeSpellings_AreRead(string shape, DeadzoneShape expected)
    {
        Assert.Equal(expected, LoadWith($$"""{ "LeftStick": { "Shape": {{shape}} } }""").Tuning.LeftStick.Shape);
    }

    [Theory]
    [InlineData("\"NaN\"")]
    [InlineData("null")]
    [InlineData("\"2\"")]
    [InlineData("[]")]
    [InlineData("1e400")]
    public void BadCurve_FallsBackToDefault(string curve)
    {
        var p = LoadWith($$"""{ "RightStick": { "Curve": {{curve}}, "Sensitivity": 1.5 }, "L2": { "Curve": {{curve}} } }""");
        Assert.Equal(1f, p.Tuning.RightStick.Curve);
        Assert.Equal(1.5f, p.Tuning.RightStick.Sensitivity);
        Assert.Equal(1f, p.Tuning.L2.Curve);
    }

    [Theory]
    [InlineData("4.5")]
    [InlineData("99999999999")]
    [InlineData("null")]
    [InlineData("\"4\"")]
    public void BadPollInterval_FallsBackToDefault(string value)
    {
        var p = LoadWith($$"""{ "BluetoothPollIntervalMs": {{value}}, "OutputRateHz": 250 }""");
        Assert.Equal(1, p.Tuning.BluetoothPollIntervalMs);
        Assert.Equal(250, p.Tuning.OutputRateHz);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("12.5")]
    [InlineData("\"fast\"")]
    public void BadOutputRate_FallsBackToDefault(string value)
    {
        var p = LoadWith($$"""{ "OutputRateHz": {{value}}, "BluetoothPollIntervalMs": 4 }""");
        Assert.Equal(0, p.Tuning.OutputRateHz);
        Assert.Equal(4, p.Tuning.BluetoothPollIntervalMs);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("\"true\"")]
    [InlineData("null")]
    public void BadBool_ReadsAsFalse(string value)
    {
        var p = LoadWith($$"""{ "LeftStick": { "SquareOutput": {{value}}, "InvertX": true } }""");
        Assert.False(p.Tuning.LeftStick.SquareOutput);
        Assert.True(p.Tuning.LeftStick.InvertX);
    }

    [Theory]
    [InlineData("\"tuning\"")]
    [InlineData("[1, 2]")]
    [InlineData("42")]
    [InlineData("{ \"LeftStick\": \"x\", \"L2\": [], \"R2\": null }")]
    public void BadSections_FallBackToDefault(string tuning)
    {
        Assert.Equal(InputTuning.Default, LoadWith(tuning).Tuning);
    }

    [Fact]
    public void ToleratedTuning_IsWrittenBackClean()
    {
        var store = new ProfileStore(_dir);
        var p = LoadWith("""{ "leftstick": { "shape": "Hexagon", "innerdeadzone": 0.2, "Unknown": 5 }, "OutputRateHz": null }""");
        store.Save(p);
        var again = store.LoadStrict(Id)!;
        Assert.Equal(p.Tuning, again.Tuning);
        Assert.Equal(0.2f, again.Tuning.LeftStick.InnerDeadzone);
        Assert.Contains("\"Radial\"", File.ReadAllText(PathOf()));
    }

    [Fact]
    public void Transfer_WithBadTuning_StillImportsTheCalibration()
    {
        var imported = ProfileTransfer.Deserialize(ProfileJson("""{ "LeftStick": { "Shape": "Hexagon", "Curve": "NaN" }, "OutputRateHz": null }"""));
        Assert.Equal(200f, imported.Profile.R2.EndStop);
        Assert.Equal(new StickTuning(), imported.Profile.Tuning.LeftStick);
    }

    // ---- unreadable files ----

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ \"ControllerId\": 5 }")]
    public void LoadStrict_UnreadableFile_Throws_WhileLoadReturnsNull(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathOf(), content);
        var store = new ProfileStore(_dir);
        Assert.Null(store.Load(Id));
        Assert.Throws<InvalidDataException>(() => store.LoadStrict(Id));
        Assert.True(store.Exists(Id));
        Assert.Empty(store.LoadAll()); // other callers keep tolerating bad files
        Assert.Equal(content, File.ReadAllText(PathOf())); // nothing touched it
    }

    [Fact]
    public void LoadStrict_MissingFile_ReturnsNull()
    {
        var store = new ProfileStore(_dir);
        Assert.Null(store.LoadStrict(Id));
        Assert.False(store.Exists(Id));
    }

    // ---- output rate limit: digital edges ----

    [Fact]
    public void XboxEdges_ButtonsAndTriggerZeroCrossings()
    {
        var a = new XboxReport(0, 0, 0, 0, 0, 0, XboxButtons.None);
        Assert.False(ReportEdges.HasDigitalEdge(a, a with { LeftThumbX = 1000, RightThumbY = -5 }));
        Assert.True(ReportEdges.HasDigitalEdge(a, a with { Buttons = XboxButtons.A }));
        Assert.True(ReportEdges.HasDigitalEdge(a, a with { LeftTrigger = 1 }));
        Assert.True(ReportEdges.HasDigitalEdge(a with { RightTrigger = 9 }, a));
        Assert.False(ReportEdges.HasDigitalEdge(a with { RightTrigger = 9 }, a with { RightTrigger = 200 }));
    }

    [Fact]
    public void Ds4Edges_ButtonsDpadSpecialAndTriggers()
    {
        var a = new Ds4OutputReport(128, 128, 128, 128, 0, 0, Ds4OutputButtons.None, Ds4DPad.None, Ds4SpecialButtons.None);
        Assert.False(ReportEdges.HasDigitalEdge(a, a with { LeftX = 200, L2 = 0 }));
        Assert.True(ReportEdges.HasDigitalEdge(a, a with { Buttons = Ds4OutputButtons.Cross }));
        Assert.True(ReportEdges.HasDigitalEdge(a, a with { DPad = Ds4DPad.North }));
        Assert.True(ReportEdges.HasDigitalEdge(a, a with { Special = Ds4SpecialButtons.Ps }));
        Assert.True(ReportEdges.HasDigitalEdge(a, a with { R2 = 3 }));
        Assert.False(ReportEdges.HasDigitalEdge(a with { L2 = 50 }, a with { L2 = 51 }));
    }

    [Fact]
    public void Ds4Edges_SeeTheDigitalTriggerBit()
    {
        // The DS4 digital L2 bit follows the analog value past Ds4Mapper.DigitalTriggerThreshold.
        var released = Ds4Mapper.Map(new CalibratedState(0, 0, 0, 0, 0.05f, 0, Buttons.None));
        var pressed = Ds4Mapper.Map(new CalibratedState(0, 0, 0, 0, 0.2f, 0, Buttons.None));
        Assert.NotEqual(released.Buttons, pressed.Buttons);
        Assert.True(ReportEdges.HasDigitalEdge(released, pressed));
    }
}
