using Beam.App.Sessions;
using Beam.Core.Display;

namespace Beam.App.Tests;

public class PauseResumeTests
{
    private static DpiScale ScaleAt200 => DpiScale.From(-4, 0, 4)!;

    [Fact]
    public async Task Resume_RealPresentationDisconnectedWhilePaused_RestoresWithoutSecondEvent()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        h.Manager.PauseFor(TimeSpan.Zero);
        h.Topology.Topology = Topologies.InternalOnly();
        h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1));
        await h.Manager.PendingTask;

        await h.Manager.ResumeAsync();

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.RestoreCalls);
    }

    [Fact]
    public async Task Resume_SimulatedPresentation_StaysActiveWithoutRealProjector()
    {
        using var h = new PresentationHarness(Topologies.InternalOnly(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        h.Manager.PauseFor(TimeSpan.Zero);

        await h.Manager.ResumeAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(0, h.Mode.RestoreCalls);
    }

    [Fact]
    public async Task Resume_IdleWithNewTrigger_EvaluatesWithoutSecondEvent()
    {
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200);
        h.Manager.PauseFor(TimeSpan.Zero);
        h.Topology.Topology = Topologies.MirroredWithMismatch();

        await h.Manager.ResumeAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(1, h.Mode.ApplyCalls);
    }

    [Fact]
    public async Task Disable_WhilePausedActive_RestoresOwnedPresentation()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        h.Manager.PauseFor(TimeSpan.Zero);

        await h.Manager.SetEnabledAsync(false);

        Assert.False(h.Manager.IsEnabled);
        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.RestoreCalls);
    }

    [Fact]
    public async Task IndefinitePause_CancelsPreviousTimedResume()
    {
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200);
        h.Manager.PauseFor(TimeSpan.FromMilliseconds(40));
        await h.Manager.ResumeAsync();

        h.Manager.PauseFor(TimeSpan.Zero);
        await Task.Delay(150);

        Assert.Equal(SessionState.Paused, h.Session.State);
    }

    [Fact]
    public async Task Pause_DuringApplying_IsDeferredUntilStableState()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Mode.BeforeCall = () =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        };

        Task apply = h.Manager.ApplyNowAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        h.Manager.PauseFor(TimeSpan.Zero);

        Assert.Equal(SessionState.Applying, h.Session.State);
        release.TrySetResult();
        await apply;
        Assert.Equal(SessionState.Active, h.Session.State);
    }
}
