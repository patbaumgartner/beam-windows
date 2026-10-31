using Beam.Core.Display;

namespace Beam.Core.Tests;

public class AspectRatioTests
{
    [Theory]
    [InlineData(1920u, 1080u, AspectKind.SixteenNine)]
    [InlineData(3840u, 2160u, AspectKind.SixteenNine)]
    [InlineData(1366u, 768u, AspectKind.SixteenNine)]
    [InlineData(3840u, 2400u, AspectKind.SixteenTen)]
    [InlineData(1920u, 1200u, AspectKind.SixteenTen)]
    [InlineData(2560u, 1600u, AspectKind.SixteenTen)]
    [InlineData(1024u, 768u, AspectKind.Other)]
    [InlineData(0u, 0u, AspectKind.Other)]
    public void Classify_KnownRatios(uint width, uint height, AspectKind expected)
    {
        Assert.Equal(expected, AspectRatio.Classify(width, height));
    }

    [Fact]
    public void Classify_FromMode_UsesModeSize()
    {
        var mode = new DisplayMode(1920, 1080, 60);
        Assert.Equal(AspectKind.SixteenNine, AspectRatio.Classify(mode));
    }

    [Theory]
    [InlineData(3840u, 2400u, 1920u, 1080u, true)] // 16:10 vs 16:9 -> mismatch
    [InlineData(1920u, 1080u, 1280u, 720u, false)] // 16:9 vs 16:9 -> match
    [InlineData(2560u, 1440u, 1920u, 1080u, false)] // 16:9 vs 16:9 -> match
    [InlineData(1920u, 1200u, 1920u, 1080u, true)] // 16:10 vs 16:9 -> mismatch
    public void Mismatch_DetectsRatioDifference(uint aw, uint ah, uint bw, uint bh, bool expected)
    {
        Assert.Equal(expected, AspectRatio.Mismatch(aw, ah, bw, bh));
    }

    [Theory]
    [InlineData(1920u, 1080u, "16:9")]
    [InlineData(3840u, 2400u, "16:10")]
    [InlineData(1024u, 768u, "1.333:1")]
    public void Describe_Readable(uint width, uint height, string expected)
    {
        Assert.Equal(expected, AspectRatio.Describe(width, height));
    }
}

public class DisplayTopologyTests
{
    private static DisplayPath Path(
        uint sourceId,
        uint targetId,
        string targetName,
        string outputTechnology,
        bool isActive,
        bool isInternal,
        uint sourceW,
        uint sourceH,
        uint targetW,
        uint targetH,
        uint refresh = 60,
        bool targetAvailable = true) =>
        new(
            $@"\\.\DISPLAY{sourceId + 1}",
            sourceId,
            "00000000:00010000",
            targetId,
            new DisplayTarget(targetName, "TST", (ushort)(0x1000 + targetId), outputTechnology,
                $@"monitor\{targetName}", isInternal, targetAvailable),
            isActive,
            new DisplayMode(sourceW, sourceH, refresh),
            new DisplayMode(targetW, targetH, refresh),
            0,
            0,
            "0",
            null,
            null,
            null,
            null);

    [Fact]
    public void ActiveCloneGroups_GroupPathsBySource()
    {
        // Two targets on source 0 = a clone group.
        var paths = new List<DisplayPath>
        {
            Path(0, 1, "Laptop", "Internal", true, true, 3840, 2400, 3840, 2400),
            Path(0, 2, "Projector", "HDMI", true, false, 3840, 2400, 1920, 1080),
        };

        var topology = new DisplayTopology(paths, DisplayTopologyId.Clone);
        var groups = topology.ActiveCloneGroups;

        Assert.Single(groups);
        Assert.True(groups[0].IsClone);
        Assert.Equal(2, groups[0].Paths.Count);
    }

    [Fact]
    public void HasMirrorAspectMismatch_TrueWhenCloneRationsDiffer()
    {
        // Laptop panel 16:10 mirrored to 1080p 16:9 projector.
        var paths = new List<DisplayPath>
        {
            Path(0, 1, "Laptop", "Internal", true, true, 3840, 2400, 3840, 2400),
            Path(0, 2, "Projector", "HDMI", true, false, 3840, 2400, 1920, 1080),
        };

        var topology = new DisplayTopology(paths, DisplayTopologyId.Clone);
        Assert.True(topology.HasMirrorAspectMismatch);
    }

    [Fact]
    public void HasMirrorAspectMismatch_FalseWhenRatiosMatch()
    {
        // Both 16:9.
        var paths = new List<DisplayPath>
        {
            Path(0, 1, "Laptop", "Internal", true, true, 1920, 1080, 1920, 1080),
            Path(0, 2, "Projector", "HDMI", true, false, 1920, 1080, 1920, 1080),
        };

        var topology = new DisplayTopology(paths, DisplayTopologyId.Clone);
        Assert.False(topology.HasMirrorAspectMismatch);
    }

    [Fact]
    public void HasMirrorAspectMismatch_FalseInExtendedMode()
    {
        // Two separate sources = extended, not mirrored.
        var paths = new List<DisplayPath>
        {
            Path(0, 1, "Laptop", "Internal", true, true, 3840, 2400, 3840, 2400),
            Path(1, 2, "Monitor", "DisplayPort external", true, false, 2560, 1440, 2560, 1440),
        };

        var topology = new DisplayTopology(paths, DisplayTopologyId.Extend);
        Assert.False(topology.HasMirrorAspectMismatch);
    }

    [Fact]
    public void ExternalPaths_ExcludesInternalPanel()
    {
        var paths = new List<DisplayPath>
        {
            Path(0, 1, "Laptop", "Internal", true, true, 3840, 2400, 3840, 2400),
            Path(1, 2, "Projector", "HDMI", true, false, 1920, 1080, 1920, 1080),
        };

        var topology = new DisplayTopology(paths, DisplayTopologyId.Clone);

        Assert.Single(topology.ExternalPaths);
        Assert.Equal("Projector", topology.ExternalPaths[0].Target.FriendlyName);
        Assert.NotNull(topology.InternalPanel);
    }

    [Fact]
    public void IsMirrored_True_WhenTopologyIdIsClone()
    {
        var topology = new DisplayTopology(Array.Empty<DisplayPath>(), DisplayTopologyId.Clone);
        Assert.True(topology.IsMirrored);
    }

    [Fact]
    public void IsMirrored_False_WhenTopologyIdIsNotClone()
    {
        var topology = new DisplayTopology(Array.Empty<DisplayPath>(), DisplayTopologyId.Extend);
        Assert.False(topology.IsMirrored);
    }

    [Fact]
    public void HasMirrorAspectMismatch_DetectsTwoSourceDuplicate()
    {
        // Windows 11 "Duplicate" on this laptop is TWO sources (src=0 internal panel,
        // src=1 DELL) rendering the same desktop. OS topology id is Clone. The DELL's
        // target signal differs from the shared 16:10 desktop -> side bars. This is the
        // case S6 hit: even though the sources differ, the OS topology id says clone.
        var paths = new List<DisplayPath>
        {
            Path(0, 1, "Laptop", "Internal", true, true, 3840, 2400, 3840, 2400, targetAvailable: true),
            Path(1, 2, "DELL", "DisplayPort external", true, false, 3840, 2400, 3840, 2160, targetAvailable: true),
        };

        var topology = new DisplayTopology(paths, DisplayTopologyId.Clone);
        Assert.True(topology.HasMirrorAspectMismatch);
    }

    [Fact]
    public void HasMirrorAspectMismatch_False_TwoSourceDuplicateSameAspect()
    {
        // Two sources, identical 16:9 desktop and 16:9 signal on both: no bars even in
        // duplicate topology.
        var paths = new List<DisplayPath>
        {
            Path(0, 1, "Laptop", "Internal", true, true, 1920, 1080, 1920, 1080),
            Path(1, 2, "DELL", "DisplayPort external", true, false, 1920, 1080, 1920, 1080),
        };

        var topology = new DisplayTopology(paths, DisplayTopologyId.Clone);
        Assert.False(topology.HasMirrorAspectMismatch);
    }
}
