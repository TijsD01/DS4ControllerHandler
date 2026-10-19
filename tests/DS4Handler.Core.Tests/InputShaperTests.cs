using DS4Handler.Core.Output;
using DS4Handler.Core.Tuning;

namespace DS4Handler.Core.Tests;

public class InputShaperTests
{
    private const float Tol = 1e-4f;

    private static (float X, float Y) Stick(float x, float y, StickTuning t) => InputShaper.ShapeStick(x, y, t);

    private static float Len((float X, float Y) v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    // ---- identity ----

    [Fact]
    public void DefaultStick_ReturnsInputBitForBit()
    {
        var t = new StickTuning();
        var rng = new Random(1);
        for (int i = 0; i < 2000; i++)
        {
            float x = (float)(rng.NextDouble() * 2 - 1), y = (float)(rng.NextDouble() * 2 - 1);
            var (ox, oy) = Stick(x, y, t);
            Assert.Equal(BitConverter.SingleToInt32Bits(x), BitConverter.SingleToInt32Bits(ox));
            Assert.Equal(BitConverter.SingleToInt32Bits(y), BitConverter.SingleToInt32Bits(oy));
        }
    }

    [Theory]
    [InlineData(DeadzoneShape.Square)]
    [InlineData(DeadzoneShape.Axial)]
    public void ShapeAlone_WithoutDeadzone_ChangesNothing(DeadzoneShape shape)
    {
        var t = new StickTuning { Shape = shape };
        Assert.True(InputShaper.IsNeutral(t));
        Assert.Equal((0.3f, -0.4f), Stick(0.3f, -0.4f, t));
    }

    [Fact]
    public void DefaultTrigger_ReturnsInputBitForBit()
    {
        var t = new TriggerTuning();
        for (int i = 0; i <= 1000; i++)
        {
            float v = i / 1000f;
            Assert.Equal(BitConverter.SingleToInt32Bits(v), BitConverter.SingleToInt32Bits(InputShaper.ShapeTrigger(v, t)));
        }
    }

    // ---- inner deadzone by shape ----

    [Fact]
    public void SquareDeadzone_KillsSmallDiagonal_ThatRadialPasses()
    {
        // (0.12, 0.12): radius 0.17 leaves a 0.15 circle, but both axes are inside a 0.15 square.
        var radial = Stick(0.12f, 0.12f, new StickTuning { InnerDeadzone = 0.15f });
        var square = Stick(0.12f, 0.12f, new StickTuning { Shape = DeadzoneShape.Square, InnerDeadzone = 0.15f });
        Assert.True(radial.X > 0 && radial.Y > 0);
        Assert.Equal((0f, 0f), square);
    }

    [Fact]
    public void SmallDiagonal_InsideBoth_IsZero()
    {
        Assert.Equal((0f, 0f), Stick(0.1f, 0.1f, new StickTuning { InnerDeadzone = 0.15f }));
        Assert.Equal((0f, 0f), Stick(0.1f, 0.1f, new StickTuning { Shape = DeadzoneShape.Square, InnerDeadzone = 0.15f }));
    }

    [Fact]
    public void AxialDeadzone_DropsSidewaysWobble_RadialKeepsIt()
    {
        var radial = Stick(0.6f, 0.08f, new StickTuning { InnerDeadzone = 0.1f });
        var axial = Stick(0.6f, 0.08f, new StickTuning { Shape = DeadzoneShape.Axial, InnerDeadzone = 0.1f });
        Assert.True(radial.Y > 0.05f);
        Assert.Equal(0f, axial.Y);
        // Per-axis rescale, divided by what a full push in this direction gives, so full push stays 1.
        float r = MathF.Sqrt(0.6f * 0.6f + 0.08f * 0.08f);
        float fullX = (0.6f / r - 0.1f) / 0.9f, fullY = (0.08f / r - 0.1f) / 0.9f;
        Assert.True(fullY > 0f);
        Assert.Equal((0.6f - 0.1f) / 0.9f / MathF.Sqrt(fullX * fullX + fullY * fullY), axial.X, Tol);
    }

    [Theory]
    [InlineData(DeadzoneShape.Radial)]
    [InlineData(DeadzoneShape.Square)]
    [InlineData(DeadzoneShape.Axial)]
    public void FullPushStraight_StillReachesFull(DeadzoneShape shape)
    {
        var t = new StickTuning { Shape = shape, InnerDeadzone = 0.2f };
        Assert.Equal(1f, Stick(1f, 0f, t).X, Tol);
        Assert.Equal(-1f, Stick(0f, -1f, t).Y, Tol);
    }

    [Theory]
    [InlineData(DeadzoneShape.Radial, 0.3f)]
    [InlineData(DeadzoneShape.Square, 0.3f)]
    [InlineData(DeadzoneShape.Axial, 0.1f)]
    [InlineData(DeadzoneShape.Axial, 0.3f)]
    [InlineData(DeadzoneShape.Square, 0.6f)]
    [InlineData(DeadzoneShape.Axial, 0.6f)]
    public void FullPushDiagonal_ReachesFullRadius(DeadzoneShape shape, float dz)
    {
        float d = MathF.Sqrt(0.5f);
        var o = Stick(d, d, InputShaper.Sanitize(new StickTuning { Shape = shape, InnerDeadzone = dz }));
        Assert.Equal(1f, Len(o), Tol);
        Assert.Equal(o.X, o.Y, Tol); // direction kept
    }

    [Theory]
    [InlineData(DeadzoneShape.Radial)]
    [InlineData(DeadzoneShape.Square)]
    [InlineData(DeadzoneShape.Axial)]
    public void FullPush_ReachesFull_InEveryDirection_EvenAtTheMaxDeadzone(DeadzoneShape shape)
    {
        foreach (float outer in new[] { 0f, 0.2f })
        {
            var t = InputShaper.Sanitize(new StickTuning { Shape = shape, InnerDeadzone = 0.9f, OuterDeadzone = outer });
            Assert.Equal(InputShaper.MaxInnerDeadzone(shape, 0f), t.InnerDeadzone);
            for (int k = 0; k < 360; k++)
            {
                float a = k * MathF.PI / 180f;
                Assert.True(Len(Stick(MathF.Cos(a), MathF.Sin(a), t)) >= 0.999f, $"{shape} outer {outer} angle {k}");
            }
        }
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.3f)]
    public void AxialWithSquareOutput_FullDiagonalHitsTheCorner(float dz)
    {
        float d = MathF.Sqrt(0.5f);
        var o = Stick(d, d, new StickTuning { Shape = DeadzoneShape.Axial, InnerDeadzone = dz, SquareOutput = true });
        Assert.Equal((short)32767, XboxMapper.StickToShort(o.X));
        Assert.Equal((short)32767, XboxMapper.StickToShort(o.Y));
    }

    [Fact]
    public void Axial_SaturatedAxisNeverDrops_WhileTheOtherGrows()
    {
        var t = new StickTuning { Shape = DeadzoneShape.Axial, InnerDeadzone = 0.15f, OuterDeadzone = 0.3f, AntiDeadzone = 0.3f, Curve = 0.5f, Sensitivity = 2f };
        float prev = 0f;
        for (int i = 0; i <= 1000; i++)
        {
            float r = i / 1000f;
            var o = Stick(r * 0.9f, r * 0.43f, t); // near the x axis, inside the unit circle
            Assert.True(o.X >= prev - 1e-6f, $"x falls at r={r}: {prev} -> {o.X}");
            prev = o.X;
        }
    }

    [Theory]
    [InlineData(DeadzoneShape.Radial, 0f, 0.9f)]
    [InlineData(DeadzoneShape.Radial, 0.3f, 0.65f)]
    [InlineData(DeadzoneShape.Square, 0f, 0.95f / 1.41421356f)]
    [InlineData(DeadzoneShape.Axial, 0.2f, 0.75f / 1.41421356f)]
    [InlineData(DeadzoneShape.Square, float.NaN, 0.95f / 1.41421356f)]
    public void MaxInnerDeadzone_DependsOnShapeAndOuter(DeadzoneShape shape, float outer, float expected)
    {
        Assert.Equal(expected, InputShaper.MaxInnerDeadzone(shape, outer), 1e-5f);
    }

    [Fact]
    public void Sanitize_SquareDeadzone_KeepsDiagonalsLive()
    {
        var t = InputShaper.Sanitize(new StickTuning { Shape = DeadzoneShape.Square, InnerDeadzone = 0.6f, OuterDeadzone = 0.3f });
        Assert.Equal(0.6f, t.InnerDeadzone);
        Assert.True(t.InnerDeadzone * MathF.Sqrt(2f) + t.OuterDeadzone <= 1f - InputShaper.MinSpan + 1e-5f);
        Assert.Same(t, InputShaper.Sanitize(t)); // stable
    }

    [Theory]
    [InlineData(DeadzoneShape.Radial, 0f)]
    [InlineData(DeadzoneShape.Radial, 0.6f)]
    [InlineData(DeadzoneShape.Square, 0f)]
    [InlineData(DeadzoneShape.Square, 0.5f)]
    [InlineData(DeadzoneShape.Square, 0.785f)]
    [InlineData(DeadzoneShape.Axial, 0f)]
    [InlineData(DeadzoneShape.Axial, 0.4f)]
    public void Output_IsContinuous_AcrossTheDeadzoneEdge(DeadzoneShape shape, float angle)
    {
        var t = new StickTuning { Shape = shape, InnerDeadzone = 0.2f };
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        var prev = Stick(0f, 0f, t);
        // Walk outwards in tiny steps: no step may jump.
        for (int i = 1; i <= 1000; i++)
        {
            float r = i / 1000f;
            var o = Stick(r * c, r * s, t);
            Assert.True(MathF.Abs(o.X - prev.X) < 0.01f && MathF.Abs(o.Y - prev.Y) < 0.01f, $"jump at r={r}");
            prev = o;
        }
    }

    [Fact]
    public void JustOutsideTheEdge_IsNearZero()
    {
        var t = new StickTuning { InnerDeadzone = 0.2f };
        Assert.True(Len(Stick(0.201f, 0f, t)) < 0.002f);
    }

    // ---- outer, anti, curve, sensitivity ----

    [Fact]
    public void OuterDeadzone_ReachesFullEarlier()
    {
        var t = new StickTuning { OuterDeadzone = 0.1f };
        Assert.Equal(1f, Stick(0.9f, 0f, t).X, Tol);
        Assert.Equal(1f, Stick(0.95f, 0f, t).X, Tol);
        Assert.Equal(0.5f / 0.9f, Stick(0.5f, 0f, t).X, Tol);
    }

    [Fact]
    public void AntiDeadzone_StartsOutputAtAnti()
    {
        var t = new StickTuning { InnerDeadzone = 0.1f, AntiDeadzone = 0.25f };
        Assert.Equal((0f, 0f), Stick(0.05f, 0f, t));
        Assert.Equal(0.25f, Stick(0.1001f, 0f, t).X, 1e-3f);
        Assert.Equal(1f, Stick(1f, 0f, t).X, Tol);
        Assert.Equal(0.25f + 0.75f * 0.5f, Stick(0.55f, 0f, t).X, Tol);
    }

    [Fact]
    public void Curve_MatchesXboxMapperSemantics()
    {
        var t = new StickTuning { Curve = 2f };
        Assert.Equal(XboxMapper.ApplyResponseCurve(0.5f, 2f), Stick(0.5f, 0f, t).X, Tol);
        Assert.Equal(-0.25f, Stick(0f, -0.5f, t).Y, Tol);
        Assert.Equal(1f, Stick(1f, 0f, t).X, Tol);
    }

    [Fact]
    public void Curve_ActsOnMagnitude_KeepsDirection()
    {
        var o = Stick(0.3f, 0.4f, new StickTuning { Curve = 2f });
        Assert.Equal(0.25f, Len(o), Tol);
        Assert.Equal(0.75f, o.X / o.Y, Tol);
    }

    [Fact]
    public void Curve_DoesNotShrinkAntiDeadzone()
    {
        var t = new StickTuning { InnerDeadzone = 0.1f, AntiDeadzone = 0.3f, Curve = 3f };
        Assert.Equal(0.3f, Stick(0.1001f, 0f, t).X, 1e-3f);
    }

    [Fact]
    public void Sensitivity_ScalesAndClamps()
    {
        Assert.Equal(0.6f, Stick(0.3f, 0f, new StickTuning { Sensitivity = 2f }).X, Tol);
        Assert.Equal(1f, Stick(0.8f, 0f, new StickTuning { Sensitivity = 2f }).X, Tol);
        Assert.Equal(0.25f, Stick(0.5f, 0f, new StickTuning { Sensitivity = 0.5f }).X, Tol);
        var diag = Stick(0.6f, 0.6f, new StickTuning { Sensitivity = 3f });
        Assert.Equal(1f, Len(diag), Tol); // clamped radially, not per axis
    }

    // ---- square output, invert ----

    [Fact]
    public void SquareOutput_DiagonalReachesCorner()
    {
        float d = MathF.Sqrt(0.5f);
        var o = Stick(d, -d, new StickTuning { SquareOutput = true });
        Assert.Equal(1f, o.X, 1e-3f);
        Assert.Equal(-1f, o.Y, 1e-3f);
    }

    [Fact]
    public void SquareOutput_KeepsStraightPushesAndCenter()
    {
        var t = new StickTuning { SquareOutput = true };
        Assert.Equal((0f, 0f), Stick(0f, 0f, t));
        Assert.Equal(1f, Stick(1f, 0f, t).X, Tol);
        Assert.Equal(0f, Stick(1f, 0f, t).Y, Tol);
        Assert.Equal(0.5f, Stick(0.5f, 0f, t).X, 1e-3f); // the rim overscale is ~0.02% here
        // Small movements barely change, so aiming feels the same.
        var small = Stick(0.05f, 0.05f, t);
        Assert.Equal(0.05f, small.X, 2e-3f);
    }

    [Fact]
    public void SquareOutput_StaysInRange_AndIsMonotonicAlongTheDiagonal()
    {
        var t = new StickTuning { SquareOutput = true };
        float prev = 0f;
        for (int i = 1; i <= 100; i++)
        {
            float r = i / 100f * MathF.Sqrt(0.5f);
            var o = Stick(r, r, t);
            Assert.InRange(o.X, prev, 1f);
            prev = o.X;
        }
        var outside = InputShaper.DiscToSquare(1f, 1f); // outside the disc: normalised first
        Assert.Equal(1f, outside.X, 1e-3f);
    }

    [Fact]
    public void SquareOutput_FullDiagonalWithFloatRounding_GivesExactCorner()
    {
        // A calibrated full push is often r = 0.99999994, where the plain mapping gives 32761.
        float d = 0.70710677f * 0.99999994f;
        var o = Stick(d, -d, new StickTuning { SquareOutput = true });
        Assert.Equal((short)32767, XboxMapper.StickToShort(o.X));
        Assert.Equal((short)-32768, XboxMapper.StickToShort(o.Y));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(2.5f)]
    [InlineData(-2.5f)]
    public void SquareOutput_CornerHasAFewDegreesOfWidth(float degreesOff)
    {
        float a = (45f + degreesOff) * MathF.PI / 180f;
        var o = Stick(MathF.Cos(a), MathF.Sin(a), new StickTuning { SquareOutput = true });
        Assert.Equal((short)32767, XboxMapper.StickToShort(o.X));
        Assert.Equal((short)32767, XboxMapper.StickToShort(o.Y));
    }

    [Fact]
    public void SquareOutput_IsMonotonicAlongEveryRay()
    {
        var t = new StickTuning { SquareOutput = true };
        for (int k = 0; k < 90; k++)
        {
            float a = k * MathF.PI / 45f + 0.01f;
            float px = 0f, py = 0f;
            for (int i = 0; i <= 2000; i++)
            {
                float r = i / 2000f;
                var o = Stick(r * MathF.Cos(a), r * MathF.Sin(a), t);
                Assert.True(MathF.Abs(o.X) >= px - 1e-6f && MathF.Abs(o.Y) >= py - 1e-6f, $"angle {k} r={r}");
                px = MathF.Abs(o.X); py = MathF.Abs(o.Y);
            }
        }
    }

    [Fact]
    public void Invert_FlipsAxesExactly()
    {
        Assert.Equal((-0.3f, 0.4f), Stick(0.3f, 0.4f, new StickTuning { InvertX = true }));
        Assert.Equal((0.3f, -0.4f), Stick(0.3f, 0.4f, new StickTuning { InvertY = true }));
    }

    [Fact]
    public void NaNInput_ReadsAsCentered()
    {
        var t = new StickTuning { InnerDeadzone = 0.1f, SquareOutput = true };
        var o = Stick(float.NaN, float.NaN, t);
        Assert.Equal((0f, 0f), o);
        var half = Stick(float.NaN, 0.5f, new StickTuning { InvertX = true });
        Assert.False(float.IsNaN(half.X));
    }

    [Fact]
    public void EveryCombination_StaysInRange()
    {
        var rng = new Random(7);
        foreach (DeadzoneShape shape in Enum.GetValues<DeadzoneShape>())
        {
            var t = new StickTuning
            {
                Shape = shape, InnerDeadzone = 0.3f, OuterDeadzone = 0.2f, AntiDeadzone = 0.2f,
                Curve = 0.5f, Sensitivity = 2.5f, SquareOutput = true, InvertX = true,
            };
            for (int i = 0; i < 2000; i++)
            {
                double a = rng.NextDouble() * Math.Tau, r = rng.NextDouble();
                var o = Stick((float)(r * Math.Cos(a)), (float)(r * Math.Sin(a)), t);
                Assert.InRange(o.X, -1f, 1f);
                Assert.InRange(o.Y, -1f, 1f);
            }
        }
    }

    // ---- triggers ----

    [Fact]
    public void Trigger_InnerDeadzone_RescalesWithoutJump()
    {
        var t = new TriggerTuning { InnerDeadzone = 0.1f };
        Assert.Equal(0f, InputShaper.ShapeTrigger(0.1f, t));
        Assert.Equal(0f, InputShaper.ShapeTrigger(0.05f, t));
        Assert.True(InputShaper.ShapeTrigger(0.101f, t) < 0.002f);
        Assert.Equal(0.5f, InputShaper.ShapeTrigger(0.55f, t), Tol);
        Assert.Equal(1f, InputShaper.ShapeTrigger(1f, t), Tol);
    }

    [Fact]
    public void Trigger_OuterDeadzone_FullEarlier()
    {
        var t = new TriggerTuning { OuterDeadzone = 0.2f };
        Assert.Equal(1f, InputShaper.ShapeTrigger(0.8f, t), Tol);
        Assert.Equal(0.5f, InputShaper.ShapeTrigger(0.4f, t), Tol);
    }

    [Fact]
    public void Trigger_AntiAndCurve()
    {
        var t = new TriggerTuning { InnerDeadzone = 0.1f, AntiDeadzone = 0.2f, Curve = 2f };
        Assert.Equal(0f, InputShaper.ShapeTrigger(0.05f, t));
        Assert.Equal(0.2f, InputShaper.ShapeTrigger(0.1001f, t), 1e-3f);
        Assert.Equal(0.2f + 0.8f * 0.25f, InputShaper.ShapeTrigger(0.55f, t), Tol);
        Assert.Equal(1f, InputShaper.ShapeTrigger(1f, t), Tol);
        Assert.Equal(0f, InputShaper.ShapeTrigger(float.NaN, t));
    }

    // ---- sanitize ----

    [Fact]
    public void Sanitize_Null_GivesDefault()
    {
        Assert.Same(InputTuning.Default, InputShaper.Sanitize((InputTuning?)null));
        var t = new InputTuning { LeftStick = null!, L2 = null! };
        var s = InputShaper.Sanitize(t);
        Assert.Equal(new StickTuning(), s.LeftStick);
        Assert.Equal(new TriggerTuning(), s.L2);
    }

    [Fact]
    public void Sanitize_ValidTuning_ReturnsSameInstance()
    {
        var t = new InputTuning { LeftStick = new StickTuning { InnerDeadzone = 0.1f, Shape = DeadzoneShape.Square }, OutputRateHz = 250 };
        Assert.Same(t, InputShaper.Sanitize(t));
    }

    [Fact]
    public void Sanitize_ClampsEveryStickValue()
    {
        var s = InputShaper.Sanitize(new StickTuning
        {
            Shape = (DeadzoneShape)42,
            InnerDeadzone = 2f, OuterDeadzone = -1f, AntiDeadzone = 5f, Curve = 100f, Sensitivity = 0f,
        });
        Assert.Equal(DeadzoneShape.Radial, s.Shape);
        Assert.Equal(InputShaper.MaxStickInnerDeadzone, s.InnerDeadzone);
        Assert.Equal(0f, s.OuterDeadzone);
        Assert.Equal(InputShaper.MaxAntiDeadzone, s.AntiDeadzone);
        Assert.Equal(InputShaper.MaxCurve, s.Curve);
        Assert.Equal(InputShaper.MinSensitivity, s.Sensitivity);
    }

    [Fact]
    public void Sanitize_NonFinite_BecomesDefault()
    {
        var s = InputShaper.Sanitize(new StickTuning
        {
            InnerDeadzone = float.NaN, OuterDeadzone = float.PositiveInfinity, AntiDeadzone = float.NegativeInfinity,
            Curve = float.NaN, Sensitivity = float.PositiveInfinity,
        });
        Assert.Equal(new StickTuning(), s);
        var tr = InputShaper.Sanitize(new TriggerTuning { InnerDeadzone = float.NaN, Curve = float.NegativeInfinity });
        Assert.Equal(new TriggerTuning(), tr);
    }

    [Fact]
    public void Sanitize_KeepsAUsableSpanBetweenInnerAndOuter()
    {
        var s = InputShaper.Sanitize(new StickTuning { InnerDeadzone = 0.8f, OuterDeadzone = 0.5f });
        Assert.Equal(0.8f, s.InnerDeadzone);
        Assert.True(1f - s.InnerDeadzone - s.OuterDeadzone >= InputShaper.MinSpan - 1e-6f);
        var tr = InputShaper.Sanitize(new TriggerTuning { InnerDeadzone = 0.9f, OuterDeadzone = 0.5f });
        Assert.True(tr.OuterDeadzone >= 0f && 1f - tr.InnerDeadzone - tr.OuterDeadzone >= InputShaper.MinSpan - 1e-6f);
        // The extreme still works and reaches full output.
        Assert.Equal(1f, InputShaper.ShapeTrigger(1f, tr), Tol);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 125)]
    [InlineData(125, 125)]
    [InlineData(190, 250)]
    [InlineData(300, 250)]
    [InlineData(500, 500)]
    [InlineData(800, 1000)]
    [InlineData(int.MaxValue, 1000)]
    public void Sanitize_SnapsOutputRate(int input, int expected)
    {
        Assert.Equal(expected, InputShaper.Sanitize(new InputTuning { OutputRateHz = input }).OutputRateHz);
    }

    [Theory]
    [InlineData(-3, 1)]
    [InlineData(0, 1)]
    [InlineData(4, 4)]
    [InlineData(16, 16)]
    [InlineData(99, 16)]
    public void Sanitize_ClampsPollInterval(int input, int expected)
    {
        Assert.Equal(expected, InputShaper.Sanitize(new InputTuning { BluetoothPollIntervalMs = input }).BluetoothPollIntervalMs);
    }

    // ---- DS4 Bluetooth poll byte ----

    [Theory]
    [InlineData(1, 0xC0)]
    [InlineData(0, 0xC0)]
    [InlineData(2, 0xC2)]
    [InlineData(4, 0xC4)]
    [InlineData(16, 0xD0)]
    [InlineData(64, 0xD0)]
    public void Ds4BluetoothControlByte_CarriesPollInterval(int ms, int expected)
    {
        Assert.Equal((byte)expected, Ds4BluetoothOutput.ControlByte(ms));
    }
}
