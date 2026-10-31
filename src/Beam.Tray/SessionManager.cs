namespace Beam.Tray;

/// <summary>
/// The state of Beam's presentation session.
/// </summary>
public enum SessionState
{
    /// <summary>Monitoring but no external display with aspect mismatch is active.</summary>
    Idle,

    /// <summary>Detected a candidate; deciding whether to apply the fix.</summary>
    Detecting,

    /// <summary>Applying Night Light → duplicate → DPI. Brief, transient.</summary>
    Applying,

    /// <summary>The presentation fix is active. Restore is armed.</summary>
    Active,

    /// <summary>Restoring the original display state. Brief, transient.</summary>
    Restoring,

    /// <summary>Monitoring is paused; nothing is applied regardless of topology.</summary>
    Paused,
}

/// <summary>
/// A change in session state, with context for diagnostics.
/// </summary>
public sealed record SessionEvent(
    SessionState From,
    SessionState To,
    string Reason,
    DateTimeOffset Timestamp);

/// <summary>
/// Orchestrates the state machine that drives Beam's presentation session.
///
/// Transitions are always forward (no self-loops unless the state changes):
/// <code>
/// Idle → Detecting  (external + mirrored + aspect mismatch found)
/// Detecting → Applying  (automatic; all preconditions met)
/// Applying → Active  (fix sequence completed)
/// Active → Restoring  (external display disconnected or manual undo)
/// Restoring → Idle  (original state restored)
/// [Any] → Paused  (user paused)
/// Paused → [previous non-paused state]  (user resumed)
/// </code>
/// </summary>
public sealed class SessionManager : IDisposable
{
    private readonly object _lock = new();
    private SessionState _state = SessionState.Idle;
    private SessionState _prePause = SessionState.Idle;

    public event Action<SessionEvent>? StateChanged;

    public SessionState State
    {
        get { lock (_lock) return _state; }
    }

    /// <summary>
    /// Called by the presentation manager when the trigger condition becomes true
    /// (external display + mirrored + aspect mismatch).
    /// </summary>
    public bool TryBeginFix()
    {
        lock (_lock)
        {
            if (_state is not SessionState.Idle and not SessionState.Detecting)
            {
                return false;
            }

            TransitionTo(SessionState.Detecting, "trigger condition met");
            TransitionTo(SessionState.Applying, "preconditions satisfied");
            return true;
        }
    }

    /// <summary>
    /// Called by the presentation manager after the fix sequence completes successfully.
    /// </summary>
    public void MarkActive()
    {
        lock (_lock)
        {
            if (_state != SessionState.Applying)
            {
                return;
            }

            TransitionTo(SessionState.Active, "fix sequence applied");
        }
    }

    /// <summary>
    /// Called by the presentation manager when the fix should be undone
    /// (display disconnected, manual undo, or error during apply).
    /// </summary>
    public void RequestRestore()
    {
        lock (_lock)
        {
            if (_state is SessionState.Paused or SessionState.Restoring or SessionState.Idle)
            {
                return;
            }

            TransitionTo(SessionState.Restoring, "restore requested");
        }
    }

    /// <summary>
    /// Called by the presentation manager after restore completes.
    /// </summary>
    public void MarkIdle()
    {
        lock (_lock)
        {
            if (_state != SessionState.Restoring)
            {
                return;
            }

            TransitionTo(SessionState.Idle, "restore complete");
        }
    }

    /// <summary>User-requested pause — stops applying fixes without undoing an active one.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (_state == SessionState.Paused)
            {
                return;
            }

            _prePause = _state is SessionState.Idle or SessionState.Detecting
                ? SessionState.Idle
                : SessionState.Idle; // if active, pausing still stops monitoring

            TransitionTo(SessionState.Paused, "user paused");
        }
    }

    /// <summary>User-requested resume from pause.</summary>
    public void Resume()
    {
        lock (_lock)
        {
            if (_state != SessionState.Paused)
            {
                return;
            }

            TransitionTo(SessionState.Idle, "user resumed");
        }
    }

    private void TransitionTo(SessionState to, string reason)
    {
        SessionState from = _state;
        _state = to;
        StateChanged?.Invoke(new SessionEvent(from, to, reason, DateTimeOffset.Now));
    }

    public void Dispose()
    {
        // No unmanaged resources in this class, but follow the pattern.
    }
}
