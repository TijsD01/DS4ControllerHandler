using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;
using DS4Handler.Core.Output;
using Xunit.Abstractions;

namespace DS4Handler.Core.Tests;

/// <summary>
/// Adversarial scenarios for the adaptive stick jitter filter (StickJitterFilter inside
/// StickCalibrator.Process(x, y, dt)): realistic jitter types, report-rate changes and gameplay
/// (micro-aim, rim rotation, flicks). Fixed seeds; measured numbers are written to the test output.
/// </summary>
public class JitterScenarioTests
{
    private readonly ITestOutputHelper _out;
    public JitterScenarioTests(ITestOutputHelper output) => _out = output;

    private const double Dt250 = 1.0 / 250;

    // ------------------------------------------------------------------ simulation

    /// <summary>Noise in raw counts for (rng, time in s, normalized position) → (nx, ny).</summary>
    private delegate (double X, double Y) Noise(Random rng, double t, double x, double y);

    private static Noise Uniform(int a) => (r, _, _, _) => (r.Next(-a, a + 1), r.Next(-a, a + 1));
    private static Noise UniformX(int a) => (r, _, _, _) => (r.Next(-a, a + 1), 0);
    private static Noise Gauss(double sigma) => (r, _, _, _) => (G(r) * sigma, G(r) * sigma);
    private static Noise Wobble(double hz, double amp, int white = 1) => (r, t, _, _) =>
        (amp * Math.Sin(2 * Math.PI * hz * t) + r.Next(-white, white + 1),
         amp * 0.6 * Math.Cos(2 * Math.PI * hz * 1.07 * t) + r.Next(-white, white + 1));
    private static Noise EdgeWorse(int center, int edge) => (r, _, x, y) =>
    {
        int a = (int)Math.Round(center + (edge - center) * Math.Min(1, Math.Sqrt(x * x + y * y)));
        return (r.Next(-a, a + 1), r.Next(-a, a + 1));
    };
    private static Noise WithSpikes(Noise baseNoise, double perSecond, int size) => (r, t, x, y) =>
    {
        var n = baseNoise(r, t, x, y);
        if (r.NextDouble() < perSecond * Dt250) return (n.X + (r.Next(2) == 0 ? -size : size), n.Y);
        return n;
    };

    private static double G(Random r)
    {
        double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static byte B(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    private sealed class Sim
    {
        public readonly StickCalibrator C;
        public readonly Random Rng;
        public Noise Noise;
        public double Dt = Dt250;
        /// <summary>Probability that a report is a repeat of the previous one (BT).</summary>
        public double RepeatChance;
        /// <summary>Relative random variation of dt (0.5 = ±50%).</summary>
        public double DtJitter;
        public double T;
        private byte _lx = 128, _ly = 128;

        public Sim(Noise noise, int seed, StickCalibrator? c = null)
        {
            Noise = noise;
            Rng = new Random(seed);
            C = c ?? new StickCalibrator();
        }

        public (byte X, byte Y) Raw(double x, double y)
        {
            var n = Noise(Rng, T, x, y);
            return (B(x * 127.5 + 127.5 + n.X), B(-y * 127.5 + 127.5 + n.Y));
        }

        /// <summary>Feeds the commanded position; returns (filtered, unfiltered map of the same raw, noise-free ideal).</summary>
        public ((float X, float Y) F, (float X, float Y) M, (float X, float Y) Ideal) Feed(double x, double y)
        {
            double dt = Dt * (1 + (Rng.NextDouble() * 2 - 1) * DtJitter);
            T += dt;
            byte bx, by;
            if (RepeatChance > 0 && Rng.NextDouble() < RepeatChance) (bx, by) = (_lx, _ly);
            else (bx, by) = Raw(x, y);
            _lx = bx; _ly = by;
            var f = C.Process(bx, by, dt);
            Assert.True(float.IsFinite(f.X) && float.IsFinite(f.Y), "NaN output");
            Assert.True(f.X * f.X + f.Y * f.Y <= 1.0001f, $"outside unit circle {f}");
            var m = C.Map(bx, by);
            var ideal = C.Map(B(x * 127.5 + 127.5), B(-y * 127.5 + 127.5));
            return (f, m, ideal);
        }

        public int PerSecond => (int)Math.Round(1 / Dt);

        /// <summary>Normal play so the jitter is learned: releases, flicks, holds at a few spots.</summary>
        public void Train(int seconds = 8)
        {
            for (int s = 0; s < seconds; s++)
            {
                for (int i = 0; i < PerSecond / 2; i++) Feed(0, 0);
                for (int i = 0; i < 10; i++) Feed(0.9, 0.1);
                var (hx, hy) = (s % 3) switch { 0 => (0.5, 0.2), 1 => (-0.3, 0.4), _ => (0.2, -0.6) };
                for (int i = 0; i < PerSecond / 2; i++) Feed(hx, hy);
            }
        }
    }

    private static float Len((float X, float Y) v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    private sealed record HoldResult(double Filtered, double Raw, double FilteredPeakToPeak, double Bias)
    {
        public double Ratio => Raw / Math.Max(1e-9, Filtered);
    }

    /// <summary>Holds at (x, y): RMS spread of filtered vs unfiltered output, filtered peak-to-peak, bias.</summary>
    private static HoldResult Hold(Sim s, double x, double y, double seconds = 2)
    {
        for (int i = 0; i < s.PerSecond / 4; i++) s.Feed(x, y);
        var f = new List<(float X, float Y)>();
        var m = new List<(float X, float Y)>();
        int n = (int)(seconds * s.PerSecond);
        for (int i = 0; i < n; i++)
        {
            var r = s.Feed(x, y);
            f.Add(r.F);
            m.Add(r.M);
        }
        static double Spread(List<(float X, float Y)> v)
        {
            double mx = v.Average(p => p.X), my = v.Average(p => p.Y);
            return Math.Sqrt(v.Average(p => (p.X - mx) * (p.X - mx) + (p.Y - my) * (p.Y - my)));
        }
        double fmx = f.Average(p => p.X), fmy = f.Average(p => p.Y);
        double ptp = f.Max(p => Math.Sqrt((p.X - fmx) * (p.X - fmx) + (p.Y - fmy) * (p.Y - fmy))) * 2;
        double bias = Math.Sqrt(Math.Pow(fmx - m.Average(p => p.X), 2) + Math.Pow(fmy - m.Average(p => p.Y), 2));
        return new HoldResult(Spread(f), Spread(m), ptp, bias);
    }

    private sealed record TrackResult(double StartDelayMs, double SteadyLagMs, double MaxStepX);

    /// <summary>
    /// Holds at (x0, y0), then moves slowly along +X at <paramref name="speed"/> (calibrated units/s)
    /// for <paramref name="seconds"/>. Start delay: time from the ideal output moving 0.01 to the
    /// filtered output moving 0.01. Steady lag: average (ideal - filtered) / speed over the last half.
    /// </summary>
    private static TrackResult Track(Sim s, double speed, double seconds = 2, double x0 = 0.3, double y0 = 0.1)
    {
        for (int i = 0; i < s.PerSecond; i++) s.Feed(x0, y0);
        var first = s.Feed(x0, y0);
        double fx0 = first.F.X, ix0 = first.Ideal.X;
        double tIdeal = double.NaN, tOut = double.NaN, lagSum = 0, maxStep = 0, prevX = first.F.X;
        int lagN = 0, n = (int)(seconds * s.PerSecond);
        double t0 = s.T;
        // Speed is in output units; raw moves slightly slower (output scale ~1/(1-dz)), close enough.
        for (int i = 1; i <= n; i++)
        {
            double x = x0 + speed * i / s.PerSecond;
            var r = s.Feed(x, y0);
            double t = s.T - t0;
            if (double.IsNaN(tIdeal) && r.Ideal.X - ix0 >= 0.01) tIdeal = t;
            if (double.IsNaN(tOut) && r.F.X - fx0 >= 0.01) tOut = t;
            if (i > n / 2) { lagSum += (r.Ideal.X - r.F.X) / speed; lagN++; }
            maxStep = Math.Max(maxStep, Math.Abs(r.F.X - prevX));
            prevX = r.F.X;
        }
        return new TrackResult((tOut - tIdeal) * 1000, lagSum / lagN * 1000, maxStep);
    }

    private static Sim Trained(Noise noise, int seed, double dt = Dt250)
    {
        var s = new Sim(noise, seed) { Dt = dt };
        s.Train();
        return s;
    }

    // ------------------------------------------------------------------ 1. tremble removal per jitter type

    public static IEnumerable<object[]> JitterTypes() => new[]
    {
        new object[] { "uniform±2" },
        new object[] { "uniform±3" },
        new object[] { "uniform±5" },
        new object[] { "gauss σ1.5" },
        new object[] { "gauss σ2.5" },
    };

    private static Noise ByName(string name) => name switch
    {
        "uniform±2" => Uniform(2),
        "uniform±3" => Uniform(3),
        "uniform±5" => Uniform(5),
        "gauss σ1.5" => Gauss(1.5),
        "gauss σ2.5" => Gauss(2.5),
        "x-only±3" => UniformX(3),
        "wobble 10Hz ±4" => Wobble(10, 4),
        "wobble 8Hz ±3" => Wobble(8, 3),
        "wobble 15Hz ±4" => Wobble(15, 4),
        _ => throw new ArgumentException(name),
    };

    [Theory]
    [MemberData(nameof(JitterTypes))]
    public void White_jitter_held_off_center_is_reduced_at_least_3x(string type)
    {
        var s = Trained(ByName(type), 401);
        var h = Hold(s, 0.45, -0.25);
        _out.WriteLine($"{type}: jitter {s.C.LearnedJitter / 0.00784:F2} counts, strength {s.C.JitterFilterStrength:F2}, raw {h.Raw:F4}, filtered {h.Filtered:F4} ({h.Ratio:F1}x), ptp {h.FilteredPeakToPeak:F4}, bias {h.Bias:F4}");
        Assert.True(h.Ratio >= 3, $"{type}: only {h.Ratio:F1}x quieter (raw {h.Raw:F4} → {h.Filtered:F4}), strength {s.C.JitterFilterStrength:F2}");
        Assert.True(h.Bias < 0.015, $"bias {h.Bias:F4}");
    }

    [Fact]
    public void Jitter_in_one_axis_only_is_reduced_at_least_3x()
    {
        var s = Trained(UniformX(3), 402);
        var h = Hold(s, 0.45, -0.25);
        _out.WriteLine($"x-only±3: jitter {s.C.LearnedJitter / 0.00784:F2} counts, strength {s.C.JitterFilterStrength:F2}, {h.Ratio:F1}x");
        Assert.True(h.Ratio >= 3, $"only {h.Ratio:F1}x quieter, strength {s.C.JitterFilterStrength:F2}, learned {s.C.LearnedJitter / 0.00784:F2} counts");
    }

    [Theory]
    [InlineData("wobble 8Hz ±3")]
    [InlineData("wobble 10Hz ±4")]
    [InlineData("wobble 15Hz ±4")]
    public void Low_frequency_wobble_of_a_worn_pot_is_reduced(string type)
    {
        var s = Trained(ByName(type), 403);
        var h = Hold(s, 0.45, -0.25);
        _out.WriteLine($"{type}: jitter {s.C.LearnedJitter / 0.00784:F2} counts, strength {s.C.JitterFilterStrength:F2}, raw {h.Raw:F4} → {h.Filtered:F4} ({h.Ratio:F1}x)");
        Assert.True(h.Ratio >= 2, $"{type}: only {h.Ratio:F1}x quieter (raw {h.Raw:F4} → {h.Filtered:F4}), strength {s.C.JitterFilterStrength:F2}");
    }

    [Fact]
    public void Jitter_that_is_worse_near_the_edge_is_handled()
    {
        var s = Trained(EdgeWorse(1, 5), 404);
        var inner = Hold(s, 0.25, 0.1);
        var outer = Hold(s, 0.6, 0.5);
        _out.WriteLine($"edge-worse: jitter {s.C.LearnedJitter / 0.00784:F2} counts, strength {s.C.JitterFilterStrength:F2}, inner {inner.Ratio:F1}x, outer {outer.Raw:F4}→{outer.Filtered:F4} ({outer.Ratio:F1}x)");
        Assert.True(outer.Ratio >= 2, $"outer only {outer.Ratio:F1}x");
        Assert.True(outer.Bias < 0.015 && inner.Bias < 0.015);
    }

    [Fact]
    public void Healthy_stick_is_unchanged()
    {
        // ±1 flicker at most, mostly still: output must equal Map exactly.
        var s = new Sim((r, _, _, _) => (r.Next(10) == 0 ? r.Next(-1, 2) : 0, r.Next(10) == 0 ? r.Next(-1, 2) : 0), 405);
        s.Train();
        Assert.Equal(0f, s.C.JitterFilterStrength);
        for (int i = 0; i < 2000; i++)
        {
            double a = i / 200.0;
            var r = s.Feed(0.7 * Math.Cos(a) * Math.Sin(i / 50.0), 0.7 * Math.Sin(a));
            Assert.Equal(r.M, r.F);
        }
    }

    [Fact]
    public void Healthy_stick_with_occasional_small_spikes_stays_unfiltered()
    {
        // Clean stick (±1 flicker) with a 4-count single-sample spike twice per second: the
        // spikes must not convince the learner the stick is shaky (that would add lag for nothing).
        var s = new Sim(WithSpikes((r, _, _, _) => (r.Next(4) == 0 ? r.Next(-1, 2) : 0, 0), 2, 4), 406);
        s.Train(20);
        _out.WriteLine($"spiky healthy: jitter {s.C.LearnedJitter / 0.00784:F2} counts, strength {s.C.JitterFilterStrength:F2}");
        Assert.True(s.C.JitterFilterStrength < 0.3f, $"strength {s.C.JitterFilterStrength:F2}, learned {s.C.LearnedJitter / 0.00784:F2} counts");
    }

    [Fact]
    public void Single_sample_spikes_on_a_jittery_stick_do_not_get_worse_than_unfiltered()
    {
        var s = Trained(WithSpikes(Uniform(3), 3, 12), 407);
        var h = Hold(s, 0.45, -0.25, seconds: 4);
        _out.WriteLine($"±3 + 12-count spikes: strength {s.C.JitterFilterStrength:F2}, raw {h.Raw:F4} → {h.Filtered:F4} ({h.Ratio:F1}x), ptp {h.FilteredPeakToPeak:F4}");
        Assert.True(h.Ratio >= 1.5, $"{h.Ratio:F1}x");
        Assert.True(h.Bias < 0.015);
    }

    // ------------------------------------------------------------------ 2. report rate, BT repeats, irregular dt

    [Fact]
    public void Same_stick_at_800_Hz_is_filtered_about_as_well_as_at_250_Hz()
    {
        var s250 = Trained(Uniform(3), 410);
        var s800 = Trained(Uniform(3), 410, dt: 1.0 / 800);
        var h250 = Hold(s250, 0.45, -0.25);
        var h800 = Hold(s800, 0.45, -0.25);
        _out.WriteLine($"250 Hz: strength {s250.C.JitterFilterStrength:F2} {h250.Ratio:F1}x; 800 Hz: strength {s800.C.JitterFilterStrength:F2} {h800.Ratio:F1}x");
        // At 800 Hz there are 3.2x more noisy samples per second, so per-sample tremble should drop at least as much.
        Assert.True(h800.Ratio >= 3, $"800 Hz only {h800.Ratio:F1}x (250 Hz {h250.Ratio:F1}x)");
    }

    [Fact]
    public void Switching_report_rate_mid_session_250_to_800_Hz_keeps_working()
    {
        var s = Trained(Uniform(3), 411);
        s.Dt = 1.0 / 800;
        var h = Hold(s, 0.45, -0.25);
        var flick = FlickSamples(s);
        _out.WriteLine($"after switch to 800 Hz: {h.Ratio:F1}x, flick {flick} samples");
        Assert.True(h.Ratio >= 3, $"{h.Ratio:F1}x");
        Assert.True(flick <= 6, $"flick took {flick} samples at 800 Hz"); // 6 samples at 800 Hz = 7.5 ms
    }

    [Fact]
    public void Bluetooth_repeated_reports_still_learn_and_filter()
    {
        // Lossy BT: 40% of reads return the previous report unchanged.
        var s = new Sim(Uniform(3), 412) { RepeatChance = 0.4 };
        s.Train();
        var h = Hold(s, 0.45, -0.25);
        _out.WriteLine($"BT 40% repeats: jitter {s.C.LearnedJitter / 0.00784:F2} counts, strength {s.C.JitterFilterStrength:F2}, {h.Ratio:F1}x");
        Assert.True(h.Ratio >= 2.5, $"only {h.Ratio:F1}x, strength {s.C.JitterFilterStrength:F2}");
    }

    [Fact]
    public void Irregular_dt_does_not_break_filtering_or_flicks()
    {
        var s = new Sim(Uniform(3), 413) { DtJitter = 0.8 };
        s.Train();
        var h = Hold(s, 0.45, -0.25);
        int flick = FlickSamples(s);
        _out.WriteLine($"irregular dt: {h.Ratio:F1}x, flick {flick}");
        Assert.True(h.Ratio >= 2.5, $"{h.Ratio:F1}x");
        Assert.True(flick <= 2, $"flick took {flick} samples");
    }

    [Fact]
    public void Huge_dt_gaps_and_zero_dt_do_not_produce_jumps_or_NaN()
    {
        var s = Trained(Uniform(3), 414);
        var c = s.C;
        var rng = new Random(1);
        double[] dts = { 0, 1e-9, 0.5, 5, double.NaN, -1, double.PositiveInfinity, 0.004 };
        for (int i = 0; i < 4000; i++)
        {
            var (bx, by) = s.Raw(0.4, 0.3);
            var o = c.Process(bx, by, dts[rng.Next(dts.Length)]);
            Assert.True(float.IsFinite(o.X) && float.IsFinite(o.Y) && o.X * o.X + o.Y * o.Y <= 1.0001f);
        }
    }

    // ------------------------------------------------------------------ 3. gameplay feel

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.1)]
    public void Micro_aim_slow_tracking_on_jittery_stick_does_not_feel_sticky(double speed)
    {
        var s = Trained(Uniform(3), 420);
        var tr = Track(s, speed);
        _out.WriteLine($"±3, track {speed} u/s: start delay {tr.StartDelayMs:F0} ms, steady lag {tr.SteadyLagMs:F0} ms, strength {s.C.JitterFilterStrength:F2}");
        Assert.True(tr.StartDelayMs < 150, $"output starts moving {tr.StartDelayMs:F0} ms after the stick (steady lag {tr.SteadyLagMs:F0} ms)");
        Assert.True(tr.SteadyLagMs < 150, $"steady lag {tr.SteadyLagMs:F0} ms");
    }

    [Fact]
    public void Micro_aim_very_slow_tracking_does_not_feel_sticky()
    {
        var s = Trained(Uniform(3), 421);
        var tr = Track(s, 0.05, seconds: 3);
        _out.WriteLine($"±3, track 0.05 u/s: start delay {tr.StartDelayMs:F0} ms, steady lag {tr.SteadyLagMs:F0} ms");
        Assert.True(tr.StartDelayMs < 150, $"output starts moving {tr.StartDelayMs:F0} ms after the stick");
        Assert.True(tr.SteadyLagMs < 150, $"steady lag {tr.SteadyLagMs:F0} ms");
    }

    [Fact]
    public void Slow_tracking_on_healthy_stick_has_no_lag()
    {
        var s = new Sim((r, _, _, _) => (r.Next(8) == 0 ? r.Next(-1, 2) : 0, 0), 422);
        s.Train();
        var tr = Track(s, 0.1);
        _out.WriteLine($"healthy, track 0.1 u/s: start {tr.StartDelayMs:F0} ms, lag {tr.SteadyLagMs:F0} ms");
        Assert.True(Math.Abs(tr.StartDelayMs) < 30 && Math.Abs(tr.SteadyLagMs) < 30, $"{tr}");
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Medium_speed_aim_sweep_lags_little(double speed)
    {
        // Sweeping the crosshair across the screen: 0.3 → 0.3 + speed*0.25 s.
        var s = Trained(Uniform(3), 423);
        var tr = Track(s, speed, seconds: 0.25, x0: 0.2);
        _out.WriteLine($"±3, sweep {speed} u/s: start {tr.StartDelayMs:F0} ms, lag {tr.SteadyLagMs:F0} ms");
        Assert.True(tr.SteadyLagMs < 40, $"sweep at {speed} u/s lags {tr.SteadyLagMs:F0} ms (start delay {tr.StartDelayMs:F0} ms)");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(3.0)]
    public void Rim_rotation_keeps_radius_at_least_098_without_wobble(double revPerSecond)
    {
        var s = Trained(Uniform(3), 424);
        double min = 2, max = 0;
        int n = (int)(2 * s.PerSecond);
        for (int i = 0; i < 50; i++) s.Feed(1.05, 0);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * revPerSecond * i / s.PerSecond;
            var r = s.Feed(1.05 * Math.Cos(a), 1.05 * Math.Sin(a));
            double len = Len(r.F);
            min = Math.Min(min, len);
            max = Math.Max(max, len);
        }
        _out.WriteLine($"rim {revPerSecond} rev/s: radius {min:F4}..{max:F4}");
        Assert.True(min >= 0.98, $"radius dips to {min:F4}");
        Assert.True(max - min <= 0.02, $"radius wobbles {min:F4}..{max:F4}");
    }

    [Fact]
    public void Rim_rotation_at_real_reach_keeps_full_radius()
    {
        // Physical reach just at the edge (0.99..1.0) rather than past it.
        var s = Trained(Uniform(3), 425);
        double min = 2;
        for (int i = 0; i < 4 * s.PerSecond; i++)
        {
            double a = 2 * Math.PI * 0.7 * i / s.PerSecond;
            var r = s.Feed(0.995 * Math.Cos(a), 0.995 * Math.Sin(a));
            if (i > 50) min = Math.Min(min, Len(r.F));
        }
        _out.WriteLine($"rim at reach: min radius {min:F4}");
        Assert.True(min >= 0.98, $"radius dips to {min:F4}");
    }

    /// <summary>Hold at center-ish, flick to (0.9, 0): samples until filtered X ≥ 90% of ideal.</summary>
    private static int FlickSamples(Sim s, double fromX = 0.2, double toX = 0.9)
    {
        for (int i = 0; i < s.PerSecond / 2; i++) s.Feed(fromX, 0.05);
        for (int k = 1; k <= 200; k++)
        {
            var r = s.Feed(toX, 0.05);
            if (r.F.X >= 0.9 * r.Ideal.X) return k;
        }
        return int.MaxValue;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Fast_flick_is_followed_within_two_samples(int noise)
    {
        var s = Trained(Uniform(noise), 430 + noise);
        int a = FlickSamples(s, 0.2, 0.9);
        int b = FlickSamples(s, 0.9, -0.8);
        _out.WriteLine($"±{noise}: flick {a} samples, reverse flick {b} samples");
        Assert.True(a <= 2, $"flick took {a} samples");
        Assert.True(b <= 2, $"reverse flick took {b}");
    }

    [Fact]
    public void Ramp_flick_over_a_few_samples_is_followed_closely()
    {
        // Real flicks take 15-40 ms, not one sample: 0.2 → 0.9 over 6 samples.
        var s = Trained(Uniform(3), 435);
        for (int i = 0; i < 125; i++) s.Feed(0.2, 0.05);
        double worst = 0;
        int worstAt = 0;
        for (int i = 1; i <= 30; i++)
        {
            double x = 0.2 + 0.7 * Math.Min(1, i / 6.0);
            var r = s.Feed(x, 0.05);
            double err = r.Ideal.X - r.F.X;
            if (err > worst) { worst = err; worstAt = i; }
            if (i >= 9) Assert.True(Math.Abs(err) < 0.05, $"sample {i}: {r.F.X:F3} vs {r.Ideal.X:F3}");
        }
        _out.WriteLine($"ramp flick: worst trail {worst:F3} at sample {worstAt}");
    }

    [Fact]
    public void Direction_reversals_do_not_overshoot()
    {
        var s = Trained(Uniform(3), 436);
        double overshoot = 0, maxErr = 0;
        for (int cycle = 0; cycle < 10; cycle++)
        {
            // Strafe-aim: triangle wave between -0.6 and 0.6 at 3 u/s, sharp reversals.
            for (int i = 0; i < 100; i++)
            {
                double ph = (i % 100) / 100.0;
                double x = ph < 0.5 ? -0.6 + 2.4 * ph * 2 / 2 : 0.6 - 2.4 * (ph - 0.5) * 2 / 2;
                var r = s.Feed(x, 0.1);
                overshoot = Math.Max(overshoot, Math.Abs(r.F.X) - Math.Abs(s.C.Map(B(0.6 * 127.5 + 127.5), 128).X));
                maxErr = Math.Max(maxErr, Math.Abs(r.F.X - r.Ideal.X));
            }
        }
        _out.WriteLine($"reversals: overshoot {overshoot:F4}, max error {maxErr:F4}");
        Assert.True(overshoot <= 0.02, $"overshoot {overshoot:F4}");
        Assert.True(maxErr <= 0.08, $"max tracking error {maxErr:F4}");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void Returning_to_center_reads_exactly_zero(int noise)
    {
        var s = Trained(Uniform(noise), 440 + noise);
        for (int k = 0; k < 20; k++)
        {
            for (int i = 0; i < 60; i++) s.Feed(0.6 * Math.Cos(k), 0.6 * Math.Sin(k));
            // Spring back over 5 samples, then rest (with only ±1 at rest: the pot settles at center).
            for (int i = 1; i <= 5; i++) s.Feed(0.6 * Math.Cos(k) * (1 - i / 5.0), 0.6 * Math.Sin(k) * (1 - i / 5.0));
            var saved = s.Noise;
            s.Noise = Uniform(1);
            for (int i = 0; i < 10; i++) s.Feed(0, 0);
            for (int i = 0; i < 200; i++)
            {
                var r = s.Feed(0, 0);
                Assert.Equal(0f, r.F.X);
                Assert.Equal(0f, r.F.Y);
            }
            s.Noise = saved;
        }
    }

    [Fact]
    public void Gentle_return_to_center_has_no_jump_bigger_than_the_deadzone_step()
    {
        // Slowly letting the stick come back (0.5 → 0 over 1 s): the last nonzero output before
        // it reads zero must be small, i.e. no visible snap.
        var s = Trained(Uniform(3), 445);
        for (int i = 0; i < 125; i++) s.Feed(0.5, 0);
        double prev = 0.5, maxStep = 0;
        for (int i = 0; i <= 250; i++)
        {
            var r = s.Feed(0.5 * (1 - i / 250.0), 0);
            maxStep = Math.Max(maxStep, Math.Abs(r.F.X - prev));
            prev = r.F.X;
        }
        _out.WriteLine($"gentle return: largest single-sample step {maxStep:F4}");
        Assert.True(maxStep < 0.05, $"snap of {maxStep:F4}");
    }

    // ------------------------------------------------------------------ 4. interaction with drift learning

    [Fact]
    public void Filter_does_not_change_drift_learning()
    {
        StickCalibrator Run(bool filter)
        {
            var c = new StickCalibrator { JitterFilterEnabled = filter };
            var s = new Sim(Uniform(1), 450, c);
            double dx = 0.08, dy = -0.05;
            for (int k = 0; k < 40; k++)
            {
                for (int i = 0; i < 20; i++) s.Feed(0.9 * Math.Cos(k), 0.9 * Math.Sin(k));
                for (int i = 0; i < 250; i++) s.Feed(dx, dy);
                if (k == 20) { dx = 0.12; dy = -0.02; }
            }
            return c;
        }
        var on = Run(true);
        var off = Run(false);
        _out.WriteLine($"on: center ({on.CenterX:F4},{on.CenterY:F4}) releases {on.Releases} dz {on.Deadzone:F4}; off: ({off.CenterX:F4},{off.CenterY:F4}) releases {off.Releases}");
        Assert.Equal(off.CenterX, on.CenterX);
        Assert.Equal(off.CenterY, on.CenterY);
        Assert.Equal(off.Releases, on.Releases);
        Assert.Equal(off.Deadzone, on.Deadzone);
        Assert.InRange(on.CenterX, 0.10f, 0.14f);
    }

    [Fact]
    public void Slightly_jittery_stick_resting_with_drift_reads_zero_after_learning() => JitteryRestWithDrift(1);

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Jittery_stick_resting_with_drift_reads_zero_after_learning(int noise) => JitteryRestWithDrift(noise);

    [Theory]
    [InlineData(3)]
    public void Jittery_stick_resting_at_center_reads_zero(int noise)
    {
        var s = new Sim(Uniform(noise), 452);
        for (int k = 0; k < 30; k++)
        {
            for (int i = 0; i < 20; i++) s.Feed(0.9 * Math.Cos(k), 0.9 * Math.Sin(k));
            for (int i = 0; i < 250; i++) s.Feed(0, 0);
        }
        int nonzero = 0;
        double max = 0;
        for (int i = 0; i < 1000; i++)
        {
            var o = s.Feed(0, 0).F;
            if (Len(o) > 0) nonzero++;
            max = Math.Max(max, Len(o));
        }
        _out.WriteLine($"±{noise} at center: dz {s.C.Deadzone:F3} releases {s.C.Releases}, nonzero {nonzero}/1000, max {max:F3}");
        Assert.True(nonzero <= 5, $"{nonzero}/1000 rest samples nonzero, max {max:F3}, dz {s.C.Deadzone:F3}, releases {s.C.Releases}");
    }

    private void JitteryRestWithDrift(int noise)
    {
        // Same jitter at rest as when held (pot noise does not care where the stick is), drifting at (0.06, 0.04).
        var s = new Sim(Uniform(noise), 451);
        for (int k = 0; k < 30; k++)
        {
            for (int i = 0; i < 20; i++) s.Feed(0.9 * Math.Cos(k), 0.9 * Math.Sin(k));
            for (int i = 0; i < 250; i++) s.Feed(0.06, 0.04);
        }
        int nonzero = 0;
        for (int i = 0; i < 1000; i++) if (Len(s.Feed(0.06, 0.04).F) > 0) nonzero++;
        _out.WriteLine($"±{noise} at rest: center ({s.C.CenterX:F3},{s.C.CenterY:F3}) dz {s.C.Deadzone:F3} releases {s.C.Releases}, nonzero {nonzero}/1000");
        Assert.True(s.C.Releases >= 10, $"only {s.C.Releases} releases detected in 30");
        Assert.True(nonzero <= 10, $"{nonzero}/1000 rest samples are nonzero (dz {s.C.Deadzone:F3}, releases {s.C.Releases})");
    }

    // ------------------------------------------------------------------ 5. allocations with the filter on

    [Fact]
    public void Pipeline_with_jitter_filter_allocates_nothing_per_report()
    {
        var cal = new ControllerCalibration("alloc", ControllerModel.DualShock4);
        var rng = new Random(460);
        var reports = new byte[5000][];
        for (int i = 0; i < reports.Length; i++)
        {
            var b = new byte[64];
            b[0] = 0x01;
            double a = i / 40.0;
            b[1] = B(128 + 100 * Math.Cos(a) + rng.Next(-3, 4));
            b[2] = B(128 + 100 * Math.Sin(a) + rng.Next(-3, 4));
            b[3] = B(128 + 50 + rng.Next(-3, 4));
            b[4] = B(128 - 40 + rng.Next(-3, 4));
            b[5] = 0x08;
            reports[i] = b;
        }
        long sum = 0;
        void Pass(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!ReportParser.TryParse(ControllerModel.DualShock4, reports[i % reports.Length], out var raw, out _)) continue;
                var st = cal.Process(raw, 0.004);
                var x = XboxMapper.Map(st);
                var d = Ds4Mapper.Map(st);
                sum += x.RightThumbX + d.RightX;
            }
        }
        Pass(30_000);
        Assert.True(cal.RightStick.JitterFilterStrength > 0.5f, $"filter not active: {cal.RightStick.JitterFilterStrength}");
        long before = GC.GetAllocatedBytesForCurrentThread();
        Pass(100_000);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(bytes == 0, $"{bytes} bytes over 100k reports");
        Assert.NotEqual(0, sum);
    }
}
