using Beam.App.Journal;
using Beam.App.Sessions;
using Beam.Core.Display;

namespace Beam.App.Tests;

/// <summary>
/// B3 presentation extras (keep-awake, DND/toasts, audio-to-HDMI): applied on fix,
/// journaled with their previous state, restored on restore — including crash recovery —
/// and individually failure-isolated so an extra failure never fails the session.
/// </summary>
public class PresentationExtrasTests
{
    private static DpiScale ScaleAt200 => DpiScale.From(-4, 0, 4)!;

    [Fact]
    public async Task Fix_AppliesAllExtras_AndJournalsPreviousState()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Dnd.SnapshotResult = true;
        h.Audio.DefaultEndpoint = "prev-speakers";

        await h.Manager.ApplyNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(1, h.KeepAwake.HoldCalls);
        Assert.Equal(new[] { false }, h.Dnd.Requested);       // toasts silenced
        Assert.Equal(1, h.Audio.SwitchCalls);                  // audio switched to HDMI
        Assert.NotNull(h.Journal.LastExtras);
        Assert.Equal(true, h.Journal.LastExtras!.ToastsWereEnabled);
        Assert.Equal("prev-speakers", h.Journal.LastExtras.PreviousAudioEndpoint);
        // The journal record itself carries the extras, so a crash mid-fix can recover them.
        Assert.Equal(h.Journal.LastExtras, h.Journal.Record?.Extras);
    }

    [Fact]
    public async Task Restore_UndoesAllExtras()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        await h.Manager.ApplyNowAsync();
        await h.Manager.RestoreNowAsync();

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.KeepAwake.ReleaseCalls);
        Assert.Equal(new[] { false, true }, h.Dnd.Requested);  // silenced on fix, restored on restore
        Assert.Equal(new[] { "prev-speakers" }, h.Audio.Restored);
        Assert.Equal(1, h.Journal.ClearCalls);
    }

    [Fact]
    public async Task Restore_UserReenabledNotifications_PreservesUserChoice()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        h.Dnd.SnapshotResult = true;

        await h.Manager.RestoreNowAsync();

        Assert.Equal(new[] { false }, h.Dnd.Requested);
        Assert.Empty(h.Dnd.Requested.Skip(1));
    }

    [Fact]
    public async Task Restore_UserChangedAudioEndpoint_PreservesUserChoice()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        h.Audio.DefaultEndpoint = "user-headphones";

        await h.Manager.RestoreNowAsync();

        Assert.Empty(h.Audio.Restored);
    }

    [Fact]
    public async Task ExtrasFailure_NeverFailsSession()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Dnd.ThrowOnSet = true;
        h.Audio.ThrowOnSwitch = true;

        await h.Manager.ApplyNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);    // failures isolated
        Assert.Equal(1, h.KeepAwake.HoldCalls);                // the other extras still applied
        Assert.False(h.Manager.IsSafeMode);
    }

    [Fact]
    public async Task AudioWithoutCapturedPreviousEndpoint_SkipsSwitch()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Audio.DefaultEndpoint = null;

        await h.Manager.ApplyNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(0, h.Audio.SwitchCalls);
    }

    [Fact]
    public async Task PolicyOff_SkipsThoseExtras()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Manager.KeepAwake = false;
        h.Manager.DoNotDisturb = false;
        h.Manager.AudioToHdmi = false;

        await h.Manager.ApplyNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(0, h.KeepAwake.HoldCalls);
        Assert.Empty(h.Dnd.Requested);
        Assert.Equal(0, h.Dnd.SnapshotCalls);
        Assert.Equal(0, h.Audio.SwitchCalls);
        Assert.Equal(0, h.Audio.GetDefaultCalls);
        Assert.Null(h.Journal.LastExtras); // nothing captured → nothing journaled
    }

    [Fact]
    public async Task PolicyOff_AudioOnly_ExtrasCarryOnlyToasts()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Manager.AudioToHdmi = false;

        await h.Manager.ApplyNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(0, h.Audio.SwitchCalls);
        Assert.Equal(0, h.Audio.GetDefaultCalls);
        Assert.NotNull(h.Journal.LastExtras);
        Assert.Equal(true, h.Journal.LastExtras!.ToastsWereEnabled);
        Assert.Null(h.Journal.LastExtras.PreviousAudioEndpoint);
    }

    [Fact]
    public async Task PortsAbsent_ExtrasSkipped_SessionStillWorks()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200, wireExtras: false);

        await h.Manager.ApplyNowAsync();
        await h.Manager.RestoreNowAsync();

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.ApplyCalls);
        Assert.Equal(1, h.Mode.RestoreCalls);
        Assert.Null(h.Journal.LastExtras);
    }

    [Fact]
    public async Task Dnd_AlreadyOff_SkipsWrite_ButStillJournaled()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Dnd.SnapshotResult = false; // toasts already off

        await h.Manager.ApplyNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Empty(h.Dnd.Requested); // no write needed when already off
        Assert.Equal(false, h.Journal.LastExtras?.ToastsWereEnabled); // journaled as false
    }

    [Fact]
    public async Task CrashRecovery_RestoresExtrasFromJournal()
    {
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200);
        h.Journal.Record = new JournalRecord(
            IsActive: true,
            Reason: "fix-sequence-started",
            AppliedAt: DateTimeOffset.Now.ToString("o"),
            NightLight: null,
            Dpi: null,
            Version: JournalRecord.CurrentVersion,
            Extras: new JournalExtras(ToastsWereEnabled: true, PreviousAudioEndpoint: "prev-speakers"));
        h.Dnd.SnapshotResult = false; // Beam left notifications silenced before the crash

        await h.Manager.TryRecoverAsync();

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(new[] { true }, h.Dnd.Requested);        // toasts re-enabled from the journal
        Assert.Equal(new[] { "prev-speakers" }, h.Audio.Restored);
        Assert.Equal(1, h.KeepAwake.ReleaseCalls);
        Assert.Equal(1, h.Journal.ClearCalls);
    }

    [Fact]
    public async Task CrashRecovery_UserChangedAudioEndpoint_PreservesUserChoice()
    {
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200);
        h.Journal.Record = new JournalRecord(
            true, "fix-sequence-started", DateTimeOffset.Now.ToString("o"), null,
            Extras: new JournalExtras(PreviousAudioEndpoint: "prev-speakers"),
            AppliedAudioEndpoint: "hdmi-projector",
            ModeChangeAttempted: false);
        h.Audio.DefaultEndpoint = "user-headphones";

        await h.Manager.TryRecoverAsync();

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Empty(h.Audio.Restored);
    }

    [Fact]
    public async Task Dispose_ReleasesKeepAwakeHold()
    {
        var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        Assert.Equal(1, h.KeepAwake.HoldCalls);

        h.Manager.Dispose();

        Assert.Equal(1, h.KeepAwake.ReleaseCalls);
    }

    [Fact]
    public void JournalStore_ExtrasRoundTrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), "beam-extras-journal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JournalStore(dir);
            var extras = new JournalExtras(ToastsWereEnabled: true, PreviousAudioEndpoint: "prev-speakers");

            store.BeginApply("test", null, null, extras);
            JournalRecord? j = store.Read();

            Assert.NotNull(j);
            Assert.True(j!.IsActive);
            Assert.Equal(JournalRecord.CurrentVersion, j.Version);
            Assert.NotNull(j.Extras);
            Assert.Equal(true, j.Extras!.ToastsWereEnabled);
            Assert.Equal("prev-speakers", j.Extras.PreviousAudioEndpoint);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void JournalStore_LegacyJournalWithoutExtras_ReadsWithNullExtras()
    {
        string dir = Path.Combine(Path.GetTempPath(), "beam-legacy-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // A journal written by a pre-B3 build has no Extras field at all.
            File.WriteAllText(
                Path.Combine(dir, "journal.json"),
                """{"IsActive":true,"Reason":"old","AppliedAt":"2026-01-01T00:00:00+00:00","NightLight":null,"Dpi":null,"Version":1}""");

            var store = new JournalStore(dir);
            JournalRecord? j = store.Read();

            Assert.NotNull(j);
            Assert.True(j!.IsActive);
            Assert.Null(j.Extras);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
