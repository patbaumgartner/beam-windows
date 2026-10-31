using Beam.Core.Display;

namespace Beam.Core.Tests;

/// <summary>
/// Small domain types that the rest of the system reasons with. They are cheap to test and
/// a bug here (an off-by-one topology id, a wrong identity string) would be expensive to
/// diagnose in the field.
/// </summary>
public class DisplayModelTests
{
    private static DisplayPath Path(
        uint sourceId,
        uint targetId,
        bool isInternal,
        bool active,
        uint sourceW = 1920,
        uint sourceH = 1080,
        uint targetW = 1920,
        uint targetH = 1080,
        string? devicePath = null) =>
        new(
            $@"\\.\DISPLAY{sourceId + 1}",
            sourceId,
            "00000001:00000002",
            targetId,
            new DisplayTarget(
                "Test display",
                "DEL",
                0x1234,
                "HDMI",
                devicePath ?? $@"monitor\test{targetId}",
                isInternal,
                true),
            active,
            new DisplayMode(sourceW, sourceH, 60),
            new DisplayMode(targetW, targetH, 60),
            0, 0, "0", null, null, null, null);

    // ── DisplayTopologyId ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(DisplayTopologyId.Internal, "internal")]
    [InlineData(DisplayTopologyId.Clone, "clone")]
    [InlineData(DisplayTopologyId.Extend, "extend")]
    [InlineData(DisplayTopologyId.External, "external")]
    [InlineData(0u, "unknown")]
    [InlineData(0x20u, "unknown(32)")]
    public void TopologyId_Describe_IsReadable(uint id, string expected)
    {
        Assert.Equal(expected, DisplayTopologyId.Describe(id));
    }

    [Theory]
    [InlineData(DisplayTopologyId.Internal, true)]
    [InlineData(DisplayTopologyId.Clone, true)]
    [InlineData(DisplayTopologyId.Extend, true)]
    [InlineData(DisplayTopologyId.External, true)]
    [InlineData(0u, false)]
    [InlineData(0x10u, false)]
    public void TopologyId_IsKnown_MatchesTheDefinedKinds(uint id, bool expected)
    {
        Assert.Equal(expected, DisplayTopologyId.IsKnown(id));
    }

    [Fact]
    public void TopologyId_ValuesMatchTheWindowsConstants()
    {
        // These are DISPLAYCONFIG_TOPOLOGY_ID / SDC_TOPOLOGY_* values; getting them wrong
        // silently disables the trigger.
        Assert.Equal(0x1u, DisplayTopologyId.Internal);
        Assert.Equal(0x2u, DisplayTopologyId.Clone);
        Assert.Equal(0x4u, DisplayTopologyId.Extend);
        Assert.Equal(0x8u, DisplayTopologyId.External);
    }

    // ── DisplayMode ─────────────────────────────────────────────────────────────

    [Fact]
    public void DisplayMode_AspectRatio_DividesDimensions()
    {
        var mode = new DisplayMode(2560, 1440, 60);

        Assert.Equal(2560.0 / 1440.0, mode.AspectRatio, precision: 6);
    }

    [Fact]
    public void DisplayMode_AspectRatio_ZeroHeightIsZero()
    {
        Assert.Equal(0, new DisplayMode(1920, 0, 60).AspectRatio);
    }

    [Fact]
    public void DisplayMode_Pixels_MultipliesDimensions()
    {
        Assert.Equal(3840UL * 2160UL, new DisplayMode(3840, 2160, 60).Pixels);
    }

    [Theory]
    [InlineData(1920u, 1080u, 60.0, "1920x1080@60Hz")]
    [InlineData(3840u, 2160u, 59.94, "3840x2160@59.94Hz")]
    [InlineData(1920u, 1080u, 0.0, "1920x1080")]
    public void DisplayMode_ToString_IncludesRefreshOnlyWhenKnown(uint w, uint h, double hz, string expected)
    {
        Assert.Equal(expected, new DisplayMode(w, h, hz).ToString());
    }

    // ── DisplayTarget / DisplayPath ─────────────────────────────────────────────

    [Fact]
    public void DisplayTarget_Identity_UsesDevicePathWhenPresent()
    {
        var target = new DisplayTarget("Dell", "DEL", 0x1234, "HDMI", @"\\?\DISPLAY#DEL1234#1", false, true);

        Assert.Equal(@"\\?\DISPLAY#DEL1234#1", target.Identity);
    }

    [Fact]
    public void DisplayTarget_Identity_FallsBackToVendorAndProduct()
    {
        var target = new DisplayTarget("Dell", "DEL", 0x1234, "HDMI", string.Empty, false, true);

        Assert.Equal("DEL:1234", target.Identity);
    }

    [Fact]
    public void DisplayPath_EffectiveMode_PrefersSourceMode()
    {
        DisplayPath path = Path(0, 1, isInternal: false, active: true, 1920, 1080, 1280, 720);

        Assert.Equal(new DisplayMode(1920, 1080, 60), path.EffectiveMode);
    }

    [Fact]
    public void DisplayPath_EffectiveMode_FallsBackToTargetMode()
    {
        DisplayPath path = Path(0, 1, isInternal: false, active: true) with { SourceMode = null };

        Assert.Equal(new DisplayMode(1920, 1080, 60), path.EffectiveMode);
    }

    // ── CloneGroup ──────────────────────────────────────────────────────────────

    [Fact]
    public void CloneGroup_IsClone_RequiresMoreThanOnePath()
    {
        DisplayPath only = Path(0, 1, isInternal: false, active: true);

        Assert.False(new CloneGroup(only.SourceGdiName, 0, only.SourceMode, new[] { only }).IsClone);
        Assert.True(new CloneGroup(only.SourceGdiName, 0, only.SourceMode, new[] { only, only with { TargetId = 2 } }).IsClone);
    }

    // ── DisplayTopology ─────────────────────────────────────────────────────────

    [Fact]
    public void DisplayTopology_Empty_HasNoPaths()
    {
        Assert.Empty(DisplayTopology.Empty.Paths);
        Assert.Empty(DisplayTopology.Empty.ActivePaths);
        Assert.False(DisplayTopology.Empty.IsMirrored);
        Assert.False(DisplayTopology.Empty.HasMirrorAspectMismatch);
        Assert.Null(DisplayTopology.Empty.InternalPanel);
    }

    [Fact]
    public void DisplayTopology_ActivePaths_FiltersInactive()
    {
        var topology = new DisplayTopology(
            new[]
            {
                Path(0, 1, isInternal: true, active: true),
                Path(1, 2, isInternal: false, active: false),
            },
            DisplayTopologyId.Extend);

        Assert.Single(topology.ActivePaths);
        Assert.Single(topology.ActiveCloneGroups);
    }

    [Fact]
    public void DisplayTopology_IsMirrored_FallsBackToSourceGroupingWhenTopologyIdUnreadable()
    {
        // Id 0 means the topology id could not be read. Two active paths on one source
        // still means duplicate, and the trigger must not silently turn itself off.
        var topology = new DisplayTopology(
            new[]
            {
                Path(0, 1, isInternal: true, active: true),
                Path(0, 2, isInternal: false, active: true),
            },
            0);

        Assert.True(topology.IsMirrored);
    }

    [Fact]
    public void DisplayTopology_IsMirrored_FalseWhenUnknownIdAndSeparateSources()
    {
        var topology = new DisplayTopology(
            new[]
            {
                Path(0, 1, isInternal: true, active: true),
                Path(1, 2, isInternal: false, active: true),
            },
            0);

        Assert.False(topology.IsMirrored);
    }

    [Fact]
    public void DisplayTopology_ToString_SummarisesCounts()
    {
        var topology = new DisplayTopology(
            new[]
            {
                Path(0, 1, isInternal: true, active: true),
                Path(0, 2, isInternal: false, active: true),
            },
            DisplayTopologyId.Clone);

        Assert.Equal("2 active path(s), 1 external, 1 clone group(s)", topology.ToString());
    }

    // ── TriggerCondition ────────────────────────────────────────────────────────

    [Fact]
    public void Trigger_RejectsNullTopology()
    {
        Assert.Throws<ArgumentNullException>(() => TriggerCondition.IsMet(null!));
    }

    [Fact]
    public void Trigger_Met_ForMirroredExternalWithMismatch()
    {
        var topology = new DisplayTopology(
            new[]
            {
                Path(0, 1, isInternal: true, active: true, 3840, 2400, 3840, 2400),
                Path(1, 2, isInternal: false, active: true, 3840, 2400, 3840, 2160),
            },
            DisplayTopologyId.Clone);

        Assert.True(TriggerCondition.IsMet(topology));
    }

    [Fact]
    public void Trigger_NotMet_WithoutExternalDisplay()
    {
        var topology = new DisplayTopology(
            new[] { Path(0, 1, isInternal: true, active: true, 3840, 2400, 3840, 2160) },
            DisplayTopologyId.Clone);

        Assert.False(TriggerCondition.IsMet(topology));
    }

    [Fact]
    public void Trigger_NotMet_WhenExtended()
    {
        var topology = new DisplayTopology(
            new[]
            {
                Path(0, 1, isInternal: true, active: true, 3840, 2400, 3840, 2400),
                Path(1, 2, isInternal: false, active: true, 3840, 2400, 3840, 2160),
            },
            DisplayTopologyId.Extend);

        Assert.False(TriggerCondition.IsMet(topology));
    }

    [Fact]
    public void Trigger_NotMet_WhenAspectsMatch()
    {
        var topology = new DisplayTopology(
            new[]
            {
                Path(0, 1, isInternal: true, active: true),
                Path(1, 2, isInternal: false, active: true),
            },
            DisplayTopologyId.Clone);

        Assert.False(TriggerCondition.IsMet(topology));
    }
}
