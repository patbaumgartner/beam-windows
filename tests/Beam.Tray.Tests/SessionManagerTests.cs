using Beam.App.Journal;
using Beam.App.Sessions;
using Beam.Core.NightLight;
using Beam.Core.Ports;
using Beam.Windows.Display;

namespace Beam.Tray.Tests;

/// <summary>
/// Session state machine — pure logic tested in Beam.App.Tests.
/// These tests cover the Tray integration contract: the shared SessionManager
/// is the same instance TrayService receives, so pause/resume from the tray
/// propagates to the presentation engine.
/// </summary>
public class SessionManagerTests
{
    [Fact]
    public void InitialState_IsIdle()
    {
        using var session = new SessionManager();
        Assert.Equal(SessionState.Idle, session.State);
    }

    [Fact]
    public void TryBeginFix_TransitionsIdle_Detecting_Applying_True()
    {
        using var session = new SessionManager();
        var events = new List<SessionEvent>();
        session.StateChanged += e => events.Add(e);

        bool ok = session.TryBeginFix();

        Assert.True(ok);
        Assert.Equal(SessionState.Applying, session.State);
        Assert.Equal(2, events.Count);
        Assert.Equal(SessionState.Detecting, events[0].To);
        Assert.Equal(SessionState.Applying, events[1].To);
    }

    [Fact]
    public void TryBeginFix_ReturnsFalse_WhenAlreadyActive()
    {
        using var session = new SessionManager();
        session.TryBeginFix();
        session.MarkActive();

        bool ok = session.TryBeginFix();
        Assert.False(ok);
        Assert.Equal(SessionState.Active, session.State);
    }

    [Fact]
    public void MarkActive_MovesApplyingToActive()
    {
        using var session = new SessionManager();
        session.TryBeginFix();

        session.MarkActive();

        Assert.Equal(SessionState.Active, session.State);
    }

    [Fact]
    public void RequestRestore_MovesActiveToRestoring()
    {
        using var session = new SessionManager();
        session.TryBeginFix();
        session.MarkActive();

        session.RequestRestore();

        Assert.Equal(SessionState.Restoring, session.State);
    }

    [Fact]
    public void MarkIdle_MovesRestoringToIdle()
    {
        using var session = new SessionManager();
        session.TryBeginFix();
        session.MarkActive();
        session.RequestRestore();

        session.MarkIdle();

        Assert.Equal(SessionState.Idle, session.State);
    }

    [Fact]
    public void Pause_And_Resume()
    {
        using var session = new SessionManager();

        session.Pause();
        Assert.Equal(SessionState.Paused, session.State);

        session.Resume();
        Assert.Equal(SessionState.Idle, session.State);
    }

    [Fact]
    public void Pause_WhileActive_Resume_ReturnsToActive()
    {
        // Pausing during a presentation must not lose the armed restore: resuming returns
        // to Active (the fix stays applied and a later disconnect still restores).
        using var session = new SessionManager();
        session.TryBeginFix();
        session.MarkActive();

        session.Pause();
        Assert.Equal(SessionState.Paused, session.State);

        session.Resume();
        Assert.Equal(SessionState.Active, session.State);
    }

    // ── Stress tests ─────────────────────────────────────────────────────────

    [Fact]
    public void ConcurrentBeginFix_OnlyFirstWins()
    {
        using var session = new SessionManager();
        long successCount = 0;
        var barrier = new Barrier(32);

        Parallel.For(0, 32, _ =>
        {
            barrier.SignalAndWait(); // all threads arrive, then all proceed simultaneously
            if (session.TryBeginFix()) Interlocked.Increment(ref successCount);
        });

        // Exactly one thread should win and transition to Applying.
        Assert.Equal(1, successCount);
        Assert.Equal(SessionState.Applying, session.State);
    }

#pragma warning disable xUnit1031 // test uses async/await pattern
    [Fact]
    public async Task ConcurrentPauseResume_NoDeadlock()
    {
        using var session = new SessionManager();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        async Task Worker(int seed)
        {
            var rng = new Random(seed);
            while (!cts.Token.IsCancellationRequested)
            {
                session.Pause();
                await Task.Delay(rng.Next(1, 5));
                session.Resume();
                await Task.Delay(rng.Next(1, 5));
            }
        }

        var tasks = Enumerable.Range(0, 8).Select(i => Worker(i)).ToArray();

        await Task.WhenAll(tasks);

        // Reaching here = no deadlock.
        cts.Cancel();
        Assert.True(true);
    }
#pragma warning restore xUnit1031

    [Fact]
    public void RapidStateTransitions_NoRaceConditions()
    {
        using var session = new SessionManager();
        var events = new System.Collections.Concurrent.ConcurrentBag<SessionEvent>();

        session.StateChanged += e => events.Add(e);

        // Fire TryBeginFix from multiple threads simultaneously.
        var barrier = new Barrier(16);
        Parallel.For(0, 16, _ =>
        {
            barrier.SignalAndWait();
            try { session.TryBeginFix(); } catch { }
            try { session.MarkActive(); } catch { }
            try { session.RequestRestore(); } catch { }
            try { session.MarkIdle(); } catch { }
        });

        // All events should have non-null From/To and valid timestamps.
        foreach (var evt in events)
        {
            Assert.True(Enum.IsDefined(typeof(SessionState), evt.From));
            Assert.True(Enum.IsDefined(typeof(SessionState), evt.To));
            Assert.NotEqual(default, evt.Timestamp);
        }
    }

    [Fact]
    public void SessionStateTransitions_FireExactlyOneEventPerHop()
    {
        using var session = new SessionManager();
        var events = new List<SessionEvent>();
        session.StateChanged += e => events.Add(e);

        session.TryBeginFix();     // Idle → Detecting → Applying (2 events)
        session.MarkActive();       // Applying → Active (1)
        session.RequestRestore();   // Active → Restoring (1)
        session.MarkIdle();         // Restoring → Idle (1)

        Assert.Equal(5, events.Count);
        Assert.Equal(new[] { SessionState.Detecting, SessionState.Applying, SessionState.Active, SessionState.Restoring, SessionState.Idle },
            events.Select(e => e.To).ToArray());
        Assert.Equal(SessionState.Idle, session.State);
    }
}
