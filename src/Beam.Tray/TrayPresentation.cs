using Beam.App.Sessions;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Beam.Tray.Tests")]

namespace Beam.Tray;

/// <summary>
/// Pure text mapping between session state and what the tray shows. Kept separate from
/// <see cref="TrayService"/> so it can be tested without creating WinForms controls.
/// </summary>
internal static class TrayPresentation
{
    /// <summary>Menu/status wording for a session state.</summary>
    public static string StatusText(SessionState state) => state switch
    {
        SessionState.Idle => "Monitoring",
        SessionState.Detecting => "Detecting…",
        SessionState.Applying => "Applying…",
        SessionState.Active => "Presenting",
        SessionState.Restoring => "Restoring…",
        SessionState.Paused => "Paused",
        _ => state.ToString(),
    };

    /// <summary>Pause item label: the action the user can take next.</summary>
    public static string PauseItemText(SessionState state) =>
        state == SessionState.Paused ? "Resume Beam" : "Pause Beam";
}
