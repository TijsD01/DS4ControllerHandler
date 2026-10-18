using DS4Handler.Core.Calibration;

namespace DS4Handler.Core.Tests;

public class StickCalibratorTests
{
    // Converts a stick position (right/up positive, -1..1) to raw bytes, with optional count noise.
    private static (byte X, byte Y) Raw(float x, float y, Random? rnd = null, int noiseCounts = 0)
    {
        int nx = rnd is null ? 0 : rnd.Next(-noiseCounts, noiseCounts + 1);
        int ny = rnd is null ? 0 : rnd.Next(-noiseCounts, noiseCounts + 1);
        byte bx = (byte)Math.Clamp((int)MathF.Round(x * 127.5f + 127.5f) + nx, 0, 255);
        byte by = (byte)Math.Clamp((int)MathF.Round(-y * 127.5f + 127.5f) + ny, 0, 255);
        return (bx, by);
    }

    private static float Magnitude((float X, float Y) v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    private static void Rest(StickCalibrator c, float x, float y, Random rnd, int samples, int noise = 1)
    {
        for (int i = 0; i < samples; i++)
        {
            var (bx, by) = Raw(x, y, rnd, noise);
            c.Process(bx, by);
        }
    }

    // A quick flick out to the edge in some direction and back.
    private static void Flick(StickCalibrator c, float fromX, float fromY, float angle, float reach = 1f)
    {
        const int steps = 10;
        for (int i = 1; i <= steps; i++) Feed(c, fromX, fromY, angle, reach * i / steps);
        for (int i = 0; i < 4; i++) Feed(c, fromX, fromY, angle, reach);
        for (int i = steps; i >= 0; i--) Feed(c, fromX, fromY, angle, reach * i / steps);

        static void Feed(StickCalibrator c, float fx, float fy, float a, float r)
        {
            // The physical gate is a unit circle around the middle of the raw range.
            float px = fx + MathF.Cos(a) * r, py = fy + MathF.Sin(a) * r;
            float len = MathF.Sqrt(px * px + py * py);
            if (len > 1f) { px /= len; py /= len; }
            var (bx, by) = Raw(px, py);
            c.Process(bx, by);
        }
    }

    // One full-radius circle around the physical middle, as players do when they spin the stick.
    private static void Circle(StickCalibrator c, float reach = 1f, int steps = 128)
    {
        for (int i = 0; i <= steps; i++)
        {
            float a = 2f * MathF.PI * i / steps;
            var (bx, by) = Raw(MathF.Cos(a) * reach, MathF.Sin(a) * reach);
            c.Process(bx, by);
        }
    }

    private static StickCalibrator LearnDrift(float dx, float dy, int seed)
    {
        var c = new StickCalibrator();
        var rnd = new Random(seed);
        for (int release = 0; release < 30; release++)
        {
            Rest(c, dx, dy, rnd, 150);
            Flick(c, dx, dy, (float)(rnd.NextDouble() * 2 * Math.PI));
        }
        Rest(c, dx, dy, rnd, 150);
        return c;
    }

    [Fact]
    public void DriftingStick_RestsAtZero_AfterLearning()
    {
        const float dx = 0.12f, dy = -0.08f;
        var factory = new StickCalibrator();
        var learned = LearnDrift(dx, dy, seed: 1);

        Assert.InRange(learned.CenterX, dx - 0.01f, dx + 0.01f);
        Assert.InRange(learned.CenterY, dy - 0.01f, dy + 0.01f);

        var rnd = new Random(2);
        for (int i = 0; i < 500; i++)
        {
            var (bx, by) = Raw(dx, dy, rnd, 1);
            Assert.Equal((0f, 0f), learned.Map(bx, by));
        }

        var (fx, fy) = Raw(dx, dy);
        Assert.True(Magnitude(factory.Map(fx, fy)) > 0.05f);
    }

    [Fact]
    public void FullPush_StillReachesOne_AfterLearningDrift()
    {
        var c = LearnDrift(0.12f, -0.08f, seed: 3);
        Circle(c);
        Circle(c);

        for (int d = 0; d < 8; d++)
        {
            float a = d * MathF.PI / 4f;
            // Push all the way to the physical edge in this direction.
            var (bx, by) = Raw(MathF.Cos(a) * 1.5f, MathF.Sin(a) * 1.5f);
            if (d % 2 == 1) (bx, by) = Raw(MathF.Cos(a), MathF.Sin(a));
            c.Process(bx, by);
            var v = c.Process(bx, by);
            Assert.True(Magnitude(v) >= 0.95f && Magnitude(v) <= 1.0001f, $"dir {d}: {Magnitude(v)} radii {string.Join(",", c.Export().OuterRadii)} c {c.CenterX},{c.CenterY} dz {c.Deadzone}");
            // Direction is preserved roughly.
            float outAngle = MathF.Atan2(v.Y, v.X);
            float diff = MathF.Abs(MathF.IEEERemainder(outAngle - a, 2 * MathF.PI));
            Assert.True(diff < 0.2f, $"direction {d}: angle off by {diff}");
        }
    }

    [Fact]
    public void ScatteredReleases_GrowDeadzoneToCoverThem()
    {
        var c = new StickCalibrator();
        float factoryDeadzone = c.Deadzone;
        var rnd = new Random(4);

        (float X, float Y) Spot()
        {
            double a = rnd.NextDouble() * 2 * Math.PI;
            double r = 0.06 * Math.Sqrt(rnd.NextDouble());
            return (0.05f + (float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r));
        }

        for (int release = 0; release < 60; release++)
        {
            var s = Spot();
            Rest(c, s.X, s.Y, rnd, 150);
            Flick(c, s.X, s.Y, (float)(rnd.NextDouble() * 2 * Math.PI));
        }

        Assert.True(c.Deadzone > factoryDeadzone + 0.03f, $"deadzone {c.Deadzone}");
        Assert.True(c.Deadzone < 0.3f);

        // New releases at random spots of the same scatter should read as centered.
        int zero = 0, total = 0;
        for (int release = 0; release < 50; release++)
        {
            var s = Spot();
            for (int i = 0; i < 50; i++)
            {
                var (bx, by) = Raw(s.X, s.Y, rnd, 1);
                if (c.Map(bx, by) == (0f, 0f)) zero++;
                total++;
            }
        }
        Assert.True(zero >= total * 0.95, $"{zero}/{total} rest samples read as zero");
    }

    [Fact]
    public void ThumbHoldingOffCenter_DoesNotDragCenter()
    {
        var c = new StickCalibrator();
        var rnd = new Random(5);
        Rest(c, 0f, 0f, rnd, 400);

        // Thumb holds a gentle 25% push with natural tremor for a long time.
        Rest(c, 0.25f, 0.1f, rnd, 5000, noise: 4);

        Assert.InRange(c.CenterX, -0.02f, 0.02f);
        Assert.InRange(c.CenterY, -0.02f, 0.02f);
    }

    [Fact]
    public void ShortReach_IsStretchedToFull()
    {
        var c = new StickCalibrator();
        var rnd = new Random(6);
        Rest(c, 0f, 0f, rnd, 300);

        var (bx, by) = Raw(0.8f, 0f);
        Assert.True(c.Map(bx, by).X < 0.85f);

        for (int i = 0; i < 3; i++)
        {
            Flick(c, 0f, 0f, 0f, reach: 0.8f);
            Rest(c, 0f, 0f, rnd, 50);
        }

        var v = c.Map(bx, by);
        Assert.InRange(v.X, 0.98f, 1.0001f);
        Assert.InRange(v.Y, -0.01f, 0.01f);

        // Half way to the weak edge reads roughly half.
        var (hx, hy) = Raw(0.4f, 0f);
        Assert.InRange(c.Map(hx, hy).X, 0.4f, 0.55f);
    }

    [Fact]
    public void YAxis_IsInverted_UpIsPositive()
    {
        var c = new StickCalibrator();
        Assert.True(c.Map(127, 0).Y > 0.95f);   // raw 0 = pushed up
        Assert.True(c.Map(127, 255).Y < -0.95f); // raw 255 = pulled down
        Assert.True(c.Map(255, 127).X > 0.95f);
        Assert.True(c.Map(0, 127).X < -0.95f);
    }

    [Fact]
    public void ExportImport_RoundTrip_MapsIdentically()
    {
        var a = LearnDrift(0.1f, 0.05f, seed: 7);
        Circle(a, reach: 0.9f);
        var data = a.Export();

        var b = new StickCalibrator();
        b.Import(data);

        Assert.Equal(a.CenterX, b.CenterX);
        Assert.Equal(a.CenterY, b.CenterY);
        Assert.Equal(a.Deadzone, b.Deadzone);
        Assert.Equal(data.OuterRadii, b.Export().OuterRadii);
        Assert.Equal(data.RestSamples, b.Export().RestSamples);
        for (int x = 0; x < 256; x += 5)
            for (int y = 0; y < 256; y += 5)
                Assert.Equal(a.Map((byte)x, (byte)y), b.Map((byte)x, (byte)y));
    }

    [Fact]
    public void FactoryExport_HasDefaults()
    {
        var data = new StickCalibrator().Export();
        Assert.Equal(0f, data.CenterX);
        Assert.Equal(0f, data.CenterY);
        Assert.Empty(data.OuterRadii);
        Assert.Equal(0, data.RestSamples);
    }

    [Fact]
    public void LearningDisabled_FreezesModel()
    {
        var c = new StickCalibrator { LearningEnabled = false };
        var rnd = new Random(8);
        for (int i = 0; i < 10; i++)
        {
            Rest(c, 0.15f, 0.1f, rnd, 150);
            Flick(c, 0.15f, 0.1f, 0f, reach: 0.8f);
        }
        Assert.Equal(0, c.Export().RestSamples);
        Assert.Equal(new StickCalibrator().Deadzone, c.Deadzone);
        Assert.Empty(c.Export().OuterRadii);
        Assert.Equal(0f, c.CenterX);

        var learned = LearnDrift(0.12f, -0.08f, seed: 9);
        var before = learned.Export();
        learned.LearningEnabled = false;
        Rest(learned, -0.2f, 0.2f, rnd, 2000);
        Assert.Equal(before.CenterX, learned.CenterX);
        Assert.Equal(before.CenterY, learned.CenterY);
        Assert.Equal(before.OuterRadii, learned.Export().OuterRadii);
    }

    [Fact]
    public void Reset_RestoresFactory()
    {
        var c = LearnDrift(0.12f, -0.08f, seed: 10);
        Circle(c, reach: 0.85f);
        c.Reset();

        var factory = new StickCalibrator();
        Assert.Equal(0f, c.CenterX);
        Assert.Equal(0f, c.CenterY);
        Assert.Equal(factory.Deadzone, c.Deadzone);
        Assert.Empty(c.Export().OuterRadii);
        for (int x = 0; x < 256; x += 15)
            for (int y = 0; y < 256; y += 15)
                Assert.Equal(factory.Map((byte)x, (byte)y), c.Map((byte)x, (byte)y));
    }
}
