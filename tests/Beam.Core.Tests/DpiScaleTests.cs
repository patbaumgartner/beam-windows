using Beam.Core.Display;

namespace Beam.Core.Tests;

public class DpiScaleLadderTests
{
    [Theory]
    [InlineData(100, 0)]
    [InlineData(125, 1)]
    [InlineData(150, 2)]
    [InlineData(175, 3)]
    [InlineData(200, 4)]
    [InlineData(225, 5)]
    [InlineData(250, 6)]
    [InlineData(300, 7)]
    [InlineData(350, 8)]
    [InlineData(400, 9)]
    [InlineData(450, 10)]
    [InlineData(500, 11)]
    public void IndexOf_KnownValues(int percent, int expectedIndex)
    {
        Assert.Equal(expectedIndex, DpiScaleLadder.IndexOf(percent));
    }

    [Fact]
    public void IndexOf_UnknownValue_ReturnsMinusOne()
    {
        Assert.Equal(-1, DpiScaleLadder.IndexOf(275));
        Assert.Equal(-1, DpiScaleLadder.IndexOf(0));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(11, 500)]
    [InlineData(-1, 100)]
    [InlineData(99, 500)]
    public void At_ClampsToTable(int index, int expected)
    {
        Assert.Equal(expected, DpiScaleLadder.At(index));
    }
}

public class DpiScaleTests
{
    [Fact]
    public void From_ValidSteps_ResolvesPercentages()
    {
        // Internal panel observed on the 2026-10-29 machine: (-2, -2, +1).
        // abs(min)=2 => recommended=ladder[2]=150; current=ladder[2-2]=100; max=ladder[3]=175.
        var dpi = DpiScale.From(-2, -2, 1);

        Assert.NotNull(dpi);
        Assert.True(dpi!.IsSupported);
        Assert.Equal(new DpiScaleValue(100, 150, 100, 175, -2, -2, 1), dpi.Value);
    }

    [Fact]
    public void From_AtRecommended_Resolves()
    {
        // HP at recommended: abs(min)=3 => recommended=ladder[3]=175; curRel=0 => current=175;
        // maxRel=4 => max = ladder[7]=300.
        var dpi = DpiScale.From(-3, 0, 4);
        Assert.True(dpi!.IsSupported);
        Assert.Equal(new DpiScaleValue(100, 175, 175, 300, -3, 0, 4), dpi.Value);
    }

    [Theory]
    [InlineData(175, 0)]  // recommended 175, target 175 -> 0
    [InlineData(100, -3)] // target 100 is 3 steps below recommended 175
    [InlineData(300, 4)]  // target 300 (idx 7) is 4 steps above recommended 175 (idx 3)
    public void RelativeFor_ComputesStep(int target, int expected)
    {
        // recommended=175 (ladder idx 3)
        var dpi = new DpiScaleValue(100, 175, 175, 300, -3, 0, 4);
        Assert.Equal(expected, dpi.RelativeFor(target));
    }

    [Fact]
    public void RelativeFor_UnknownTarget_ReturnsNull()
    {
        var dpi = new DpiScaleValue(100, 175, 175, 300, -3, 0, 4);
        Assert.Null(dpi.RelativeFor(275));
    }

    [Fact]
    public void From_UnsupportedShape_ReturnsNull()
    {
        Assert.Null(DpiScale.From(1, 1, 1));    // min > 0
        Assert.Null(DpiScale.From(-2, 5, 1));   // cur > max
        Assert.Null(DpiScale.From(-2, -5, 1));  // cur < min
        Assert.Null(DpiScale.From(-100, -100, 100)); // beyond ladder
        Assert.Null(DpiScale.From(null, null, null));
    }
}

public class ScalePlannerTests
{
    [Fact]
    public void ChooseScale_1080pSource_Picks100Percent()
    {
        // 1920x1080 shared source, recommended 150, range 100..175.
        var dpi = new DpiScaleValue(100, 150, 100, 175, -3, -3, 1);
        int scale = ScalePlanner.ChooseScale(1920, dpi);

        // logicalW = 1920*100/100 = 1920, in range, closest to preferred 1920.
        Assert.Equal(100, scale);
    }

    [Fact]
    public void ChooseScale_4kSource_Picks200Percent()
    {
        // 3840x2160 shared source, range 100..250.
        var dpi = new DpiScaleValue(100, 200, 200, 250, -4, 0, 1);
        int scale = ScalePlanner.ChooseScale(3840, dpi);

        // 100% -> 3840 logical (too big); 200% -> 1920 logical (preferred).
        Assert.Equal(200, scale);
    }

    [Fact]
    public void ChooseScale_ClampsToMaximum()
    {
        // Narrow range forces the largest available scale.
        var dpi = new DpiScaleValue(100, 125, 125, 175, -3, -2, 0);
        int scale = ScalePlanner.ChooseScale(3840, dpi);

        Assert.True(scale <= dpi.Maximum);
        Assert.True(scale >= dpi.Minimum);
    }

    [Fact]
    public void ChooseScale_UnsupportedDpi_StillPicksSomething()
    {
        // Should never happen in practice, but never throw.
        var dpi = new DpiScaleValue(100, 100, 100, 100, 0, 0, 0);
        int scale = ScalePlanner.ChooseScale(1920, dpi);
        Assert.Equal(100, scale);
    }

    [Theory]
    [InlineData(1920u, 100, 1920)]
    [InlineData(3840u, 200, 1920)]
    [InlineData(2560u, 100, 2560)]
    [InlineData(1920u, 200, 960)]
    [InlineData(3840u, 300, 1280)]
    public void LogicalWidth_ComputesDipSize(uint pixelWidth, int percent, int expected)
    {
        Assert.Equal(expected, ScalePlanner.LogicalWidth(pixelWidth, percent));
    }
}
