using DS4Handler.Core.Calibration;

namespace DS4Handler.Core.Tests;

public class TriggerCalibratorTests
{
    /// <summary>
    /// Simulates one press: ramp up, hold, ramp down, rest. Values are raw counts. <paramref name="stop"/>
    /// is the physical end stop: pushing past it pins the value there (a worn trigger pressed fully).
    /// </summary>
    private static void Press(TriggerCalibrator c, Random rng, int target, int holdSamples = 250, int jitter = 1, int rest = 0, int stop = 255)
    {
        int top = Math.Min(target, stop);
        for (int i = 0; i <= 20; i++) c.Process((byte)(rest + (top - rest) * i / 20));
        for (int i = 0; i < holdSamples; i++) c.Process(Clamp(Math.Min(stop, target + rng.Next(-jitter, jitter + 1))));
        for (int i = 20; i >= 0; i--) c.Process((byte)(rest + (top - rest) * i / 20));
        for (int i = 0; i < 60; i++) c.Process(Clamp(rest + rng.Next(0, 2)));
    }

    /// <summary>A worn trigger pressed all the way: it pins at <paramref name="stop"/>.</summary>
    private static void FullPress(TriggerCalibrator c, Random rng, int stop, int holdSamples = 250, int rest = 0)
        => Press(c, rng, 255, holdSamples, jitter: 2, rest: rest, stop: stop);

    /// <summary>A human holding a partial press: physiological tremor (~10 Hz) plus slow drift plus noise.</summary>
    private static void HumanHold(TriggerCalibrator c, Random rng, double level, int holdSamples)
    {
        for (int i = 0; i <= 20; i++) c.Process(Clamp((int)(level * i / 20)));
        double phase = rng.NextDouble() * 2 * Math.PI;
        for (int i = 0; i < holdSamples; i++)
        {
            double v = level + 2.5 * Math.Sin(2 * Math.PI * i / 25 + phase) + 4 * Math.Sin(2 * Math.PI * i / 500) + rng.Next(-1, 2);
            c.Process(Clamp((int)Math.Round(v)));
        }
        for (int i = 20; i >= 0; i--) c.Process(Clamp((int)(level * i / 20)));
        for (int i = 0; i < 60; i++) c.Process(0);
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

        for (int i = 0; i < 4; i++) FullPress(c, rng, stop: 200);
        Assert.False(c.EndStopLearned); // not on four presses yet
        FullPress(c, rng, stop: 200);

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
        Assert.True(c.EndStop >= 160f);
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
        for (int i = 0; i < 10; i++) FullPress(c, rng, stop: 90);
        Assert.Equal(160f, c.EndStop);
    }

    [Fact]
    public void Trigger_that_wears_further_is_followed_down_with_enough_evidence()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(5);
        for (int i = 0; i < 5; i++) FullPress(c, rng, stop: 200);
        Assert.InRange(c.EndStop, 195f, 201f);

        for (int i = 0; i < 7; i++) FullPress(c, rng, stop: 190);
        Assert.InRange(c.EndStop, 195f, 201f); // not enough evidence yet

        for (int i = 0; i < 16; i++) FullPress(c, rng, stop: 190);
        Assert.InRange(c.EndStop, 185f, 191f);
        for (int i = 0; i < 24; i++) FullPress(c, rng, stop: 180);
        Assert.InRange(c.EndStop, 175f, 181f);
        Assert.Equal(1f, c.Map(180));
    }

    [Fact]
    public void Sudden_big_drop_is_not_learned_automatically_but_guided_calibration_fixes_it()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(15);
        for (int i = 0; i < 5; i++) FullPress(c, rng, stop: 255);
        for (int i = 0; i < 30; i++) FullPress(c, rng, stop: 200);
        Assert.True(c.EndStop >= 250f, $"end stop {c.EndStop}"); // a 55-count jump looks like a steady partial hold

        c.StartGuidedEndStop();
        Assert.True(c.GuidedInProgress);
        for (int i = 0; i < 3; i++) FullPress(c, rng, stop: 200);
        Assert.False(c.GuidedInProgress);
        Assert.Equal(PlateauKind.Guided, c.EndStopKind);
        Assert.Equal(1f, c.Map(200));
    }

    [Fact]
    public void Trigger_that_reaches_further_again_is_accepted_quickly()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(6);
        for (int i = 0; i < 5; i++) FullPress(c, rng, stop: 190);
        Assert.InRange(c.EndStop, 185f, 191f);

        for (int i = 0; i < 3; i++) FullPress(c, rng, stop: 240);
        Assert.InRange(c.EndStop, 235f, 241f);
    }

    [Fact]
    public void Resting_offset_is_learned_and_reads_as_zero()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(7);
        Assert.True(c.Map(12) > 0f);
        for (int i = 0; i < 5; i++) FullPress(c, rng, stop: 220, rest: 12);
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
        for (int i = 0; i < 8; i++) FullPress(c, rng, stop: 200);
        Assert.False(c.EndStopLearned);
        Assert.Equal(255f, c.EndStop);
    }

    [Fact]
    public void Export_import_round_trip_restores_the_mapping()
    {
        var a = new TriggerCalibrator();
        var rng = new Random(9);
        for (int i = 0; i < 6; i++) FullPress(a, rng, stop: 205, rest: 5);

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
        for (int i = 0; i < 5; i++) FullPress(c, rng, stop: 200);
        Assert.True(c.EndStopLearned);
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
        for (int i = 0; i < 5; i++) FullPress(c, rng, stop: 200, rest: 8);
        float prev = -1;
        for (int v = 0; v <= 255; v++)
        {
            float o = c.Map(v);
            Assert.True(o >= prev);
            prev = o;
        }
    }

    // ------------------------------------------------------------ regressions from code review

    [Fact]
    public void Light_steady_hold_is_not_learned_as_rest()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(20);
        for (int i = 0; i < 500; i++) c.Process(0);
        float last = 0;
        for (int i = 0; i < 5 * 250; i++) last = c.Process((byte)(35 + rng.Next(-1, 2)));
        Assert.InRange(last, 0.12f, 0.15f);
        Assert.True(c.Rest < 2f, $"rest crept to {c.Rest}");
    }

    [Fact]
    public void Very_light_hold_right_above_rest_only_creeps_slowly()
    {
        var c = new TriggerCalibrator();
        for (int i = 0; i < 500; i++) c.Process(0);
        for (int i = 0; i < 250; i++) c.Process(3); // within the rise window: 1 s at ~1 count/s
        Assert.InRange(c.Rest, 0.5f, 1.5f);
    }

    [Fact]
    public void Steady_partial_holds_with_human_drift_are_not_learned_as_end_stop()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(21);
        for (int i = 0; i < 10; i++) HumanHold(c, rng, 150, holdSamples: 4 * 250);
        Assert.False(c.EndStopLearned);
        Assert.InRange(c.Map(150), 0.55f, 0.62f);
    }

    [Fact]
    public void Three_perfectly_still_partial_holds_are_not_enough()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(22);
        for (int i = 0; i < 3; i++) Press(c, rng, 150, holdSamples: 500, jitter: 0);
        Assert.False(c.EndStopLearned);
        Assert.True(c.Map(150) < 0.65f);
    }

    [Fact]
    public void Wobbly_partial_holds_never_lower_an_end_stop_confirmed_by_pinned_presses()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(23);
        for (int i = 0; i < 5; i++) Press(c, rng, 255, jitter: 0);
        Assert.True(c.EndStopLearned);
        Assert.Equal(PlateauKind.Pinned, c.EndStopKind);
        for (int i = 0; i < 30; i++) Press(c, rng, 178, holdSamples: 1000, jitter: 1);
        Assert.True(c.EndStop >= 250f, $"end stop dropped to {c.EndStop}");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(185)]
    public void Worn_trigger_learns_within_ten_long_full_presses(int stop)
    {
        var c = new TriggerCalibrator();
        var rng = new Random(stop);
        int presses = 0;
        while (c.Map(stop) < 1f && presses < 20)
        {
            FullPress(c, rng, stop, holdSamples: 5 * 250);
            presses++;
        }
        Assert.InRange(presses, 1, 10);
        Assert.Equal(1f, c.Map(stop - 1));
    }

    [Fact]
    public void Import_sanitizes_corrupt_values()
    {
        var c = new TriggerCalibrator();
        c.Import(new TriggerCalibrationData
        {
            Rest = float.NaN,
            EndStop = float.PositiveInfinity,
            EndStopLearned = true,
            PressCount = -7,
            RecentPeaks = new[] { float.NaN, 999f, -5f, 200f },
        });
        Assert.Equal(0f, c.Rest);
        Assert.Equal(255f, c.EndStop);
        Assert.False(c.EndStopLearned);
        Assert.Equal(0, c.PressCount);
        Assert.All(c.Export().RecentPeaks, p => Assert.InRange(p, 0f, 255f));
        Assert.Equal(3, c.Export().RecentPeaks.Length);
        for (int v = 0; v <= 255; v++) Assert.True(float.IsFinite(c.Map(v)));

        c.Import(new TriggerCalibrationData { RecentPeaks = null!, RecentPeakKinds = null!, EndStop = float.NaN });
        Assert.Empty(c.Export().RecentPeaks);
        Assert.Equal(255f, c.EndStop);

        c.Import(null);
        Assert.Equal(255f, c.EndStop);
    }

    // ------------------------------------------------------------ round 2: impact, rest, guided

    [Fact]
    public void Slowly_eased_into_partial_holds_are_not_learned_even_when_pinned()
    {
        var c = new TriggerCalibrator();
        for (int p = 0; p < 12; p++)
        {
            for (int v = 0; v <= 180; v++) c.Process((byte)v);           // ease in at 1 count per sample
            for (int i = 0; i < 1000; i++) c.Process(180);                // perfectly still
            for (int v = 180; v >= 0; v -= 10) c.Process((byte)v);
            for (int i = 0; i < 60; i++) c.Process(0);
        }
        Assert.False(c.EndStopLearned);
    }

    [Fact]
    public void First_learning_never_goes_below_the_highest_plateau_ever_seen()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(30);
        // One long full press on the healthy trigger, then many pinned presses at 180 (e.g. a game
        // whose trigger effect blocks travel). The one press proves the trigger goes further.
        Press(c, rng, 255, holdSamples: 500, jitter: 0);
        for (int i = 0; i < 20; i++) FullPress(c, rng, stop: 180);
        Assert.True(c.HighestPlateau >= 250f);
        Assert.False(c.EndStopLearned && c.EndStop < 250f, $"end stop {c.EndStop}");

        var d = new TriggerCalibrator();
        d.Import(c.Export());
        Assert.Equal(c.HighestPlateau, d.HighestPlateau);
    }

    [Fact]
    public void Rest_jumps_to_where_a_sticky_trigger_settles_after_release()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(31);
        for (int i = 0; i < 500; i++) c.Process(0);
        FullPress(c, rng, stop: 220);
        // Next press: it now comes back only to 15 and sits there for 4 s.
        for (int v = 0; v <= 220; v += 20) c.Process((byte)v);
        for (int i = 0; i < 250; i++) c.Process(220);
        for (int v = 220; v >= 15; v -= 15) c.Process((byte)v);
        for (int i = 0; i < 1000; i++) c.Process((byte)(15 + rng.Next(0, 2)));
        Assert.InRange(c.Rest, 14f, 17f);
        Assert.Equal(0f, c.Map(16));
    }

    [Fact]
    public void Slow_squeeze_after_a_release_is_not_learned_as_rest()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(32);
        for (int i = 0; i < 500; i++) c.Process(0);
        FullPress(c, rng, stop: 220);
        for (int i = 0; i < 300; i++) c.Process(0);
        for (int i = 0; i < 1000; i++) c.Process((byte)(i * 35 / 1000));  // squeeze up to 35 over 4 s
        for (int i = 0; i < 1000; i++) c.Process((byte)(35 + rng.Next(-1, 2)));
        Assert.True(c.Rest < 2f, $"rest {c.Rest}");
    }

    [Fact]
    public void Guided_calibration_learns_a_noisy_stop_in_three_presses_and_is_never_lowered()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(33);
        c.StartGuidedEndStop();
        Assert.Equal(3, c.GuidedPressesRemaining);
        Press(c, rng, 196, holdSamples: 100, jitter: 3);
        Assert.Equal(2, c.GuidedPressesRemaining);
        Press(c, rng, 196, holdSamples: 100, jitter: 3);
        Press(c, rng, 196, holdSamples: 100, jitter: 3);
        Assert.False(c.GuidedInProgress);
        Assert.True(c.EndStopLearned);
        Assert.Equal(PlateauKind.Guided, c.EndStopKind);
        Assert.True(c.Map(196) >= 0.99f, $"end stop {c.EndStop}");

        // Automatic learning never lowers it ...
        for (int i = 0; i < 30; i++) FullPress(c, rng, stop: 186);
        Assert.True(c.EndStop >= 190f, $"guided end stop lowered to {c.EndStop}");
        // ... but raises it when the trigger reaches further.
        for (int i = 0; i < 3; i++) FullPress(c, rng, stop: 230);
        Assert.InRange(c.EndStop, 225f, 231f);
        Assert.Equal(PlateauKind.Guided, c.EndStopKind);

        // Persisted.
        var d = new TriggerCalibrator();
        d.Import(c.Export());
        Assert.Equal(PlateauKind.Guided, d.EndStopKind);
        Assert.Equal(c.EndStop, d.EndStop);
    }

    [Fact]
    public void Guided_calibration_respects_the_floor_works_with_learning_off_and_can_be_cancelled()
    {
        var c = new TriggerCalibrator { LearningEnabled = false };
        var rng = new Random(34);
        c.StartGuidedEndStop();
        for (int i = 0; i < 3; i++) FullPress(c, rng, stop: 120);
        Assert.Equal(160f, c.EndStop);
        Assert.Equal(PlateauKind.Guided, c.EndStopKind);

        var d = new TriggerCalibrator();
        d.StartGuidedEndStop();
        FullPress(d, rng, stop: 200);
        d.CancelGuided();
        Assert.False(d.GuidedInProgress);
        Assert.Equal(0, d.GuidedPressesRemaining);
        FullPress(d, rng, stop: 200);
        FullPress(d, rng, stop: 200);
        Assert.False(d.EndStopLearned);
        Assert.Equal(255f, d.EndStop);
    }

    [Fact]
    public void Guided_quick_taps_do_not_count()
    {
        var c = new TriggerCalibrator();
        c.StartGuidedEndStop();
        for (int k = 0; k < 5; k++)
        {
            for (int v = 0; v <= 200; v += 40) c.Process((byte)v);
            for (int v = 200; v >= 0; v -= 40) c.Process((byte)v);
            for (int i = 0; i < 20; i++) c.Process(0);
        }
        Assert.Equal(3, c.GuidedPressesRemaining);
    }
}
