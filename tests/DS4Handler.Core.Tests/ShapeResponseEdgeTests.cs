using DS4Handler.Core.Tuning;

namespace DS4Handler.Core.Tests;

/// <summary>Edge cases for InputShaper.ShapeResponse: the preview must match the engine for every value the UI can produce.</summary>
public class ShapeResponseEdgeTests
{
    private const float Tol = 1e-4f;
    private static readonly float[] Antis = { 0f, 0.3f, 0.9f };
    private static readonly float[] Curves = { 0.2f, 1f, 5f };

    /// <summary>
    /// The preview passes the raw slider values, the engine runs on Sanitize()d values. The UI now limits
    /// Inner to MaxTriggerInner(Outer), but even uncoupled values (inner >= 1 - outer) must agree.
    /// </summary>
    [Fact]
    public void Trigger_RawSliderValues_MatchSanitizedEngine_IncludingInnerPastEndPoint()
    {
        for (int ii = 0; ii <= 90; ii += 3)
        for (int oi = 0; oi <= 50; oi += 5)
        foreach (float anti in Antis)
        foreach (float curve in Curves)
        {
            float inner = ii / 100f, outer = oi / 100f;
            var engine = InputShaper.Sanitize(new TriggerTuning { InnerDeadzone = inner, OuterDeadzone = outer, AntiDeadzone = anti, Curve = curve });
            for (int i = 0; i <= 255; i++)
            {
                float v = i / 255f;
                float preview = InputShaper.ShapeResponse(v, inner, outer, anti, curve);
                Assert.True(MathF.Abs(preview - InputShaper.ShapeTrigger(v, engine)) <= Tol,
                    $"inner {inner} outer {outer} anti {anti} curve {curve} v {v}: preview {preview} engine {InputShaper.ShapeTrigger(v, engine)}");
            }
        }
    }

    /// <summary>Stick: inner limited by the VM to floor(MaxInnerDeadzone(shape, outer)), every shape, curve 0.2/5, sensitivity 0.1/3.</summary>
    [Theory]
    [InlineData(DeadzoneShape.Radial)]
    [InlineData(DeadzoneShape.Square)]
    [InlineData(DeadzoneShape.Axial)]
    public void Stick_VmReachableValues_MatchEngineOnAxis(DeadzoneShape shape)
    {
        float[] sens = { 0.1f, 1f, 3f };
        for (int oi = 0; oi <= 50; oi += 10)
        {
            float outer = oi / 100f;
            float maxInner = MathF.Floor(InputShaper.MaxInnerDeadzone(shape, outer) * 100f + 1e-4f) / 100f;
            foreach (float inner in new[] { 0f, maxInner / 2f, maxInner })
            foreach (float anti in Antis)
            foreach (float curve in Curves)
            foreach (float s in sens)
            {
                var raw = new StickTuning { Shape = shape, InnerDeadzone = inner, OuterDeadzone = outer, AntiDeadzone = anti, Curve = curve, Sensitivity = s };
                var engine = InputShaper.Sanitize(raw);
                Assert.Equal(raw.OuterDeadzone, engine.OuterDeadzone, 1e-5f); // VM values survive Sanitize
                for (int i = 0; i <= 200; i++)
                {
                    float v = i / 200f;
                    float preview = InputShaper.ShapeResponse(v, inner, outer, anti, curve, s);
                    float x = InputShaper.ShapeStick(v, 0f, engine).X;
                    Assert.True(MathF.Abs(preview - x) <= Tol, $"{shape} in {inner} out {outer} anti {anti} curve {curve} sens {s} v {v}: {preview} vs {x}");
                }
            }
        }
    }

    [Fact]
    public void ExtremeCurves_AreMonotonicAndBounded()
    {
        foreach (float curve in new[] { 0.2f, 5f })
        {
            float prev = 0f;
            for (int i = 0; i <= 1000; i++)
            {
                float y = InputShaper.ShapeResponse(i / 1000f, 0.05f, 0.05f, 0f, curve, 1f);
                Assert.InRange(y, prev, 1f);
                prev = y;
            }
            Assert.Equal(1f, InputShaper.ShapeResponse(0.95f, 0.05f, 0.05f, 0f, curve, 1f), Tol);
        }
        // curve 0.2 is steep near the edge, curve 5 is flat there
        Assert.True(InputShaper.ShapeResponse(0.1f, 0f, 0f, 0f, 0.2f) > 0.6f);
        Assert.True(InputShaper.ShapeResponse(0.5f, 0f, 0f, 0f, 5f) < 0.04f);
    }

    [Fact]
    public void AntiDeadzone_WithLowSensitivity_NeverReachesFull()
    {
        // sensitivity 0.5: magnitude tops out at 0.5, anti 0.2 maps it to 0.2 + 0.8 * 0.5 = 0.6
        Assert.Equal(0.6f, InputShaper.ShapeResponse(1f, 0f, 0f, 0.2f, 1f, 0.5f), Tol);
        Assert.Equal(0.9f, InputShaper.ShapeResponse(0.0001f, 0f, 0f, 0.9f, 1f, 1f), 1e-3f);
    }

    [Fact]
    public void InnerAtOrPastEndPoint_UsesMinSpan()
    {
        // inner 0.9, outer 0.5: live range is MinSpan (0.05), full output at 0.95
        Assert.Equal(0f, InputShaper.ShapeResponse(0.9f, 0.9f, 0.5f, 0f, 1f));
        Assert.Equal(0.5f, InputShaper.ShapeResponse(0.925f, 0.9f, 0.5f, 0f, 1f), Tol);
        Assert.Equal(1f, InputShaper.ShapeResponse(0.95f, 0.9f, 0.5f, 0f, 1f), Tol);
        Assert.Equal(1f, InputShaper.ShapeResponse(1f, 0.9f, 0.5f, 0f, 1f));
    }

    [Fact]
    public void OutOfRangeInput_IsClamped()
    {
        Assert.Equal(0f, InputShaper.ShapeResponse(-0.5f, 0f, 0f, 0.3f, 1f));
        Assert.Equal(1f, InputShaper.ShapeResponse(1.5f, 0f, 0f, 0f, 2f, 1f));
        Assert.Equal(0f, InputShaper.ShapeResponse(float.NegativeInfinity, 0f, 0f, 0f, 1f));
        Assert.Equal(1f, InputShaper.ShapeResponse(float.PositiveInfinity, 0f, 0f, 0f, 1f));
    }

    /// <summary>Neutral stick overload returns the raw input even outside 0..1, unlike the raw overload.</summary>
    [Fact]
    public void NeutralStickOverload_DoesNotClamp()
    {
        Assert.Equal(1.5f, InputShaper.ShapeResponse(1.5f, new StickTuning()));
        Assert.Equal(-0.3f, InputShaper.ShapeResponse(-0.3f, new StickTuning()));
    }
}
