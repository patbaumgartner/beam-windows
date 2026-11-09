using Beam.App.Journal;
using Beam.App.Sessions;
using Beam.Core.Display;

namespace Beam.App.Tests;

/// <summary>
/// Roadmap 1.9 (DPI restore via journal) and 1.13 (minimal settings: master switch +
/// policy flags) behaviour of <see cref="Beam.App.Presentation.PresentationManager"/>.
/// </summary>
public class Phase1SettingsAndDpiTests
{
    private static DpiScale ScaleAt200 => DpiScale.From(-4, 0, 4)!;

    // ── Roadmap 1.9: DPI capture / restore / crash recovery ────────────────────

    [Fact]
    public async Task ApplyNow_CapturesOriginalDpiInJournal()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);

        await h.Manager.ApplyNowAsync();

        // The pre-apply scale (200%) is persisted so a crash mid-fix can restore it.
        JournalDpiEntry entry = Assert.Single(h.Journal.LastDpi!);
        Assert.Equal(200, entry.Percent);
        Assert.Equal("0001594D:00000000", entry.AdapterId);
        Assert.Equal(0u, entry.SourceId);
    }

    [Fact]
    public async Task RestoreNow_PutsOriginalDpiBack()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        h.Dpi.RequestedPercents.Clear();

        await h.Manager.RestoreNowAsync();

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.RestoreCalls);
        Assert.Contains(200, h.Dpi.RequestedPercents); // original scale restored
        Assert.Equal(1, h.Journal.ClearCalls);
    }

    [Fact]
    public async Task DisconnectWhileActive_RestoresOriginalDpi()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        h.Dpi.RequestedPercents.Clear();

        h.Changes.Raise(Topologies.Snapshot(2), Topologies.Snapshot(1));
        await h.Manager.PendingTask;

        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Contains(200, h.Dpi.RequestedPercents);
    }

    [Fact]
    public async Task TryRecover_WithDpiJournal_RestoresCapturedDpi()
    {
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200);
        h.Journal.Record = new JournalRecord(
            IsActive: true,
            Reason: "fix-sequence-started",
            AppliedAt: DateTimeOffset.Now.ToString("o"),
            NightLight: null,
            Dpi: new[] { new JournalDpiEntry("0001594D:00000000", 0, 200) });

        await h.Manager.TryRecoverAsync();

        Assert.Contains(200, h.Dpi.RequestedPercents);
        Assert.Equal(1, h.Mode.RestoreCalls);
        Assert.Equal(1, h.Journal.ClearCalls);
    }

    // ── Roadmap 1.13: master switch (UC-12 rule 12 kill switch) ────────────────

    [Fact]
    public async Task SetEnabled_WhileActive_RestoresImmediately()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.ApplyNowAsync();
        Assert.Equal(SessionState.Active, h.Session.State);

        await h.Manager.SetEnabledAsync(false);

        Assert.False(h.Manager.IsEnabled);
        Assert.Equal(SessionState.Idle, h.Session.State);
        Assert.Equal(1, h.Mode.RestoreCalls);
        Assert.Equal(1, h.Journal.ClearCalls);
    }

    [Fact]
    public async Task Disabled_IgnoresDisplayChanges_AndStartupCheck()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.SetEnabledAsync(false);

        h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2));
        await h.Manager.PendingTask;
        await h.Manager.CheckCurrentStateAsync();

        Assert.Equal(0, h.Mode.ApplyCalls);
        Assert.Equal(SessionState.Idle, h.Session.State);
    }

    [Fact]
    public async Task ReEnabled_AllowsAutoApplyAgain()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        await h.Manager.SetEnabledAsync(false);
        await h.Manager.SetEnabledAsync(true);

        h.Changes.Raise(Topologies.Snapshot(1), Topologies.Snapshot(2));
        await h.Manager.PendingTask;

        Assert.True(h.Manager.IsEnabled);
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(1, h.Mode.ApplyCalls);
    }

    // ── Roadmap 1.13: policy flags gate the fix sequence ───────────────────────

    [Fact]
    public async Task UseMode_False_SkipsModeStep_ButStillActive()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Manager.UseMode = false;

        await h.Manager.ApplyNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(0, h.Mode.ApplyCalls);
    }

    [Fact]
    public async Task UseDpi_False_SkipsDpiStep()
    {
        using var h = new PresentationHarness(Topologies.MirroredNeedScale(), ScaleAt200);
        h.Manager.UseDpi = false;

        await h.Manager.ApplyNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Empty(h.Dpi.RequestedPercents);
    }

    [Fact]
    public async Task UseNightLight_False_SkipsNightLightStep()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Manager.UseNightLight = false;

        await h.Manager.ApplyNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(0, h.NightLight.SetActiveCalls);
        Assert.Equal(0, h.NightLight.SnapshotCalls);
    }

    [Fact]
    public async Task AllPoliciesOff_AppliesNothing_ButStillActive()
    {
        using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
        h.Manager.UseMode = false;
        h.Manager.UseDpi = false;
        h.Manager.UseNightLight = false;

        await h.Manager.ApplyNowAsync();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(0, h.Mode.ApplyCalls);
        Assert.Empty(h.Dpi.RequestedPercents);
        Assert.Equal(0, h.NightLight.SetActiveCalls);
    }

    [Fact]
    public void SetNightLight_ManualToggle_DelegatesToController()
    {
        using var h = new PresentationHarness(Topologies.Extended(), ScaleAt200);

        h.Manager.SetNightLight(true);

        Assert.Equal(1, h.NightLight.SetActiveCalls);
        Assert.True(h.NightLight.LastActive);
    }
}
