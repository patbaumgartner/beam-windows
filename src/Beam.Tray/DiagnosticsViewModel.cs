using System.IO;
using Beam.App.Diagnostics;
using Beam.App.Presentation;
using Beam.App.Sessions;
using Beam.Core.Diagnostics;
using Beam.Core.Settings;

namespace Beam.Tray;

/// <summary>
/// C4: the diagnostics window's view model. Pure logic over the timeline ring buffer,
/// the session, and the settings — no WPF types — so it is unit-testable. Builds the
/// sanitized summary (A4: machine/user names are redacted) and exports the bundle zip.
/// </summary>
public sealed class DiagnosticsViewModel
{
    private readonly PresentationManager _pm;
    private readonly SessionManager _session;
    private readonly TimelineBuffer _timeline;
    private readonly string _appVersion;
    private readonly string _logDir;
    private readonly string _statusFile;
    private readonly string[] _secrets;

    /// <summary>
    /// </summary>
    /// <param name="pm">The presentation engine (settings, safe mode, re-evaluate).</param>
    /// <param name="session">The session (state for the summary).</param>
    /// <param name="timeline">The live ring buffer fed by <see cref="TimelineSink"/>.</param>
    /// <param name="appVersion">App version string for the summary header.</param>
    /// <param name="logDir">Log directory (<see cref="FileLogSink.DefaultDirectory"/>).</param>
    /// <param name="statusFile">Live status file path (status.json), if any.</param>
    /// <param name="secrets">Values redacted from exported text (machine name, user name). Collected here, in the host — Core never touches <c>Environment</c>.</param>
    public DiagnosticsViewModel(
        PresentationManager pm,
        SessionManager session,
        TimelineBuffer timeline,
        string appVersion,
        string logDir,
        string? statusFile,
        params string[] secrets)
    {
        _pm = pm ?? throw new ArgumentNullException(nameof(pm));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        _appVersion = string.IsNullOrEmpty(appVersion) ? "unknown" : appVersion;
        _logDir = logDir ?? throw new ArgumentNullException(nameof(logDir));
        _statusFile = statusFile ?? Path.Combine(logDir, "status.json");
        _secrets = secrets ?? Array.Empty<string>();
    }

    /// <summary>C4: the timeline lines to show, oldest first.</summary>
    public IReadOnlyList<string> EventLines() =>
        _timeline.Snapshot().Select(e => e.ToLine()).ToList();

    public int EventCount => _timeline.Count;
    public int EventCapacity => _timeline.Capacity;
    public SessionState SessionState => _session.State;
    public bool IsSafeMode => _pm.IsSafeMode;

    /// <summary>C4: the sanitized summary text (also the "copy summary" payload).</summary>
    public string BuildSummary() => DiagnosticsSummary.Build(
        _appVersion, _session.State, _pm.IsSafeMode, _pm.CurrentSettings, _timeline, _secrets);

    /// <summary>
    /// C4: exports <c>beam-diagnostics-&lt;ts&gt;.zip</c> into <paramref name="destinationDir"/>
    /// (summary + sanitized log files + status file) and returns the zip path.
    /// </summary>
    public string ExportBundle(string destinationDir)
    {
        var logFiles = new[]
        {
            Path.Combine(_logDir, "beam.log"),
            Path.Combine(_logDir, "beam-detailed.jsonl"),
        };

        return new DiagnosticsBundleExporter().Export(
            destinationDir, BuildSummary(), logFiles, _statusFile, _secrets);
    }

    /// <summary>C4: "re-evaluate now" — why didn't it trigger?</summary>
    public Task ReevaluateNow() => _pm.CheckCurrentStateAsync();

    /// <summary>C4: reset safe mode from the diagnostics window.</summary>
    public void ResetSafeMode() => _pm.ClearSafeMode();
}
