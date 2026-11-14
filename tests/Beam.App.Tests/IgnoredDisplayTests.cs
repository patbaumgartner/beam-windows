using Beam.App.Presentation;
using Beam.App.Sessions;
using Beam.Core.Display;

namespace Beam.App.Tests;

/// <summary>B5: the presentation manager honours the per-display ignore list.</summary>
public class IgnoredDisplayTests
{
    // Topologies.Path builds DisplayTarget with DevicePath = @"monitor\<name>".
    private const string ProjectorIdentity = @"monitor\DELL";

    [Fact]
    public void IgnoreDisplay_RemovesDisplayFromTrigger()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch());
        Assert.True(PresentationManager.IsTriggerConditionMet(h.Topology.Topology));

        h.Manager.IgnoreDisplay(ProjectorIdentity);

        Assert.False(PresentationManager.IsTriggerConditionMet(h.Topology.Topology, h.Manager.IgnoredDisplays));
        Assert.Contains(ProjectorIdentity, h.Manager.IgnoredDisplays);
    }

    [Fact]
    public async Task IgnoredDisplay_PreventsAutoFix_OnDisplayChange()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch());
        h.Manager.IgnoreDisplay(ProjectorIdentity);

        h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2));
        await h.Manager.PendingTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, h.Mode.ApplyCalls);
        Assert.Equal(SessionState.Idle, h.Session.State);
    }

    [Fact]
    public async Task UnignoredDisplay_ReenablesAutoFix()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch());
        h.Manager.IgnoreDisplay(ProjectorIdentity);
        h.Manager.UnignoreDisplay(ProjectorIdentity);

        h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2));
        await h.Manager.PendingTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, h.Mode.ApplyCalls);
        Assert.Equal(SessionState.Active, h.Session.State);
    }

    [Fact]
    public void IgnoreDisplay_RaisesIgnoredDisplaysChanged()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch());
        IReadOnlyCollection<string>? seen = null;
        h.Manager.IgnoredDisplaysChanged += list => seen = list;

        h.Manager.IgnoreDisplay(ProjectorIdentity);

        Assert.NotNull(seen);
        Assert.Contains(ProjectorIdentity, seen!);
    }

    [Fact]
    public void IgnoreDisplay_IsIdempotent_AndValidatesInput()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch());
        h.Manager.IgnoreDisplay(ProjectorIdentity);
        h.Manager.IgnoreDisplay(ProjectorIdentity); // no duplicate
        Assert.Single(h.Manager.IgnoredDisplays);

        Assert.Throws<ArgumentException>(() => h.Manager.IgnoreDisplay(""));
        Assert.Throws<ArgumentNullException>(() => h.Manager.UnignoreDisplay(null!));
    }

    [Fact]
    public void SetIgnoredDisplays_ReplacesList_AndFiltersCache()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch());
        h.Manager.RefreshExternalPaths(); // warm the cache (fake topology — safe in tests)
        Assert.Single(h.Manager.CurrentExternalPaths);

        // duplicates + empty entries are normalised away
        h.Manager.SetIgnoredDisplays(new[] { ProjectorIdentity, "", ProjectorIdentity });

        Assert.Single(h.Manager.IgnoredDisplays);
        Assert.Empty(h.Manager.CurrentExternalPaths);
    }

    [Fact]
    public void IgnoreDisplay_RefiltersCache_WithoutNewTopologyQuery()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch());
        h.Manager.RefreshExternalPaths();
        Assert.Single(h.Manager.CurrentExternalPaths);
        int queriesAfterWarm = h.Topology.QueryCount;

        h.Manager.IgnoreDisplay(ProjectorIdentity); // cache-only — must not query (A6)

        Assert.Empty(h.Manager.CurrentExternalPaths);
        Assert.Equal(queriesAfterWarm, h.Topology.QueryCount);
    }
}
