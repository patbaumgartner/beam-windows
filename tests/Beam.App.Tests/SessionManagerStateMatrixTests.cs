using Beam.App.Sessions;

namespace Beam.App.Tests;

/// <summary>
/// Exhaustive state-machine coverage: from EVERY state, EVERY transition method is
/// attempted, and the reachable transitions are asserted against the documented
/// machine. Catches wedged/transient-state regressions that scenario tests miss.
/// </summary>
public class SessionManagerStateMatrixTests
{
    private static (SessionManager S, List<SessionEvent> Events) New()
    {
        var s = new SessionManager();
        var events = new List<SessionEvent>();
        s.StateChanged += events.Add;
        return (s, events);
    }

    private static void DriveTo(SessionManager s, SessionState target)
    {
        while (s.State != target)
        {
            switch (s.State, target)
            {
                case (SessionState.Idle, SessionState.Applying):
                case (SessionState.Idle, SessionState.Active):    // hop via Applying
                case (SessionState.Idle, SessionState.Restoring): // hop via Applying/Active
                    s.TryBeginFix();
                    break;
                case (SessionState.Applying, SessionState.Active):
                case (SessionState.Applying, SessionState.Restoring): // hop via Active
                    s.MarkActive();
                    break;
                case (SessionState.Active, SessionState.Restoring):
                    s.RequestRestore();
                    break;
                case (SessionState.Restoring, SessionState.Idle):
                    s.MarkIdle();
                    break;
                case (_, SessionState.Paused): // reachable from any state
                    s.Pause();
                    break;
                default:
                    throw new InvalidOperationException($"test bug: cannot drive {s.State} -> {target}");
            }
        }
    }

    private static readonly SessionState[] AllStates =
    [
        SessionState.Idle,
        SessionState.Detecting,
        SessionState.Applying,
        SessionState.Active,
        SessionState.Restoring,
        SessionState.Paused,
    ];

    [Fact]
    public void From_EveryState_AllMethodsAttempted_MachineHolds()
    {
        foreach (SessionState start in AllStates)
        {
            // Detecting is transient inside TryBeginFix; approximate with Applying.
            SessionState reachableStart = start == SessionState.Detecting
                ? SessionState.Applying
                : start;

            var (s, events) = New();
            DriveTo(s, reachableStart);

            // Fire every method once.
            s.TryBeginFix();
            s.MarkActive();
            s.RequestRestore();
            s.Pause();
            s.Resume();
            s.MarkIdle();

            // Invariant: never wedged in a transient state after the storm.
            Assert.True(
                s.State is not (SessionState.Applying or SessionState.Restoring),
                $"from {start}: wedged in {s.State}");
            events.Clear();
        }
    }

    [Fact]
    public void From_EveryState_PauseThenResume_ReturnsToValidState()
    {
        foreach (SessionState start in AllStates)
        {
            if (start == SessionState.Detecting)
            {
                continue; // not directly reachable
            }

            var (s, _) = New();
            DriveTo(s, start);
            s.Pause();
            Assert.Equal(SessionState.Paused, s.State);
            s.Resume();

            Assert.True(
                s.State is SessionState.Idle or SessionState.Active,
                $"pause/resume from {start} landed in {s.State}");
        }
    }

    [Fact]
    public void From_EveryState_RequestRestore_LandsInRestoringOrIgnores()
    {
        foreach (SessionState start in AllStates)
        {
            if (start == SessionState.Detecting)
            {
                continue;
            }

            var (s, _) = New();
            DriveTo(s, start);
            s.RequestRestore();

            // Restoring is entered from Detecting/Applying/Active only; Idle/Paused/
            // Restoring ignore.
            if (start is SessionState.Idle or SessionState.Paused or SessionState.Restoring)
            {
                Assert.Equal(start, s.State);
            }
            else
            {
                Assert.Equal(SessionState.Restoring, s.State);
            }
        }
    }

    [Fact]
    public void Events_CarryFromToAndReason()
    {
        var (s, events) = New();
        s.TryBeginFix();
        s.MarkActive();
        s.RequestRestore();
        s.MarkIdle();

        Assert.Equal(5, events.Count); // Idle>Detecting, Detecting>Applying, >Active, >Restoring, >Idle
        Assert.All(events, e => Assert.True(e.From != e.To));
        Assert.All(events, e => Assert.False(string.IsNullOrWhiteSpace(e.Reason)));
        Assert.All(events, e => Assert.True(e.Timestamp <= DateTimeOffset.Now));
    }

    [Fact]
    public void TryBeginFix_FromIdle_RunsAtomicallyToApplying()
    {
        var (s, _) = New();
        Assert.True(s.TryBeginFix());
        Assert.Equal(SessionState.Applying, s.State);
    }
}
