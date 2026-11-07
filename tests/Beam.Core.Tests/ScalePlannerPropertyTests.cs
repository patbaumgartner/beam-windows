using Beam.Core.Display;

namespace Beam.Core.Tests;

/// <summary>
/// Property-style tests for the DPI scale planner: for ANY pixel width and ANY
/// representable DPI range, the chosen scale must be on the ladder, inside the
/// reported range, and the score model must prefer a logical width in
/// [1280, 2560] whenever one exists.
/// </summary>
public class ScalePlannerPropertyTests
{
    private static DpiScaleValue Dpi(int minPercent, int maxPercent)
    {
        int minIdx = DpiScaleLadder.IndexOf(minPercent);
        int maxIdx = DpiScaleLadder.IndexOf(maxPercent);
        Assert.True(minIdx >= 0 && maxIdx >= minIdx, "test bug: bad range");
        int minRel = -minIdx;
        int curRel = 0;
        int maxRel = maxIdx - minIdx;
        return new DpiScaleValue(
            minPercent,
            DpiScaleLadder.Values[minIdx],
            DpiScaleLadder.Values[minIdx + curRel],
            DpiScaleLadder.Values[maxIdx],
            minRel, curRel, maxRel);
    }

    public static TheoryData<uint> InterestingWidths => new()
    {
        0u, 1u, 640u, 1024u, 1280u, 1366u, 1600u, 1920u, 2048u, 2560u,
        2561u, 3440u, 3840u, 4096u, 5120u, 7680u, uint.MaxValue,
    };

    [Theory]
    [MemberData(nameof(InterestingWidths))]
    public void ChooseScale_FullLadderRange_ResultOnLadder(uint width)
    {
        int chosen = ScalePlanner.ChooseScale(width, Dpi(100, 500));
        Assert.Contains(chosen, DpiScaleLadder.Values);
    }

    [Theory]
    [MemberData(nameof(InterestingWidths))]
    public void ChooseScale_RestrictedRange_RespectsBounds(uint width)
    {
        var dpi = Dpi(150, 250);
        int chosen = ScalePlanner.ChooseScale(width, dpi);
        Assert.InRange(chosen, 150, 250);
    }

    [Fact]
    public void ChooseScale_EmptyCandidateRange_FallsBackToMinimum()
    {
        // A source reporting 110% max with 100% min: neither is on the ladder, so the
        // candidate list is empty and the planner falls back to dpi.Minimum.
        var dpi = new DpiScaleValue(110, 110, 110, 110, 0, 0, 0);
        Assert.Equal(110, ScalePlanner.ChooseScale(1920, dpi));
    }

    [Fact]
    public void ChooseScale_NullDpi_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ScalePlanner.ChooseScale(1920, null!));
    }

    [Fact]
    public void ChooseScale_ExtremeWidth_DoesNotOverflowScore()
    {
        // uint.MaxValue * 100 / 100 = 4294967295 logical — must clamp, not overflow.
        var ex = Record.Exception(() => ScalePlanner.ChooseScale(uint.MaxValue, Dpi(100, 500)));
        Assert.Null(ex);
    }

    [Fact]
    public void ChooseScale_1080pSource_AlwaysPicks100Percent()
    {
        // The ADR-0013 contract: a 1080p source must end at 100% so the logical
        // desktop is 1920 wide.
        Assert.Equal(100, ScalePlanner.ChooseScale(1920, Dpi(100, 500)));
    }

    [Fact]
    public void ChooseScale_4KSource_Picks200Percent()
    {
        Assert.Equal(200, ScalePlanner.ChooseScale(3840, Dpi(100, 500)));
    }

    [Fact]
    public void ChooseScale_NarrowRangeNeverBelowBand_WhenImpossible_PicksBestAvailable()
    {
        // A 640px source can never reach a 1280+ logical width except at 50%... which
        // is not on the ladder. At 100% logical = 640 (below band) — the planner must
        // still return the least-bad in-range candidate rather than crash.
        int chosen = ScalePlanner.ChooseScale(640, Dpi(100, 500));
        Assert.Equal(100, chosen);
    }

    [Fact]
    public void LogicalWidth_ValidPercent_IsExact()
    {
        Assert.Equal(1920, ScalePlanner.LogicalWidth(1920, 100));
        Assert.Equal(960, ScalePlanner.LogicalWidth(1920, 200));
        Assert.Equal(1920, ScalePlanner.LogicalWidth(3840, 200));
    }

    [Fact]
    public void LogicalWidth_OverflowClamps()
    {
        Assert.Equal(int.MaxValue, ScalePlanner.LogicalWidth(uint.MaxValue, 1));
    }
}
