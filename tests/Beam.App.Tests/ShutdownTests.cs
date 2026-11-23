using Beam.App.Sessions;
using Beam.Core.Display;

namespace Beam.App.Tests;

public class ShutdownTests
{
    private static DpiScale ScaleAt200 => DpiScale.From(-4, 0, 4)!;

    [Fact]
    public async Task Shutdown_ActivePresentation_DrainsAndRestoresOnce()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();

        await h.Manager.ShutdownAsync();
        await h.Manager.ShutdownAsync(); // idempotent; no duplicate restore

        Assert.True(h.Changes.Stopped);
        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.RestoreCalls);
        Assert.Null(h.Journal.Record);
    }

    [Fact]
    public async Task Shutdown_DuringBlockedApply_DrainsThenRestores()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Mode.BeforeCall = () =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        };

        _ = h.Manager.ApplyNowAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task shutdown = h.Manager.ShutdownAsync();
        Assert.False(shutdown.IsCompleted);

        release.TrySetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, h.Mode.ApplyCalls);
        Assert.Equal(1, h.Mode.RestoreCalls);
        Assert.Equal(SessionState.Idle, h.Session.State);
    }

    [Fact]
    public async Task Shutdown_PausedActivePresentation_RestoresOwnedChanges()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        h.Manager.PauseFor(TimeSpan.Zero);

        await h.Manager.ShutdownAsync();

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.RestoreCalls);
    }

    [Fact]
    public async Task Shutdown_RestoreFailure_KeepsRecoveryJournal()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        h.Mode.RestoreResult = "driver still rejecting restore";

        await h.Manager.ShutdownAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.True(h.Journal.Record!.IsActive);
        Assert.Equal(0, h.Journal.ClearCalls);
    }
}
