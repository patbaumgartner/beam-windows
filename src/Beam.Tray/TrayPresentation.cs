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

    /// <summary>
    /// Whether "Apply projector mode now" is meaningful right now. Only when idle: if a fix
    /// is already active or in progress (or Beam is paused), applying again is a no-op, so
    /// the item is disabled to avoid a dead click.
    /// </summary>
    public static bool CanApplyNow(SessionState state) => state == SessionState.Idle;

    /// <summary>
    /// Whether "Undo projector mode" is meaningful right now (UC-09 A3). Only while a fix
    /// is active; disabled while restoring (already in progress), paused, or idle —
    /// undo never appears when nothing was changed (UC-09 AC3).
    /// </summary>
    public static bool CanUndo(SessionState state) => state == SessionState.Active;
}
