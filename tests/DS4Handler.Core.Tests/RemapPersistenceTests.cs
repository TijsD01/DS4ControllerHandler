using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;
using DS4Handler.Core.Mapping;
using DS4Handler.Core.Profiles;
using DS4Handler.Core.Tuning;

namespace DS4Handler.Core.Tests;

public sealed class RemapPersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ds4handler-remap-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void RoundTrip_PreservesRemap()
    {
        var remap = ButtonRemap.Identity
            .WithSwapSticks(true)
            .With(PadControl.L1, RemapTarget.ToPad(PadControl.Cross))
            .With(PadControl.Square, RemapTarget.ToKey(57))
            .With(PadControl.PS, RemapTarget.Disabled);

        var store = new ProfileStore(_dir);
        store.Save(new CalibrationProfile
        {
            ControllerId = "ds4-a4538512abcd",
            Tuning = new InputTuning { Remap = remap },
        });

        var loaded = store.Load("ds4-a4538512abcd");
        Assert.NotNull(loaded);
        Assert.Equal(remap, loaded!.Tuning.Remap);
    }

    private const string OldProfileJson = """
        {
          "Version": 1,
          "ControllerId": "ds4-a4538512abcd",
          "Model": "DualShock4",
          "LeftStick": { "CenterX": 0.05, "CenterY": 0, "OuterRadii": [] },
          "RightStick": { "CenterX": 0, "CenterY": 0, "OuterRadii": [] },
          "L2": { "Rest": 0, "EndStop": 255 },
          "R2": { "Rest": 0, "EndStop": 200, "EndStopLearned": true },
          "Tuning": {
            "LeftStick": { "Shape": "Radial" },
            "OutputRateHz": 500
          }
        }
        """;

    [Fact]
    public void OldProfile_WithoutRemap_LoadsIdentity()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, ProfileStore.SanitizeId("ds4-a4538512abcd") + ".json"), OldProfileJson);
        var loaded = new ProfileStore(_dir).Load("ds4-a4538512abcd");
        Assert.NotNull(loaded);
        Assert.Equal(ButtonRemap.Identity, loaded!.Tuning.Remap);
        Assert.True(loaded.Tuning.Remap.IsIdentity);
    }

    [Fact]
    public void GarbageRemap_IgnoredEntriesKeepRest()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, ProfileStore.SanitizeId("ds4-a4538512abcd") + ".json");
        var store = new ProfileStore(_dir);

        const string badEntriesJson = """
            {
              "Version": 1,
              "ControllerId": "ds4-a4538512abcd",
              "Model": "DualShock4",
              "LeftStick": { "CenterX": 0, "CenterY": 0, "OuterRadii": [] },
              "RightStick": { "CenterX": 0, "CenterY": 0, "OuterRadii": [] },
              "L2": { "Rest": 0, "EndStop": 255 },
              "R2": { "Rest": 0, "EndStop": 255 },
              "Tuning": {
                "OutputRateHz": 500,
                "Remap": {
                  "Map": {
                    "Bogus": { "Pad": "Cross" },
                    "L1": { "Pad": "Mute" },
                    "R1": { "Key": 70000 },
                    "Cross": { "Pad": "Circle" }
                  }
                }
              }
            }
            """;
        File.WriteAllText(path, badEntriesJson);
        var loaded = store.Load("ds4-a4538512abcd");
        Assert.NotNull(loaded);
        Assert.Equal(500, loaded!.Tuning.OutputRateHz);
        var expected = ButtonRemap.Identity.With(PadControl.Cross, RemapTarget.ToPad(PadControl.Circle));
        Assert.Equal(expected, loaded.Tuning.Remap);

        const string numberRemapJson = """
            {
              "Version": 1,
              "ControllerId": "ds4-a4538512abcd",
              "Model": "DualShock4",
              "LeftStick": { "CenterX": 0, "CenterY": 0, "OuterRadii": [] },
              "RightStick": { "CenterX": 0, "CenterY": 0, "OuterRadii": [] },
              "L2": { "Rest": 0, "EndStop": 255 },
              "R2": { "Rest": 0, "EndStop": 255 },
              "Tuning": {
                "OutputRateHz": 500,
                "Remap": 5
              }
            }
            """;
        File.WriteAllText(path, numberRemapJson);
        var loadedNumber = store.Load("ds4-a4538512abcd");
        Assert.NotNull(loadedNumber);
        Assert.Equal(500, loadedNumber!.Tuning.OutputRateHz);
        Assert.Equal(ButtonRemap.Identity, loadedNumber.Tuning.Remap);
    }

    [Theory]
    [InlineData(256, false)]
    [InlineData(57344, false)]
    [InlineData(-1, false)]
    [InlineData(57, true)]
    [InlineData(57437, true)]
    public void KeyScanCode_OnlySendableShapesLoad(int key, bool loads)
    {
        Directory.CreateDirectory(_dir);
        var json = $$"""
            {
              "Version": 1,
              "ControllerId": "ds4-a4538512abcd",
              "Model": "DualShock4",
              "LeftStick": { "CenterX": 0, "CenterY": 0, "OuterRadii": [] },
              "RightStick": { "CenterX": 0, "CenterY": 0, "OuterRadii": [] },
              "L2": { "Rest": 0, "EndStop": 255 },
              "R2": { "Rest": 0, "EndStop": 255 },
              "Tuning": { "OutputRateHz": 500, "Remap": { "Map": { "Square": { "Key": {{key}} } } } }
            }
            """;
        File.WriteAllText(Path.Combine(_dir, ProfileStore.SanitizeId("ds4-a4538512abcd") + ".json"), json);
        var loaded = new ProfileStore(_dir).Load("ds4-a4538512abcd");
        Assert.NotNull(loaded);
        var expected = loads ? RemapTarget.ToKey((ushort)key) : RemapTarget.Same;
        Assert.Equal(expected, loaded!.Tuning.Remap[PadControl.Square]);
    }

    [Fact]
    public void Transfer_CarriesRemap()
    {
        var remap = ButtonRemap.Identity
            .WithSwapSticks(true)
            .With(PadControl.L1, RemapTarget.ToPad(PadControl.Cross))
            .With(PadControl.Square, RemapTarget.ToKey(57));

        var p = new CalibrationProfile
        {
            ControllerId = "ds4-a4538512abcd",
            Model = ControllerModel.DualShock4,
            Tuning = new InputTuning { Remap = remap },
        };

        var exported = ProfileTransfer.Serialize(p, "Exported");
        var imported = ProfileTransfer.Deserialize(exported);
        Assert.Equal(remap, imported.Profile.Tuning.Remap);

        var copy = ProfileTransfer.CopyFor(p, "dualsense-0011223344ff", ControllerModel.DualSense);
        Assert.Equal(remap, copy.Tuning.Remap);
    }

    [Fact]
    public void Sanitize_NullRemap_IsIdentity()
    {
        var tuning = new InputTuning { Remap = null! };
        var sanitized = InputShaper.Sanitize(tuning);
        Assert.Equal(ButtonRemap.Identity, sanitized.Remap);
        Assert.Same(ButtonRemap.Identity, sanitized.Remap);

        var custom = ButtonRemap.Identity.WithSwapSticks(true);
        var kept = InputShaper.Sanitize(new InputTuning { Remap = custom, BluetoothPollIntervalMs = 0 });
        Assert.Equal(custom, kept.Remap);
    }
}
