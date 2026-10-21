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

    /// <summary>Slow aiming: a 0.5 Hz sweep of +-0.3 around 0.4. Returns (lag in seconds, output spread around the true path).</summary>
    private static (double Lag, double Noise) SlowAim(StickCalibrator c)
    {
        var rng = new Random(7);
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
        double lag = Lag(reference, output);
        int shift = (int)Math.Round(lag / Dt);
        double sum = 0;
        int count = 0;
        for (int i = Hz + shift; i < n; i++) { double d = output[i] - reference[i - shift]; sum += d * d; count++; }
        return (lag, Math.Sqrt(sum / count));
    }

    private static double Lag(double[] reference, double[] output)
    {
        double best = double.MaxValue, bestShift = 0;
        for (int shift = 0; shift <= 50; shift++)
        {
            double err = 0;
            for (int i = Hz; i < reference.Length; i++) { double d = output[i] - reference[i - shift]; err += d * d; }
            if (err < best) { best = err; bestShift = shift; }
        }
        return bestShift * Dt;
    }

    [Fact]
    public void Fast_response_cuts_slow_aim_lag_at_least_in_half()
    {
        var (calmLag, calmNoise) = SlowAim(Worn(0f));
        var (midLag, midNoise) = SlowAim(Worn(0.5f));
        var (fastLag, fastNoise) = SlowAim(Worn(1f));
        _out.WriteLine($"calm: lag {calmLag * 1000:F0} ms, noise {calmNoise:F4}; half: lag {midLag * 1000:F0} ms, noise {midNoise:F4}; fast: lag {fastLag * 1000:F0} ms, noise {fastNoise:F4}");
        Assert.True(midLag < calmLag && midLag >= fastLag, "lag must fall steadily with the setting");
        Assert.True(fastLag <= calmLag / 2, $"lag {calmLag * 1000:F0} ms -> {fastLag * 1000:F0} ms");
        Assert.True(fastLag <= 0.024, $"fast lag {fastLag * 1000:F0} ms");
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
