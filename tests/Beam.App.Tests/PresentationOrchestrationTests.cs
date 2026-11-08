using Beam.App.Journal;
using Beam.App.Sessions;
using Beam.Core.Display;
using Beam.Core.NightLight;

namespace Beam.App.Tests;

/// <summary>
/// End-to-end tests for the presentation sequence, using fake ports. These cover the
/// apply → active → disconnect → restore round-trip, pause semantics, and crash recovery.
/// </summary>
public class PresentationOrchestrationTests
{
    private static readonly byte[] RealDefaultOff =
    [
        0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06,
        0x86, 0xE6, 0x8B, 0xD7, 0x06,
        0x2A, 0x2B, 0x0E, 0x10, 0x43, 0x42, 0x01,
        0x00, 0xC6, 0x14,
        0xAA, 0x8E, 0xD0, 0xE6, 0xD8, 0xEE, 0xD9, 0xEE, 0x01,
        0x00, 0x00, 0x00, 0x00,
    ];

    private static DpiScale ScaleAt200 => DpiScale.From(-4, 0, 4)!;

    [Fact]
    public async Task ApplyNow_RunsFullSequence_AndBecomesActive()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        await h.Manager.ApplyNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(1, h.Mode.ApplyCalls);
        Assert.Equal(1, h.NightLight.SnapshotCalls);
        Assert.Equal(1, h.NightLight.SetActiveCalls);
        Assert.False(h.NightLight.LastActive);
        Assert.Equal(1, h.Journal.BeginCalls);
        Assert.Equal(0, h.Mode.RestoreCalls);
    }

    [Fact]
    public async Task ApplyNow_SetsOptimalDpi()
    {
        using var h = new PresentationHarness(Topologies.MirroredNeedScale(), ScaleAt200);

        await h.Manager.ApplyNowAsync();

        // 1920 px at 200% gives a 960 px logical desktop; the planner prefers 100%.
        Assert.Equal(new[] { 100 }, h.Dpi.RequestedPercents);
    }

    [Fact]
    public async Task ApplyNow_WhenDpiUnsupported_StillCompletes()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), dpi: null);

        await h.Manager.ApplyNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Empty(h.Dpi.RequestedPercents);
    }

    [Fact]
    public async Task ApplyNow_IsManualOverride_EvenWithoutTrigger()
    {
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200);

        await h.Manager.ApplyNowAsync();

        // "Apply projector mode now" is an explicit user action, so it does not consult
        // the automatic trigger. (The automatic path does; see the trigger tests.)
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(1, h.Mode.ApplyCalls);
        Assert.Equal(1, h.NightLight.SetActiveCalls);
    }

    [Fact]
    public async Task ApplyNow_WhilePaused_ResumesFirst()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Session.Pause();

        await h.Manager.ApplyNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(1, h.Mode.ApplyCalls);
    }

    [Fact]
    public async Task ApplyNow_WithModeError_UnwindsViaRestore_NotActive()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Mode.ApplyResult = "mode rejected";

        await h.Manager.ApplyNowAsync();

        // UC-12: a failed mode apply must not leave a half-applied session — unwind via
        // restore and return to Idle. One failure is below the safe-mode threshold.
        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.ApplyCalls);
        Assert.Equal(1, h.Mode.RestoreCalls);
        Assert.False(h.Manager.IsSafeMode);
    }

    [Fact]
    public async Task CheckCurrentState_AppliesWhenTriggerAlreadyMet()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        await h.Manager.CheckCurrentStateAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
    }

    [Fact]
    public async Task CheckCurrentState_NoOpWhenSafe()
    {
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200);

        await h.Manager.CheckCurrentStateAsync();

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(0, h.Mode.ApplyCalls);
    }

    [Fact]
    public async Task DisconnectWhileActive_RestoresAndReturnsToIdle()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.NightLight.Snapshots = new[] { Snapshot() };
        await h.Manager.ApplyNowAsync();

        h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1));
        await h.Manager.PendingTask;

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.RestoreCalls);
        Assert.Equal(1, h.NightLight.RestoreCalls);
        Assert.Equal(1, h.Journal.ClearCalls);
    }

    private static NightLightSnapshot Snapshot() =>
        new(@"KEY\default", "Data", RealDefaultOff, NightLightBlobCodec.Parse(RealDefaultOff));

    [Fact]
    public async Task PausedSession_IgnoresDisplayChanges()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        h.Session.Pause();

        h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1));
        await h.Manager.PendingTask;

        Assert.Equal(SessionState.Paused, h.Session.State);
        Assert.Equal(0, h.Mode.RestoreCalls);
    }

    [Fact]
    public async Task DisplayChangeWithTrigger_StartsFix()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2));
        await h.Manager.PendingTask;

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(1, h.Mode.ApplyCalls);
    }

    [Fact]
    public async Task RestoreNow_WhenActive_Restores()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();

        await h.Manager.RestoreNowAsync();

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.RestoreCalls);
    }

    [Fact]
    public async Task RestoreNow_WhenPaused_DoesNothing()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        h.Session.Pause();

        await h.Manager.RestoreNowAsync();

        Assert.Equal(SessionState.Paused, h.Session.State);
        Assert.Equal(0, h.Mode.RestoreCalls);
    }

    [Fact]
    public async Task TryRecover_ActiveJournal_RestoresCapturedNightLight()
    {
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200);
        h.Journal.Record = new JournalRecord(
            IsActive: true,
            Reason: "fix-sequence-started",
            AppliedAt: DateTimeOffset.Now.ToString("o"),
            NightLight: new[]
            {
                new JournalEntry(@"KEY\default", "Data", Convert.ToBase64String(RealDefaultOff)),
            });

        await h.Manager.TryRecoverAsync();

        Assert.Equal(1, h.Mode.RestoreCalls);
        Assert.Equal(1, h.NightLight.RestoreCalls);
        Assert.Single(h.NightLight.LastRestored!);
        Assert.Equal(1, h.Journal.ClearCalls);
    }

    [Fact]
    public async Task TryRecover_IdleJournal_DoesNothing()
    {
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200);
        h.Journal.Record = new JournalRecord(false, "done", DateTimeOffset.Now.ToString("o"), null);

        await h.Manager.TryRecoverAsync();

        Assert.Equal(0, h.Mode.RestoreCalls);
        Assert.Equal(0, h.Journal.ClearCalls);
    }

    [Fact]
    public async Task TryRecover_NoJournal_DoesNothing()
    {
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200);

        await h.Manager.TryRecoverAsync();

        Assert.Equal(0, h.Mode.RestoreCalls);
    }
}
