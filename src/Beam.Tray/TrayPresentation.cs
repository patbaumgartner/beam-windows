using Beam.App.Sessions;
using Beam.Core.Input;
using Beam.Core.Settings;
using Beam.Core.Status;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Beam.Tray.Tests")]

namespace Beam.Tray;

/// <summary>
/// B1: the state badge overlaid on the tray icon — the glanceable signal that Beam is
/// presenting, paused, or in safe mode.
/// </summary>
public enum TrayBadge
{
    None,
    Presenting,
    Paused,
    SafeMode,
}

/// <summary>B7: what the global hotkey triggers in a given session state.</summary>
public enum HotkeyAction
{
    None,
    Apply,
    Undo,
}

/// <summary>
/// Pure text mapping between session state and what the tray shows. Kept separate from
/// <see cref="TrayService"/> so it can be tested without creating WinForms controls.
/// </summary>
internal static class TrayPresentation
{
    /// <summary>
    /// WinForms <see cref="System.Windows.Forms.NotifyIcon.Text"/> throws above 63 chars,
    /// so every tooltip is capped at this length (truncated with an ellipsis).
    /// </summary>
    public const int MaxTooltipChars = 63;

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

    /// <summary>
    /// B5: the "Ignore this display" submenu is only meaningful while at least one
    /// (non-ignored) external display is connected.
    /// </summary>
    public static bool CanIgnoreDisplay(int externalPathCount) => externalPathCount > 0;

    /// <summary>B5: label for one external display inside the ignore submenu.</summary>
    public static string IgnoreItemText(string displayName, bool isIgnored) =>
        isIgnored ? $"{displayName} (ignored)" : $"Ignore {displayName}";

    /// <summary>B5: placeholder shown in the ignore submenu when no external display is present.</summary>
    public const string IgnoreMenuEmptyText = "No external display connected";

    /// <summary>
    /// B1: the icon badge for a session state. Safe mode wins over everything; Presenting
    /// and Paused are the two glanceable "something is happening" states; everything else
    /// is the plain brand mark.
    /// </summary>
    public static TrayBadge BadgeFor(SessionState state, bool safeMode) =>
        safeMode ? TrayBadge.SafeMode
        : state switch
        {
            SessionState.Active => TrayBadge.Presenting,
            SessionState.Paused => TrayBadge.Paused,
            _ => TrayBadge.None,
        };

    /// <summary>
    /// B1: the full status line for the tray tooltip, e.g.
    /// <c>Presenting — Epson EB-2250U · 1920×1080@60 · 100% · Night Light off</c>.
    /// Always capped at <see cref="MaxTooltipChars"/> (the WinForms NotifyIcon.Text
    /// limit): detail segments are dropped most-expensive-first, and the head itself is
    /// truncated with an ellipsis as a last resort.
    /// </summary>
    public static string TooltipText(
        SessionState state,
        bool safeMode,
        string? externalName,
        StatusFileRecord? status)
    {
        string head = safeMode ? "Beam — safe mode" : $"Beam — {StatusText(state)}";

        if (state != SessionState.Active || status is null)
        {
            return Fit(head);
        }

        // Most important first: the projector, then what the fix applied.
        var segments = new List<string?>();
        if (!string.IsNullOrWhiteSpace(externalName))
        {
            segments.Add(externalName);
        }

        if (status.AppliedSourceMode is { } mode)
        {
            segments.Add(mode.Replace('x', '×'));
        }

        if (status.AppliedDpiPercent is int dpi)
        {
            segments.Add($"{dpi}%");
        }

        if (status.NightLightDisabled == true)
        {
            segments.Add("Night Light off");
        }

        string text = StatusText(state);
        for (int i = 0; i < segments.Count; i++)
        {
            string separator = i == 0 ? " — " : " · ";
            string candidate = text + separator + segments[i];
            if (candidate.Length <= MaxTooltipChars)
            {
                text = candidate;
                continue;
            }

            if (i == 0)
            {
                // Even the first (most important) segment overflows — truncate it with an
                // ellipsis rather than dropping it entirely.
                int room = MaxTooltipChars - text.Length - separator.Length - 1;
                if (room > 0)
                {
                    text = text + separator + segments[i]![..room] + "…";
                }
            }

            break; // keep the most important segments; drop the rest
        }

        return Fit(text);
    }

    /// <summary>
    /// B1: balloons only for the transitions that matter — becoming Active (the fix landed).
    /// Errors and safe mode raise their own warning balloon via SafeModeChanged.
    /// </summary>
    public static bool ShouldBalloon(SessionState state) => state == SessionState.Active;

    /// <summary>
    /// B7: the hotkey toggles apply/undo — Undo while a fix is active, Apply while idle
    /// (the same rules as the menu items), None while a transition is in progress or paused.
    /// </summary>
    public static HotkeyAction ActionFor(SessionState state) =>
        state == SessionState.Active ? HotkeyAction.Undo
        : CanApplyNow(state) ? HotkeyAction.Apply
        : HotkeyAction.None;

    /// <summary>
    /// C2: the timed pause presets offered next to the indefinite "Pause Beam" item.
    /// </summary>
    public static IReadOnlyList<PausePreset> TimedPausePresets() =>
        new[] { PausePreset.FifteenMinutes, PausePreset.OneHour };

    /// <summary>C2: menu label for a timed pause item, e.g. "Pause for 15 min".</summary>
    public static string PausePresetItemText(PausePreset preset) => preset.MenuText();

    /// <summary>
    /// C2: whether a timed-pause item is meaningful right now (not while already paused).
    /// </summary>
    public static bool CanPauseFor(SessionState state) => state is SessionState.Idle or SessionState.Active;

    /// <summary>Pause/resume is safe from stable states only; Paused still allows Resume.</summary>
    public static bool CanPauseOrResume(SessionState state) =>
        state is SessionState.Idle or SessionState.Active or SessionState.Paused;

    /// <summary>Maps the persisted pause preset to the primary tray Pause action.</summary>
    public static TimeSpan PauseDuration(int? minutes) =>
        minutes is > 0 ? TimeSpan.FromMinutes(minutes.Value) : TimeSpan.Zero;

    /// <summary>
    /// C2: the "Apply projector mode now" label with the global-hotkey shortcut hint
    /// appended, e.g. <c>Apply projector mode now (Win+Alt+P)</c>.
    /// </summary>
    public static string ApplyItemText(string? hotkeyDisplay) =>
        string.IsNullOrWhiteSpace(hotkeyDisplay)
            ? "Apply projector mode now"
            : $"Apply projector mode now  ({hotkeyDisplay})";

    /// <summary>Truncates to <see cref="MaxTooltipChars"/> with a trailing ellipsis.</summary>
    private static string Fit(string text) =>
        text.Length <= MaxTooltipChars ? text : text[..(MaxTooltipChars - 1)] + "…";
}
