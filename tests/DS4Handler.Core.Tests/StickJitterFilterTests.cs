using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;
using DS4Handler.Core.Profiles;
using Xunit.Abstractions;

namespace DS4Handler.Core.Tests;

/// <summary>Learned jitter and the adaptive (One Euro style) stick jitter filter.</summary>
public class StickJitterFilterTests
{
    private const int Hz = 250;
    private const double Dt = 1.0 / Hz;
    private readonly ITestOutputHelper _out;

    public StickJitterFilterTests(ITestOutputHelper output) => _out = output;

    private static byte B(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    /// <summary>Normalized (-1..1, up positive) to raw bytes, plus uniform +-noise counts per axis.</summary>
    private static (byte X, byte Y) Raw(double x, double y, Random? rng = null, int noise = 0)
    {
        int nx = rng is null || noise == 0 ? 0 : rng.Next(-noise, noise + 1);
        int ny = rng is null || noise == 0 ? 0 : rng.Next(-noise, noise + 1);
        return (B(x * 127.5 + 127.5 + nx), B(-y * 127.5 + 127.5 + ny));
    }

    private static float Len((float X, float Y) v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    /// <summary>Normal play: rest at the center, a few flicks, and holds around (0.5, 0.2).</summary>
    private static void Play(StickCalibrator c, Random rng, int noise, int seconds = 6, double dt = Dt)
    {
        int perSecond = (int)Math.Round(1 / dt);
        for (int s = 0; s < seconds; s++)
        {
            for (int i = 0; i < perSecond / 2; i++) { var r = Raw(0, 0, rng, Math.Min(noise, 1)); c.Process(r.X, r.Y, dt); }
            for (int i = 0; i < 10; i++) { var r = Raw(0.9, 0.1, rng, noise); c.Process(r.X, r.Y, dt); }
            for (int i = 0; i < perSecond / 2; i++) { var r = Raw(0.5, 0.2, rng, noise); c.Process(r.X, r.Y, dt); }
        }
    }

    /// <summary>Spread (RMS distance from the mean) of a list of outputs.</summary>
    private static double Spread(List<(float X, float Y)> v)
    {
        double mx = v.Average(p => p.X), my = v.Average(p => p.Y);
        return Math.Sqrt(v.Average(p => (p.X - mx) * (p.X - mx) + (p.Y - my) * (p.Y - my)));
    }

    /// <summary>Holds the stick at (x, y) with noise and returns (filtered, unfiltered) spreads; also checks the filtered mean stays put.</summary>
    private static (double Filtered, double Raw) HoldSpread(StickCalibrator c, Random rng, double x, double y, int noise, int samples, double dt = Dt)
    {
        var f = new List<(float, float)>();
        var m = new List<(float, float)>();
        // Move there and settle first; only the hold itself is measured.
        int settle = (int)Math.Round(0.2 / dt);
        for (int i = 0; i < settle; i++) { var r = Raw(x, y, rng, noise); c.Process(r.X, r.Y, dt); }
        for (int i = 0; i < samples; i++)
        {
            var r = Raw(x, y, rng, noise);
            f.Add(c.Process(r.X, r.Y, dt));
            m.Add(c.Map(r.X, r.Y));
        }
        double bias = Math.Sqrt(Math.Pow(f.Average(p => p.Item1) - m.Average(p => p.Item1), 2) + Math.Pow(f.Average(p => p.Item2) - m.Average(p => p.Item2), 2));
        Assert.True(bias < 0.02, $"filtered output sits {bias:F4} away from the real position");
        return (Spread(f), Spread(m));
    }

    private static StickCalibrator Jittery(int noise = 3, int seed = 1, double dt = Dt)
    {
        var c = new StickCalibrator();
        Play(c, new Random(seed), noise, dt: dt);
        return c;
    }

    // ------------------------------------------------------------------ learning and smoothing

    [Fact]
    public void Jittery_stick_held_off_center_is_smoothed_at_least_4x_after_learning()
    {
        var c = Jittery(noise: 3);
        // Uniform +-3 counts has a std of 2 counts = 0.0157.
        Assert.InRange(c.LearnedJitter, 0.012f, 0.019f);
        Assert.True(c.JitterFilterStrength >= 0.8f, $"strength {c.JitterFilterStrength}");

        var (f, raw) = HoldSpread(c, new Random(2), 0.5, 0, noise: 3, samples: 2 * Hz);
        _out.WriteLine($"learned jitter {c.LearnedJitter / (1 / 127.5f):F2} counts; spread unfiltered {raw:F4}, filtered {f:F4}, reduction {raw / f:F1}x");
        Assert.True(raw / f >= 4, $"reduction only {raw / f:F2}x ({raw:F4} -> {f:F4})");
    }

    [Fact]
    public void Jitter_is_learned_from_held_periods_alone()
    {
        var c = new StickCalibrator();
        var rng = new Random(3);
        // Never released: the thumb holds the stick off-center the whole time.
        for (int i = 0; i < 3 * Hz; i++) { var r = Raw(-0.4, 0.3, rng, 3); c.Process(r.X, r.Y); }
        Assert.Equal(0, c.Releases);
        Assert.InRange(c.LearnedJitter, 0.012f, 0.019f);
    }

    [Fact]
    public void Learning_is_fast_enough_to_work_live()
    {
        var c = new StickCalibrator();
        var rng = new Random(4);
        for (int i = 0; i < Hz; i++) { var r = Raw(0.5, 0, rng, 3); c.Process(r.X, r.Y); }
        // One second of holding gives a usable estimate.
        Assert.True(c.LearnedJitter > 0.010f, $"after 1 s: {c.LearnedJitter}");
    }

    [Fact]
    public void Clean_stick_is_passed_through_exactly()
    {
        var c = new StickCalibrator();
        var rng = new Random(5);
        Play(c, rng, noise: 0);
        Assert.True(c.LearnedJitter < 0.001f, $"{c.LearnedJitter}");
        Assert.Equal(0f, c.JitterFilterStrength);
        for (int i = 0; i < 2000; i++)
        {
            double a = i * 0.013, r = 0.5 + 0.5 * Math.Sin(i * 0.007);
            var raw = Raw(r * Math.Cos(a), r * Math.Sin(a));
            var o = c.Process(raw.X, raw.Y);
            Assert.Equal(c.Map(raw.X, raw.Y), o);
        }
    }

    /// <summary>
    /// A healthy stick (+-1 count flicker on every report) used for minutes of precise aiming by a
    /// real thumb: slow curved moves, small corrections, holds with 8-12 Hz physiological tremor and
    /// slow wander, the odd release. None of that is sensor jitter, so the filter must stay off.
    /// </summary>
    [Theory]
    [InlineData(250, 31)]
    [InlineData(250, 32)]
    [InlineData(1000, 33)]
    public void Clean_stick_used_for_minutes_of_human_aiming_never_gets_filtered(int rate, int seed)
    {
        var c = new StickCalibrator();
        var rng = new Random(seed);
        double dt = 1.0 / rate, t = 0;
        double x = 0, y = 0;
        float maxStrength = 0, maxJitter = 0;
        int n = 0;

        void Feed(double px, double py, double tremor, double tremorHz)
        {
            t += dt;
            // Physiological tremor: two close frequencies, slightly different per axis.
            double tx = tremor * (Math.Sin(2 * Math.PI * tremorHz * t) + 0.5 * Math.Sin(2 * Math.PI * tremorHz * 1.31 * t + 1));
            double ty = tremor * (0.8 * Math.Cos(2 * Math.PI * tremorHz * 0.93 * t) + 0.4 * Math.Sin(2 * Math.PI * tremorHz * 1.17 * t));
            var r = Raw(px + tx, py + ty, rng, 1);
            c.Process(r.X, r.Y, dt);
            if (++n % 16 == 0)
            {
                maxStrength = Math.Max(maxStrength, c.JitterFilterStrength);
                maxJitter = Math.Max(maxJitter, c.LearnedJitter);
            }
        }

        while (t < 180)
        {
            double tremor = 0.004 + rng.NextDouble() * 0.004, hz = 8 + rng.NextDouble() * 4;
            switch (rng.Next(6))
            {
                case 0: // release
                    for (int i = 0; i < (int)(0.6 / dt); i++) Feed(0, 0, 0, hz);
                    x = y = 0;
                    break;
                case 1: // micro-correction: 0.01-0.03 over 100-200 ms, then a short hold
                {
                    double a = rng.NextDouble() * 2 * Math.PI, d = 0.01 + rng.NextDouble() * 0.02;
                    int m = (int)((0.1 + rng.NextDouble() * 0.1) / dt);
                    double x0 = x, y0 = y;
                    for (int i = 1; i <= m; i++)
                    {
                        double u = (double)i / m, sm = u * u * (3 - 2 * u);
                        Feed(x0 + d * Math.Cos(a) * sm, y0 + d * Math.Sin(a) * sm, tremor, hz);
                    }
                    x = x0 + d * Math.Cos(a);
                    y = y0 + d * Math.Sin(a);
                    for (int i = 0; i < (int)(0.3 / dt); i++) Feed(x, y, tremor, hz);
                    break;
                }
                default: // slow curved tracking, ending in a hold with wander
                {
                    double r1 = 0.15 + rng.NextDouble() * 0.5, a1 = rng.NextDouble() * 2 * Math.PI;
                    double x1 = r1 * Math.Cos(a1), y1 = r1 * Math.Sin(a1);
                    double bend = (rng.NextDouble() - 0.5) * 0.2;
                    int m = (int)((0.4 + rng.NextDouble() * 1.5) / dt);
                    double x0 = x, y0 = y;
                    for (int i = 1; i <= m; i++)
                    {
                        double u = (double)i / m, sm = u * u * u * (10 - 15 * u + 6 * u * u);
                        double curve = bend * Math.Sin(Math.PI * u);
                        Feed(x0 + (x1 - x0) * sm - (y1 - y0) * curve, y0 + (y1 - y0) * sm + (x1 - x0) * curve, tremor, hz);
                    }
                    x = x1;
                    y = y1;
                    int hold = (int)((0.3 + rng.NextDouble() * 2) / dt);
                    double wx = 0, wy = 0;
                    for (int i = 0; i < hold; i++)
                    {
                        // Slow wander of a thumb holding still (a few thousandths per second).
                        wx += (rng.NextDouble() - 0.5) * 0.0004;
                        wy += (rng.NextDouble() - 0.5) * 0.0004;
                        Feed(x + wx, y + wy, tremor, hz);
                    }
                    x += wx;
                    y += wy;
                    break;
                }
            }
        }
        _out.WriteLine($"{rate} Hz seed {seed}: 3 min of aiming, max learned jitter {maxJitter / (1 / 127.5f):F2} counts, max strength {maxStrength:F2}");
        Assert.Equal(0f, maxStrength);
    }

    [Theory]
    [InlineData(1, 0.0, 0.0)]
    [InlineData(3, 0.012, 0.019)]
    public void Late_reports_do_not_skew_the_jitter_estimate(int noise, float min, float max)
    {
        // Held still; every 50th report comes 40 ms late (a Bluetooth hiccup).
        var c = new StickCalibrator();
        var rng = new Random(42);
        for (int i = 0; i < 10 * Hz; i++)
        {
            var r = Raw(0.4, -0.2, rng, noise);
            c.Process(r.X, r.Y, i % 50 == 49 ? 0.04 : Dt);
        }
        _out.WriteLine($"+-{noise} with late reports: jitter {c.LearnedJitter / (1 / 127.5f):F2} counts, strength {c.JitterFilterStrength:F2}");
        if (noise == 1) Assert.Equal(0f, c.JitterFilterStrength);
        else Assert.InRange(c.LearnedJitter, min, max);
    }

    [Fact]
    public void Stick_with_one_count_flicker_is_filtered_only_lightly()
    {
        var c = Jittery(noise: 1, seed: 6);
        float strength = c.JitterFilterStrength;
        var rng = new Random(7);
        double maxErr = 0;
        // Moderate movement: the output stays very close to the unfiltered value.
        for (int i = 0; i < 4 * Hz; i++)
        {
            double a = 2 * Math.PI * i / Hz; // one turn per second
            var raw = Raw(0.7 * Math.Cos(a), 0.7 * Math.Sin(a), rng, 1);
            var f = c.Process(raw.X, raw.Y);
            var m = c.Map(raw.X, raw.Y);
            if (i > 10) maxErr = Math.Max(maxErr, Len((f.X - m.X, f.Y - m.Y)));
        }
        _out.WriteLine($"+-1 count: jitter {c.LearnedJitter / (1 / 127.5f):F2} counts, strength {strength:F2}, max deviation while turning {maxErr:F4}");
        Assert.True(strength < 0.35f, $"strength {strength}");
        Assert.True(maxErr < 0.03, $"max deviation {maxErr}");
    }

    [Fact]
    public void Jittery_stick_at_1000_Hz_is_smoothed_too()
    {
        const double dt = 1.0 / 1000;
        var c = Jittery(noise: 3, seed: 8, dt: dt);
        var (f, raw) = HoldSpread(c, new Random(9), 0.5, 0, noise: 3, samples: 2000, dt: dt);
        _out.WriteLine($"1000 Hz: reduction {raw / f:F1}x");
        Assert.True(raw / f >= 4, $"reduction only {raw / f:F2}x");
    }

    // ------------------------------------------------------------------ latency

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Flick_from_center_to_full_reaches_90_percent_within_3_samples(int noise)
    {
        var c = Jittery(noise: 3);
        var rng = new Random(10);
        foreach (var (x, y) in new[] { (1.0, 0.0), (0.0, 1.0), (-0.7071, 0.7071), (0.0, -1.0) })
        {
            for (int i = 0; i < Hz / 2; i++) { var r = Raw(0, 0); c.Process(r.X, r.Y); }
            int reached = -1;
            float last = 0;
            for (int i = 0; i < 20; i++)
            {
                var r = Raw(x, y, rng, noise);
                var o = c.Process(r.X, r.Y);
                last = Len(o);
                if (reached < 0 && last >= 0.9f) reached = i + 1;
            }
            _out.WriteLine($"flick to ({x},{y}) noise {noise}: >=90% after {reached} sample(s), then {last:F4}");
            Assert.InRange(reached, 1, 3);
            Assert.True(last >= 0.9999f, $"held full push reads {last}");
        }
    }

    [Fact]
    public void Step_to_partial_deflection_reaches_90_percent_within_3_samples()
    {
        var c = Jittery(noise: 3);
        foreach (double target in new[] { 0.3, 0.6, 0.8 })
        {
            for (int i = 0; i < Hz / 2; i++) c.Process(128, 128);
            var r = Raw(target, 0);
            float want = c.Map(r.X, r.Y).X;
            int reached = -1;
            for (int i = 0; i < 10 && reached < 0; i++)
                if (c.Process(r.X, r.Y).X >= 0.9f * want) reached = i + 1;
            _out.WriteLine($"step to {target}: 90% after {reached} sample(s)");
            Assert.InRange(reached, 1, 3);
        }
    }

    /// <summary>Delay (seconds) that best aligns <paramref name="output"/> with <paramref name="reference"/>.</summary>
    private static double Lag(double[] reference, double[] output, double dt)
    {
        double best = double.MaxValue, bestShift = 0;
        for (double shift = 0; shift <= 10; shift += 0.05)
        {
            double err = 0;
            for (int i = 20; i < reference.Length - 1; i++)
            {
                double t = i - shift;
                int i0 = (int)Math.Floor(t);
                double w = t - i0;
                double v = reference[i0] * (1 - w) + reference[i0 + 1] * w;
                err += (output[i] - v) * (output[i] - v);
            }
            if (err < best) { best = err; bestShift = shift; }
        }
        return bestShift * dt;
    }

    [Theory]
    [InlineData(250, 0)]
    [InlineData(250, 3)]
    [InlineData(1000, 3)]
    public void Four_Hz_full_sine_lags_at_most_10_ms(int rate, int noise)
    {
        double dt = 1.0 / rate;
        var c = Jittery(noise: 3, dt: dt);
        var rng = new Random(11);
        int n = 2 * rate;
        var refX = new double[n];
        var outX = new double[n];
        for (int i = 0; i < n; i++)
        {
            double x = Math.Sin(2 * Math.PI * 4 * i * dt);
            var r = Raw(x, 0, rng, noise);
            outX[i] = c.Process(r.X, r.Y, dt).X;
            refX[i] = c.Map(r.X, r.Y).X;
        }
        double lag = Lag(refX, outX, dt);
        _out.WriteLine($"4 Hz sine at {rate} Hz, noise {noise}: lag {lag * 1000:F1} ms");
        Assert.True(lag <= 0.010, $"lag {lag * 1000:F1} ms");
    }

    // ------------------------------------------------------------------ exact ends

    [Fact]
    public void Released_stick_reads_exactly_zero()
    {
        var c = Jittery(noise: 3);
        var rng = new Random(12);
        for (int k = 0; k < 20; k++)
        {
            for (int i = 0; i < 8; i++) { var f = Raw(0.8, -0.3, rng, 3); c.Process(f.X, f.Y); }
            for (int i = 0; i < 60; i++)
            {
                var r = Raw(0, 0, rng, 1);
                var m = c.Map(r.X, r.Y);
                var o = c.Process(r.X, r.Y);
                if (m == (0f, 0f)) Assert.Equal((0f, 0f), o);
            }
            var still = c.Process(128, 128);
            Assert.Equal((0f, 0f), still);
        }
    }

    [Fact]
    public void Output_stays_inside_the_unit_circle_and_finite()
    {
        var c = Jittery(noise: 3);
        var rng = new Random(13);
        for (int i = 0; i < 200_000; i++)
        {
            var o = rng.Next(4) == 0
                ? c.Process((byte)rng.Next(256), (byte)rng.Next(256), rng.NextDouble() * 0.01)
                : c.Process(B(128 + 140 * Math.Sin(i * 0.01)), B(128 + 140 * Math.Cos(i * 0.017)));
            Assert.True(float.IsFinite(o.X) && float.IsFinite(o.Y) && Len(o) <= 1.0001f, $"{o}");
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_dt_falls_back_to_the_default(double dt)
    {
        var a = Jittery(noise: 3);
        var b = Jittery(noise: 3);
        var rng = new Random(14);
        for (int i = 0; i < 500; i++)
        {
            var r = Raw(0.5, 0.1, rng, 3);
            Assert.Equal(a.Process(r.X, r.Y), b.Process(r.X, r.Y, dt));
        }
    }

    // ------------------------------------------------------------------ switches, persistence, cost

    [Fact]
    public void Filter_off_gives_exactly_the_old_output()
    {
        var off = new StickCalibrator { JitterFilterEnabled = false };
        var on = new StickCalibrator();
        var rng1 = new Random(15);
        var rng2 = new Random(15);
        Play(off, rng1, noise: 3);
        Play(on, rng2, noise: 3);
        var rng = new Random(16);
        for (int i = 0; i < 5000; i++)
        {
            var r = Raw(0.9 * Math.Sin(i * 0.01), 0.6 * Math.Cos(i * 0.023), rng, 3);
            var o = off.Process(r.X, r.Y);
            Assert.Equal(off.Map(r.X, r.Y), o);
            on.Process(r.X, r.Y);
        }
        // The filter never changes what is learned.
        Assert.Equal(on.CenterX, off.CenterX);
        Assert.Equal(on.CenterY, off.CenterY);
        Assert.Equal(on.Deadzone, off.Deadzone);
        Assert.Equal(on.LearnedJitter, off.LearnedJitter);
        Assert.Equal(0f, off.JitterFilterStrength);
    }

    [Fact]
    public void Learning_disabled_freezes_the_jitter_but_keeps_filtering()
    {
        var c = Jittery(noise: 3);
        float learned = c.LearnedJitter;
        c.LearningEnabled = false;
        var (f, raw) = HoldSpread(c, new Random(17), -0.3, 0.5, noise: 3, samples: 3 * Hz);
        Assert.Equal(learned, c.LearnedJitter);
        Assert.True(raw / f >= 2, $"no filtering with learning off: {raw:F4} -> {f:F4}");
    }

    [Fact]
    public void Learned_jitter_round_trips_through_export_and_import()
    {
        var a = Jittery(noise: 3);
        var data = a.Export();
        Assert.Equal(a.LearnedJitter, data.LearnedJitter);

        var b = new StickCalibrator();
        b.Import(data);
        Assert.Equal(a.LearnedJitter, b.LearnedJitter);
        Assert.Equal(data, b.Export() with { OuterRadii = data.OuterRadii });
        Assert.Equal(a.JitterFilterStrength, b.JitterFilterStrength);

        // Filtering works straight away after loading.
        var (f, raw) = HoldSpread(b, new Random(18), 0.5, 0, noise: 3, samples: Hz);
        Assert.True(raw / f >= 4, $"{raw / f:F2}x");
    }

    [Fact]
    public void Learned_jitter_is_saved_in_the_profile_and_old_profiles_load()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ds4h-jitter-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cal = new ControllerCalibration("aa:bb", ControllerModel.DualShock4);
            Play(cal.RightStick, new Random(19), noise: 3);
            var store = new ProfileStore(dir);
            store.Save(cal.Export());
            var loaded = store.Load("aa:bb")!;
            Assert.Equal(cal.RightStick.LearnedJitter, loaded.RightStick.LearnedJitter);
            var again = new ControllerCalibration("aa:bb", ControllerModel.DualShock4);
            again.Import(loaded);
            Assert.Equal(cal.RightStick.LearnedJitter, again.RightStick.LearnedJitter);

            // A profile written before the jitter filter existed.
            var path = Directory.GetFiles(dir, "*.json").Single();
            var text = File.ReadAllText(path);
            var old = System.Text.RegularExpressions.Regex.Replace(text, @",\s*""LearnedJitter"":\s*[-0-9.Ee]+", "");
            Assert.DoesNotContain("LearnedJitter", old);
            File.WriteAllText(path, old);
            var legacy = store.Load("aa:bb")!;
            Assert.Equal(0f, legacy.RightStick.LearnedJitter);
            var fromOld = new ControllerCalibration("aa:bb", ControllerModel.DualShock4);
            fromOld.Import(legacy);
            Assert.Equal(cal.RightStick.CenterX, fromOld.RightStick.CenterX);
            Assert.Equal(0f, fromOld.RightStick.LearnedJitter);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_saved_jitter_is_ignored()
    {
        var c = new StickCalibrator();
        c.Import(new StickCalibrationData { LearnedJitter = float.NaN });
        Assert.Equal(0f, c.LearnedJitter);
        c.Import(new StickCalibrationData { LearnedJitter = 50f });
        Assert.True(c.LearnedJitter <= 0.06f);
        c.Import(new StickCalibrationData { LearnedJitter = -1f });
        Assert.Equal(0f, c.LearnedJitter);
    }

    [Fact]
    public void Reset_forgets_the_jitter()
    {
        var c = Jittery(noise: 3);
        c.Reset();
        Assert.Equal(0f, c.LearnedJitter);
        var raw = Raw(0.5, 0.5);
        var o = c.Process(raw.X, raw.Y);
        Assert.Equal(c.Map(raw.X, raw.Y), o);
    }

    [Fact]
    public void Controller_switch_turns_the_filter_off_for_both_sticks()
    {
        var cal = new ControllerCalibration("x", ControllerModel.DualShock4);
        Assert.True(cal.JitterFilterEnabled);
        cal.JitterFilterEnabled = false;
        Assert.False(cal.LeftStick.JitterFilterEnabled);
        Assert.False(cal.RightStick.JitterFilterEnabled);
        cal.JitterFilterEnabled = true;
        Assert.True(cal.LeftStick.JitterFilterEnabled && cal.RightStick.JitterFilterEnabled);
    }

    [Fact]
    public void Controller_process_with_the_default_interval_matches_the_old_call()
    {
        var a = new ControllerCalibration("a", ControllerModel.DualShock4);
        var b = new ControllerCalibration("b", ControllerModel.DualShock4);
        var rng = new Random(23);
        for (int i = 0; i < 3000; i++)
        {
            var r = Raw(0.5 + 0.3 * Math.Sin(i * 0.01), 0.2, rng, 3);
            var raw = new RawControllerState(r.X, r.Y, r.X, r.Y, 0, 0, Buttons.None);
            Assert.Equal(a.Process(raw), b.Process(raw, 1.0 / 250));
        }
        Assert.True(a.RightStick.LearnedJitter > 0.01f);
    }

    [Fact]
    public void Same_input_gives_the_same_output()
    {
        var a = Jittery(noise: 3, seed: 20);
        var b = Jittery(noise: 3, seed: 20);
        var rng = new Random(21);
        for (int i = 0; i < 3000; i++)
        {
            var r = Raw(Math.Sin(i * 0.02), 0.3, rng, 3);
            double dt = 0.001 + (i % 7) * 0.0007;
            Assert.Equal(a.Process(r.X, r.Y, dt), b.Process(r.X, r.Y, dt));
        }
        Assert.Equal(a.Export() with { OuterRadii = [] }, b.Export() with { OuterRadii = [] });
    }

    [Fact]
    public void Filtering_and_learning_allocate_nothing_per_report()
    {
        var c = Jittery(noise: 3);
        var reports = new (byte X, byte Y)[4096];
        var rng = new Random(22);
        for (int i = 0; i < reports.Length; i++)
            reports[i] = i % 512 < 256 ? Raw(0.5, 0.2, rng, 3) : Raw(Math.Sin(i * 0.05), Math.Cos(i * 0.05), rng, 3);
        float sum = 0;
        for (int i = 0; i < 10_000; i++) { var r = reports[i % reports.Length]; sum += c.Process(r.X, r.Y, 0.004).X; }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200_000; i++) { var r = reports[i % reports.Length]; sum += c.Process(r.X, r.Y, 0.004).X; }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.True(float.IsFinite(sum));
    }
}
