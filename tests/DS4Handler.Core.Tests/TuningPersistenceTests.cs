using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;
using DS4Handler.Core.Profiles;
using DS4Handler.Core.Tuning;

namespace DS4Handler.Core.Tests;

public sealed class TuningPersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ds4handler-tuning-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static InputTuning Custom() => new()
    {
        LeftStick = new StickTuning { Shape = DeadzoneShape.Square, InnerDeadzone = 0.12f, Curve = 1.5f, InvertY = true },
        RightStick = new StickTuning { Shape = DeadzoneShape.Axial, OuterDeadzone = 0.1f, AntiDeadzone = 0.2f, Sensitivity = 1.4f, SquareOutput = true },
        L2 = new TriggerTuning { InnerDeadzone = 0.05f, Curve = 0.7f },
        R2 = new TriggerTuning { OuterDeadzone = 0.15f, AntiDeadzone = 0.1f },
        BluetoothPollIntervalMs = 4,
        OutputRateHz = 500,
    };

    private static RawControllerState Raw(byte lx, byte ly, byte r2 = 0) => new(lx, ly, 128, 128, 0, r2, Buttons.None);

    // ---- ControllerCalibration ----

    [Fact]
    public void Process_AppliesTuningAfterCalibration()
    {
        var plain = new ControllerCalibration("pad", ControllerModel.DualShock4) { LearningEnabled = false };
        var tuned = new ControllerCalibration("pad", ControllerModel.DualShock4)
        {
            LearningEnabled = false,
            Tuning = new InputTuning { LeftStick = new StickTuning { InvertX = true }, R2 = new TriggerTuning { InnerDeadzone = 0.5f } },
        };
        var raw = Raw(230, 128, r2: 100);
        var a = plain.Process(raw);
        var b = tuned.Process(raw, 0.004);
        Assert.True(a.LeftX > 0.5f);
        Assert.Equal(-a.LeftX, b.LeftX);
        Assert.True(a.R2 > 0f);
        Assert.Equal(0f, b.R2);
    }

    [Fact]
    public void DefaultTuning_LeavesCalibratedOutputUnchanged()
    {
        var a = new ControllerCalibration("pad", ControllerModel.DualShock4);
        var b = new ControllerCalibration("pad", ControllerModel.DualShock4) { Tuning = new InputTuning() };
        for (int v = 0; v <= 255; v += 3)
            Assert.Equal(a.Process(Raw((byte)v, (byte)(255 - v), (byte)v)), b.Process(Raw((byte)v, (byte)(255 - v), (byte)v)));
    }

    [Fact]
    public void TuningSetter_Sanitizes()
    {
        var c = new ControllerCalibration("pad", ControllerModel.DualShock4)
        {
            Tuning = new InputTuning { OutputRateHz = 333, LeftStick = new StickTuning { Curve = float.NaN } },
        };
        Assert.Equal(250, c.Tuning.OutputRateHz);
        Assert.Equal(1f, c.Tuning.LeftStick.Curve);
        c.Tuning = null!;
        Assert.Equal(InputTuning.Default, c.Tuning);
    }

    [Fact]
    public void Reset_KeepsTuning()
    {
        var c = new ControllerCalibration("pad", ControllerModel.DualShock4) { Tuning = Custom() };
        for (int i = 0; i < 300; i++) c.Process(Raw(140, 120));
        c.Reset();
        Assert.Equal(Custom(), c.Tuning);
    }

    [Fact]
    public void ExportImport_RoundTripsTuning()
    {
        var c = new ControllerCalibration("pad", ControllerModel.DualShock4) { Tuning = Custom() };
        var p = c.Export();
        Assert.Equal(Custom(), p.Tuning);

        var d = new ControllerCalibration("pad", ControllerModel.DualShock4);
        d.Import(p);
        Assert.Equal(Custom(), d.Tuning);
    }

    [Fact]
    public void Import_NullOrInvalidTuning_IsSanitized()
    {
        var d = new ControllerCalibration("pad", ControllerModel.DualShock4) { Tuning = Custom() };
        d.Import(new CalibrationProfile { ControllerId = "pad", Tuning = null! });
        Assert.Equal(InputTuning.Default, d.Tuning);

        d.Import(new CalibrationProfile { ControllerId = "pad", Tuning = new InputTuning { BluetoothPollIntervalMs = 0 } });
        Assert.Equal(1, d.Tuning.BluetoothPollIntervalMs);
    }

    // ---- ProfileStore ----

    [Fact]
    public void ProfileStore_RoundTripsTuning()
    {
        var store = new ProfileStore(_dir);
        store.Save(new CalibrationProfile { ControllerId = "ds4-a4538512abcd", Tuning = Custom() });
        var json = File.ReadAllText(Assert.Single(Directory.GetFiles(_dir)));
        Assert.Contains("\"Square\"", json); // enums as strings, like the rest of the file
        Assert.Equal(Custom(), store.Load("ds4-a4538512abcd")!.Tuning);
    }

    private const string OldProfileJson = """
        {
          "Version": 1,
          "ControllerId": "ds4-a4538512abcd",
          "Model": "DualShock4",
          "LeftStick": { "CenterX": 0.05, "CenterY": 0, "OuterRadii": [] },
          "RightStick": { "CenterX": 0, "CenterY": 0, "OuterRadii": [] },
          "L2": { "Rest": 0, "EndStop": 255 },
          "R2": { "Rest": 0, "EndStop": 200, "EndStopLearned": true }
        }
        """;

    [Fact]
    public void ProfileStore_OldFileWithoutTuning_LoadsWithDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, ProfileStore.SanitizeId("ds4-a4538512abcd") + ".json"), OldProfileJson);
        var p = new ProfileStore(_dir).Load("ds4-a4538512abcd");
        Assert.NotNull(p);
        Assert.Equal(200f, p!.R2.EndStop);
        Assert.Equal(InputTuning.Default, p.Tuning);
    }

    [Fact]
    public void ProfileStore_NullOrPartialTuning_LoadsSanitized()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, ProfileStore.SanitizeId("ds4-a4538512abcd") + ".json");
        var store = new ProfileStore(_dir);

        File.WriteAllText(path, OldProfileJson.Replace("\"Version\": 1,", "\"Version\": 1, \"Tuning\": null,"));
        Assert.Equal(InputTuning.Default, store.Load("ds4-a4538512abcd")!.Tuning);

        File.WriteAllText(path, OldProfileJson.Replace("\"Version\": 1,",
            "\"Version\": 1, \"Tuning\": { \"LeftStick\": null, \"R2\": { \"InnerDeadzone\": 3 }, \"OutputRateHz\": 999 },"));
        var t = store.Load("ds4-a4538512abcd")!.Tuning;
        Assert.Equal(new StickTuning(), t.LeftStick);
        Assert.Equal(new StickTuning(), t.RightStick);
        Assert.Equal(InputShaper.MaxTriggerInnerDeadzone, t.R2.InnerDeadzone);
        Assert.Equal(1000, t.OutputRateHz);
    }

    // ---- ProfileTransfer ----

    [Fact]
    public void ProfileTransfer_ExportImport_CarriesTuning()
    {
        var p = new CalibrationProfile { ControllerId = "ds4-a4538512abcd", Model = ControllerModel.DualShock4, Tuning = Custom() };
        var imported = ProfileTransfer.Deserialize(ProfileTransfer.Serialize(p, "Vriend"));
        Assert.Equal(Custom(), imported.Profile.Tuning);
    }

    [Fact]
    public void ProfileTransfer_CopyFor_CarriesTuning_WithoutSharing()
    {
        var p = new CalibrationProfile { ControllerId = "ds4-a4538512abcd", Model = ControllerModel.DualShock4, Tuning = Custom() };
        var copy = ProfileTransfer.CopyFor(p, "dualsense-0011223344ff", ControllerModel.DualSense);
        Assert.Equal(Custom(), copy.Tuning);
        Assert.NotSame(p.Tuning, copy.Tuning);
    }

    [Fact]
    public void ProfileTransfer_OldFileWithoutTuning_Imports()
    {
        var imported = ProfileTransfer.Deserialize(OldProfileJson);
        Assert.Equal(InputTuning.Default, imported.Profile.Tuning);
        var nulled = ProfileTransfer.Deserialize(OldProfileJson.Replace("\"Version\": 1,", "\"Version\": 1, \"Tuning\": null,"));
        Assert.Equal(InputTuning.Default, nulled.Profile.Tuning);
    }
}
