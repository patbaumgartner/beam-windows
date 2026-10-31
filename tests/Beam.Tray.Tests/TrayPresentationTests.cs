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
}
