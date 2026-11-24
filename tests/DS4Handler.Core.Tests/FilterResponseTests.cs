using System.Text.Json;
using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;
using DS4Handler.Core.Tuning;
using Xunit.Abstractions;

namespace DS4Handler.Core.Tests;

/// <summary>The "Reactie" setting: trades jitter smoothing for less lag on a worn stick.</summary>
public class FilterResponseTests
{
    private const int Hz = 250;
    private const double Dt = 1.0 / Hz;
    private readonly ITestOutputHelper _out;

    public FilterResponseTests(ITestOutputHelper output) => _out = output;

    private static byte B(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    private static (byte X, byte Y) Raw(double x, double y, Random rng, int noise) =>
        (B(x * 127.5 + 127.5 + rng.Next(-noise, noise + 1)), B(-y * 127.5 + 127.5 + rng.Next(-noise, noise + 1)));

    /// <summary>A worn stick (+-3 counts of shake) after a few seconds of play, so the filter is at full strength.</summary>
    private static StickCalibrator Worn(float response)
    {
        var c = new StickCalibrator { FilterResponse = response };
        var rng = new Random(1);
        for (int s = 0; s < 6; s++)
        {
            for (int i = 0; i < Hz / 2; i++) { var r = Raw(0, 0, rng, 1); c.Process(r.X, r.Y, Dt); }
            for (int i = 0; i < 10; i++) { var r = Raw(0.9, 0.1, rng, 3); c.Process(r.X, r.Y, Dt); }
            for (int i = 0; i < Hz / 2; i++) { var r = Raw(0.5, 0.2, rng, 3); c.Process(r.X, r.Y, Dt); }
        }
        Assert.True(c.JitterFilterStrength >= 0.8f, $"strength {c.JitterFilterStrength}");
        return c;
    }

    /// <summary>
    /// Slow aiming: a 0.5 Hz sweep of +-0.3 around 0.4. Returns (lag in seconds, output spread around the
    /// true path shifted by that lag), each averaged over a few noise seeds: one seed alone is off by
    /// up to about 2 ms.
    /// </summary>
    private static (double Lag, double Noise) SlowAim(float response)
    {
        double lagSum = 0, noiseSum = 0;
        const int seeds = 8;
        for (int seed = 0; seed < seeds; seed++)
        {
            var c = Worn(response);
            var rng = new Random(100 + seed);
            int n = 4 * Hz;
            var reference = new double[n];
            var output = new double[n];
            for (int i = 0; i < n; i++)
            {
                double x = 0.4 + 0.3 * Math.Sin(2 * Math.PI * 0.5 * i * Dt);
                var r = Raw(x, 0, rng, 3);
                output[i] = c.Process(r.X, r.Y, Dt).X;
                reference[i] = c.Map(B(x * 127.5 + 127.5), 128).X; // the path without shake
            }
            var (lag, noise) = Lag(reference, output);
            lagSum += lag;
            noiseSum += noise;
        }
        return (lagSum / seeds, noiseSum / seeds);
    }

    /// <summary>Delay (to 0.05 sample) that best aligns output with reference after the first second, and the RMS error left.</summary>
    private static (double Lag, double Noise) Lag(double[] reference, double[] output)
    {
        double best = double.MaxValue, bestShift = 0;
        for (double shift = -2; shift <= 12; shift += 0.05)
        {
            double err = 0;
            int count = 0;
            for (int i = Hz; i < reference.Length - 3; i++)
            {
                double t = i - shift;
                int i0 = (int)Math.Floor(t);
                double v = reference[i0] + (reference[i0 + 1] - reference[i0]) * (t - i0);
                err += (output[i] - v) * (output[i] - v);
                count++;
            }
            err /= count;
            if (err < best) { best = err; bestShift = shift; }
        }
        return (bestShift * Dt, Math.Sqrt(best));
    }

    [Fact]
    public void Slow_aim_lags_far_less_than_the_plain_low_pass()
    {
        var (calmLag, calmNoise) = SlowAim(0f);
        var (midLag, midNoise) = SlowAim(0.5f);
        var (fastLag, fastNoise) = SlowAim(1f);
        _out.WriteLine($"calm: lag {calmLag * 1000:F1} ms, noise {calmNoise:F4}; half: lag {midLag * 1000:F1} ms, noise {midNoise:F4}; fast: lag {fastLag * 1000:F1} ms, noise {fastNoise:F4}");
        // The plain One Euro filter lagged 15.9 / 5.1 / 1.4 ms here (noise 0.0085 / 0.0099 / 0.0128).
        // The price of no lag is somewhat more leftover shake while aiming.
        Assert.True(calmLag <= 0.004, $"calm lag {calmLag * 1000:F1} ms");
        Assert.True(midLag <= 0.0051, $"half lag {midLag * 1000:F1} ms");
        Assert.True(fastLag <= 0.0016, $"fast lag {fastLag * 1000:F1} ms");
        Assert.True(calmNoise <= 0.0110, $"calm noise {calmNoise:F4}");
        Assert.True(calmNoise < midNoise && midNoise < fastNoise, "noise must rise with the setting");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void Steady_move_has_no_lag(float response)
    {
        // Noise-free so only the filter itself is measured; the old low-pass trailed by ~15 ms here.
        var c = Worn(response);
        const double speed = 1;
        double sum = 0;
        int count = 0;
        for (int i = 0; i < 0.75 * Hz; i++)
        {
            double x = 0.1 + speed * i * Dt;
            var raw = Raw(x, 0, new Random(0), 0);
            float o = c.Process(raw.X, raw.Y, Dt).X;
            if (i >= 0.4 * Hz) { sum += c.Map(raw.X, raw.Y).X - o; count++; }
        }
        double lagMs = sum / count / speed * 1000;
        _out.WriteLine($"response {response}: steady lag {lagMs:F2} ms");
        Assert.True(Math.Abs(lagMs) <= 0.5, $"steady lag {lagMs:F2} ms");
    }

    /// <summary>
    /// Sweeps from -0.5 to 0.4 at <paramref name="speed"/> units per second (infinity: one step), then
    /// holds, noise-free. Returns the largest overshoot past the target and the number of reports
    /// after the stop until the overshoot stays below 0.25%.
    /// </summary>
    private static (double Overshoot, int Reports) Stop(float response, double speed)
    {
        var c = Worn(response);
        var none = new Random(0);
        for (int i = 0; i < Hz / 2; i++) { var r = Raw(-0.5, 0, none, 0); c.Process(r.X, r.Y, Dt); }
        double x = -0.5;
        while (x < 0.4) { x = Math.Min(0.4, x + speed * Dt); var r = Raw(x, 0, none, 0); c.Process(r.X, r.Y, Dt); }
        double target = c.Map(B(0.4 * 127.5 + 127.5), 128).X, worst = 0;
        int last = 0;
        for (int i = 1; i <= 50; i++)
        {
            var r = Raw(0.4, 0, none, 0);
            double over = c.Process(r.X, r.Y, Dt).X - target;
            worst = Math.Max(worst, over);
            if (over > 0.0025) last = i;
        }
        return (worst, last);
    }

    [Fact]
    public void Sudden_stop_overshoots_little_and_briefly()
    {
        foreach (float response in new[] { 0f, 1f })
            foreach (double speed in new[] { 1, 3, 10, 30, 100, double.PositiveInfinity })
            {
                var (over, reports) = Stop(response, speed);
                _out.WriteLine($"response {response}, stop from {speed} u/s: overshoot {over * 100:F2}% for {reports} report(s)");
                // A flick or step (the motion threshold sees it) is taken at once: no overshoot.
                if (speed >= 100) Assert.True(over <= 0.0025 && reports == 0, $"{speed} u/s: {over:F4} for {reports}");
                // A stop out of a sweep is only seen over a few reports, the price of no lag while moving.
                Assert.True(over <= 0.012, $"{speed} u/s: overshoot {over:F4}");
                Assert.True(reports <= (response > 0 ? 4 : 10), $"{speed} u/s: overshoot lasts {reports} reports");
            }
    }

    [Fact]
    public void Fast_response_still_smooths_a_held_stick()
    {
        var c = Worn(1f);
        var rng = new Random(3);
        var filtered = new List<float>();
        var raw = new List<float>();
        for (int i = 0; i < 50; i++) { var r = Raw(0.5, 0, rng, 3); c.Process(r.X, r.Y, Dt); }
        for (int i = 0; i < 2 * Hz; i++)
        {
            var r = Raw(0.5, 0, rng, 3);
            filtered.Add(c.Process(r.X, r.Y, Dt).X);
            raw.Add(c.Map(r.X, r.Y).X);
        }
        double Spread(List<float> v) { double m = v.Average(); return Math.Sqrt(v.Average(p => (p - m) * (p - m))); }
        _out.WriteLine($"held spread raw {Spread(raw):F4}, fast {Spread(filtered):F4}");
        Assert.True(Spread(raw) / Spread(filtered) >= 1.3, $"reduction {Spread(raw) / Spread(filtered):F2}x");
    }

    [Fact]
    public void Default_response_is_zero_and_changes_nothing()
    {
        Assert.Equal(0f, new StickTuning().FilterResponse);
        var a = Worn(0f);
        var b = new StickCalibrator();
        var rng = new Random(1);
        // Same input as Worn(0): the default calibrator must give the same output.
        for (int s = 0; s < 6; s++)
        {
            for (int i = 0; i < Hz / 2; i++) { var r = Raw(0, 0, rng, 1); b.Process(r.X, r.Y, Dt); }
            for (int i = 0; i < 10; i++) { var r = Raw(0.9, 0.1, rng, 3); b.Process(r.X, r.Y, Dt); }
            for (int i = 0; i < Hz / 2; i++) { var r = Raw(0.5, 0.2, rng, 3); b.Process(r.X, r.Y, Dt); }
        }
        Assert.Equal(a.Process(180, 100, Dt), b.Process(180, 100, Dt));
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0.4f, 0.4f)]
    [InlineData(7f, 1f)]
    [InlineData(float.NaN, 0f)]
    public void Sanitize_keeps_response_in_range(float value, float expected)
    {
        var t = InputShaper.Sanitize(new StickTuning { FilterResponse = value });
        Assert.Equal(expected, t.FilterResponse);
    }

    [Fact]
    public void Response_round_trips_through_json_and_reaches_the_sticks()
    {
        var tuning = new InputTuning { LeftStick = new StickTuning { FilterResponse = 0.25f }, RightStick = new StickTuning { FilterResponse = 1f } };
        var back = JsonSerializer.Deserialize<InputTuning>(JsonSerializer.Serialize(tuning))!;
        Assert.Equal(0.25f, back.LeftStick.FilterResponse);
        Assert.Equal(1f, back.RightStick.FilterResponse);

        var cal = new ControllerCalibration("pad", ControllerModel.DualShock4) { Tuning = back };
        Assert.Equal(0.25f, cal.LeftStick.FilterResponse);
        Assert.Equal(1f, cal.RightStick.FilterResponse);
    }

    [Fact]
    public void Old_profiles_without_the_field_read_as_calm()
    {
        var back = JsonSerializer.Deserialize<InputTuning>("{\"LeftStick\":{\"Curve\":1.5}}")!;
        Assert.Equal(0f, back.LeftStick.FilterResponse);
        Assert.Equal(1.5f, back.LeftStick.Curve);
    }
}
