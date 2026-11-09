using Beam.App.Sessions;

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
}
