using System.Text;
using Beam.App.Sessions;
using Beam.Core.Diagnostics;
using Beam.Core.Settings;

namespace Beam.App.Diagnostics;

/// <summary>
/// C4/UC-14: builds the diagnostics summary shown in the diagnostics window, offered as
/// "copy summary", and included in the export bundle. A4 privacy: the caller passes the
/// secrets (machine name, user name) and every line is run through
/// <see cref="LogSanitizer"/>, so no machine or user names appear in the output.
/// </summary>
public static class DiagnosticsSummary
{
    /// <summary>
    /// Builds the sanitized summary text: version, session state, settings, and the tail
    /// of the in-memory timeline.
    /// </summary>
    public static string Build(
        string appVersion,
        SessionState sessionState,
        bool isSafeMode,
        BeamSettings settings,
        TimelineBuffer timeline,
        params string[] secrets)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeline);

        var sb = new StringBuilder();
        sb.AppendLine($"Beam {appVersion} — diagnostics summary");
        sb.AppendLine($"Generated (UTC): {DateTimeOffset.UtcNow:o}");
        sb.AppendLine($"Session state: {sessionState}{(isSafeMode ? " (SAFE MODE)" : "")}");
        sb.AppendLine(
            $"Settings: enabled={settings.IsEnabled} mode={settings.UseMode} dpi={settings.UseDpi} " +
            $"nightLight={settings.UseNightLight} keepAwake={settings.KeepAwake} dnd={settings.DoNotDisturb} " +
            $"audioToHdmi={settings.AudioToHdmi} hotkey={settings.Hotkey} " +
            $"pauseMinutes={(settings.TimedPauseMinutes?.ToString() ?? "indefinite")} " +
            $"dpiTarget={(settings.DpiTargetPercent == 0 ? "auto" : settings.DpiTargetPercent + "%")} " +
            $"preferNative={settings.PreferNativeMode} restoreOnDisconnect={settings.RestoreOnDisconnect} " +
            $"ignoredDisplays={settings.IgnoredDisplays?.Count ?? 0} knownDevices={settings.KnownDevices?.Count ?? 0}");
        sb.AppendLine();
        sb.AppendLine($"Recent events ({timeline.Count}/{timeline.Capacity}, oldest first):");
        foreach (TimelineEvent e in timeline.Snapshot())
        {
            sb.AppendLine(e.ToLine());
        }

        // Sanitize the whole block once (covers the timeline lines and any header data).
        return LogSanitizer.Sanitize(sb.ToString(), secrets);
    }
}
