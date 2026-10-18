using DS4Handler.Core.Calibration;

namespace DS4Handler.Core.Tests;

public class TriggerCalibratorTests
{
    /// <summary>Simulates one press: ramp up, hold, ramp down, rest. Values are raw counts.</summary>
    private static void Press(TriggerCalibrator c, Random rng, int target, int holdSamples = 250, int jitter = 1, int rest = 0)
    {
        for (int i = 0; i <= 20; i++) c.Process((byte)(rest + (target - rest) * i / 20));
        for (int i = 0; i < holdSamples; i++) c.Process(Clamp(target + rng.Next(-jitter, jitter + 1)));
        for (int i = 20; i >= 0; i--) c.Process((byte)(rest + (target - rest) * i / 20));
        for (int i = 0; i < 60; i++) c.Process(Clamp(rest + rng.Next(0, 2)));
    }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);

    [Fact]
    public void Factory_calibration_maps_full_range()
    {
        var c = new TriggerCalibrator();
        Assert.Equal(0f, c.Map(0));
        Assert.Equal(1f, c.Map(255));
        Assert.InRange(c.Map(128), 0.48f, 0.52f);
    }

    [Fact]
    public void Worn_R2_that_stops_at_200_reaches_full_output_after_a_few_presses()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(1);

        // Without learning, full press only gives ~78%: the Rocket League "no full throttle" problem.
        Assert.InRange(c.Map(200), 0.75f, 0.80f);

        for (int i = 0; i < 4; i++) Press(c, rng, target: 200);

        Assert.True(c.EndStopLearned);
        Assert.InRange(c.EndStop, 195f, 201f);
        Assert.Equal(1f, c.Map(200));
        Assert.Equal(1f, c.Map(199));
        Assert.Equal(0f, c.Map(0));
        Assert.InRange(c.Map(100), 0.45f, 0.55f);
    }

    [Fact]
    public void Healthy_trigger_with_partial_presses_keeps_full_range()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(2);
        for (int i = 0; i < 40; i++)
        {
            if (i % 4 == 0) Press(c, rng, 255, jitter: 0);
            else Press(c, rng, rng.Next(60, 240), holdSamples: 80, jitter: 3);
        }

        Assert.True(c.EndStop >= 250f, $"end stop {c.EndStop}");
        Assert.InRange(c.Map(128), 0.45f, 0.55f);
    }

    [Fact]
    public void Only_partial_presses_never_shrink_the_range()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(3);
        for (int i = 0; i < 30; i++) Press(c, rng, rng.Next(60, 230), holdSamples: 80, jitter: 3);

        // Random partial presses rarely agree on an end stop; if they happen to, the floor still applies.
        Assert.True(c.EndStop >= 128f);
        Assert.True(c.Map(100) < 0.85f);
    }

    [Fact]
    public void Quick_taps_do_not_count_as_end_stops()
    {
        var c = new TriggerCalibrator();
        for (int i = 0; i < 20; i++)
        {
            for (int v = 0; v <= 180; v += 30) c.Process((byte)v);
            for (int v = 180; v >= 0; v -= 30) c.Process((byte)v);
            for (int k = 0; k < 20; k++) c.Process(0);
        }
        Assert.False(c.EndStopLearned);
        Assert.Equal(255f, c.EndStop);
    }

    [Fact]
    public void Learned_end_stop_can_never_go_below_the_floor()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(4);
        for (int i = 0; i < 10; i++) Press(c, rng, 90);
        Assert.Equal(128f, c.EndStop);
    }

    [Fact]
    public void Trigger_that_wears_further_is_followed_down_with_enough_evidence()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(5);
        for (int i = 0; i < 5; i++) Press(c, rng, 200);
        Assert.InRange(c.EndStop, 195f, 201f);

        Press(c, rng, 180);
        Press(c, rng, 180);
        Assert.InRange(c.EndStop, 195f, 201f); // not enough evidence yet

        for (int i = 0; i < 16; i++) Press(c, rng, 180);
        Assert.InRange(c.EndStop, 175f, 181f);
        Assert.Equal(1f, c.Map(180));
    }

    [Fact]
    public void Trigger_that_reaches_further_again_is_accepted_quickly()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(6);
        for (int i = 0; i < 4; i++) Press(c, rng, 190);
        Assert.InRange(c.EndStop, 185f, 191f);

        for (int i = 0; i < 3; i++) Press(c, rng, 240);
        Assert.InRange(c.EndStop, 235f, 241f);
    }

    [Fact]
    public void Resting_offset_is_learned_and_reads_as_zero()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(7);
        Assert.True(c.Map(12) > 0f);
        for (int i = 0; i < 5; i++) Press(c, rng, 220, rest: 12);
        for (int i = 0; i < 400; i++) c.Process((byte)(12 + rng.Next(0, 2)));

        Assert.InRange(c.Rest, 10f, 14f);
        Assert.Equal(0f, c.Map(12));
        Assert.Equal(0f, c.Map(13));
        Assert.Equal(1f, c.Map(220));
    }

    [Fact]
    public void Learning_disabled_freezes_the_model()
    {
        var c = new TriggerCalibrator { LearningEnabled = false };
        var rng = new Random(8);
        for (int i = 0; i < 6; i++) Press(c, rng, 200);
        Assert.False(c.EndStopLearned);
        Assert.Equal(255f, c.EndStop);
    }

    [Fact]
    public void Export_import_round_trip_restores_the_mapping()
    {
        var a = new TriggerCalibrator();
        var rng = new Random(9);
        for (int i = 0; i < 4; i++) Press(a, rng, 205, rest: 5);

        var b = new TriggerCalibrator();
        b.Import(a.Export());

        Assert.Equal(a.EndStop, b.EndStop);
        Assert.Equal(a.Rest, b.Rest);
        Assert.Equal(a.EndStopLearned, b.EndStopLearned);
        for (int v = 0; v <= 255; v += 5) Assert.Equal(a.Map(v), b.Map(v));

        // The restored history keeps counting toward later decisions.
        Assert.Equal(a.Export().RecentPeaks, b.Export().RecentPeaks);
    }

    [Fact]
    public void Reset_returns_to_factory()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(10);
        for (int i = 0; i < 4; i++) Press(c, rng, 200);
        c.Reset();
        Assert.False(c.EndStopLearned);
        Assert.Equal(255f, c.EndStop);
        Assert.Equal(0f, c.Rest);
    }

    [Fact]
    public void Output_is_monotonic()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(11);
        for (int i = 0; i < 4; i++) Press(c, rng, 200, rest: 8);
        float prev = -1;
        for (int v = 0; v <= 255; v++)
        {
            float o = c.Map(v);
            Assert.True(o >= prev);
            prev = o;
        }
    }
}
