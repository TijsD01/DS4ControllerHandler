using DS4Handler.Core.Calibration;

namespace DS4Handler.Core.Tests;

/// <summary>
/// Round 2 of adversarial play simulations, aimed at the plateau-flatness trigger logic and the
/// release-based stick center learning. Fixed seeds; 250 samples is about one second.
/// </summary>
public class CalibrationScenarioTests2
{
    private const int Hz = 250;

    private static byte B(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    // ================================================================ triggers

    private sealed class Trig
    {
        public readonly TriggerCalibrator C;
        private readonly Random _rng;
        public int Stop;
        public int Rest;

        public Trig(TriggerCalibrator c, Random rng, int stop = 255, int rest = 0)
        {
            C = c;
            _rng = rng;
            Stop = stop;
            Rest = rest;
        }

        public float Feed(double v) => C.Process(B(Math.Clamp(v, Rest, Stop)));

        public void Ramp(double from, double to, int n)
        {
            for (int i = 0; i <= n; i++) Feed(from + (to - from) * i / n);
        }

        public void Idle(int n) { for (int i = 0; i < n; i++) Feed(Rest); }

        /// <summary>Press to the level (clamped by the stop) and hold with uniform +-jitter, optional linear sag.</summary>
        public void Press(double level, int hold, int jitter = 1, double sag = 0)
        {
            double top = Math.Min(level, Stop);
            Ramp(Rest, top, 15);
            for (int i = 0; i < hold; i++)
            {
                double v = top - sag * i / hold + _rng.Next(-jitter, jitter + 1);
                Feed(Math.Min(v, Stop));
            }
            Ramp(top - sag, Rest, 15);
            Idle(40);
        }

        /// <summary>Hold that only ever shows two adjacent counts (a very steady finger).</summary>
        public void PinnedHold(int level, int hold)
        {
            Ramp(Rest, level, 15);
            for (int i = 0; i < hold; i++) Feed(level + _rng.Next(0, 2));
            Ramp(level, Rest, 15);
            Idle(40);
        }
    }

    private static int PressesUntilFull(Trig t, int maxPresses, Action<Trig> press, int at)
    {
        for (int p = 1; p <= maxPresses; p++)
        {
            press(t);
            if (t.C.Map(at) >= 0.99f) return p;
        }
        return int.MaxValue;
    }

    [Fact]
    public void Worn_R2_with_plus_minus_2_noise_at_the_stop_is_learned_within_a_dozen_presses()
    {
        var t = new Trig(new TriggerCalibrator(), new Random(101), stop: 202);
        // Physical stop wobbles 198..202 while the player is pinning it.
        int n = PressesUntilFull(t, 30, x => x.Press(200, 3 * Hz, jitter: 2), at: 199);
        Assert.True(n <= 12, $"took {n} presses");
        Assert.True(t.C.Map(198) >= 0.99f, $"Map(198) = {t.C.Map(198)}");
    }

    [Fact]
    public void Worn_R2_with_plus_minus_3_noise_at_the_stop_is_learned_eventually()
    {
        var t = new Trig(new TriggerCalibrator(), new Random(102), stop: 203);
        int n = PressesUntilFull(t, 40, x => x.Press(200, 3 * Hz, jitter: 3), at: 199);
        Assert.True(n <= 40, $"never learned in 40 presses, end stop {t.C.EndStop}");
    }

    [Fact]
    public void Worn_R2_that_sags_two_counts_during_every_hold_is_learned()
    {
        var t = new Trig(new TriggerCalibrator(), new Random(103), stop: 202);
        // Player eases off a little during long boosts: 201 -> 199 over 4 s, +-1 jitter.
        int n = PressesUntilFull(t, 20, x => x.Press(201, 4 * Hz, jitter: 1, sag: 2), at: 199);
        Assert.True(n <= 8, $"took {n} presses, end stop {t.C.EndStop}");
    }

    [Fact]
    public void Rocket_League_worn_R2_with_feathering_mixed_in_is_learned_quickly()
    {
        var rng = new Random(104);
        var t = new Trig(new TriggerCalibrator(), rng, stop: 195);
        int full = 0;
        while (full < 30 && t.C.Map(195) < 0.999f)
        {
            t.Press(255, rng.Next(Hz, 6 * Hz), jitter: 1);
            full++;
            for (int k = rng.Next(0, 4); k > 0; k--) t.Press(rng.Next(60, 180), rng.Next(20, 200), jitter: 2);
        }
        Assert.True(full <= 6, $"took {full} full presses, end stop {t.C.EndStop}");
    }

    [Fact]
    public void Trigger_that_wears_gradually_during_a_long_session_keeps_reaching_full()
    {
        var rng = new Random(105);
        var t = new Trig(new TriggerCalibrator(), rng);
        for (int i = 0; i < 6; i++) t.Press(255, Hz, jitter: 0);
        Assert.True(t.C.EndStop >= 250f);

        // 400 full presses while the stop wears from 255 down to 190, with feathers in between.
        int notFull = 0;
        for (int p = 0; p < 400; p++)
        {
            t.Stop = (int)Math.Round(255 - 65.0 * p / 399);
            t.Press(255, Hz, jitter: 1);
            if (t.C.Map(t.Stop) < 0.999f) notFull++;
            if (rng.Next(3) == 0) t.Press(rng.Next(60, 150), 60, jitter: 2);
        }
        // Then it stops wearing.
        for (int p = 0; p < 12; p++) t.Press(255, Hz, jitter: 1);
        Assert.Equal(1f, t.C.Map(190));
        // During wear the output may lag, but most full presses must still read full.
        Assert.True(notFull < 120, $"{notFull}/400 full presses did not reach 100% (end stop {t.C.EndStop})");
    }

    [Fact]
    public void Cruise_control_at_70_percent_on_fresh_healthy_trigger_is_not_learned_as_full()
    {
        var t = new Trig(new TriggerCalibrator(), new Random(106));
        // Start of a race: long steady 70% straights, no full press yet (e.g. pit limiter / fuel save / formation lap).
        for (int i = 0; i < 6; i++) t.Press(178, 4 * Hz, jitter: 1);
        Assert.InRange(t.C.Map(178), 0.6f, 0.8f);
    }

    [Fact]
    public void Very_steady_partial_holds_do_not_lower_a_learned_full_end_stop()
    {
        var t = new Trig(new TriggerCalibrator(), new Random(107));
        for (int i = 0; i < 6; i++) t.Press(255, Hz, jitter: 0);
        Assert.True(t.C.EndStop >= 250f);
        for (int i = 0; i < 20; i++) t.PinnedHold(178, 5 * Hz);
        Assert.True(t.C.EndStop >= 250f, $"end stop dropped to {t.C.EndStop}");
    }

    [Fact]
    public void Steady_partial_holds_mixed_with_full_presses_keep_the_full_range()
    {
        var rng = new Random(108);
        var t = new Trig(new TriggerCalibrator(), rng);
        for (int i = 0; i < 300; i++)
        {
            if (i % 10 == 0) t.Press(255, Hz, jitter: 0);
            else t.PinnedHold(170 + rng.Next(0, 3), 3 * Hz);
        }
        Assert.True(t.C.EndStop >= 250f, $"end stop {t.C.EndStop}");
    }

    [Fact]
    public void Sticky_trigger_resting_at_30_reads_zero_when_released()
    {
        var t = new Trig(new TriggerCalibrator(), new Random(109), rest: 30);
        t.Idle(2 * Hz);
        for (int i = 0; i < 10; i++) t.Press(255, Hz, jitter: 0);
        Assert.Equal(0f, t.C.Map(30));
    }

    [Fact]
    public void Rest_that_rises_during_the_session_is_followed()
    {
        var t = new Trig(new TriggerCalibrator(), new Random(110));
        t.Idle(2 * Hz);
        for (int i = 0; i < 6; i++) t.Press(255, Hz, jitter: 0);
        // Trigger gets sticky: after every release it now comes back to 10 instead of 0.
        t.Rest = 10;
        for (int i = 0; i < 30; i++) { t.Press(255, Hz, jitter: 0); t.Idle(5 * Hz); }
        Assert.Equal(0f, t.C.Map(10));
    }

    [Fact]
    public void Trigger_export_import_mid_learning_keeps_behaviour()
    {
        var rngA = new Random(111);
        var a = new Trig(new TriggerCalibrator(), rngA, stop: 200);
        a.Idle(Hz);
        for (int i = 0; i < 3; i++) a.Press(255, Hz, jitter: 1); // not learned yet: needs 5

        var b = new TriggerCalibrator();
        b.Import(a.C.Export());
        for (int raw = 0; raw <= 255; raw++) Assert.Equal(a.C.Map(raw), b.Map(raw));

        // Continue the same session on both: they must learn at the same press.
        var rng1 = new Random(5);
        var rng2 = new Random(5);
        var ta = new Trig(a.C, rng1, stop: 200);
        var tb = new Trig(b, rng2, stop: 200);
        for (int i = 0; i < 4; i++)
        {
            ta.Press(255, Hz, jitter: 1);
            tb.Press(255, Hz, jitter: 1);
            Assert.Equal(a.C.EndStop, b.EndStop);
        }
        Assert.Equal(1f, b.Map(200));
    }

    // ================================================================ sticks

    private static (byte X, byte Y) ToRaw(double x, double y) => (B(x * 127.5 + 127.5), B(-y * 127.5 + 127.5));

    private static float Len((float X, float Y) v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    private sealed class Stick
    {
        public readonly StickCalibrator C;
        private readonly Random _rng;
        public double DriftX, DriftY;
        /// <summary>Random release-to-release scatter of the rest spot (worn spring).</summary>
        public double Scatter;
        public double Noise = 0.003;

        public Stick(StickCalibrator c, Random rng, double dx = 0, double dy = 0)
        {
            C = c;
            _rng = rng;
            DriftX = dx;
            DriftY = dy;
        }

        public (float X, float Y) Feed(double x, double y)
        {
            var (bx, by) = ToRaw(x + G() * Noise, y + G() * Noise);
            var o = C.Process(bx, by);
            Assert.True(float.IsFinite(o.X) && float.IsFinite(o.Y));
            Assert.True(Len(o) <= 1.0001f);
            return o;
        }

        public (double X, double Y) NewRestSpot()
        {
            double a = _rng.NextDouble() * 2 * Math.PI, r = Math.Sqrt(_rng.NextDouble()) * Scatter;
            return (DriftX + r * Math.Cos(a), DriftY + r * Math.Sin(a));
        }

        /// <summary>Flick somewhere far, then let go: the stick springs back to a rest spot and sits there.</summary>
        public void FlickAndRelease(int restSamples = Hz)
        {
            double a = _rng.NextDouble() * 2 * Math.PI;
            for (int i = 0; i < 20; i++) Feed(0.9 * Math.Cos(a), 0.9 * Math.Sin(a));
            var (rx, ry) = NewRestSpot();
            for (int i = 0; i < restSamples; i++) Feed(rx, ry);
        }

        /// <summary>Flick, then the thumb holds a small offset dead still.</summary>
        public void FlickAndHold(double hx, double hy, int samples)
        {
            for (int i = 0; i < 20; i++) Feed(-0.9, 0.2);
            for (int i = 0; i < samples; i++) Feed(hx, hy);
        }

        /// <summary>Gentle movement only (never past <paramref name="maxR"/>) around the rest spot.</summary>
        public void Gentle(double maxR, int samples)
        {
            for (int i = 0; i < samples; i++)
            {
                double a = 2 * Math.PI * i / 300.0;
                double r = maxR * (0.5 + 0.5 * Math.Sin(i / 37.0));
                Feed(DriftX + r * Math.Cos(a), DriftY + r * Math.Sin(a));
            }
        }

        private double G()
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }

    /// <summary>Fraction of fresh releases (scatter + noise) that read as exactly zero.</summary>
    private static double ZeroRate(Stick s, Random rng, int n = 400)
    {
        int zero = 0;
        for (int i = 0; i < n; i++)
        {
            var (rx, ry) = s.NewRestSpot();
            var raw = ToRaw(rx + (rng.NextDouble() - 0.5) * 0.01, ry + (rng.NextDouble() - 0.5) * 0.01);
            if (Len(s.C.Map(raw.X, raw.Y)) == 0f) zero++;
        }
        return (double)zero / n;
    }

    [Fact]
    public void Drift_that_jumps_suddenly_after_many_releases_needs_guided_centering()
    {
        var rng = new Random(201);
        var s = new Stick(new StickCalibrator(), rng);
        for (int i = 0; i < 50; i++) s.FlickAndRelease();
        Assert.Equal(1.0, ZeroRate(s, rng));

        // Dropped controller / dust: drift jumps to (0.1, 0). Releases outside the deadzone are ignored,
        // so normal learning does not follow.
        s.DriftX = 0.10;
        for (int i = 0; i < 60; i++) s.FlickAndRelease();
        Assert.InRange(s.C.CenterX, -0.02f, 0.02f);
        Assert.True(ZeroRate(s, rng) < 0.5);

        // Guided centering picks up the new spot.
        s.C.StartGuidedCenter();
        for (int i = 0; i < 300; i++) s.Feed(s.DriftX, s.DriftY);
        Assert.True(ZeroRate(s, rng) > 0.98, $"released stick drifts: center ({s.C.CenterX:F3},{s.C.CenterY:F3}) dz {s.C.Deadzone:F3}");
    }

    [Fact]
    public void Drift_that_appeared_between_sessions_needs_guided_centering_after_import()
    {
        var rng = new Random(202);
        var s = new Stick(new StickCalibrator(), rng);
        for (int i = 0; i < 30; i++) s.FlickAndRelease();
        var saved = s.C.Export();

        var next = new Stick(new StickCalibrator(), rng, dx: -0.06, dy: 0.07);
        next.C.Import(saved);
        for (int i = 0; i < 60; i++) next.FlickAndRelease();
        Assert.InRange(next.C.CenterX, -0.02f, 0.02f);

        next.C.StartGuidedCenter();
        for (int i = 0; i < 300; i++) next.Feed(next.DriftX, next.DriftY);
        Assert.True(ZeroRate(next, rng) > 0.98, $"center ({next.C.CenterX:F3},{next.C.CenterY:F3}) dz {next.C.Deadzone:F3}");
    }

    [Fact]
    public void Drift_that_grows_slowly_after_bootstrap_is_followed()
    {
        var rng = new Random(203);
        var s = new Stick(new StickCalibrator(), rng);
        for (int i = 0; i < 20; i++) s.FlickAndRelease();
        for (int i = 0; i < 300; i++)
        {
            s.DriftX = 0.15 * i / 299;
            s.DriftY = -0.08 * i / 299;
            s.FlickAndRelease();
        }
        for (int i = 0; i < 20; i++) s.FlickAndRelease();
        Assert.InRange(s.C.CenterX, 0.13f, 0.17f);
        Assert.InRange(s.C.CenterY, -0.10f, -0.06f);
        Assert.True(ZeroRate(s, rng) > 0.99);
        Assert.True(s.C.Deadzone < 0.08f, $"deadzone {s.C.Deadzone}");
    }

    [Fact]
    public void Held_aim_during_bootstrap_does_not_lock_in_a_wrong_center()
    {
        var rng = new Random(204);
        var s = new Stick(new StickCalibrator(), rng);
        // Connect with the stick released, then the first two "releases" are flicks into a
        // steady small aim offset (scope in, hold on target for a second).
        for (int i = 0; i < Hz; i++) s.Feed(0, 0);
        s.FlickAndHold(0.16, 0.02, Hz);
        s.FlickAndHold(0.15, 0.03, Hz);
        for (int i = 0; i < 100; i++) s.FlickAndRelease();
        Assert.True(ZeroRate(s, rng) > 0.98, $"center ({s.C.CenterX:F3},{s.C.CenterY:F3}) dz {s.C.Deadzone:F3}");
    }

    [Fact]
    public void Held_aim_after_bootstrap_is_not_learned()
    {
        var rng = new Random(205);
        var s = new Stick(new StickCalibrator(), rng, dx: 0.05, dy: -0.03);
        for (int i = 0; i < 10; i++) s.FlickAndRelease();
        for (int i = 0; i < 200; i++) s.FlickAndHold(0.05 + 0.12, -0.03 + 0.04, Hz);
        Assert.InRange(s.C.CenterX, 0.03f, 0.07f);
        Assert.InRange(s.C.CenterY, -0.05f, -0.01f);
        Assert.True(s.C.Deadzone < 0.07f);
    }

    [Fact]
    public void Stick_never_deflected_far_still_reads_zero_at_rest()
    {
        // A gentle player (menus, slow walking) who never pushes past 0.35 for a long time.
        var rng = new Random(206);
        var s = new Stick(new StickCalibrator(), rng, dx: 0.08, dy: 0.05);
        for (int i = 0; i < Hz; i++) s.Feed(s.DriftX, s.DriftY); // connected while released
        for (int round = 0; round < 100; round++)
        {
            s.Gentle(0.30, 2 * Hz);
            for (int i = 0; i < Hz; i++) s.Feed(s.DriftX, s.DriftY);
        }
        Assert.True(ZeroRate(s, rng) > 0.99, $"center ({s.C.CenterX:F3},{s.C.CenterY:F3})");
    }

    [Fact]
    public void Stick_never_deflected_far_whose_drift_changes_is_not_followed()
    {
        // Documents a limitation: without a deflection >= 0.4 no release is counted, so a drift
        // change during gentle-only play is not followed. This passes as long as it is not worse
        // than factory (released output stays small).
        var rng = new Random(207);
        var s = new Stick(new StickCalibrator(), rng, dx: 0.04, dy: 0);
        for (int i = 0; i < Hz; i++) s.Feed(s.DriftX, s.DriftY);
        s.DriftX = 0.09;
        for (int round = 0; round < 50; round++)
        {
            s.Gentle(0.25, 2 * Hz);
            for (int i = 0; i < Hz; i++) s.Feed(s.DriftX, s.DriftY);
        }
        var raw = ToRaw(0.09, 0);
        Assert.True(Len(s.C.Map(raw.X, raw.Y)) < 0.06f);
    }

    [Fact]
    public void Worn_spring_with_release_scatter_reads_zero_when_released()
    {
        var rng = new Random(208);
        var s = new Stick(new StickCalibrator(), rng, dx: 0.06, dy: 0.02) { Scatter = 0.08 };
        for (int i = 0; i < 300; i++) s.FlickAndRelease();
        double z = ZeroRate(s, rng);
        Assert.True(z > 0.97, $"only {z:P1} of releases read zero, dz {s.C.Deadzone:F3}");
        Assert.True(s.C.Deadzone < 0.25f);
    }

    [Fact]
    public void Stick_export_import_mid_learning_keeps_behaviour()
    {
        var rng = new Random(209);
        var a = new Stick(new StickCalibrator(), rng, dx: 0.07, dy: -0.05) { Scatter = 0.02 };
        for (int i = 0; i < 2; i++) a.FlickAndRelease(); // still in bootstrap
        for (int k = 0; k < 64; k++) a.Feed(0.95 * Math.Cos(k / 10.0), 0.95 * Math.Sin(k / 10.0));

        var b = new StickCalibrator();
        b.Import(a.C.Export());
        for (int i = 0; i < 256; i += 3)
        for (int j = 0; j < 256; j += 3)
            Assert.Equal(a.C.Map((byte)i, (byte)j), b.Map((byte)i, (byte)j));

        // Same continued play on both.
        var sa = new Stick(a.C, new Random(9), dx: 0.07, dy: -0.05) { Scatter = 0.02 };
        var sb = new Stick(b, new Random(9), dx: 0.07, dy: -0.05) { Scatter = 0.02 };
        for (int i = 0; i < 30; i++)
        {
            sa.FlickAndRelease();
            sb.FlickAndRelease();
        }
        Assert.InRange(b.CenterX - a.C.CenterX, -0.01f, 0.01f);
        Assert.InRange(b.CenterY - a.C.CenterY, -0.01f, 0.01f);
        Assert.InRange(b.Deadzone - a.C.Deadzone, -0.01f, 0.01f);
    }
}
