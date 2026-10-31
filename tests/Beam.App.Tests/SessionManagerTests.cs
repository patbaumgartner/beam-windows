using Beam.App.Sessions;

namespace Beam.App.Tests;

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
}
