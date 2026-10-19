using DS4Handler.Core.Tuning;

namespace DS4Handler.Core.Tests;

/// <summary>
/// <see cref="InputShaper.ShapeResponse(float, float, float, float, float, float)"/> is what the curve
/// preview in the app draws; it must match the real stick and trigger output.
/// </summary>
public class ShapeResponseTests
{
    private const float Tol = 1e-5f;

    public static TheoryData<DeadzoneShape, float, float, float, float, float> StickCases => new()
    {
        { DeadzoneShape.Radial, 0.1f, 0f, 0f, 1f, 1f },
        { DeadzoneShape.Radial, 0.05f, 0.1f, 0.2f, 1.4f, 1f },
        { DeadzoneShape.Radial, 0f, 0f, 0f, 0.5f, 1.8f },
        { DeadzoneShape.Square, 0.2f, 0.05f, 0.1f, 2f, 0.7f },
        { DeadzoneShape.Square, 0f, 0.3f, 0f, 1f, 1f },
        { DeadzoneShape.Axial, 0.15f, 0.1f, 0.3f, 1.6f, 1.2f },
        { DeadzoneShape.Axial, 0.3f, 0f, 0f, 0.4f, 2.5f },
    };

    [Theory]
    [MemberData(nameof(StickCases))]
    public void MatchesStraightStickPush_OnBothAxes(DeadzoneShape shape, float inner, float outer, float anti, float curve, float sens)
    {
        var t = InputShaper.Sanitize(new StickTuning
        {
            Shape = shape, InnerDeadzone = inner, OuterDeadzone = outer, AntiDeadzone = anti, Curve = curve, Sensitivity = sens,
        });
        for (int i = 0; i <= 200; i++)
        {
            float v = i / 200f;
            float expected = InputShaper.ShapeResponse(v, t);
            Assert.Equal(expected, InputShaper.ShapeStick(v, 0f, t).X, Tol);
            Assert.Equal(expected, InputShaper.ShapeStick(0f, v, t).Y, Tol);
            Assert.Equal(-expected, InputShaper.ShapeStick(-v, 0f, t).X, Tol);
        }
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 1f)]
    [InlineData(0.1f, 0.05f, 0f, 1f)]
    [InlineData(0.2f, 0.2f, 0.15f, 2.2f)]
    [InlineData(0f, 0.4f, 0.5f, 0.3f)]
    public void MatchesTrigger_BitForBit(float inner, float outer, float anti, float curve)
    {
        var t = InputShaper.Sanitize(new TriggerTuning { InnerDeadzone = inner, OuterDeadzone = outer, AntiDeadzone = anti, Curve = curve });
        for (int i = 0; i <= 255; i++)
        {
            float v = i / 255f;
            Assert.Equal(InputShaper.ShapeTrigger(v, t), InputShaper.ShapeResponse(v, t));
            if (!InputShaper.IsNeutral(t))
                Assert.Equal(InputShaper.ShapeTrigger(v, t), InputShaper.ShapeResponse(v, t.InnerDeadzone, t.OuterDeadzone, t.AntiDeadzone, t.Curve));
        }
    }

    /// <summary>Hard-coded results of the trigger formula as it was before ShapeResponse existed.</summary>
    [Theory]
    [InlineData(0.5f, 0.1f, 0.05f, 0f, 1f, 0.470588f)]
    [InlineData(0.5f, 0.2f, 0.2f, 0.15f, 2.2f, 0.334992f)]
    [InlineData(0.3f, 0f, 0.4f, 0.5f, 0.3f, 0.906126f)]
    [InlineData(0.12f, 0.1f, 0f, 0.25f, 1f, 0.266667f)]
    [InlineData(0.95f, 0.1f, 0.05f, 0f, 1f, 1f)]
    [InlineData(0.7f, 0f, 0f, 0f, 3f, 0.343f)]
    public void Trigger_MatchesTheOriginalFormula(float v, float inner, float outer, float anti, float curve, float expected)
    {
        var t = new TriggerTuning { InnerDeadzone = inner, OuterDeadzone = outer, AntiDeadzone = anti, Curve = curve };
        Assert.Equal(expected, InputShaper.ShapeTrigger(v, t), 1e-5f);
        Assert.Equal(expected, InputShaper.ShapeResponse(v, inner, outer, anti, curve), 1e-5f);
    }

    [Theory]
    [InlineData(0f, 0.9f)]
    [InlineData(0.05f, 0.9f)]
    [InlineData(0.2f, 0.75f)]
    [InlineData(0.5f, 0.45f)]
    public void MaxTriggerInner_KeepsTheEndPointThroughSanitize(float outer, float expectedMax)
    {
        float max = InputShaper.MaxTriggerInner(outer);
        Assert.Equal(expectedMax, max, 1e-5f);
        // What the slider can show (whole percents, rounded down) survives Sanitize unchanged.
        float inner = MathF.Floor(max * 100f + 1e-4f) / 100f;
        var t = InputShaper.Sanitize(new TriggerTuning { InnerDeadzone = inner, OuterDeadzone = outer });
        Assert.Equal(inner, t.InnerDeadzone);
        Assert.Equal(outer, t.OuterDeadzone, 1e-5f);
    }

    [Fact]
    public void ZeroInsideDeadzone_FullAtEndPoint_Monotonic()
    {
        const float inner = 0.12f, outer = 0.1f;
        Assert.Equal(0f, InputShaper.ShapeResponse(0f, inner, outer, 0.2f, 1.5f, 1f));
        Assert.Equal(0f, InputShaper.ShapeResponse(inner, inner, outer, 0.2f, 1.5f, 1f));
        Assert.Equal(1f, InputShaper.ShapeResponse(1f - outer, inner, outer, 0.2f, 1.5f, 1f), Tol);
        Assert.Equal(1f, InputShaper.ShapeResponse(1f, inner, outer, 0.2f, 1.5f, 1f));

        float previous = 0f;
        for (int i = 0; i <= 1000; i++)
        {
            float y = InputShaper.ShapeResponse(i / 1000f, inner, outer, 0.2f, 1.5f, 1.3f);
            Assert.InRange(y, previous, 1f);
            previous = y;
        }
    }

    [Fact]
    public void AntiDeadzone_JumpsRightOutsideTheDeadzone()
    {
        Assert.Equal(0f, InputShaper.ShapeResponse(0.1f, 0.1f, 0f, 0.25f, 1f, 1f));
        Assert.True(InputShaper.ShapeResponse(0.1001f, 0.1f, 0f, 0.25f, 1f, 1f) >= 0.25f);
    }

    [Fact]
    public void Sensitivity_ReachesFullEarlier_NeverAboveOne()
    {
        Assert.Equal(1f, InputShaper.ShapeResponse(0.5f, 0f, 0f, 0f, 1f, 2f), Tol);
        Assert.Equal(1f, InputShaper.ShapeResponse(0.9f, 0f, 0f, 0f, 1f, 3f));
        Assert.Equal(0.25f, InputShaper.ShapeResponse(0.5f, 0f, 0f, 0f, 1f, 0.5f), Tol);
    }

    [Fact]
    public void NeutralStick_ReturnsInput_NaNReadsAsReleased()
    {
        Assert.Equal(0.37f, InputShaper.ShapeResponse(0.37f, new StickTuning()));
        Assert.Equal(0f, InputShaper.ShapeResponse(float.NaN, 0.1f, 0f, 0f, 1f, 1f));
    }
}
