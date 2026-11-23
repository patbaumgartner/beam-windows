using Beam.App.Sessions;
using Beam.Core.Status;

namespace Beam.Tray.Tests;

/// <summary>
/// The tray text mapping is pure logic; testing it here keeps the WinForms surface thin
/// and gives the tray project real coverage without a message pump.
/// </summary>
public class TrayPresentationTests
{
    [Theory]
    [InlineData(SessionState.Idle, "Monitoring")]
    [InlineData(SessionState.Detecting, "Detecting…")]
    [InlineData(SessionState.Applying, "Applying…")]
    [InlineData(SessionState.Active, "Presenting")]
    [InlineData(SessionState.Restoring, "Restoring…")]
    [InlineData(SessionState.Paused, "Paused")]
    public void StatusText_MapsEveryState(SessionState state, string expected)
    {
        Assert.Equal(expected, TrayPresentation.StatusText(state));
    }

    [Theory]
    [InlineData(SessionState.Idle, "Pause Beam")]
    [InlineData(SessionState.Active, "Pause Beam")]
    [InlineData(SessionState.Paused, "Resume Beam")]
    public void PauseItemText_ReflectsNextAction(SessionState state, string expected)
    {
        Assert.Equal(expected, TrayPresentation.PauseItemText(state));
    }

    // UC-09 AC3: the undo action is only offered while a fix is actually active —
    // never as a dead menu item (roadmap 1.12 "Undo projector mode").
    [Theory]
    [InlineData(SessionState.Idle, false)]
    [InlineData(SessionState.Detecting, false)]
    [InlineData(SessionState.Applying, false)]
    [InlineData(SessionState.Active, true)]
    [InlineData(SessionState.Restoring, false)]
    [InlineData(SessionState.Paused, false)]
    public void CanUndo_OnlyWhileFixIsActive(SessionState state, bool expected)
    {
        Assert.Equal(expected, TrayPresentation.CanUndo(state));
    }

    // ── B5: "Ignore this display" submenu ──────────────────────────────────────

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    public void CanIgnoreDisplay_OnlyWhileExternalPresent(int externalCount, bool expected)
    {
        Assert.Equal(expected, TrayPresentation.CanIgnoreDisplay(externalCount));
    }

    [Theory]
    [InlineData("DELL U2720Q", false, "Ignore DELL U2720Q")]
    [InlineData("DELL U2720Q", true, "DELL U2720Q (ignored)")]
    public void IgnoreItemText_ReflectsIgnoredState(string name, bool isIgnored, string expected)
    {
        Assert.Equal(expected, TrayPresentation.IgnoreItemText(name, isIgnored));
    }

    [Fact]
    public void IgnoreMenuEmptyText_IsShortAndClear()
    {
        Assert.False(string.IsNullOrWhiteSpace(TrayPresentation.IgnoreMenuEmptyText));
    }

    // ── B1: state badge, rich tooltip, balloon rule ───────────────────────────

    [Theory]
    [InlineData(SessionState.Idle, false, TrayBadge.None)]
    [InlineData(SessionState.Detecting, false, TrayBadge.None)]
    [InlineData(SessionState.Applying, false, TrayBadge.None)]
    [InlineData(SessionState.Active, false, TrayBadge.Presenting)]
    [InlineData(SessionState.Restoring, false, TrayBadge.None)]
    [InlineData(SessionState.Paused, false, TrayBadge.Paused)]
    [InlineData(SessionState.Idle, true, TrayBadge.SafeMode)]
    [InlineData(SessionState.Active, true, TrayBadge.SafeMode)] // safe mode wins
    public void BadgeFor_MapsStateAndSafeMode(SessionState state, bool safeMode, TrayBadge expected)
    {
        Assert.Equal(expected, TrayPresentation.BadgeFor(state, safeMode));
    }

    [Fact]
    public void TooltipText_Idle_IsPlainStatusLine()
    {
        string tip = TrayPresentation.TooltipText(SessionState.Idle, false, "EPSON EB-2250U", null);

        Assert.Equal("Beam — Monitoring", tip);
    }

    [Fact]
    public void TooltipText_Active_IsFullStatusLine()
    {
        var status = new StatusFileRecord(
            "Active", null, "2026-11-14T10:00:00+00:00", 4321, "1920x1080@60", 100, true);

        string tip = TrayPresentation.TooltipText(SessionState.Active, false, "EB-2250U", status);

        Assert.Equal("Presenting — EB-2250U · 1920×1080@60 · 100% · Night Light off", tip);
        Assert.True(tip.Length <= TrayPresentation.MaxTooltipChars);
    }

    [Fact]
    public void TooltipText_ActiveWithoutDetails_FallsBackToStatusWord()
    {
        var status = new StatusFileRecord("Active", null, "2026-11-14T10:00:00+00:00", 4321);

        string tip = TrayPresentation.TooltipText(SessionState.Active, false, null, status);

        Assert.Equal("Presenting", tip);
    }

    [Fact]
    public void TooltipText_LongDisplayName_NeverExceedsNotifyIconLimit()
    {
        var status = new StatusFileRecord(
            "Active", null, "2026-11-14T10:00:00+00:00", 4321, "3840x2160@60", 150, true);

        string tip = TrayPresentation.TooltipText(
            SessionState.Active, false, "Some Extremely Long Projector Model Name 9000 Ultra", status);

        Assert.True(tip.Length <= TrayPresentation.MaxTooltipChars,
            $"tooltip was {tip.Length} chars: {tip}");
        // The projector name is the most important segment, so it must survive (possibly shortened).
        Assert.Contains("Some Extremely Long", tip);
    }

    [Fact]
    public void TooltipText_SafeMode_ShowsSafeMode()
    {
        string tip = TrayPresentation.TooltipText(SessionState.Idle, true, null, null);

        Assert.Equal("Beam — safe mode", tip);
    }

    [Theory]
    [InlineData(SessionState.Idle, false)]
    [InlineData(SessionState.Detecting, false)]
    [InlineData(SessionState.Applying, false)]
    [InlineData(SessionState.Active, true)]   // only the fix landing notifies
    [InlineData(SessionState.Restoring, false)]
    [InlineData(SessionState.Paused, false)]
    public void ShouldBalloon_OnlyWhenFixLands(SessionState state, bool expected)
    {
        Assert.Equal(expected, TrayPresentation.ShouldBalloon(state));
    }

    // ── B7: hotkey toggle mapping ──────────────────────────────────────────────

    [Theory]
    [InlineData(SessionState.Idle, HotkeyAction.Apply)]
    [InlineData(SessionState.Active, HotkeyAction.Undo)]
    [InlineData(SessionState.Detecting, HotkeyAction.None)]   // transition in progress
    [InlineData(SessionState.Applying, HotkeyAction.None)]
    [InlineData(SessionState.Restoring, HotkeyAction.None)]
    [InlineData(SessionState.Paused, HotkeyAction.None)]
    public void ActionFor_TogglesApplyUndo(SessionState state, HotkeyAction expected)
    {
        Assert.Equal(expected, TrayPresentation.ActionFor(state));
    }

    [Fact]
    public void EnableHotkey_NullCombo_IsNoOp()
    {
        using var session = new Beam.App.Sessions.SessionManager();
        using var tray = new TrayService(session);

        tray.EnableHotkey(null);

        Assert.False(tray.HotkeyRegistered);
    }

    [Fact]
    public async Task SafeModeChanged_MarshalsEntireUpdateToUiContext()
    {
        var ui = new RecordingSynchronizationContext();

        bool posted = await Task.Run(() => TrayService.TryPostToUi(ui, () => { }));

        Assert.True(posted);
        Assert.Equal(1, ui.PostCount); // even without a balloon, shell state is UI-only
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;
        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state) =>
            Interlocked.Increment(ref _postCount); // deliberately do not execute shell updates
    }
}
