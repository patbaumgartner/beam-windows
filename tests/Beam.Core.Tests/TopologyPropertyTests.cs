using Beam.Core.Display;

namespace Beam.Core.Tests;

/// <summary>
/// Property-style and invariant tests over DisplayTopology: for ANY combination of
/// path shapes, HasMirrorAspectMismatch must agree with the observable contract
/// (mirrored + shape difference = bars) and never throw.
/// </summary>
public class TopologyPropertyTests
{
    private static DisplayPath P(
        uint sourceId,
        uint targetId,
        bool isInternal,
        uint sw, uint sh,      // source mode
        uint tw, uint th,      // target mode
        int x = 0, int y = 0,
        bool active = true) =>
        new(
            $@"\\.\DISPLAY{sourceId + 1}",
            sourceId,
            "00000000:00010000",
            targetId,
            new DisplayTarget($"T{targetId}", "TST", (ushort)(0x1000 + targetId),
                isInternal ? "Internal" : "HDMI", $@"monitor\{targetId}", isInternal, true),
            active,
            new DisplayMode(sw, sh, 60),
            new DisplayMode(tw, th, 60),
            x, y, "0",
            null, null, null, null);

    [Fact]
    public void EmptyTopology_IsConsistent()
    {
        var t = DisplayTopology.Empty;
        Assert.False(t.IsMirrored);
        Assert.False(t.HasMirrorAspectMismatch);
        Assert.Empty(t.ActivePaths);
        Assert.Null(t.InternalPanel);
        Assert.False(TriggerCondition.IsMet(t));
        Assert.Contains("0 active", t.ToString());
    }

    [Theory]
    [InlineData(0u, false)]          // id 0 falls back to grouping: no paths, no clone group
    [InlineData(0x1u, false)]       // internal-only topology id: not clone
    [InlineData(0x4u, false)]       // extend
    [InlineData(0x8u, false)]       // external
    [InlineData(0xFFFFFFFFu, false)] // garbage: not clone
    public void IsMirrored_TopologyIdContract(uint id, bool expected)
    {
        var t = new DisplayTopology(Array.Empty<DisplayPath>(), id);
        Assert.Equal(expected, t.IsMirrored);
    }

    [Fact]
    public void IsMirrored_IdZero_FallsBackToSourceGrouping()
    {
        // One source driving two targets = a clone group under the fallback.
        var paths = new List<DisplayPath>
        {
            P(0, 1, true, 1920, 1080, 1920, 1080),
            P(0, 2, false, 1920, 1080, 1920, 1080),
        };
        var t = new DisplayTopology(paths, 0u);
        Assert.True(t.IsMirrored);
        Assert.Single(t.ActiveCloneGroups);
        Assert.True(t.ActiveCloneGroups[0].IsClone);
    }

    [Fact]
    public void IsMirrored_IdZero_TwoDistinctSources_NoCloneGroup()
    {
        var paths = new List<DisplayPath>
        {
            P(0, 1, true, 1920, 1080, 1920, 1080),
            P(1, 2, false, 1920, 1080, 1920, 1080),
        };
        var t = new DisplayTopology(paths, 0u);
        Assert.False(t.IsMirrored, "two single-path sources under id-0 fallback are not a clone");
    }

    [Fact]
    public void HasMirrorAspectMismatch_SameDesktopDifferentTargetShape_True()
    {
        // The S6 real-world case: two sources sharing a desktop, external target
        // shape differs from the shared desktop.
        var paths = new List<DisplayPath>
        {
            P(0, 1, true, 3840, 2400, 3840, 2400),
            P(1, 2, false, 3840, 2400, 3840, 2160),
        };
        var t = new DisplayTopology(paths, DisplayTopologyId.Clone);
        Assert.True(t.HasMirrorAspectMismatch);
    }

    [Fact]
    public void HasMirrorAspectMismatch_InactiveExternalPaths_Ignored()
    {
        var paths = new List<DisplayPath>
        {
            P(0, 1, true, 3840, 2400, 3840, 2400),
            P(1, 2, false, 3840, 2400, 3840, 2160, active: false),
        };
        var t = new DisplayTopology(paths, DisplayTopologyId.Clone);
        Assert.False(t.HasMirrorAspectMismatch);
    }

    [Fact]
    public void HasMirrorAspectMismatch_ZeroSizeDesktop_NoMismatch()
    {
        // A degenerate zero-size desktop with real target modes: AspectRatio.Mismatch
        // returns false when either height is 0, so no trigger.
        var paths = new List<DisplayPath>
        {
            P(0, 1, true, 0, 0, 3840, 2400),
            P(0, 2, false, 0, 0, 1920, 1080),
        };
        var t = new DisplayTopology(paths, DisplayTopologyId.Clone);
        Assert.False(t.HasMirrorAspectMismatch);
    }

    [Fact]
    public void HasMirrorAspectMismatch_NullTargetMode_SkipsPath()
    {
        // Source mode real, target mode null (signal unknown): the loop must skip it.
        var paths = new List<DisplayPath>
        {
            new(@"\\.\DISPLAY1", 0, "00000000:00010000", 1,
                new DisplayTarget("Laptop", "TST", 0x1001, "Internal", @"monitor\1", true, true),
                true, new DisplayMode(1920, 1080, 60), null, 0, 0, "0", null, null, null, null),
        };
        var t = new DisplayTopology(paths, DisplayTopologyId.Clone);
        Assert.False(t.HasMirrorAspectMismatch);
    }

    [Fact]
    public void EffectiveMode_SourceModePreferred()
    {
        var withBoth = P(0, 1, false, 1920, 1080, 1280, 720);
        Assert.Equal(1920u, withBoth.EffectiveMode!.Width);

        var targetOnly = new DisplayPath(
            @"\\.\DISPLAY1", 0, "00000000:00010000", 1,
            new DisplayTarget("X", "TST", 1, "HDMI", @"monitor\x", false, true),
            true, null, new DisplayMode(1280, 720, 60), 0, 0, "0", null, null, null, null);
        Assert.Equal(1280u, targetOnly.EffectiveMode!.Width);

        var neither = new DisplayPath(
            @"\\.\DISPLAY1", 0, "00000000:00010000", 1,
            new DisplayTarget("X", "TST", 1, "HDMI", @"monitor\x", false, true),
            true, null, null, 0, 0, "0", null, null, null, null);
        Assert.Null(neither.EffectiveMode);
    }

    [Fact]
    public void DisplayTarget_Identity_FallsBackToManufacturerCode()
    {
        var withPath = new DisplayTarget("A", "DEL", 0x1234, "HDMI", @"monitor\path", false, true);
        Assert.Equal(@"monitor\path", withPath.Identity);

        var withoutPath = new DisplayTarget("A", "DEL", 0x1234, "HDMI", "", false, true);
        Assert.Equal("DEL:1234", withoutPath.Identity);

        var emptyPathNull = new DisplayTarget("A", "DEL", 0x1234, "HDMI", null!, false, true);
        Assert.Equal("DEL:1234", emptyPathNull.Identity);
    }

    [Fact]
    public void DisplayTopologyId_Describe_AllKnownValues()
    {
        Assert.Equal("internal", DisplayTopologyId.Describe(0x1));
        Assert.Equal("clone", DisplayTopologyId.Describe(0x2));
        Assert.Equal("extend", DisplayTopologyId.Describe(0x4));
        Assert.Equal("external", DisplayTopologyId.Describe(0x8));
        Assert.Equal("unknown", DisplayTopologyId.Describe(0));
        Assert.Equal("unknown(42)", DisplayTopologyId.Describe(42));
        Assert.True(DisplayTopologyId.IsKnown(0x2));
        Assert.False(DisplayTopologyId.IsKnown(0x3));
        Assert.False(DisplayTopologyId.IsKnown(0));
    }
}
