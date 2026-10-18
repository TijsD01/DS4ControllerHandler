using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;

namespace DS4Handler.Core.Tests;

/// <summary>
/// Long, realistic and adversarial play simulations that try to break the adaptive calibration.
/// Every scenario uses a fixed seed. Sample rate assumed: 250 Hz (DS4 over USB/BT is 250..1000 Hz),
/// so 250 samples is roughly one second of play.
/// </summary>
public class CalibrationScenarioTests
{
    private const int Hz = 250;

    private static byte B(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    // ---------------------------------------------------------------- trigger helpers

    /// <summary>A trigger that physically cannot go past <paramref name="stop"/>.</summary>
    private sealed class TriggerSim
    {
        private readonly TriggerCalibrator _c;
        private readonly Random _rng;
        private readonly int _stop;
        private readonly int _rest;
        public readonly List<(byte Raw, float Out)> Log = new();

        public TriggerSim(TriggerCalibrator c, Random rng, int stop = 255, int rest = 0)
        {
            _c = c;
            _rng = rng;
            _stop = stop;
            _rest = rest;
        }

        public float Feed(double v, int jitter = 1)
        {
            int raw = (int)Math.Round(v) + (jitter > 0 ? _rng.Next(-jitter, jitter + 1) : 0);
            raw = Math.Clamp(raw, _rest, _stop);
            float o = _c.Process((byte)raw);
            Log.Add(((byte)raw, o));
            return o;
        }

        public void Ramp(double from, double to, int samples)
        {
            for (int i = 0; i <= samples; i++) Feed(from + (to - from) * i / samples, 0);
        }

        public void Hold(double v, int samples, int jitter = 1)
        {
            for (int i = 0; i < samples; i++) Feed(v, jitter);
        }

        public void Idle(int samples) => Hold(_rest, samples, 0);

        /// <summary>Press to <paramref name="level"/> (clamped by the physical stop), hold, release.</summary>
        public void Press(double level, int holdSamples, int jitter = 1)
        {
            Ramp(_rest, level, 15);
            Hold(level, holdSamples, jitter);
            Ramp(level, _rest, 15);
            Idle(40);
        }
    }

    private static void AssertFiniteInRange(float v, float lo, float hi, string what)
    {
        Assert.True(float.IsFinite(v), $"{what} not finite: {v}");
        Assert.InRange(v, lo, hi);
    }

    /// <summary>The current mapping must be monotonic, start at 0 and have no big jumps.</summary>
    private static void AssertTriggerMapSane(TriggerCalibrator c)
    {
        float prev = c.Map(0);
        Assert.Equal(0f, prev);
        for (int raw = 1; raw <= 255; raw++)
        {
            float o = c.Map(raw);
            AssertFiniteInRange(o, 0f, 1f, $"Map({raw})");
            Assert.True(o >= prev, $"not monotonic at raw {raw}: {prev} -> {o}");
            // One raw count is at most 1/126 of the range (end stop never below 128).
            Assert.True(o - prev <= 1f / 100f, $"jump at raw {raw}: {prev} -> {o}");
            prev = o;
        }
    }

    // ---------------------------------------------------------------- trigger scenarios

    [Theory]
    [InlineData(200, 11)]
    [InlineData(185, 12)]
    public void Rocket_League_session_with_worn_R2_reaches_full_throttle(int stop, int seed)
    {
        var c = new TriggerCalibrator();
        var rng = new Random(seed);
        var t = new TriggerSim(c, rng, stop: stop);

        for (int round = 0; round < 6; round++)
        {
            // Kickoff: full throttle for several seconds.
            t.Press(255, holdSamples: 5 * Hz);
            // Feathered throttle while dribbling: lots of short partial presses.
            for (int i = 0; i < 12; i++) t.Press(60 + rng.Next(0, 100), holdSamples: rng.Next(20, 120), jitter: 2);
            // Boosting with throttle pinned for a long stretch; brief taps off in between.
            for (int i = 0; i < 4; i++)
            {
                t.Ramp(0, 255, 10);
                t.Hold(255, 10 * Hz);
                t.Ramp(255, 0, 8);
                t.Idle(rng.Next(5, 30));
            }
            // Demo'd, waiting for respawn.
            t.Idle(3 * Hz);
        }

        Assert.True(c.EndStopLearned);
        Assert.InRange(c.EndStop, stop - 6, stop + 0.5f);
        Assert.Equal(1f, c.Map(stop));
        Assert.Equal(1f, c.Map(stop - 1));
        AssertTriggerMapSane(c);

        // In the last full-throttle hold the output must have been 1.0 the whole time.
        var lastHold = t.Log.Skip(t.Log.Count - 3 * Hz - 40 - 10 * Hz).Take(5 * Hz).ToArray();
        Assert.All(lastHold.Where(s => s.Raw >= stop - 1), s => Assert.Equal(1f, s.Out));
    }

    [Fact]
    public void Worn_R2_full_throttle_is_reached_within_the_first_minute()
    {
        var c = new TriggerCalibrator();
        var t = new TriggerSim(c, new Random(13), stop: 190);
        // Typical start of a match: a handful of full-throttle bursts with short releases.
        for (int i = 0; i < 5; i++)
        {
            t.Press(255, holdSamples: 3 * Hz);
        }
        Assert.Equal(1f, c.Map(190));
        Assert.Equal(1f, c.Map(189));
    }

    [Fact]
    public void Racing_game_steady_70_percent_throttle_on_healthy_trigger_is_not_learned_as_full()
    {
        var c = new TriggerCalibrator();
        var t = new TriggerSim(c, new Random(21));

        // Session start: cruising at ~70% throttle through a few corners, no full press yet.
        for (int i = 0; i < 4; i++) t.Press(178, holdSamples: 4 * Hz);

        // 70% physical travel must still read about 70%, not 100%.
        Assert.InRange(c.Map(178), 0.6f, 0.8f);
    }

    [Fact]
    public void Racing_game_long_partial_throttle_stint_does_not_lower_a_learned_full_end_stop()
    {
        var c = new TriggerCalibrator();
        var t = new TriggerSim(c, new Random(22));

        for (int i = 0; i < 5; i++) t.Press(255, holdSamples: Hz, jitter: 0);
        Assert.True(c.EndStop >= 250f);

        // Long endurance stint at a steady ~70% (fuel saving), repeatedly.
        for (int i = 0; i < 20; i++) t.Press(178, holdSamples: 6 * Hz);

        Assert.True(c.EndStop >= 250f, $"end stop dropped to {c.EndStop}");
        Assert.InRange(c.Map(178), 0.6f, 0.8f);
    }

    [Fact]
    public void Holding_light_throttle_does_not_fade_to_zero()
    {
        var c = new TriggerCalibrator();
        var t = new TriggerSim(c, new Random(23));
        t.Idle(Hz);

        // Gentle throttle at ~14% for two seconds (crawling, lining up a shot, light brake in a racer).
        t.Hold(35, 2 * Hz, jitter: 1);

        float outNow = c.Map(35);
        Assert.True(outNow > 0.08f, $"light throttle reads {outNow}, rest learned as {c.Rest}");
    }

    [Fact]
    public void Slowly_rolling_into_throttle_does_not_raise_the_rest_position()
    {
        var c = new TriggerCalibrator();
        var t = new TriggerSim(c, new Random(24));
        t.Idle(Hz);

        // Slow squeeze 0 -> 100 over 4 seconds (progressive throttle out of a corner).
        for (int i = 0; i <= 4 * Hz; i++) t.Feed(100.0 * i / (4 * Hz), 0);

        Assert.True(c.Rest < 10f, $"rest crept to {c.Rest}");
        Assert.InRange(c.Map(100), 0.33f, 0.45f);
    }

    [Fact]
    public void Trigger_spikes_and_dropouts_do_not_corrupt_learning()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(31);
        var t = new TriggerSim(c, rng, stop: 200);

        for (int i = 0; i < 6; i++) t.Press(255, holdSamples: 2 * Hz);
        float learned = c.EndStop;
        Assert.InRange(learned, 194f, 200.5f);

        // Idle with single-sample 255 spikes (electrical glitch), and full holds with single-sample
        // dropouts to 0 (bad report).
        for (int round = 0; round < 200; round++)
        {
            for (int i = 0; i < 100; i++) c.Process(rng.Next(50) == 0 ? (byte)255 : (byte)0);
            for (int i = 0; i < 20; i++) c.Process((byte)(i * 10));
            for (int i = 0; i < 300; i++) c.Process(rng.Next(60) == 0 ? (byte)0 : (byte)(199 + rng.Next(0, 2)));
            for (int i = 20; i >= 0; i--) c.Process((byte)(i * 10));
        }

        Assert.InRange(c.EndStop, 194f, 200.5f);
        Assert.InRange(c.Rest, 0f, 2f);
        Assert.Equal(1f, c.Map(200));
        Assert.Equal(0f, c.Map(0));
        AssertTriggerMapSane(c);
    }

    [Fact]
    public void Healthy_trigger_spikes_to_255_do_not_raise_a_worn_end_stop_or_lower_a_healthy_one()
    {
        // Worn trigger at 185: rare 1-sample spikes to 255 in the middle of full holds.
        var c = new TriggerCalibrator();
        var rng = new Random(32);
        for (int p = 0; p < 30; p++)
        {
            for (int i = 0; i <= 15; i++) c.Process((byte)(185 * i / 15));
            for (int i = 0; i < 400; i++) c.Process(rng.Next(100) == 0 ? (byte)255 : (byte)(184 + rng.Next(0, 2)));
            for (int i = 15; i >= 0; i--) c.Process((byte)(185 * i / 15));
            for (int i = 0; i < 40; i++) c.Process(0);
        }
        Assert.InRange(c.EndStop, 179f, 185.5f);
        Assert.Equal(1f, c.Map(185));
    }

    [Fact]
    public void Bluetooth_packet_loss_repeated_samples_still_learns_worn_stop()
    {
        // Lossy BT: the host keeps re-reading the last report, so runs of identical samples appear,
        // including identical samples in the middle of ramps.
        var c = new TriggerCalibrator();
        var rng = new Random(33);
        byte last = 0;
        void Feed(int v)
        {
            if (rng.Next(4) == 0) { for (int k = rng.Next(2, 12); k > 0; k--) c.Process(last); }
            last = B(v);
            c.Process(last);
        }

        for (int p = 0; p < 10; p++)
        {
            for (int i = 0; i <= 10; i++) Feed(200 * i / 10);
            for (int i = 0; i < 2 * Hz; i++) Feed(200 - rng.Next(0, 2));
            for (int i = 10; i >= 0; i--) Feed(200 * i / 10);
            for (int i = 0; i < 50; i++) Feed(0);
            // Fast partial taps with stuck samples mid-ramp.
            int lvl = 70 + rng.Next(100);
            for (int i = 0; i <= 5; i++) Feed(lvl * i / 5);
            for (int i = 5; i >= 0; i--) Feed(lvl * i / 5);
            for (int i = 0; i < 30; i++) Feed(0);
        }

        Assert.InRange(c.EndStop, 194f, 200.5f);
        Assert.Equal(1f, c.Map(199));
        AssertTriggerMapSane(c);
    }

    [Fact]
    public void Worn_trigger_with_noisy_end_stop_still_learns()
    {
        // Pressing hard on a worn trigger: the value at the stop wobbles +-2 counts.
        var c = new TriggerCalibrator();
        var t = new TriggerSim(c, new Random(34), stop: 205);
        for (int i = 0; i < 10; i++) t.Press(201, holdSamples: 2 * Hz, jitter: 2);
        Assert.True(c.EndStopLearned, "end stop never learned with +-2 jitter at the stop");
        Assert.True(c.Map(201) >= 0.99f, $"Map(201) = {c.Map(201)}, end stop {c.EndStop}");
    }

    [Fact]
    public void Trigger_with_raised_rest_and_worn_stop()
    {
        // Old trigger: rests at 12 and stops at 190.
        var c = new TriggerCalibrator();
        var t = new TriggerSim(c, new Random(35), stop: 190, rest: 12);
        t.Idle(2 * Hz);
        for (int i = 0; i < 6; i++) t.Press(255, holdSamples: Hz);
        Assert.InRange(c.Rest, 11f, 13f);
        Assert.Equal(0f, c.Map(12));
        Assert.Equal(0f, c.Map(13));
        Assert.Equal(1f, c.Map(190));
        AssertTriggerMapSane(c);
    }

    [Fact]
    public void Trigger_one_million_samples_of_random_play_stays_sane()
    {
        var c = new TriggerCalibrator();
        var rng = new Random(36);
        int stop = 210;
        int n = 0;
        double v = 0;
        while (n < 1_000_000)
        {
            // Random target, random dwell, random ramp speed, occasional garbage.
            int mode = rng.Next(10);
            double target = mode < 3 ? 0 : mode < 6 ? 255 : rng.Next(256);
            int dwell = rng.Next(1, 600);
            double step = 1 + rng.Next(40);
            for (int i = 0; i < dwell && n < 1_000_000; i++, n++)
            {
                v += Math.Clamp(target - v, -step, step);
                int raw = (int)v + rng.Next(-1, 2);
                if (rng.Next(5000) == 0) raw = rng.Next(256);
                raw = Math.Clamp(raw, 0, stop);
                float o = c.Process((byte)raw);
                Assert.True(float.IsFinite(o) && o >= 0f && o <= 1f, $"bad output {o} at sample {n}");
            }
        }
        Assert.True(float.IsFinite(c.EndStop) && float.IsFinite(c.Rest));
        Assert.InRange(c.EndStop, 128f, 255f);
        Assert.InRange(c.EndStop, stop - 8f, stop + 0.5f);
        AssertTriggerMapSane(c);
    }

    [Fact]
    public void Trigger_import_of_corrupt_profile_never_outputs_NaN()
    {
        var c = new TriggerCalibrator();
        c.Import(new TriggerCalibrationData { Rest = float.NaN, EndStop = float.NaN, EndStopLearned = true });
        for (int raw = 0; raw <= 255; raw++) AssertFiniteInRange(c.Map(raw), 0f, 1f, $"Map({raw})");
    }

    // ---------------------------------------------------------------- stick helpers

    /// <summary>Normalized (-1..1, up positive) to raw bytes, the inverse of the calibrator's mapping.</summary>
    private static (byte X, byte Y) ToRaw(double x, double y) => (B(x * 127.5 + 127.5), B(-y * 127.5 + 127.5));

    private sealed class StickSim
    {
        private readonly StickCalibrator _c;
        private readonly Random _rng;
        public double RestX, RestY;
        public double Reach = 1.0;
        public double Noise = 0.006;

        public StickSim(StickCalibrator c, Random rng, double restX = 0, double restY = 0)
        {
            _c = c;
            _rng = rng;
            RestX = restX;
            RestY = restY;
        }

        /// <summary>Feed a commanded thumb position; the stick adds its rest offset (drift) and noise.</summary>
        public (float X, float Y) Feed(double x, double y)
        {
            double r = Math.Sqrt(x * x + y * y);
            if (r > Reach) { x *= Reach / r; y *= Reach / r; }
            // Drift fades out as the stick is pushed (it's a spring offset at the center).
            double w = Math.Max(0, 1 - r);
            double px = x + RestX * w + Gauss() * Noise;
            double py = y + RestY * w + Gauss() * Noise;
            var (bx, by) = ToRaw(px, py);
            var o = _c.Process(bx, by);
            Assert.True(float.IsFinite(o.X) && float.IsFinite(o.Y), $"NaN output for raw ({bx},{by})");
            Assert.True(o.X * o.X + o.Y * o.Y <= 1.0001f, $"output outside unit circle: {o}");
            return o;
        }

        public void Release(int samples) { for (int i = 0; i < samples; i++) Feed(0, 0); }

        public void Hold(double x, double y, int samples, double tremble = 0.0)
        {
            for (int i = 0; i < samples; i++) Feed(x + Gauss() * tremble, y + Gauss() * tremble);
        }

        public void Move(double x0, double y0, double x1, double y1, int samples)
        {
            for (int i = 0; i <= samples; i++)
            {
                double t = (double)i / samples;
                Feed(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t);
            }
        }

        public void Rotate(double radius, int turns, int samplesPerTurn)
        {
            for (int i = 0; i < turns * samplesPerTurn; i++)
            {
                double a = 2 * Math.PI * i / samplesPerTurn;
                Feed(radius * Math.Cos(a), radius * Math.Sin(a));
            }
        }

        private double Gauss()
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }

    private static float Len((float X, float Y) v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    /// <summary>Released stick (at its real rest + small noise) must read zero.</summary>
    private static void AssertReleasedReadsZero(StickCalibrator c, StickSim s, Random rng)
    {
        for (int i = 0; i < 200; i++)
        {
            double nx = s.RestX + (rng.NextDouble() - 0.5) * 2 * 0.012;
            double ny = s.RestY + (rng.NextDouble() - 0.5) * 2 * 0.012;
            var (bx, by) = ToRaw(nx, ny);
            var o = c.Map(bx, by);
            Assert.True(Len(o) == 0f, $"released stick reads {o} (center {c.CenterX:F3},{c.CenterY:F3}, dz {c.Deadzone:F3})");
        }
    }

    // ---------------------------------------------------------------- stick scenarios

    [Fact]
    public void Stick_resting_exactly_at_center_keeps_factory_behaviour()
    {
        var c = new StickCalibrator();
        var rng = new Random(41);
        var s = new StickSim(c, rng) { Noise = 0.003 };
        for (int round = 0; round < 50; round++)
        {
            s.Release(2 * Hz);
            s.Rotate(1.0, 1, Hz / 2);
            s.Move(0, 0, 0.5, 0.2, 30);
            s.Move(0.5, 0.2, 0, 0, 30);
        }
        Assert.InRange(c.CenterX, -0.02f, 0.02f);
        Assert.InRange(c.CenterY, -0.02f, 0.02f);
        Assert.InRange(c.Deadzone, 0.03f, 0.06f);
        // Small deflections still register, full deflection reaches 1.
        Assert.True(Len(c.Map(ToRaw(0.15, 0).X, ToRaw(0.15, 0).Y)) > 0.08f);
        foreach (var (x, y) in new[] { (1.0, 0.0), (0.0, 1.0), (-1.0, 0.0), (0.0, -1.0) })
        {
            var r = ToRaw(x, y);
            Assert.True(Len(c.Map(r.X, r.Y)) >= 0.99f, $"full push ({x},{y}) gives {Len(c.Map(r.X, r.Y))}");
        }
    }

    [Fact]
    public void Stick_rotations_reach_full_output_all_around()
    {
        var c = new StickCalibrator();
        var rng = new Random(42);
        var s = new StickSim(c, rng) { Reach = 0.88 };
        for (int i = 0; i < 20; i++)
        {
            s.Rotate(1.0, 2, Hz);
            s.Release(Hz);
        }
        for (int k = 0; k < 64; k++)
        {
            double a = 2 * Math.PI * k / 64;
            var r = ToRaw(0.88 * Math.Cos(a), 0.88 * Math.Sin(a));
            float l = Len(c.Map(r.X, r.Y));
            Assert.True(l >= 0.97f, $"angle {k}/64 at the physical edge reads {l}");
        }
        // Monotonic along a radius in every direction.
        for (int k = 0; k < 32; k++)
        {
            double a = 2 * Math.PI * k / 32;
            float prev = 0;
            for (int i = 0; i <= 50; i++)
            {
                var r = ToRaw(0.88 * i / 50 * Math.Cos(a), 0.88 * i / 50 * Math.Sin(a));
                float l = Len(c.Map(r.X, r.Y));
                Assert.True(l >= prev - 0.02f, $"non-monotonic at angle {k}/32 step {i}: {prev} -> {l}");
                prev = l;
            }
        }
    }

    [Fact]
    public void Drifting_stick_whose_drift_changes_over_time_is_followed()
    {
        var c = new StickCalibrator();
        var rng = new Random(43);
        var s = new StickSim(c, rng, restX: 0.06, restY: -0.04);

        // Hours of play condensed: drift wanders from (0.06,-0.04) to (0.14,-0.10).
        for (int session = 0; session <= 20; session++)
        {
            s.RestX = 0.06 + 0.08 * session / 20.0;
            s.RestY = -0.04 - 0.06 * session / 20.0;
            for (int i = 0; i < 10; i++)
            {
                s.Rotate(1.0, 1, Hz / 2);
                s.Move(0, 0, -0.7, 0.4, 40);
                s.Move(-0.7, 0.4, 0, 0, 40);
                s.Release(Hz);
            }
            AssertReleasedReadsZero(c, s, rng);
        }
        Assert.InRange(c.CenterX, 0.12f, 0.16f);
        Assert.InRange(c.CenterY, -0.12f, -0.08f);
        Assert.True(c.Deadzone < 0.12f, $"deadzone grew to {c.Deadzone}");
    }

    [Fact]
    public void FPS_player_holding_aim_slightly_off_center_is_not_learned_as_drift()
    {
        var c = new StickCalibrator();
        var rng = new Random(44);
        var s = new StickSim(c, rng) { Noise = 0.004 };

        // Warm up with normal play, then long holds slightly off-center (tracking a target,
        // slow strafe, walking with the left stick half forward) separated by short releases.
        for (int i = 0; i < 5; i++) { s.Rotate(1.0, 1, Hz / 2); s.Release(Hz); }
        for (int i = 0; i < 30; i++)
        {
            s.Move(0, 0, 0.18, 0.05, 20);
            s.Hold(0.18, 0.05, 5 * Hz, tremble: 0.002);
            s.Move(0.18, 0.05, 0, 0, 20);
            s.Release(Hz / 2);
        }

        Assert.InRange(c.CenterX, -0.03f, 0.03f);
        Assert.InRange(c.CenterY, -0.03f, 0.03f);
        AssertReleasedReadsZero(c, s, rng);
        var held = ToRaw(0.18, 0.05);
        Assert.True(Len(c.Map(held.X, held.Y)) > 0.1f, "held aim offset reads as zero");
    }

    [Fact]
    public void FPS_holds_and_releases_do_not_inflate_the_deadzone()
    {
        var c = new StickCalibrator();
        var rng = new Random(45);
        var s = new StickSim(c, rng) { Noise = 0.004 };
        s.Release(2 * Hz);
        for (int i = 0; i < 40; i++)
        {
            double x = (rng.NextDouble() - 0.5) * 0.4, y = (rng.NextDouble() - 0.5) * 0.4;
            s.Move(0, 0, x, y, 15);
            s.Hold(x, y, Hz, tremble: 0.002);
            s.Move(x, y, 0, 0, 15);
            s.Release(Hz / 2);
        }
        Assert.True(c.Deadzone < 0.08f, $"deadzone grew to {c.Deadzone} (center {c.CenterX:F3},{c.CenterY:F3})");
    }

    [Fact]
    public void Repeated_corner_glitch_does_not_shrink_diagonal_output()
    {
        var c = new StickCalibrator();
        var rng = new Random(46);
        var s = new StickSim(c, rng);
        for (int i = 0; i < 5; i++) { s.Rotate(1.0, 1, Hz); s.Release(Hz); }

        // One garbage report at raw (255,0) that the host reads twice.
        c.Process(255, 0);
        c.Process(255, 0);
        s.Release(Hz);
        for (int i = 0; i < 5; i++) { s.Rotate(1.0, 1, Hz); s.Release(Hz); }

        var r = ToRaw(Math.Cos(Math.PI / 4), Math.Sin(Math.PI / 4));
        float l = Len(c.Map(r.X, r.Y));
        Assert.True(l >= 0.97f, $"full up-right push reads {l}");
    }

    [Fact]
    public void Single_sample_stick_spikes_are_ignored_for_reach()
    {
        var c = new StickCalibrator();
        var rng = new Random(47);
        var s = new StickSim(c, rng) { Reach = 0.85 };
        for (int i = 0; i < 5; i++) { s.Rotate(1.0, 1, Hz); s.Release(Hz); }
        for (int i = 0; i < 200; i++)
        {
            s.Release(rng.Next(5, 50));
            byte gx = (byte)rng.Next(256), gy = (byte)rng.Next(256);
            c.Process(gx, gy); // single glitched sample
        }
        for (int k = 0; k < 32; k++)
        {
            double a = 2 * Math.PI * k / 32;
            var r = ToRaw(0.85 * Math.Cos(a), 0.85 * Math.Sin(a));
            Assert.True(Len(c.Map(r.X, r.Y)) >= 0.97f, $"edge at angle {k}/32 reads {Len(c.Map(r.X, r.Y))}");
        }
        AssertReleasedReadsZero(c, s, rng);
    }

    [Fact]
    public void Sticks_slammed_into_raw_corners_and_edges_stay_in_range()
    {
        var c = new StickCalibrator();
        var rng = new Random(48);
        var s = new StickSim(c, rng);
        byte[] ext = { 0, 255 };
        for (int round = 0; round < 500; round++)
        {
            byte x = ext[rng.Next(2)], y = ext[rng.Next(2)];
            for (int i = 0; i < 30; i++)
            {
                var o = c.Process(x, y);
                Assert.True(float.IsFinite(o.X) && float.IsFinite(o.Y));
                Assert.True(Len(o) <= 1.0001f);
            }
            if (rng.Next(3) == 0) s.Release(Hz / 4);
        }
        for (int i = 0; i < 256; i += 15)
        for (int j = 0; j < 256; j += 15)
        {
            var o = c.Map((byte)i, (byte)j);
            Assert.True(float.IsFinite(o.X) && float.IsFinite(o.Y) && Len(o) <= 1.0001f);
        }
        // Cardinal full pushes still read full.
        Assert.True(Math.Abs(c.Map(255, 128).X) >= 0.97f, $"{c.Map(255, 128)}");
        Assert.True(Math.Abs(c.Map(0, 128).X) >= 0.97f, $"{c.Map(0, 128)}");
    }

    [Fact]
    public void Stick_one_million_samples_of_random_play_stays_sane()
    {
        var c = new StickCalibrator();
        var rng = new Random(49);
        var s = new StickSim(c, rng, restX: 0.08, restY: 0.05) { Reach = 0.92 };
        int n = 0;
        while (n < 1_000_000)
        {
            int mode = rng.Next(5);
            int len = rng.Next(20, 800);
            switch (mode)
            {
                case 0: s.Release(len); break;
                case 1: s.Rotate(1.0, 1, Math.Max(len, 40)); break;
                case 2:
                {
                    double a = rng.NextDouble() * 2 * Math.PI, r = 0.4 + rng.NextDouble() * 0.6;
                    s.Move(0, 0, r * Math.Cos(a), r * Math.Sin(a), rng.Next(5, 40));
                    s.Hold(r * Math.Cos(a), r * Math.Sin(a), len, tremble: 0.01);
                    s.Move(r * Math.Cos(a), r * Math.Sin(a), 0, 0, rng.Next(5, 40));
                    break;
                }
                case 3:
                    for (int i = 0; i < len; i++) s.Feed((rng.NextDouble() - 0.5) * 2, (rng.NextDouble() - 0.5) * 2);
                    break;
                default:
                    for (int i = 0; i < 5; i++) c.Process((byte)rng.Next(256), (byte)rng.Next(256));
                    len = 5;
                    break;
            }
            n += len;
        }
        Assert.True(float.IsFinite(c.CenterX) && float.IsFinite(c.CenterY) && float.IsFinite(c.Deadzone));
        Assert.InRange(c.Deadzone, 0.03f, 0.35f);
        Assert.InRange(c.CenterX, 0.04f, 0.12f);
        Assert.InRange(c.CenterY, 0.01f, 0.09f);
        AssertReleasedReadsZero(c, s, rng);
    }

    [Fact]
    public void Stick_import_of_corrupt_profile_never_outputs_NaN()
    {
        var c = new StickCalibrator();
        c.Import(new StickCalibrationData
        {
            CenterX = float.NaN,
            CenterY = float.PositiveInfinity,
            NoiseRadius = float.NaN,
            RestSpread = float.NegativeInfinity,
            OuterRadii = Enumerable.Repeat(float.NaN, 16).ToArray(),
            RestSamples = -5,
        });
        for (int i = 0; i < 256; i += 5)
        for (int j = 0; j < 256; j += 5)
        {
            var o = c.Map((byte)i, (byte)j);
            Assert.True(float.IsFinite(o.X) && float.IsFinite(o.Y) && Len(o) <= 1.0001f, $"({i},{j}) -> {o}");
        }
    }

    // ---------------------------------------------------------------- whole controller

    [Fact]
    public void Full_controller_session_learns_each_input_independently()
    {
        var cal = new ControllerCalibration("friend", ControllerModel.DualShock4);
        var rng = new Random(51);
        float lastR2 = 0, lastL2 = 0;
        int l2Level = 0;
        for (int round = 0; round < 8; round++)
        {
            // R2 worn at 200 held to full, L2 healthy feathered, left stick drifting, right stick aiming.
            for (int i = 0; i < 3 * Hz; i++)
            {
                double a = 2 * Math.PI * i / Hz;
                var l = ToRaw(0.10 + 0.9 * Math.Cos(a), -0.07 + 0.9 * Math.Sin(a));
                var r = ToRaw(Math.Cos(-a), Math.Sin(-a));
                byte r2 = (byte)Math.Min(200, i < 20 ? i * 13 : 255 - rng.Next(0, 2));
                // L2: a new press every 100 samples, random partial level, every 4th press full.
                if (i % 100 == 0) l2Level = (i / 100) % 4 == 0 ? 255 : rng.Next(60, 240);
                byte l2 = (byte)(i % 100 < 50 ? Math.Min(l2Level, (i % 100) * 20) : 0);
                var o = cal.Process(new RawControllerState(l.X, l.Y, r.X, r.Y, l2, r2, Buttons.None));
                lastR2 = o.R2;
                lastL2 = o.L2;
            }
            for (int i = 0; i < 2 * Hz; i++)
            {
                var l = ToRaw(0.10 + (rng.NextDouble() - 0.5) * 0.01, -0.07 + (rng.NextDouble() - 0.5) * 0.01);
                cal.Process(new RawControllerState(l.X, l.Y, 128, 128, 0, 0, Buttons.None));
            }
        }
        Assert.Equal(1f, lastR2);
        Assert.InRange(cal.R2.EndStop, 194f, 200.5f);
        Assert.True(cal.L2.EndStop >= 250f, $"L2 end stop {cal.L2.EndStop}");
        Assert.InRange(cal.LeftStick.CenterX, 0.07f, 0.13f);
        Assert.InRange(cal.LeftStick.CenterY, -0.10f, -0.04f);
        Assert.InRange(cal.RightStick.CenterX, -0.02f, 0.02f);
        Assert.InRange(cal.RightStick.CenterY, -0.02f, 0.02f);
        _ = lastL2;
    }
}
