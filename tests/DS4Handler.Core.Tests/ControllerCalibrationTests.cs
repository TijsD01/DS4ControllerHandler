using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;
using DS4Handler.Core.Output;
using DS4Handler.Core.Profiles;

namespace DS4Handler.Core.Tests;

public class ControllerCalibrationTests
{
    private const byte DriftX = 134;
    private const byte DriftY = 121;
    private const byte R2Stop = 200;

    private static RawControllerState Raw(byte r2, byte lx = DriftX, byte ly = DriftY, byte l2 = 0) =>
        new(lx, ly, 128, 128, l2, r2, Buttons.None);

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);

    /// <summary>One full R2 press on a worn trigger that stops at raw 200 (+/-1 jitter), drifted left stick all the while.</summary>
    private static void PressR2(ControllerCalibration c, Random rng)
    {
        for (int i = 0; i <= 20; i++) c.Process(Raw((byte)(R2Stop * i / 20)));
        for (int i = 0; i < 250; i++) c.Process(Raw(Clamp(R2Stop + rng.Next(-1, 2))));
        for (int i = 20; i >= 0; i--) c.Process(Raw((byte)(R2Stop * i / 20)));
        for (int i = 0; i < 60; i++) c.Process(Raw(Clamp(rng.Next(0, 2))));
    }

    private static ControllerCalibration Trained(string id = "pad-1")
    {
        var c = new ControllerCalibration(id, ControllerModel.DualShock4);
        var rng = new Random(42);
        for (int i = 0; i < 8; i++) PressR2(c, rng);
        for (int i = 0; i < 600; i++) c.Process(Raw(0));
        return c;
    }

    private static void AssertSameOutput(ControllerCalibration a, ControllerCalibration b)
    {
        for (int v = 0; v <= 255; v += 5)
        {
            var ra = a.Process(Raw((byte)v, (byte)(v / 2 + 60), (byte)(255 - v), (byte)v));
            var rb = b.Process(Raw((byte)v, (byte)(v / 2 + 60), (byte)(255 - v), (byte)v));
            Assert.Equal(XboxMapper.Map(ra), XboxMapper.Map(rb));
        }
    }

    [Fact]
    public void Rocket_league_worn_R2_and_drifted_stick_are_corrected()
    {
        var c = Trained();

        var atStop = c.Process(Raw(R2Stop));
        Assert.Equal(1f, atStop.R2);
        Assert.Equal(255, XboxMapper.Map(atStop).RightTrigger);

        var released = c.Process(Raw(0));
        Assert.Equal(0f, released.R2);

        var rest = c.Process(Raw(0));
        Assert.Equal(0f, rest.LeftX);
        Assert.Equal(0f, rest.LeftY);
        var report = XboxMapper.Map(rest);
        Assert.Equal(0, report.LeftThumbX);
        Assert.Equal(0, report.LeftThumbY);
    }

    [Fact]
    public void Export_then_import_into_new_instance_gives_identical_output()
    {
        var a = Trained();
        var b = new ControllerCalibration("pad-1", ControllerModel.DualShock4);
        b.LearningEnabled = false;
        b.Import(a.Export());
        a.LearningEnabled = false;

        AssertSameOutput(a, b);
        Assert.Equal(255, XboxMapper.Map(b.Process(Raw(R2Stop))).RightTrigger);
    }

    [Fact]
    public void Learning_disabled_stops_all_four_inputs_from_learning()
    {
        var c = new ControllerCalibration("pad-2", ControllerModel.DualShock4) { LearningEnabled = false };
        Assert.False(c.LeftStick.LearningEnabled);
        Assert.False(c.RightStick.LearningEnabled);
        Assert.False(c.L2.LearningEnabled);
        Assert.False(c.R2.LearningEnabled);

        var rng = new Random(7);
        for (int i = 0; i < 6; i++) PressR2(c, rng);

        Assert.False(c.R2.EndStopLearned);
        Assert.Equal(255f, c.R2.EndStop);
        Assert.InRange(c.Process(Raw(R2Stop)).R2, 0.75f, 0.80f);
        Assert.NotEqual(0f, c.Process(Raw(0)).LeftX); // drift is not corrected

        c.LearningEnabled = true;
        Assert.True(c.LeftStick.LearningEnabled && c.RightStick.LearningEnabled && c.L2.LearningEnabled && c.R2.LearningEnabled);
    }

    [Fact]
    public void ProfileStore_round_trip_maps_identically()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ds4h-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var a = Trained("aa:bb:cc");
            var store = new ProfileStore(dir);
            store.Save(a.Export());

            var loaded = store.Load("aa:bb:cc");
            Assert.NotNull(loaded);
            var b = new ControllerCalibration("aa:bb:cc", ControllerModel.DualShock4);
            b.Import(loaded!);

            a.LearningEnabled = false;
            b.LearningEnabled = false;
            AssertSameOutput(a, b);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Import_treats_null_sections_as_defaults()
    {
        var c = Trained();
        var profile = c.Export() with { LeftStick = null!, RightStick = null!, L2 = null!, R2 = null! };
        var ex = Record.Exception(() => c.Import(profile));
        Assert.Null(ex);
        Assert.Equal(255f, c.R2.EndStop);
        Assert.Equal(0f, c.LeftStick.CenterX);
    }

    [Fact]
    public void Import_null_profile_throws_ArgumentNullException()
    {
        var c = new ControllerCalibration("x", ControllerModel.DualShock4);
        Assert.Throws<ArgumentNullException>(() => c.Import(null!));
    }
}
