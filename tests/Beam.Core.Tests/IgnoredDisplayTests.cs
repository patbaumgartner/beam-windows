using Beam.Core.Display;

namespace Beam.Core.Tests;

/// <summary>
/// B5: per-display "Ignore this display" — ignored target identities are excluded from
/// the trigger evaluation before grouping/counting.
/// </summary>
public class IgnoredDisplayTests
{
    private static DisplayPath Path(
        uint sourceId,
        uint targetId,
        string targetName,
        bool isInternal,
        bool active,
        uint sourceW,
        uint sourceH,
        uint targetW,
        uint targetH) =>
        new(
            $@"\\.\DISPLAY{sourceId + 1}",
            sourceId,
            "00000000:00010000",
            targetId,
            new DisplayTarget(targetName, "TST", (ushort)(0x1000 + targetId), "HDMI",
                $@"monitor\{targetName}", isInternal, true),
            active,
            new DisplayMode(sourceW, sourceH, 60),
            new DisplayMode(targetW, targetH, 60),
            0, 0, "0", null, null, null, null);

    /// <summary>Mirrored 16:10 laptop + 16:9 projector with bars — the trigger case.</summary>
    private static DisplayTopology MirroredMismatch() =>
        new(
            new[]
            {
                Path(0, 1, "Laptop", isInternal: true, active: true, 3840, 2400, 3840, 2400),
                Path(1, 2, "Projector", isInternal: false, active: true, 3840, 2400, 3840, 2160),
            },
            DisplayTopologyId.Clone);

    [Fact]
    public void IsMet_IgnoringIgnoredExternal_IsFalse()
    {
        var topology = MirroredMismatch();
        Assert.True(TriggerCondition.IsMet(topology));
        Assert.False(TriggerCondition.IsMet(topology, new[] { @"monitor\Projector" }));
    }

    [Fact]
    public void IsMet_IgnoringUnrelatedIdentity_StillTrue()
    {
        var topology = MirroredMismatch();
        Assert.True(TriggerCondition.IsMet(topology, new[] { @"monitor\SomethingElse" }));
    }

    [Fact]
    public void IsMet_NullOrEmptyIgnoreList_BehavesLikeUnfiltered()
    {
        var topology = MirroredMismatch();
        Assert.True(TriggerCondition.IsMet(topology, null));
        Assert.True(TriggerCondition.IsMet(topology, Array.Empty<string>()));
    }

    [Fact]
    public void IsMet_IgnoredOverload_StillRejectsNullTopology()
    {
        Assert.Throws<ArgumentNullException>(() => TriggerCondition.IsMet(null!, new[] { "x" }));
    }

    [Fact]
    public void HasMirrorAspectMismatchIgnoring_FiltersIgnoredPathBeforeGrouping()
    {
        var topology = MirroredMismatch();
        Assert.True(topology.HasMirrorAspectMismatch);
        // With the projector ignored the clone group collapses to one path → no group → no mismatch.
        Assert.False(topology.HasMirrorAspectMismatchIgnoring(new[] { @"monitor\Projector" }));
        Assert.True(topology.HasMirrorAspectMismatchIgnoring(new[] { @"monitor\Other" }));
        Assert.True(topology.HasMirrorAspectMismatchIgnoring(null));
    }

    [Fact]
    public void ExternalPathsIgnoring_ExcludesIgnoredIdentity()
    {
        var topology = MirroredMismatch();
        Assert.Single(topology.ExternalPathsIgnoring(null));
        Assert.Empty(topology.ExternalPathsIgnoring(new[] { @"monitor\Projector" }));
        Assert.Single(topology.ExternalPathsIgnoring(new[] { @"monitor\Other" }));
    }

    [Fact]
    public void IgnoringInternalIdentity_LeavesExternalButNoMismatch()
    {
        // Ignoring the laptop panel leaves only the projector in the group → single path → no mismatch.
        var topology = MirroredMismatch();
        Assert.False(topology.HasMirrorAspectMismatchIgnoring(new[] { @"monitor\Laptop" }));
        Assert.Single(topology.ExternalPathsIgnoring(new[] { @"monitor\Laptop" }));
        Assert.False(TriggerCondition.IsMet(topology, new[] { @"monitor\Laptop" }));
    }
}
