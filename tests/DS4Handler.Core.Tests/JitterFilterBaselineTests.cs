using DS4Handler.Core.Calibration;
using Xunit.Abstractions;

namespace DS4Handler.Core.Tests;

/// <summary>
/// The lag-free jitter filter must never be worse than the plain One Euro filter it replaced, in any
/// of these scenarios on a worn stick (+-3 counts): the start of a move (from rest and from a hold),
/// very slow moves and a held stick. The bounds are the old filter's numbers in this same harness
/// (20 seeds; the filter before commit ca980e4).
/// </summary>
public class JitterFilterBaselineTests
{
    private const int Hz = 250;
    private const double Dt = 1.0 / Hz;
    private const int Seeds = 20;
    private readonly ITestOutputHelper _out;

    public JitterFilterBaselineTests(ITestOutputHelper output) => _out = output;

    private delegate (double X, double Y) Noise(Random r, double t);

    private static Noise U(int a) => (r, _) => (r.Next(-a, a + 1), r.Next(-a, a + 1));
    private static Noise UX(int a) => (r, _) => (r.Next(-a, a + 1), 0);
    private static Noise G(double s) => (r, _) => (Gauss(r) * s, Gauss(r) * s);
    private static Noise Wobble(double hz, double amp) => (r, t) =>
        (amp * Math.Sin(2 * Math.PI * hz * t) + r.Next(-1, 2), amp * 0.6 * Math.Cos(2 * Math.PI * hz * 1.07 * t) + r.Next(-1, 2));

    private static double Gauss(Random r)
    {
        double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static byte B(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    private sealed class Sim
    {
        public readonly StickCalibrator C;
        public readonly Random Rng;
        public readonly Noise N;
        public double T;

        public Sim(float response, int seed, Noise noise)
        {
            C = new StickCalibrator { FilterResponse = response };
            Rng = new Random(seed);
            N = noise;
        }

        /// <summary>Feeds (x, y) plus noise; returns (filtered X, noise-free mapped X).</summary>
        public (double F, double I) Feed(double x, double y, Noise? noise = null)
        {
            T += Dt;
            var n = (noise ?? N)(Rng, T);
            var f = C.Process(B(x * 127.5 + 127.5 + n.X), B(-y * 127.5 + 127.5 + n.Y), Dt);
            return (f.X, C.Map(B(x * 127.5 + 127.5), B(-y * 127.5 + 127.5)).X);
        }

        /// <summary>A few seconds of play so the jitter is learned: releases, flicks, holds.</summary>
        public void Train()
        {
            for (int s = 0; s < 6; s++)
            {
                for (int i = 0; i < Hz / 2; i++) Feed(0, 0, U(1));
                for (int i = 0; i < 10; i++) Feed(0.9, 0.1);
                var (hx, hy) = (s % 3) switch { 0 => (0.5, 0.2), 1 => (-0.3, 0.4), _ => (0.2, -0.6) };
                for (int i = 0; i < Hz / 2; i++) Feed(hx, hy);
            }
        }
    }

    /// <summary>Mean lag (ms) 20, 40 and 80 ms after the stick starts to move along X at <paramref name="speed"/>.</summary>
    private static double[] Onset(float response, double speed, bool fromRest)
    {
        var lag = new double[3];
        int[] at = { 5, 10, 20 };
        for (int seed = 0; seed < Seeds; seed++)
        {
            var s = new Sim(response, 1000 + seed, U(3));
            s.Train();
            double x0 = fromRest ? 0 : 0.3, y0 = fromRest ? 0 : 0.1;
            Noise? still = fromRest ? U(1) : null;
            for (int i = 0; i < Hz; i++) s.Feed(x0, y0, still);
            double before = s.Feed(x0, y0, still).I;
            var outs = new List<double>();
            var ideal = new List<double>();
            int start = -1;
            for (int i = 1; i <= 75; i++)
            {
                var r = s.Feed(x0 + speed * i * Dt, y0);
                outs.Add(r.F);
                ideal.Add(r.I);
                // From rest the output only starts once the stick leaves the deadzone.
                if (start < 0 && r.I != before) start = outs.Count - 1;
            }
            int end = Math.Min(ideal.Count - 1, start + 40);
            double v = (ideal[end] - ideal[start + 10]) / ((end - start - 10) * Dt);
            for (int j = 0; j < 3; j++)
            {
                int k = Math.Min(outs.Count - 1, start + at[j]);
                lag[j] += (ideal[k] - outs[k]) / v * 1000 / Seeds;
            }
        }
        return lag;
    }

    public static IEnumerable<object[]> OnsetCases() => new[]
    {
        // response, speed, from rest, old lag at 20 / 40 / 80 ms
        new object[] { 0f, 1.0, true, 12.7, 16.9, 10.3 },
        new object[] { 0f, 3.0, true, 7.0, 5.7, 4.9 },
        new object[] { 0f, 1.0, false, 19.8, 19.9, 15.1 },
        new object[] { 0f, 3.0, false, 11.7, 6.1, 6.4 },
        new object[] { 1f, 1.0, true, -0.8, 8.7, 2.5 },
        new object[] { 1f, 3.0, true, 1.8, 1.4, 1.1 },
        new object[] { 1f, 1.0, false, 2.8, -2.7, 1.8 },
        new object[] { 1f, 3.0, false, 5.0, 1.5, 1.5 },
    };

    [Theory]
    [MemberData(nameof(OnsetCases))]
    public void Start_of_a_move_is_at_least_as_quick_as_before(float response, double speed, bool fromRest, double old20, double old40, double old80)
    {
        var lag = Onset(response, speed, fromRest);
        _out.WriteLine($"response {response}, {speed} u/s from {(fromRest ? "rest" : "a hold")}: lag {lag[0]:F1} / {lag[1]:F1} / {lag[2]:F1} ms at 20 / 40 / 80 ms (old {old20} / {old40} / {old80})");
        double[] old = { old20, old40, old80 };
        for (int j = 0; j < 3; j++)
            Assert.True(lag[j] <= old[j] + 0.3, $"at {(j == 2 ? 80 : 20 * (j + 1))} ms: {lag[j]:F1} ms, old {old[j]} ms");
    }

    /// <summary>From a hold, a slow move along X: (ms until the output moved 0.01 after the ideal did, mean lag in ms over the second half).</summary>
    private static (double Start, double Steady) Ramp(float response, double speed, double seconds)
    {
        double start = 0, steady = 0;
        for (int seed = 0; seed < Seeds; seed++)
        {
            var s = new Sim(response, 2000 + seed, U(3));
            s.Train();
            for (int i = 0; i < Hz; i++) s.Feed(0.3, 0.1);
            var first = s.Feed(0.3, 0.1);
            int n = (int)(seconds * Hz);
            var ideal = new double[n + 1];
            var outs = new double[n + 1];
            double tIdeal = double.NaN, tOut = double.NaN;
            for (int i = 1; i <= n; i++)
            {
                (outs[i], ideal[i]) = s.Feed(0.3 + speed * i * Dt, 0.1);
                if (double.IsNaN(tIdeal) && ideal[i] - first.I >= 0.01) tIdeal = i * Dt;
                if (double.IsNaN(tOut) && outs[i] - first.F >= 0.01) tOut = i * Dt;
            }
            double v = (ideal[n] - ideal[n / 2]) / ((n - n / 2) * Dt), lag = 0;
            for (int i = n / 2; i <= n; i++) lag += (ideal[i] - outs[i]) / v;
            start += (tOut - tIdeal) * 1000 / Seeds;
            steady += lag / (n - n / 2 + 1) * 1000 / Seeds;
        }
        return (start, steady);
    }

    [Theory]
    // response, speed, old start delay and steady lag (ms)
    [InlineData(0f, 0.05, -14, 42.8)]
    [InlineData(0f, 0.1, 14, 37.7)]
    [InlineData(0f, 0.3, 28, 23.8)]
    [InlineData(1f, 0.05, -218, 1.6)]
    [InlineData(1f, 0.1, -100, 2.0)]
    [InlineData(1f, 0.3, -27, 1.7)]
    public void Very_slow_moves_lag_no_more_than_before(float response, double speed, double oldStart, double oldSteady)
    {
        var (start, steady) = Ramp(response, speed, speed < 0.1 ? 3 : 2);
        _out.WriteLine($"response {response}, {speed} u/s: start {start:F0} ms (old {oldStart}), steady lag {steady:F1} ms (old {oldSteady})");
        Assert.True(start <= oldStart + 2, $"start {start:F0} ms, old {oldStart} ms");
        Assert.True(steady <= oldSteady + 0.5, $"steady lag {steady:F1} ms, old {oldSteady} ms");
    }

    /// <summary>Held at (0.45, -0.25) for 2 s: (unfiltered / filtered spread, filtered peak-to-peak), averaged over 8 seeds.</summary>
    private static (double Ratio, double PeakToPeak) Held(float response, Noise noise)
    {
        double ratio = 0, ptp = 0;
        const int seeds = 8;
        for (int seed = 0; seed < seeds; seed++)
        {
            var s = new Sim(response, 4000 + seed, noise);
            s.Train();
            for (int i = 0; i < Hz / 4; i++) s.Feed(0.45, -0.25);
            var f = new List<(double X, double Y)>();
            var m = new List<(double X, double Y)>();
            for (int i = 0; i < 2 * Hz; i++)
            {
                s.T += Dt;
                var n = noise(s.Rng, s.T);
                byte bx = B(0.45 * 127.5 + 127.5 + n.X), by = B(0.25 * 127.5 + 127.5 + n.Y);
                var o = s.C.Process(bx, by, Dt);
                var mm = s.C.Map(bx, by);
                f.Add((o.X, o.Y));
                m.Add((mm.X, mm.Y));
            }
            static double Spread(List<(double X, double Y)> v)
            {
                double mx = v.Average(p => p.X), my = v.Average(p => p.Y);
                return Math.Sqrt(v.Average(p => (p.X - mx) * (p.X - mx) + (p.Y - my) * (p.Y - my)));
            }
            double fx = f.Average(p => p.X), fy = f.Average(p => p.Y);
            ratio += Spread(m) / Spread(f) / seeds;
            ptp += 2 * f.Max(p => Math.Sqrt((p.X - fx) * (p.X - fx) + (p.Y - fy) * (p.Y - fy))) / seeds;
        }
        return (ratio, ptp);
    }

    private static Noise ByName(string name) => name switch
    {
        "uniform±5" => U(5),
        "uniform±3" => U(3),
        "uniform±2" => U(2),
        "gauss σ2.5" => G(2.5),
        "x-only±3" => UX(3),
        _ => Wobble(8, 3),
    };

    [Theory]
    // response, jitter type, old spread reduction and peak-to-peak
    [InlineData(0f, "uniform±5", 4.84, 0.0355)]
    [InlineData(0f, "uniform±3", 5.90, 0.0215)]
    [InlineData(0f, "uniform±2", 3.49, 0.0257)]
    [InlineData(0f, "gauss σ2.5", 4.64, 0.0486)]
    [InlineData(0f, "x-only±3", 2.98, 0.0370)]
    [InlineData(0f, "wobble 8Hz", 2.54, 0.0423)]
    [InlineData(1f, "uniform±3", 1.56, 0.0587)]
    [InlineData(1f, "gauss σ2.5", 1.39, 0.1136)]
    public void Held_stick_is_as_calm_as_before(float response, string type, double oldRatio, double oldPeakToPeak)
    {
        var (ratio, ptp) = Held(response, ByName(type));
        _out.WriteLine($"response {response}, {type}: {ratio:F2}x quieter, peak-to-peak {ptp:F4} (old {oldRatio}x, {oldPeakToPeak})");
        Assert.True(ratio >= oldRatio * 0.99, $"{ratio:F2}x, old {oldRatio}x");
        Assert.True(ptp <= oldPeakToPeak * 1.02, $"peak-to-peak {ptp:F4}, old {oldPeakToPeak}");
    }

    [Fact]
    public void Zero_or_invalid_interval_does_not_poison_the_filter()
    {
        var f = new StickJitterFilter();
        var rng = new Random(9);
        foreach (float dt in new[] { 0f, -1f, float.NaN, 0.004f })
            for (int i = 0; i < 50; i++)
            {
                var o = f.Filter(0.5f + (float)rng.NextDouble() * 0.02f, 0.2f, 0.016f, 1f, dt);
                Assert.True(float.IsFinite(o.X) && float.IsFinite(o.Y), $"dt {dt}: {o}");
            }
        // And it still follows the stick afterwards (a NaN inside used to freeze the output).
        (float X, float Y) last = default;
        for (int i = 0; i < 100; i++) last = f.Filter(0.8f, 0.2f, 0.016f, 1f, 0.004f);
        Assert.True(last.X > 0.75f, $"output stuck at {last}");
    }
}
