using System.Collections.Concurrent;
using Beam.App.Presentation;
using Beam.App.Sessions;
using Beam.Core.Display;

namespace Beam.App.Tests;

/// <summary>
/// Soak and chaos tests for the presentation engine: 150 apply/restore cycles, a
/// parallel event storm against the serialization gate, safe-mode churn, self-change
/// window stress, the pause kill-switch, and dispose during an event storm. Everything
/// runs on the fake ports from Fakes.cs — no hardware needed.
/// </summary>
public class PresentationStressTests
{
    private static DpiScale ScaleAt200 => DpiScale.From(-4, 0, 4)!;

    /// <summary>
    /// Clock that advances 10 s on every read, so the 3 s self-change window can never
    /// suppress a change (every read is at least 10 s after the previous one).
    /// </summary>
    private sealed class AdvancingClock
    {
        private long _offsetTicks;

        public DateTimeOffset Read() =>
            DateTimeOffset.UtcNow + TimeSpan.FromTicks(
                Interlocked.Add(ref _offsetTicks, TimeSpan.FromSeconds(10).Ticks));
    }

    private sealed class FakeClock
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset Read() => Now;
        public void Advance(TimeSpan by) => Now = Now.Add(by);
    }

    /// <summary>
    /// Awaits <see cref="PresentationManager.PendingTask"/> until it stops being replaced
    /// by newer work. Faults rethrow, unless <paramref name="swallowFaults"/> is set (used
    /// after Dispose, where late gate waiters may fault with ObjectDisposedException).
    /// </summary>
    private static async Task DrainAsync(PresentationManager manager, bool swallowFaults = false)
    {
        while (true)
        {
            Task pending = manager.PendingTask;
            if (swallowFaults)
            {
                try
                {
                    await pending.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch
                {
                    // Faulted or stuck tasks after Dispose are expected here.
                }
            }
            else
            {
                await pending.WaitAsync(TimeSpan.FromSeconds(10));
            }

            if (ReferenceEquals(manager.PendingTask, pending))
            {
                break;
            }
        }
    }

    private static void AssertChain(IEnumerable<SessionEvent> events)
    {
        SessionEvent? previous = null;
        foreach (SessionEvent e in events)
        {
            if (previous is not null)
            {
                Assert.Equal(previous.To, e.From);
            }
            else
            {
                Assert.Equal(SessionState.Idle, e.From);
            }

            Assert.True(Enum.IsDefined(e.From));
            Assert.True(Enum.IsDefined(e.To));
            previous = e;
        }
    }

    [Fact]
    public async Task Soak_150ApplyRestoreCycles_CountsAndStateStayExact()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        for (int i = 0; i < 150; i++)
        {
            await h.Manager.ApplyNowAsync();
            Assert.Equal(SessionState.Active, h.Session.State);

            await h.Manager.RestoreNowAsync();
            Assert.Equal(SessionState.Idle, h.Session.State);
        }

        Assert.Equal(150, h.Mode.ApplyCalls);
        Assert.Equal(150, h.Mode.RestoreCalls);
        Assert.Equal(150, h.Journal.BeginCalls);
        Assert.Equal(150, h.Journal.ClearCalls);
        Assert.Null(h.Journal.Record);
        Assert.False(h.Manager.IsSafeMode);
    }

    [Fact]
    public async Task EventStorm_ParallelRaisesAndCommands_DrainsToConsistentIdle()
    {
        // Raises and manual commands from many threads at once. The gate serializes the
        // work; afterwards the session must be Idle, safe mode off, at least one apply
        // happened, the journal is cleared, and the state-event chain is unbroken.
        using var h = new PresentationHarness(
            Topologies.MirroredWithMismatch(), ScaleAt200, clock: new AdvancingClock().Read);
        var events = new ConcurrentQueue<SessionEvent>();
        h.Session.StateChanged += e => events.Enqueue(e);

        Parallel.For(0, 64, i =>
        {
            switch (i % 4)
            {
                case 0:
                    h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(3));
                    break;
                case 1:
                    h.Changes.Raise(Topologies.Snapshot(3), Topologies.Snapshot(2));
                    break;
                case 2:
                    _ = h.Manager.ApplyNowAsync();
                    break;
                default:
                    _ = h.Manager.RestoreNowAsync();
                    break;
            }
        });

        await DrainAsync(h.Manager);

        if (h.Session.State == SessionState.Active)
        {
            await h.Manager.RestoreNowAsync();
            await DrainAsync(h.Manager);
        }

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.False(h.Manager.IsSafeMode);
        Assert.True(h.Mode.ApplyCalls >= 1, $"expected at least one apply, got {h.Mode.ApplyCalls}");
        Assert.Null(h.Journal.Record);
        AssertChain(events);
    }

    [Fact]
    public async Task SafeModeChurn_FiveRounds_EventsAreExactTrueFalsePairs()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Mode.ApplyResult = "mode rejected";
        var events = new List<bool>();
        h.Manager.SafeModeChanged += safe => events.Add(safe);

        for (int round = 0; round < 5; round++)
        {
            await h.Manager.ApplyNowAsync();
            await h.Manager.ApplyNowAsync();
            Assert.True(h.Manager.IsSafeMode);
            Assert.Equal(SessionState.Idle, h.Session.State);

            h.Manager.ClearSafeMode();
            Assert.False(h.Manager.IsSafeMode);
        }

        Assert.Equal(10, h.Mode.ApplyCalls);
        Assert.Equal(10, h.Mode.RestoreCalls);

        var expected = new List<bool>();
        for (int i = 0; i < 5; i++)
        {
            expected.Add(true);
            expected.Add(false);
        }

        Assert.Equal(expected, events);
    }

    [Fact]
    public async Task SelfChangeWindow_25RaisesInsideWindowSuppressed_ExpiryAllowsFix()
    {
        var clock = new FakeClock();
        using var h = new PresentationHarness(
            Topologies.MirroredWithMismatch(), ScaleAt200, clock: clock.Read);

        await h.Manager.ApplyNowAsync();
        await h.Manager.RestoreNowAsync();
        Assert.Equal(SessionState.Idle, h.Session.State);
        h.Topology.Topology = Topologies.MirroredWithMismatch(); // bars are back

        // Every raise lands inside the fixed 3 s window: all suppressed.
        for (int i = 0; i < 25; i++)
        {
            h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2));
            await h.Manager.PendingTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.ApplyCalls);

        // Past the window the same change applies normally.
        clock.Advance(TimeSpan.FromSeconds(4));
        h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2));
        await h.Manager.PendingTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(2, h.Mode.ApplyCalls);
    }

    [Fact]
    public async Task KillSwitch_PauseWhileActive_DisconnectIgnored_ResumeThenDisconnectRestores()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        Assert.Equal(SessionState.Active, h.Session.State);

        h.Session.Pause();
        h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1)); // disconnect while paused
        await h.Manager.PendingTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(SessionState.Paused, h.Session.State);
        Assert.Equal(0, h.Mode.RestoreCalls);

        h.Session.Resume();
        Assert.Equal(SessionState.Active, h.Session.State);

        h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1)); // disconnect while active
        await h.Manager.PendingTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.RestoreCalls);
    }

    [Fact]
    public async Task Dispose_DuringEventStorm_NoThrow_RaisesAfterDisposeIgnored()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        var events = new ConcurrentQueue<SessionEvent>();
        h.Session.StateChanged += e => events.Enqueue(e);

        Parallel.For(0, 120, i =>
        {
            h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2));
            if (i == 60)
            {
                h.Manager.Dispose();
            }
        });

        await DrainAsync(h.Manager, swallowFaults: true);

        SessionState afterStorm = h.Session.State;
        for (int i = 0; i < 50; i++)
        {
            h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2));
        }

        await Task.Delay(100); // let any incorrectly-started late work run

        Assert.True(Enum.IsDefined(afterStorm));
        Assert.Equal(afterStorm, h.Session.State);
        AssertChain(events);
    }
}
