using System.Text.Json.Nodes;
using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;
using DS4Handler.Core.Output;
using DS4Handler.Core.Profiles;
using DS4Handler.Core.Tuning;

namespace DS4Handler.Core.Tests;

/// <summary>
/// End-to-end QA of the user tuning: raw HID report bytes → ReportParser → ControllerCalibration
/// (learning + tuning) → XboxMapper / Ds4Mapper, plus fuzzing, sweeps and persistence.
/// Fixed seeds throughout.
/// </summary>
public sealed class TuningEndToEndTests : IDisposable
{
    private const int Hz = 250;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ds4handler-tuning-e2e-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static byte B(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);
    private static (byte X, byte Y) ToRaw(double x, double y) => (B(x * 127.5 + 127.5), B(-y * 127.5 + 127.5));
    private static float Len(float x, float y) => MathF.Sqrt(x * x + y * y);

    // ================================================================ byte-level pad

    /// <summary>A DS4 over USB or Bluetooth that produces real report bytes and runs them through the pipeline.</summary>
    private sealed class Pad
    {
        public readonly ControllerCalibration Cal;
        private readonly byte[] _buf;
        private readonly bool _bt;
        private readonly Random _rng;
        private byte _counter;
        public double DriftX, DriftY, Noise = 0.003;
        public XboxReport Xbox;
        public Ds4OutputReport Ds4;
        public CalibratedState State;

        public Pad(bool bluetooth, int seed, InputTuning? tuning = null)
        {
            _bt = bluetooth;
            _rng = new Random(seed);
            _buf = new byte[bluetooth ? 78 : 64];
            _buf[0] = bluetooth ? (byte)0x11 : (byte)0x01;
            if (bluetooth) { _buf[1] = 0xC0; _buf[2] = 0x00; }
            Cal = new ControllerCalibration("ds4-a4538512abcd", ControllerModel.DualShock4);
            if (tuning != null) Cal.Tuning = tuning;
        }

        public void Bytes(byte lx, byte ly, byte rx, byte ry, byte l2, byte r2)
        {
            var d = _buf.AsSpan(_bt ? 3 : 1);
            d[0] = lx; d[1] = ly; d[2] = rx; d[3] = ry;
            unchecked { _counter++; }
            d[4] = 8;
            d[5] = (byte)((l2 > 0 ? 0x04 : 0) | (r2 > 0 ? 0x08 : 0));
            d[6] = (byte)(_counter << 2);
            d[7] = l2; d[8] = r2;
            Assert.True(ReportParser.TryParse(ControllerModel.DualShock4, _buf, out var raw, out var conn,
                _bt ? ConnectionType.Bluetooth : ConnectionType.Usb));
            Assert.Equal(lx, raw.LeftX);
            Assert.Equal(r2, raw.R2);
            State = Cal.Process(raw, 1.0 / Hz);
            AssertStateSane(State);
            Xbox = XboxMapper.Map(State);
            Ds4 = Ds4Mapper.Map(State);
        }

        /// <summary>Left stick commanded position (drift fades out as the stick is pushed), with noise.</summary>
        public void Left(double x, double y, byte r2 = 0)
        {
            double r = Math.Sqrt(x * x + y * y);
            double w = Math.Max(0, 1 - r);
            var (bx, by) = ToRaw(x + DriftX * w + G() * Noise, y + DriftY * w + G() * Noise);
            Bytes(bx, by, 128, 128, 0, r2);
        }

        public void FlickAndRelease()
        {
            double a = _rng.NextDouble() * 2 * Math.PI;
            for (int i = 0; i < 20; i++) Left(0.95 * Math.Cos(a), 0.95 * Math.Sin(a));
            for (int i = 0; i < Hz; i++) Left(0, 0);
        }

        public void Rotate(int turns)
        {
            for (int i = 0; i < turns * Hz; i++)
            {
                double a = 2 * Math.PI * i / Hz;
                Left(Math.Cos(a), Math.Sin(a));
            }
        }

        public double G()
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }

    private static void AssertStateSane(in CalibratedState s)
    {
        foreach (var v in new[] { s.LeftX, s.LeftY, s.RightX, s.RightY })
        {
            Assert.True(float.IsFinite(v), $"stick not finite: {s}");
            Assert.InRange(v, -1f, 1f);
        }
        foreach (var v in new[] { s.L2, s.R2 })
        {
            Assert.True(float.IsFinite(v), $"trigger not finite: {s}");
            Assert.InRange(v, 0f, 1f);
        }
    }

    private static InputTuning RocketLeague(DeadzoneShape shape = DeadzoneShape.Square) => new()
    {
        LeftStick = new StickTuning { Shape = shape, InnerDeadzone = 0.08f, SquareOutput = true },
    };

    // ================================================================ Rocket League sticks

    [Theory]
    [InlineData(false, 0.08, -0.05, 1)]
    [InlineData(true, -0.06, 0.07, 2)]
    [InlineData(true, 0.0, 0.0, 3)]
    public void Rocket_League_square_deadzone_on_drifting_stick_rests_at_zero_and_diagonals_hit_both_axes_full(
        bool bluetooth, double driftX, double driftY, int seed)
    {
        var pad = new Pad(bluetooth, seed, RocketLeague()) { DriftX = driftX, DriftY = driftY };
        for (int i = 0; i < 40; i++) { pad.FlickAndRelease(); if (i % 4 == 0) pad.Rotate(1); }

        // Released on the drifting spot: nothing reaches the game.
        for (int i = 0; i < 200; i++)
        {
            pad.Left(0, 0);
            Assert.True(pad.Xbox.LeftThumbX == 0 && pad.Xbox.LeftThumbY == 0,
                $"released stick leaks {pad.Xbox.LeftThumbX},{pad.Xbox.LeftThumbY} (center {pad.Cal.LeftStick.CenterX:F3},{pad.Cal.LeftStick.CenterY:F3})");
            Assert.Equal(128, pad.Ds4.LeftX);
        }

        pad.Noise = 0;
        // Full diagonals on the round gate: both axes (close to) saturate, far beyond the ~70% a round
        // range gives. The drift model shifts a physical 45° push by a few degrees, and the elliptical
        // mapping has a sharp corner, so the minor axis is allowed to sit a bit below full when drifting.
        int minMinor = driftX == 0 && driftY == 0 ? 32700 : (int)(0.85 * 32767);
        foreach (var (sx, sy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            for (int i = 0; i < 30; i++) pad.Left(sx * Math.Sqrt(0.5), sy * Math.Sqrt(0.5));
            int ax = Math.Abs((int)pad.Xbox.LeftThumbX), ay = Math.Abs((int)pad.Xbox.LeftThumbY);
            Assert.True(Math.Sign(pad.Xbox.LeftThumbX) == sx && Math.Sign(pad.Xbox.LeftThumbY) == sy, $"diagonal ({sx},{sy}) wrong direction");
            Assert.True(Math.Max(ax, ay) >= 32700 && Math.Min(ax, ay) >= minMinor,
                $"diagonal ({sx},{sy}) gives {pad.Xbox.LeftThumbX},{pad.Xbox.LeftThumbY} (state {pad.State.LeftX},{pad.State.LeftY})");
        }

        // Straight pushes still saturate one axis and leave the other at (near) zero.
        foreach (var (sx, sy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            for (int i = 0; i < 30; i++) pad.Left(sx, sy);
            short main = sx != 0 ? pad.Xbox.LeftThumbX : pad.Xbox.LeftThumbY;
            short cross = sx != 0 ? pad.Xbox.LeftThumbY : pad.Xbox.LeftThumbX;
            Assert.True(Math.Abs((int)main) >= 32767, $"straight ({sx},{sy}) gives {pad.Xbox.LeftThumbX},{pad.Xbox.LeftThumbY}");
            // The drift model bends a physical straight push by a few degrees at full deflection, and the
            // square mapping stretches the minor axis near the edge (~1.4x); without drift it stays tiny.
            int maxLeak = driftX == 0 && driftY == 0 ? 1000 : (int)(0.16 * 32767);
            Assert.True(Math.Abs((int)cross) < maxLeak, $"straight ({sx},{sy}) leaks {cross} into the other axis");
        }
    }

    [Fact]
    public void Raw_corner_slams_with_square_output_give_full_corner_and_never_overflow()
    {
        var pad = new Pad(false, 9, RocketLeague());
        for (int i = 0; i < 10; i++) pad.FlickAndRelease();
        pad.Rotate(3);
        foreach (var (x, y) in new (byte, byte)[] { (255, 0), (0, 0), (255, 255), (0, 255) })
        {
            for (int i = 0; i < 10; i++) pad.Bytes(x, y, 128, 128, 0, 0);
            Assert.Equal(x == 255 ? 1 : -1, Math.Sign(pad.Xbox.LeftThumbX));
            Assert.Equal(y == 0 ? 1 : -1, Math.Sign(pad.Xbox.LeftThumbY));
            Assert.InRange(Math.Abs((int)pad.Xbox.LeftThumbX), 32700, 32768);
            Assert.InRange(Math.Abs((int)pad.Xbox.LeftThumbY), 32700, 32768);
        }
    }

    // ================================================================ sweeps: monotonic, continuous

    public static IEnumerable<object[]> SweepTunings()
    {
        foreach (DeadzoneShape shape in Enum.GetValues<DeadzoneShape>())
        {
            yield return new object[] { new StickTuning { Shape = shape, InnerDeadzone = 0.1f } };
            yield return new object[] { new StickTuning { Shape = shape, InnerDeadzone = 0.25f, OuterDeadzone = 0.1f } };
            yield return new object[] { new StickTuning { Shape = shape, InnerDeadzone = 0.1f, SquareOutput = true } };
            yield return new object[] { new StickTuning { Shape = shape, InnerDeadzone = 0.15f, Curve = 2.5f, Sensitivity = 1.6f } };
            yield return new object[] { new StickTuning { Shape = shape, InnerDeadzone = 0.05f, Curve = 0.3f, Sensitivity = 0.4f, SquareOutput = true } };
            yield return new object[] { new StickTuning { Shape = shape, InnerDeadzone = 0.1f, AntiDeadzone = 0.25f, Curve = 1.8f, Sensitivity = 2f } };
            yield return new object[] { new StickTuning { Shape = shape, InnerDeadzone = 0.6f, OuterDeadzone = 0.35f } }; // span floor
            yield return new object[] { new StickTuning { Shape = shape, InnerDeadzone = 0.9f, AntiDeadzone = 0.9f, Curve = 5f, Sensitivity = 3f, SquareOutput = true, InvertX = true } };
        }
    }

    [Theory]
    [MemberData(nameof(SweepTunings))]
    public void Radius_sweep_is_monotonic_and_continuous_in_every_direction(StickTuning tuning)
    {
        var t = InputShaper.Sanitize(tuning);
        const int Steps = 4000;
        for (int k = 0; k < 72; k++)
        {
            double a = 2 * Math.PI * k / 72 + 0.013;
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            float prevX = 0, prevY = 0, prevLen = 0;
            bool started = false;
            bool xLive = false, yLive = false;
            for (int i = 0; i <= Steps; i++)
            {
                float r = (float)i / Steps;
                var (x, y) = InputShaper.ShapeStick(r * c, r * s, t);
                Assert.True(float.IsFinite(x) && float.IsFinite(y));
                Assert.InRange(x, -1f, 1f);
                Assert.InRange(y, -1f, 1f);
                float len = Len(x, y);
                // Never goes backwards along the ray, on the magnitude or on either axis.
                Assert.True(len >= prevLen - 1e-5f, $"{t}: magnitude falls at angle {k} r={r}: {prevLen} -> {len}");
                // Axial works per axis, so no axis may give anything back either.
                const float axisTol = 1e-5f;
                Assert.True(MathF.Abs(x) >= MathF.Abs(prevX) - axisTol && MathF.Abs(y) >= MathF.Abs(prevY) - axisTol,
                    $"{t}: axis falls at angle {k} r={r}: ({prevX},{prevY}) -> ({x},{y})");
                // Continuous: the only allowed jump is the anti-deadzone at the first live sample.
                float jump = MathF.Max(MathF.Abs(x - prevX), MathF.Abs(y - prevY));
                float allowed = started ? 0.02f : t.AntiDeadzone * 1.05f + 0.02f;
                // A curve below 1 is steep at 0 (r^0.3) but still continuous; a 4000-step sweep allows more.
                if (!started && t.Curve < 1f) allowed += MathF.Pow(1f / Steps / MinSpan(t), t.Curve) * MathF.Min(t.Sensitivity, 1f) * 3f + 0.05f;
                else if (t.Curve < 1f) allowed = MathF.Max(allowed, 0.05f);
                // Axial applies the anti-deadzone per axis: the second axis also jumps when it goes live, and
                // with square output that jump moves the other axis too (the point leaves the disc).
                bool newAxis = t.Shape == DeadzoneShape.Axial && ((!xLive && x != 0) || (!yLive && y != 0));
                if (newAxis) allowed = MathF.Max(allowed, t.SquareOutput ? 1.01f : t.AntiDeadzone * 1.05f + 0.02f);
                Assert.True(jump <= allowed, $"{t}: jump {jump} at angle {k} r={r} ({prevX},{prevY}) -> ({x},{y})");
                xLive |= x != 0; yLive |= y != 0;
                if (len > 0) started = true;
                prevX = x; prevY = y; prevLen = len;
            }
            // Full push reaches full magnitude in every direction, for every shape (the sanitize cap keeps
            // square and cross diagonals live).
            if (t.Sensitivity >= 1f)
                Assert.True(prevLen >= 0.999f, $"{t}: full push at angle {k} only reaches {prevLen}");
        }
    }

    private static float MinSpan(StickTuning t) => MathF.Max(1f - t.InnerDeadzone - t.OuterDeadzone, InputShaper.MinSpan);

    [Theory]
    [InlineData(DeadzoneShape.Radial)]
    [InlineData(DeadzoneShape.Square)]
    [InlineData(DeadzoneShape.Axial)]
    public void Byte_level_sweep_through_the_pipeline_is_monotonic_per_axis(DeadzoneShape shape)
    {
        var cal = new ControllerCalibration("ds4-a4538512abcd", ControllerModel.DualShock4)
        {
            LearningEnabled = false,
            JitterFilterEnabled = false,
            Tuning = new InputTuning { LeftStick = new StickTuning { Shape = shape, InnerDeadzone = 0.12f, AntiDeadzone = 0.15f, Curve = 1.4f, SquareOutput = true } },
        };
        // Push right from the center at several fixed vertical offsets: X output must never fall.
        for (int y = 0; y <= 255; y += 15)
        {
            int prev = int.MinValue;
            for (int x = 128; x <= 255; x++)
            {
                var s = cal.Process(new RawControllerState((byte)x, (byte)y, 128, 128, 0, 0, Buttons.None));
                AssertStateSane(s);
                int xb = XboxMapper.Map(s).LeftThumbX;
                Assert.True(xb >= prev, $"{shape}: X falls at raw ({x},{y}): {prev} -> {xb}");
                prev = xb;
            }
            Assert.InRange(prev, 32700, 32767); // full reach on every row, axial included
        }
    }

    // ================================================================ fuzz

    private static float WildFloat(Random rng) => rng.Next(12) switch
    {
        0 => float.NaN,
        1 => float.PositiveInfinity,
        2 => float.NegativeInfinity,
        3 => -rng.NextSingle() * 10f,
        4 => rng.NextSingle() * 10f,
        5 => 0f,
        6 => 1f,
        7 => float.MaxValue,
        8 => float.Epsilon,
        _ => rng.NextSingle(),
    };

    private static StickTuning WildStick(Random rng) => new()
    {
        Shape = (DeadzoneShape)rng.Next(-1, 5),
        InnerDeadzone = WildFloat(rng),
        OuterDeadzone = WildFloat(rng),
        AntiDeadzone = WildFloat(rng),
        Curve = WildFloat(rng),
        Sensitivity = WildFloat(rng),
        SquareOutput = rng.Next(2) == 0,
        InvertX = rng.Next(2) == 0,
        InvertY = rng.Next(2) == 0,
    };

    private static TriggerTuning WildTrigger(Random rng) => new()
    {
        InnerDeadzone = WildFloat(rng),
        OuterDeadzone = WildFloat(rng),
        AntiDeadzone = WildFloat(rng),
        Curve = WildFloat(rng),
    };

    private static InputTuning WildTuning(Random rng) => new()
    {
        LeftStick = WildStick(rng),
        RightStick = WildStick(rng),
        L2 = WildTrigger(rng),
        R2 = WildTrigger(rng),
        BluetoothPollIntervalMs = rng.Next(3) == 0 ? rng.Next(int.MinValue, int.MaxValue) : rng.Next(-5, 30),
        OutputRateHz = rng.Next(3) == 0 ? rng.Next(int.MinValue, int.MaxValue) : rng.Next(-100, 3000),
    };

    [Fact]
    public void Fuzzed_tunings_over_the_whole_raw_grid_always_give_finite_in_range_output()
    {
        var rng = new Random(4242);
        for (int n = 0; n < 60; n++)
        {
            var cal = new ControllerCalibration("ds4-a4538512abcd", ControllerModel.DualShock4)
            {
                LearningEnabled = false,
                JitterFilterEnabled = false,
                Tuning = WildTuning(rng),
            };
            var t = cal.Tuning;
            // Sanitized result is inside every documented range.
            foreach (var st in new[] { t.LeftStick, t.RightStick })
            {
                Assert.True(Enum.IsDefined(st.Shape));
                Assert.InRange(st.InnerDeadzone, 0f, InputShaper.MaxStickInnerDeadzone);
                Assert.InRange(st.OuterDeadzone, 0f, InputShaper.MaxOuterDeadzone);
                Assert.True(st.InnerDeadzone + st.OuterDeadzone <= 1f - InputShaper.MinSpan + 1e-6f);
                Assert.InRange(st.AntiDeadzone, 0f, InputShaper.MaxAntiDeadzone);
                Assert.InRange(st.Curve, InputShaper.MinCurve, InputShaper.MaxCurve);
                Assert.InRange(st.Sensitivity, InputShaper.MinSensitivity, InputShaper.MaxSensitivity);
            }
            foreach (var tt in new[] { t.L2, t.R2 })
            {
                Assert.InRange(tt.InnerDeadzone, 0f, InputShaper.MaxTriggerInnerDeadzone);
                Assert.InRange(tt.OuterDeadzone, 0f, InputShaper.MaxOuterDeadzone);
                Assert.True(tt.InnerDeadzone + tt.OuterDeadzone <= 1f - InputShaper.MinSpan + 1e-6f);
                Assert.InRange(tt.AntiDeadzone, 0f, InputShaper.MaxAntiDeadzone);
                Assert.InRange(tt.Curve, InputShaper.MinCurve, InputShaper.MaxCurve);
            }
            Assert.InRange(t.BluetoothPollIntervalMs, 1, 16);
            Assert.Contains(t.OutputRateHz, InputShaper.OutputRates.ToArray());
            Assert.Same(t, InputShaper.Sanitize(t)); // idempotent

            int step = n < 4 ? 1 : 5; // the first few tunings walk every single raw byte pair
            for (int x = 0; x <= 255; x += step)
            for (int y = 0; y <= 255; y += step)
            {
                var s = cal.Process(new RawControllerState((byte)x, (byte)y, (byte)y, (byte)x, (byte)x, (byte)y, Buttons.None));
                AssertStateSane(s);
                var xb = XboxMapper.Map(s);
                _ = Ds4Mapper.Map(s);
                Assert.Equal(xb, XboxMapper.Map(s));
            }
        }
    }

    [Fact]
    public void Default_tuning_is_bit_identical_to_no_tuning_over_the_whole_raw_grid_with_learning_on()
    {
        var plain = new ControllerCalibration("ds4-a4538512abcd", ControllerModel.DualShock4);
        var tuned = new ControllerCalibration("ds4-a4538512abcd", ControllerModel.DualShock4)
        {
            Tuning = new InputTuning
            {
                // Values that look different but are neutral.
                LeftStick = new StickTuning { Shape = DeadzoneShape.Square },
                RightStick = new StickTuning { Shape = DeadzoneShape.Axial },
                BluetoothPollIntervalMs = 8,
                OutputRateHz = 250,
            },
        };
        for (int x = 0; x <= 255; x++)
        for (int y = 0; y <= 255; y++)
        {
            var raw = new RawControllerState((byte)x, (byte)y, (byte)(255 - y), (byte)x, (byte)y, (byte)x, Buttons.Cross);
            var a = plain.Process(raw, 0.004);
            var b = tuned.Process(raw, 0.004);
            Assert.True(BitEqual(a, b), $"raw ({x},{y}): {a} vs {b}");
        }
    }

    private static bool BitEqual(CalibratedState a, CalibratedState b) =>
        Bits(a.LeftX) == Bits(b.LeftX) && Bits(a.LeftY) == Bits(b.LeftY) && Bits(a.RightX) == Bits(b.RightX)
        && Bits(a.RightY) == Bits(b.RightY) && Bits(a.L2) == Bits(b.L2) && Bits(a.R2) == Bits(b.R2) && a.Buttons == b.Buttons;

    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    // ================================================================ triggers

    /// <summary>Worn R2 that physically stops at <paramref name="stop"/>: presses through the byte pipeline until learned.</summary>
    private static Pad LearnWornR2(int stop, InputTuning tuning, int seed)
    {
        var pad = new Pad(true, seed, tuning);
        var rng = new Random(seed);
        for (int i = 0; i < 2 * Hz; i++) pad.Bytes(128, 128, 128, 128, 0, 0);
        for (int press = 0; press < 15; press++)
        {
            for (int i = 0; i <= 12; i++) pad.Bytes(128, 128, 128, 128, 0, B(stop * i / 12.0));
            for (int i = 0; i < 2 * Hz; i++) pad.Bytes(128, 128, 128, 128, 0, (byte)Math.Min(stop, stop - 1 + rng.Next(0, 2)));
            for (int i = 12; i >= 0; i--) pad.Bytes(128, 128, 128, 128, 0, B(stop * i / 12.0));
            for (int i = 0; i < 60; i++) pad.Bytes(128, 128, 128, 128, 0, 0);
        }
        return pad;
    }

    [Theory]
    [InlineData(200, 0.0f, 0.10f, 0.0f, 1.0f)]
    [InlineData(180, 0.05f, 0.15f, 0.2f, 1.6f)]
    [InlineData(230, 0.1f, 0.0f, 0.0f, 0.6f)]
    public void Worn_R2_with_trigger_tuning_still_reaches_255_and_stays_monotonic(int stop, float inner, float outer, float anti, float curve)
    {
        var tuning = new InputTuning { R2 = new TriggerTuning { InnerDeadzone = inner, OuterDeadzone = outer, AntiDeadzone = anti, Curve = curve } };
        var pad = LearnWornR2(stop, tuning, stop);
        pad.Bytes(128, 128, 128, 128, 0, (byte)(stop - 1));
        Assert.Equal(255, pad.Xbox.RightTrigger);
        Assert.Equal(255, pad.Ds4.R2);

        // Whole raw range through the (frozen) mapping: monotonic, 0 at rest, full at the worn stop.
        pad.Cal.LearningEnabled = false;
        int prev = -1;
        for (int raw = 0; raw <= 255; raw++)
        {
            pad.Bytes(128, 128, 128, 128, 0, (byte)raw);
            int v = pad.Xbox.RightTrigger;
            Assert.True(v >= prev, $"R2 falls at raw {raw}: {prev} -> {v}");
            if (raw == 0) Assert.Equal(0, v);
            prev = v;
        }
        Assert.Equal(255, prev);
    }

    [Fact]
    public void Trigger_inner_deadzone_keeps_light_touches_at_zero_and_clears_the_DS4_digital_bit()
    {
        var pad = new Pad(false, 5, new InputTuning { L2 = new TriggerTuning { InnerDeadzone = 0.15f } });
        pad.Cal.LearningEnabled = false;
        for (int raw = 0; raw <= 38; raw++) // 38/255 = 0.149
        {
            pad.Bytes(128, 128, 128, 128, (byte)raw, 0);
            Assert.Equal(0, pad.Xbox.LeftTrigger);
            Assert.Equal(0, pad.Ds4.L2);
            Assert.Equal(Ds4OutputButtons.None, pad.Ds4.Buttons & Ds4OutputButtons.TriggerLeft);
        }
        pad.Bytes(128, 128, 128, 128, 255, 0);
        Assert.Equal(255, pad.Xbox.LeftTrigger);
        Assert.NotEqual(Ds4OutputButtons.None, pad.Ds4.Buttons & Ds4OutputButtons.TriggerLeft);
    }

    [Fact]
    public void Trigger_anti_deadzone_jumps_past_the_digital_threshold_on_the_first_live_count()
    {
        var pad = new Pad(false, 6, new InputTuning { R2 = new TriggerTuning { InnerDeadzone = 0.05f, AntiDeadzone = 0.3f } });
        pad.Cal.LearningEnabled = false;
        int firstLive = -1;
        for (int raw = 0; raw <= 255; raw++)
        {
            pad.Bytes(128, 128, 128, 128, 0, (byte)raw);
            bool digital = (pad.Ds4.Buttons & Ds4OutputButtons.TriggerRight) != 0;
            if (pad.Xbox.RightTrigger == 0)
            {
                Assert.False(digital, $"digital R2 on while analog is 0 at raw {raw}");
                continue;
            }
            if (firstLive < 0)
            {
                firstLive = raw;
                Assert.InRange(pad.Xbox.RightTrigger, 76, 80); // 0.3 * 255
            }
            Assert.True(digital, $"digital R2 off while analog is {pad.Xbox.RightTrigger} at raw {raw}");
        }
        Assert.InRange(firstLive, 13, 16); // just past 0.05 * 255
    }

    // ================================================================ persistence

    private static InputTuning EveryFieldChanged() => new()
    {
        LeftStick = new StickTuning
        {
            Shape = DeadzoneShape.Square, InnerDeadzone = 0.11f, OuterDeadzone = 0.07f, AntiDeadzone = 0.13f,
            Curve = 1.7f, Sensitivity = 1.3f, SquareOutput = true, InvertX = true, InvertY = false,
        },
        RightStick = new StickTuning
        {
            Shape = DeadzoneShape.Axial, InnerDeadzone = 0.21f, OuterDeadzone = 0.17f, AntiDeadzone = 0.23f,
            Curve = 0.45f, Sensitivity = 2.75f, SquareOutput = false, InvertX = false, InvertY = true,
        },
        L2 = new TriggerTuning { InnerDeadzone = 0.03f, OuterDeadzone = 0.09f, AntiDeadzone = 0.19f, Curve = 2.2f },
        R2 = new TriggerTuning { InnerDeadzone = 0.33f, OuterDeadzone = 0.29f, AntiDeadzone = 0.41f, Curve = 0.35f },
        BluetoothPollIntervalMs = 7,
        OutputRateHz = 125,
    };

    [Fact]
    public void Every_tuning_field_round_trips_through_ProfileStore_and_back_into_the_calibration()
    {
        var store = new ProfileStore(_dir);
        var cal = new ControllerCalibration("ds4-a4538512abcd", ControllerModel.DualShock4) { Tuning = EveryFieldChanged() };
        Assert.Equal(EveryFieldChanged(), cal.Tuning);
        store.Save(cal.Export());

        var loaded = store.Load("ds4-a4538512abcd")!;
        Assert.Equal(EveryFieldChanged(), loaded.Tuning);
        var next = new ControllerCalibration("ds4-a4538512abcd", ControllerModel.DualShock4);
        next.Import(loaded);
        Assert.Equal(EveryFieldChanged(), next.Tuning);
        Assert.Single(store.LoadAll());
        Assert.Equal(EveryFieldChanged(), store.LoadAll()[0].Tuning);
    }

    [Fact]
    public void A_v1_1_0_profile_file_without_Tuning_loads_with_its_learning_and_default_tuning()
    {
        // Exactly what v1.1.0 wrote: today's file minus the Tuning section (nothing else changed).
        var cal = new ControllerCalibration("ds4-a4538512abcd", ControllerModel.DualShock4);
        var rng = new Random(77);
        for (int i = 0; i < 3000; i++) cal.Process(new RawControllerState(B(140 + rng.Next(-1, 2)), 120, 128, 128, 0, (byte)(i % 400 < 200 ? 200 : 0), Buttons.None), 0.004);
        var store = new ProfileStore(_dir);
        store.Save(cal.Export());
        var path = Directory.GetFiles(_dir).Single();
        var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(node.Remove("Tuning"));
        File.WriteAllText(path, node.ToJsonString());

        var loaded = store.Load("ds4-a4538512abcd");
        Assert.NotNull(loaded);
        Assert.Equal(InputTuning.Default, loaded!.Tuning);
        Assert.Equal(cal.Export().R2.EndStop, loaded.R2.EndStop);
        var fresh = new ControllerCalibration("ds4-a4538512abcd", ControllerModel.DualShock4) { Tuning = EveryFieldChanged() };
        fresh.Import(loaded);
        Assert.Equal(InputTuning.Default, fresh.Tuning); // the old file's (absent) tuning replaces what was there

        // And through import of the same old file.
        var imported = ProfileTransfer.Deserialize(node.ToJsonString());
        Assert.Equal(InputTuning.Default, imported.Profile.Tuning);
    }

    [Fact]
    public void Hand_edited_out_of_range_numbers_are_sanitized_on_load_and_import()
    {
        var store = new ProfileStore(_dir);
        store.Save(new CalibrationProfile { ControllerId = "ds4-a4538512abcd", Model = ControllerModel.DualShock4, Tuning = EveryFieldChanged() });
        var path = Directory.GetFiles(_dir).Single();
        var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var t = node["Tuning"]!.AsObject();
        t["LeftStick"]!["InnerDeadzone"] = 7.5;
        t["LeftStick"]!["OuterDeadzone"] = -2;
        t["LeftStick"]!["Curve"] = -3;
        t["LeftStick"]!["Sensitivity"] = 1e30;
        t["LeftStick"]!["Shape"] = 99; // numeric enum value nobody defined
        t["RightStick"] = null;
        t["L2"]!["AntiDeadzone"] = 12;
        t["R2"]!["InnerDeadzone"] = 0.9;
        t["R2"]!["OuterDeadzone"] = 0.5;
        t["BluetoothPollIntervalMs"] = -40;
        t["OutputRateHz"] = 333;
        File.WriteAllText(path, node.ToJsonString());

        foreach (var tuning in new[] { store.Load("ds4-a4538512abcd")!.Tuning, ProfileTransfer.Deserialize(node.ToJsonString()).Profile.Tuning })
        {
            Assert.Equal(DeadzoneShape.Radial, tuning.LeftStick.Shape);
            Assert.Equal(InputShaper.MaxStickInnerDeadzone, tuning.LeftStick.InnerDeadzone);
            Assert.Equal(0f, tuning.LeftStick.OuterDeadzone);
            Assert.Equal(InputShaper.MinCurve, tuning.LeftStick.Curve);
            Assert.Equal(InputShaper.MaxSensitivity, tuning.LeftStick.Sensitivity);
            Assert.Equal(new StickTuning(), tuning.RightStick);
            Assert.Equal(InputShaper.MaxAntiDeadzone, tuning.L2.AntiDeadzone);
            Assert.True(tuning.R2.InnerDeadzone + tuning.R2.OuterDeadzone <= 1f - InputShaper.MinSpan + 1e-6f);
            Assert.Equal(1, tuning.BluetoothPollIntervalMs);
            Assert.Equal(250, tuning.OutputRateHz);
            Assert.Same(tuning, InputShaper.Sanitize(tuning));
        }
    }

    [Fact]
    public void Export_file_round_trip_carries_every_field_and_import_replaces_different_tuning()
    {
        var source = new ControllerCalibration("ds4-a4538512abcd", ControllerModel.DualShock4) { Tuning = EveryFieldChanged() };
        var file = Path.Combine(Directory.CreateDirectory(_dir).FullName, "friend.ds4profile.json");
        File.WriteAllText(file, ProfileTransfer.Serialize(source.Export(), "Friend's pad"));

        var imported = ProfileTransfer.Deserialize(File.ReadAllText(file));
        Assert.Equal("Friend's pad", imported.Name);
        Assert.Equal(EveryFieldChanged(), imported.Profile.Tuning);

        // A controller with its own different tuning takes over the imported one completely.
        var mine = new ControllerCalibration("ds4-a4538512abcd", ControllerModel.DualShock4)
        {
            Tuning = new InputTuning { LeftStick = new StickTuning { InvertY = true, Curve = 3f }, BluetoothPollIntervalMs = 16, OutputRateHz = 1000 },
        };
        mine.Import(imported.Profile);
        Assert.Equal(EveryFieldChanged(), mine.Tuning);

        // CopyFor to another pad carries it too.
        var copy = ProfileTransfer.CopyFor(imported.Profile, "ds4-0011223344ff", ControllerModel.DualShock4);
        Assert.Equal(EveryFieldChanged(), copy.Tuning);
    }

    [Fact]
    public void Reset_forgets_learning_but_keeps_tuning_and_tuned_output()
    {
        var pad = new Pad(false, 11, RocketLeague()) { DriftX = 0.09 };
        for (int i = 0; i < 30; i++) pad.FlickAndRelease();
        Assert.True(MathF.Abs(pad.Cal.LeftStick.CenterX) > 0.03f, "drift should have been learned");
        var before = pad.Cal.Tuning;
        pad.Cal.Reset();
        Assert.Same(before, pad.Cal.Tuning);
        Assert.Equal(0f, pad.Cal.LeftStick.CenterX);
        Assert.Equal(before, pad.Cal.Export().Tuning);
        // Tuning is still applied after the reset: square output still saturates a diagonal.
        pad.Noise = 0;
        pad.Cal.LearningEnabled = false;
        var d = ToRaw(0.75, 0.75);
        pad.Bytes(d.X, d.Y, 128, 128, 0, 0);
        Assert.InRange((int)pad.Xbox.LeftThumbX, 32700, 32767);
        Assert.InRange((int)pad.Xbox.LeftThumbY, 32700, 32767);
    }

    // ================================================================ Bluetooth poll byte

    [Fact]
    public void Poll_control_byte_is_valid_for_every_interval_including_garbage()
    {
        Assert.Equal(0xC0, Ds4BluetoothOutput.ControlByte(1)); // unchanged default
        Assert.Equal(0xC0, Ds4BluetoothOutput.ControlByte(0));
        foreach (int ms in Enumerable.Range(-20, 41).Concat(new[] { int.MinValue, int.MaxValue, 63, 64, 255 }))
        {
            byte b = Ds4BluetoothOutput.ControlByte(ms);
            Assert.Equal(0xC0, b & 0xC0); // HID + CRC flags always set
            int low = b & 0x3F;
            int meaning = low == 0 ? 1 : low;
            Assert.InRange(meaning, 1, 16);
            int expected = Math.Clamp(ms, 1, 16);
            Assert.Equal(expected, meaning);
            Assert.Equal(expected == 1 ? 0xC0 : 0xC0 | expected, (int)b);
        }
        // Whatever the profile says, the sanitized tuning gives a byte the pad accepts.
        foreach (int ms in new[] { -5, 0, 1, 2, 4, 8, 16, 17, 1000 })
        {
            var t = InputShaper.Sanitize(new InputTuning { BluetoothPollIntervalMs = ms });
            Assert.Equal(Ds4BluetoothOutput.ControlByte(ms), Ds4BluetoothOutput.ControlByte(t.BluetoothPollIntervalMs));
        }
    }
}
