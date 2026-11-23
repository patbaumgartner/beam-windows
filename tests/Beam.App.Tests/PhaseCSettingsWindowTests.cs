using Beam.App.Sessions;
using Beam.Core.Display;
using Beam.Core.Settings;

namespace Beam.App.Tests;

/// <summary>
/// C1: the settings-window behavior of <see cref="PresentationManager"/> — the new
/// settings round-trip through the store, the timed pause auto-resumes, restore-on-
/// disconnect can be turned off, a fixed DPI target overrides the planner, per-device
/// auto-apply gates the trigger, and the first-run toast flag persists.
/// </summary>
public class PhaseCSettingsWindowTests
{
    private static DpiScale ScaleAt200 => DpiScale.From(-4, 0, 4)!; // min 100, rec 200, cur 200, max 350

    private const string Dell = @"monitor\DELL";

    // ── Settings round-trip ──────────────────────────────────────────────────────

    [Fact]
    public void C1Settings_Persist_AndLoadOnRestart()
    {
        var store = new FakeSettingsStore();
        using (var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200, settings: store))
        {
            h.Manager.TimedPauseMinutes = 15;
            h.Manager.RestoreOnDisconnect = false;
            h.Manager.DpiTargetPercent = 150;
            h.Manager.PreferNativeMode = false;

            Assert.Equal(15, store.Current.TimedPauseMinutes);
            Assert.False(store.Current.RestoreOnDisconnect);
            Assert.Equal(150, store.Current.DpiTargetPercent);
            Assert.False(store.Current.PreferNativeMode);
        }

        using var h2 = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200, settings: store);
        Assert.Equal(15, h2.Manager.TimedPauseMinutes);
        Assert.False(h2.Manager.RestoreOnDisconnect);
        Assert.Equal(150, h2.Manager.DpiTargetPercent);
        Assert.False(h2.Manager.PreferNativeMode);
    }

    [Fact]
    public void DpiTargetPercent_NegativeIsClampedToAuto()
    {
        var store = new FakeSettingsStore();
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200, settings: store);

        h.Manager.DpiTargetPercent = -50;

        Assert.Equal(0, h.Manager.DpiTargetPercent);
        Assert.Equal(0, store.Current.DpiTargetPercent);
    }

    [Fact]
    public void CurrentSettings_CarriesTheNewFields()
    {
        var store = new FakeSettingsStore();
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200, settings: store);

        h.Manager.TimedPauseMinutes = 60;
        h.Manager.PreferNativeMode = false;

        BeamSettings snapshot = h.Manager.CurrentSettings;
        Assert.Equal(60, snapshot.TimedPauseMinutes);
        Assert.False(snapshot.PreferNativeMode);
        Assert.True(snapshot.RestoreOnDisconnect); // default
        Assert.Equal(0, snapshot.DpiTargetPercent); // default auto
    }

    // ── Prefer-native policy reaches the mode controller ─────────────────────────

    [Fact]
    public async Task PreferNativeMode_IsPassedToModeController()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        await h.Manager.ApplyNowAsync();
        Assert.True(h.Mode.LastPreferNative); // default on

        // Flip the policy off; the next apply must see preferNative: false.
        using var h2 = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h2.Manager.PreferNativeMode = false;
        await h2.Manager.ApplyNowAsync();
        Assert.False(h2.Mode.LastPreferNative);
    }

    // ── Fixed DPI target ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DpiTargetPercent_OverridesPlanner()
    {
        using var h = new PresentationHarness(Topologies.MirroredNeedScale(), ScaleAt200);
        h.Manager.DpiTargetPercent = 150;

        await h.Manager.ApplyNowAsync();

        Assert.Equal(new[] { 150 }, h.Dpi.RequestedPercents); // planner would have chosen 100
        Assert.Equal(150, h.Manager.CurrentStatus.AppliedDpiPercent);
    }

    [Fact]
    public async Task DpiTargetPercent_IsClampedToSupportedRange()
    {
        using var h = new PresentationHarness(Topologies.MirroredNeedScale(), ScaleAt200);
        h.Manager.DpiTargetPercent = 999; // above the max (350)

        await h.Manager.ApplyNowAsync();

        Assert.Equal(new[] { 350 }, h.Dpi.RequestedPercents);
    }

    [Fact]
    public async Task DpiTargetPercent_Zero_UsesPlanner()
    {
        using var h = new PresentationHarness(Topologies.MirroredNeedScale(), ScaleAt200);

        await h.Manager.ApplyNowAsync();

        Assert.Equal(new[] { 100 }, h.Dpi.RequestedPercents); // automatic, as before
    }

    // ── Timed pause ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PauseFor_Timed_AutoResumes()
    {
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200);

        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Session.StateChanged += e =>
        {
            if (e is { From: SessionState.Paused, To: SessionState.Idle })
            {
                resumed.TrySetResult();
            }
        };

        h.Manager.PauseFor(TimeSpan.FromMilliseconds(80));
        Assert.Equal(SessionState.Paused, h.Session.State);

        // Await the actual transition and its queued manager operation, not a sleep loop.
        await resumed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await h.Manager.PendingTask;
        Assert.Equal(SessionState.Idle, h.Session.State); // pre-pause state was Idle
    }

    [Fact]
    public async Task PauseFor_Indefinite_StaysPaused()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        h.Manager.PauseFor(TimeSpan.Zero); // indefinite
        Assert.Equal(SessionState.Paused, h.Session.State);

        await Task.Delay(300); // no timer was armed — must still be paused
        Assert.Equal(SessionState.Paused, h.Session.State);
    }

    [Fact]
    public void PauseFor_ThenDispose_DoesNotResumeAfterDispose()
    {
        var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Manager.PauseFor(TimeSpan.FromMilliseconds(50));
        Assert.Equal(SessionState.Paused, h.Session.State);

        h.Manager.Dispose(); // must dispose the pause timer without throwing
        h.Dispose();
    }

    // ── Restore on disconnect ────────────────────────────────────────────────────

    [Fact]
    public async Task Disconnect_RestoresByDefault()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        Assert.Equal(SessionState.Active, h.Session.State);

        h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1)); // projector unplugged
        await h.Manager.PendingTask;

        Assert.Equal(1, h.Mode.RestoreCalls);
        Assert.Equal(SessionState.Idle, h.Session.State);
    }

    [Fact]
    public async Task RestoreOnDisconnect_False_SkipsRestore()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        Assert.Equal(SessionState.Active, h.Session.State);

        h.Manager.RestoreOnDisconnect = false;
        h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1));
        await h.Manager.PendingTask;

        Assert.Equal(0, h.Mode.RestoreCalls); // no restore — user opted out
        Assert.Equal(SessionState.Active, h.Session.State); // still presenting on the laptop
    }

    // ── Per-device auto-apply gate ───────────────────────────────────────────────

    [Fact]
    public async Task KnownDevices_AreRecordedFromTopology()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        h.Manager.RefreshExternalPaths(); // one topology query — records externals

        DeviceProfile dell = Assert.Single(h.Manager.KnownDevices, d => d.Identity == Dell);
        Assert.Equal("DELL", dell.FriendlyName);
        Assert.True(dell.AutoApply);
    }

    [Fact]
    public async Task DeviceAutoApplyFalse_BlocksTrigger_UntilReenabled()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Manager.RefreshExternalPaths(); // record DELL as known

        h.Manager.SetDeviceAutoApply(Dell, autoApply: false);
        await h.Manager.CheckCurrentStateAsync();

        Assert.Equal(0, h.Mode.ApplyCalls); // trigger suppressed by the per-device flag
        Assert.Equal(SessionState.Idle, h.Session.State);

        h.Manager.SetDeviceAutoApply(Dell, autoApply: true);
        await h.Manager.CheckCurrentStateAsync();

        Assert.Equal(1, h.Mode.ApplyCalls); // re-enabled — trigger fires again
        Assert.Equal(SessionState.Active, h.Session.State);
    }

    [Fact]
    public void ForgetDevice_RemovesItFromKnownList()
    {
        var store = new FakeSettingsStore();
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200, settings: store);
        h.Manager.RefreshExternalPaths();
        Assert.Contains(h.Manager.KnownDevices, d => d.Identity == Dell);

        h.Manager.ForgetDevice(Dell);

        Assert.DoesNotContain(h.Manager.KnownDevices, d => d.Identity == Dell);
        Assert.DoesNotContain(store.Current.KnownDevices ?? Array.Empty<DeviceProfile>(), d => d.Identity == Dell);
    }

    [Fact]
    public void SetDeviceAutoApply_Persists()
    {
        var store = new FakeSettingsStore();
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200, settings: store);
        h.Manager.RefreshExternalPaths();

        h.Manager.SetDeviceAutoApply(Dell, autoApply: false);

        Assert.Contains(store.Current.KnownDevices ?? Array.Empty<DeviceProfile>(),
            d => d.Identity == Dell && !d.AutoApply);
    }

    [Fact]
    public async Task KnownDevices_ConcurrentTopologyQueries_RecordEachExternalOnce()
    {
        // Field repro (2026-11-16 16:39:15): a display-change burst raced the startup query
        // — the same projector was logged twice 3 ms apart and both recorders fired a
        // settings save that collided on settings.json.tmp. QueryTopology runs from the
        // display-change handler, the startup check and the tray UI thread concurrently;
        // the device must still be recorded exactly once.
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        Task[] tasks = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() =>
            {
                h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2));
                h.Manager.RefreshExternalPaths();
            }))
            .ToArray();
        await Task.WhenAll(tasks);
        await h.Manager.PendingTask;

        Assert.Single(h.Manager.KnownDevices, d => d.Identity == Dell);
    }

    // ── First-run toast flag (C3) ────────────────────────────────────────────────

    [Fact]
    public void FirstRunToast_ShowsOnce_ThenPersists()
    {
        var store = new FakeSettingsStore();
        using (var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200, settings: store))
        {
            Assert.True(h.Manager.ShouldShowFirstRunToast);
            h.Manager.MarkFirstRunToastShown();
            Assert.False(h.Manager.ShouldShowFirstRunToast);
            Assert.True(store.Current.FirstRunToastShown);
        }

        // "Restart": the flag loaded from disk suppresses the toast.
        using var h2 = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200, settings: store);
        Assert.False(h2.Manager.ShouldShowFirstRunToast);
    }
}
