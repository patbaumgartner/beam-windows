using Beam.App.Sessions;
using Beam.Core.Display;
using Beam.Core.NightLight;
using Beam.Core.Ports;

namespace Beam.App.Tests;

/// <summary>
/// Stress and edge-case tests for the presentation engine.
/// Uses the fake ports from Fakes.cs to drive the manager through adversarial
/// inputs: burst changes, concurrent commands, repeated trigger cycling, and
/// failure injection.
/// </summary>
public class StressTests
{
    private static DpiScale ScaleAt200 => DpiScale.From(-4, 0, 4)!;

    // ── Burst display changes ───────────────────────────────────────────────────

    [Fact]
    public async Task ApplyNow_WhileFixIsRunning_WaitsForGate()
    {
        // ApplyNow runs the gate; a second ApplyNow while the first is running must wait
        // and return without double-applying. The fake mode controller tracks call counts.
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        Task applyTask = h.Manager.ApplyNowAsync();
        Task restoreTask = h.Manager.RestoreNowAsync(); // should wait for ApplyNow

        await Task.WhenAll(applyTask, restoreTask);

        // One apply (the first), one restore (from ApplyNow completing), RestoreNow waits.
        Assert.Equal(1, h.Mode.ApplyCalls);
        Assert.True(h.Session.State == SessionState.Idle); // restore completed → idle
    }

    [Fact]
    public async Task RepeatedTriggerCycling_MultipleApplyRestoreCycles()
    {
        // Cycling apply → restore → apply → restore repeatedly must not accumulate state.
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        for (int i = 0; i < 8; i++)
        {
            await h.Manager.ApplyNowAsync();
            Assert.Equal(SessionState.Active, h.Session.State);

            await h.Manager.RestoreNowAsync();
            Assert.Equal(SessionState.Idle, h.Session.State);
        }

        // Mode was called 8 times, restore 8 times, journal cleared 8 times.
        Assert.Equal(8, h.Mode.ApplyCalls);
        Assert.Equal(8, h.Mode.RestoreCalls);
        Assert.Equal(8, h.Journal.ClearCalls);
    }

    [Fact]
    public async Task DisconnectWhileActive_AfterBurst_Restores()
    {
        // Apply first (→ Active), then raise a disconnect burst. The session must restore
        // to Idle. Also confirms a spurious connect after the disconnect does not re-trigger.
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        Assert.Equal(SessionState.Active, h.Session.State);

        h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1)); // disconnect
        await h.Manager.PendingTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.RestoreCalls);
    }

    // ── Journal stress ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentApplyNowCalls_OnlyOneRuns()
    {
        // Multiple concurrent ApplyNow calls must serialize through the gate; only one
        // fix sequence runs even when the user mashes the button.
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        var tasks = Enumerable.Range(0, 16).Select(_ => h.Manager.ApplyNowAsync()).ToArray();
        await Task.WhenAll(tasks);

        // Exactly one Apply call even though 16 concurrent requests were fired.
        Assert.Equal(1, h.Mode.ApplyCalls);
        Assert.Equal(SessionState.Active, h.Session.State);
    }

    [Fact]
    public async Task Journal_ClearDuringApply_BestEffort()
    {
        // Calling Clear while a fix is running (on a different thread) is a race.
        // The journal must not throw; BestEffort semantics apply.
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        Task applyTask = h.Manager.ApplyNowAsync();
        // Clear immediately while apply is writing the journal.
        h.Journal.Clear();
        await applyTask;

        // Apply completed despite concurrent Clear().
        Assert.Equal(SessionState.Active, h.Session.State);
    }

    [Fact]
    public async Task RestoreNow_WhileFixIsRunning_WaitsForFix()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        Task apply = h.Manager.ApplyNowAsync();
        Task restore = h.Manager.RestoreNowAsync();

        await Task.WhenAll(apply, restore);

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.ApplyCalls);
        Assert.Equal(1, h.Mode.RestoreCalls);
    }

    [Fact]
    public async Task TriggerChangeDuringRestore_IsIgnored()
    {
        // If a trigger-met topology arrives during Restore, the session is in Restoring
        // state and must not re-trigger.
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200);

        await h.Manager.ApplyNowAsync();
        h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1)); // triggers restore

        // Now try to trigger while restoring — must be ignored.
        h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2)); // back to 2 displays

        await h.Manager.PendingTask.WaitAsync(TimeSpan.FromSeconds(2));

        // Session is back to Idle after restore completed; no re-trigger happened.
        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.ApplyCalls);
    }

    [Fact]
    public async Task ApplyNow_WhileActive_Ignored()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();

        await h.Manager.ApplyNowAsync(); // second call while active

        Assert.Equal(1, h.Mode.ApplyCalls); // still just one apply
    }

    [Fact]
    public async Task PauseDuringActive_IgnoresDisconnect()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        h.Session.Pause();

        // Disconnect while paused must be ignored (not restore).
        h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1));
        await h.Manager.PendingTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(SessionState.Paused, h.Session.State);
        Assert.Equal(0, h.Mode.RestoreCalls);
    }

    [Fact]
    public async Task RepeatedCheckCurrentState_WithNoTrigger_NoOp()
    {
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200);

        for (int i = 0; i < 5; i++)
        {
            await h.Manager.CheckCurrentStateAsync();
        }

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(0, h.Mode.ApplyCalls);
    }

    // ── Session manager stress ──────────────────────────────────────────────────

    [Fact]
    public void ConcurrentRequestRestore_NoEffectWhileIdle()
    {
        using var session = new SessionManager();
        int events = 0;
        session.StateChanged += _ => events++;

        // RequestRestore while Idle should be a no-op.
        Parallel.For(0, 16, _ => session.RequestRestore());

        Assert.Equal(SessionState.Idle, session.State);
        Assert.Equal(0, events);
    }

    [Fact]
    public void ConcurrentMarkActive_Idempotent()
    {
        using var session = new SessionManager();
        session.TryBeginFix();

        // All callers racing MarkActive — only the first should transition.
        Parallel.For(0, 16, _ =>
        {
            try { session.MarkActive(); } catch { }
        });

        Assert.Equal(SessionState.Active, session.State);
    }

    [Fact]
    public void RapidPauseResume_NoStateCorruption()
    {
        using var session = new SessionManager();
        var events = new List<SessionEvent>();
        session.StateChanged += e => events.Add(e);

        for (int i = 0; i < 32; i++)
        {
            session.Pause();
            session.Resume();
        }

        // Final state must be consistent.
        Assert.Equal(SessionState.Idle, session.State);
        // No unexpected states in the event stream.
        Assert.All(events, e =>
        {
            Assert.True(Enum.IsDefined(typeof(SessionState), e.From));
            Assert.True(Enum.IsDefined(typeof(SessionState), e.To));
        });
    }

    [Fact]
    public void PauseDuringActive_ResumeGoesBackToActive()
    {
        using var session = new SessionManager();
        session.TryBeginFix();
        session.MarkActive();

        session.Pause();
        Assert.Equal(SessionState.Paused, session.State);

        session.Resume();
        Assert.Equal(SessionState.Active, session.State);
    }
}
