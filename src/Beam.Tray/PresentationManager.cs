using System.Diagnostics;
using Beam.Core.Display;
using Beam.Windows.Display;
using Beam.Windows.NightLight;
using static Beam.Windows.Display.DisplayNative;

namespace Beam.Tray;

/// <summary>
/// The main orchestration engine. Listens to <see cref="DisplayMonitor"/> changes,
/// evaluates the trigger condition, and sequences Night Light → Display Mode → DPI.
/// </summary>
public sealed class PresentationManager : IDisposable
{
    private readonly DisplayMonitor _monitor;
    private readonly NightLightController _nightLight = new();
    private readonly DisplayModeController _mode = new();
    private readonly SessionManager _session;
    private readonly Action<string> _log;
    private readonly JournalStore _journal = new();

    private IReadOnlyList<NightLightSnapshot>? _nightLightSnapshot;

    public PresentationManager(
        DisplayMonitor monitor,
        SessionManager session,
        Action<string>? log = null)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _log = log ?? (_ => { });

        _monitor.Changed += OnDisplayChanged;
    }

    /// <summary>True when the current trigger condition (external + mirrored + aspect mismatch) is met.</summary>
    public static bool IsTriggerConditionMet(DisplayTopology topology)
    {
        return topology.IsMirrored && topology.HasMirrorAspectMismatch
               && topology.ExternalPaths.Count > 0;
    }

    // ── Display-change handler ────────────────────────────────────────────────

    private void OnDisplayChanged(ConnectionSnapshot? previous, ConnectionSnapshot current)
    {
        var sw = Stopwatch.StartNew();
        SessionState state = _session.State;
        int prevCount = previous?.Displays.Count ?? 0;
        int currCount = current.Displays.Count;

        AppLog.Info("Display change detected", "Monitor",
            ("sessionState", state.ToString()),
            ("previousDisplays", prevCount),
            ("currentDisplays", currCount),
            ("disconnect", (prevCount > currCount).ToString()));

        if (state == SessionState.Paused)
        {
            AppLog.Info("Session is paused — ignoring change.", "Monitor");
            return;
        }

        bool displayDisconnected = previous is not null && currCount < prevCount;

        if (state == SessionState.Active && displayDisconnected)
        {
            AppLog.Info("External display disconnected while presenting — requesting restore.", "Monitor",
                ("previousDisplays", prevCount),
                ("currentDisplays", currCount));
            RequestRestore();
            sw.Stop();
            return;
        }

        // No disconnect while active: re-query topology for the trigger condition.
        DisplayInventory inventory = new();
        DisplayTopology topology = inventory.Query();
        bool triggerMet = IsTriggerConditionMet(topology);

        AppLog.Info("Trigger evaluation", "Monitor",
            ("triggerMet", triggerMet.ToString()),
            ("mirrored", topology.IsMirrored.ToString()),
            ("hasMismatch", topology.HasMirrorAspectMismatch.ToString()),
            ("externalCount", topology.ExternalPaths.Count),
            ("totalPaths", topology.ActivePaths.Count),
            ("topologyId", topology.CurrentTopologyId),
            ("topologyJson", topology.ToString()),
            ("evaluationMs", sw.ElapsedMilliseconds.ToString()));

        if (triggerMet && state is SessionState.Idle)
        {
            AppLog.Info("Trigger met and session idle — starting fix sequence.", "Monitor",
                ("evaluationMs", sw.ElapsedMilliseconds.ToString()));
            BeginFixSequence();
        }
    }

    // ── Fix sequence ─────────────────────────────────────────────────────────

    private void BeginFixSequence()
    {
        if (!_session.TryBeginFix())
        {
            AppLog.Warn("Session refused BeginFix (not in Idle/Detecting state).", "Fix");
            return;
        }

        Task.Run(() => RunFixSequence());
    }

    private void RunFixSequence()
    {
        var overall = Stopwatch.StartNew();
        try
        {
            // ── Step 1: Night Light OFF ──────────────────────────────────────────
            AppLog.Info("Fix Step 1: Night Light OFF", "Fix",
                ("step", "nightLight"));
            _nightLightSnapshot = _nightLight.SnapshotAll();

            AppLog.Info("Night Light snapshots captured", "Fix",
                ("count", _nightLightSnapshot.Count),
                ("snapshots", string.Join(",", _nightLightSnapshot.Select(s => s.KeyPath))));

            // Persist intent now, before any mutation: if Beam is killed later in the
            // sequence, the next launch sees an active journal and can recover.
            _journal.BeginApply("fix-sequence-started", _nightLightSnapshot);

            NightLightWriteResult nlResult = _nightLight.SetActive(active: false);
            AppLog.Info("Night Light set-off result", "Fix",
                ("step", "nightLight"),
                ("success", nlResult.Success.ToString()),
                ("message", nlResult.Message),
                ("details", string.Join(" | ", nlResult.Details)));

            // ── Step 2: Display mode: clone + 16:9 shared source ─────────────────
            AppLog.Info("Fix Step 2: Display mode — clone + 16:9", "Fix",
                ("step", "displayMode"));
            var topologyBefore = new DisplayInventory().Query();
            AppLog.Info("Topology before mode change", "Fix",
                ("step", "displayMode"),
                ("topologyId", topologyBefore.CurrentTopologyId),
                ("isMirrored", topologyBefore.IsMirrored.ToString()),
                ("activePaths", topologyBefore.ActivePaths.Count),
                ("mirroredSources", topologyBefore.ActiveCloneGroups.Count),
                ("topologyDetail", topologyBefore.ToString()));

            string? modeError = _mode.ApplyCloneWith16x9();

            var topologyAfter = new DisplayInventory().Query();
            AppLog.Info("Topology after mode change", "Fix",
                ("step", "displayMode"),
                ("topologyId", topologyAfter.CurrentTopologyId),
                ("isMirrored", topologyAfter.IsMirrored.ToString()),
                ("activePaths", topologyAfter.ActivePaths.Count),
                ("topologyDetail", topologyAfter.ToString()));

            if (modeError is not null)
            {
                AppLog.Warn("Display mode error (best-effort; proceeding to DPI)", "Fix",
                    ("step", "displayMode"),
                    ("error", modeError));
                _log($"[Fix] Display mode error: {modeError}");
            }
            else
            {
                AppLog.Info("Display mode applied successfully", "Fix",
                    ("step", "displayMode"),
                    ("mirrored", topologyAfter.IsMirrored.ToString()),
                    ("mismatchAfter", topologyAfter.HasMirrorAspectMismatch.ToString()));
            }

            // ── Step 3: DPI ─────────────────────────────────────────────────────
            ApplyDpi();

            // ── Done ────────────────────────────────────────────────────────────
            _session.MarkActive();
            _journal.MarkIdle("fix-applied");
            overall.Stop();
            AppLog.Info("Fix sequence complete — session is now Active", "Fix",
                ("totalMs", overall.ElapsedMilliseconds.ToString()));
        }
        catch (Exception ex)
        {
            AppLog.Exception(ex, "Fix", ("step", "unknown"));
            _log($"[Fix] Unhandled exception in fix sequence: {ex}");
            RequestRestore();
        }
    }

    private void ApplyDpi()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            DisplayInventory inventory = new();
            DisplayTopology topology = inventory.Query();

            DisplayPath? sharedSource = topology.ActivePaths.FirstOrDefault();
            if (sharedSource is null)
            {
                AppLog.Warn("DPI: no active source found.", "Fix",
                    ("step", "dpi"));
                return;
            }

            LUID adapterId = BeLuid(sharedSource.AdapterId);
            AppLog.Info("DPI step", "Fix",
                ("step", "dpi"),
                ("adapterId", sharedSource.AdapterId),
                ("sourceId", sharedSource.SourceId.ToString()),
                ("sourceMode", sharedSource.SourceMode?.Width.ToString() ?? "unknown"),
                ("sourceGdiName", sharedSource.SourceGdiName));

            DpiScale? scale = DpiController.Read(adapterId, sharedSource.SourceId);
            if (scale?.Value is null)
            {
                AppLog.Warn("DPI: could not read current scale from DisplayConfig API.", "Fix",
                    ("step", "dpi"),
                    ("adapterId", sharedSource.AdapterId),
                    ("sourceId", sharedSource.SourceId.ToString()),
                    ("scaleSupported", scale?.IsSupported.ToString() ?? "null"),
                    ("scaleValue", scale?.Value?.ToString() ?? "null"));
                return;
            }

            DisplayMode? sourceMode = sharedSource.SourceMode;
            uint sourceWidth = sourceMode?.Width ?? 1920;

            int targetPercent = ScalePlanner.ChooseScale(sourceWidth, scale.Value);
            AppLog.Info("DPI scale analysis", "Fix",
                ("step", "dpi"),
                ("sourceWidthPx", sourceWidth.ToString()),
                ("currentScale", scale.Value.Current.ToString()),
                ("recommendedScale", scale.Value.Recommended.ToString()),
                ("minimumScale", scale.Value.Minimum.ToString()),
                ("maximumScale", scale.Value.Maximum.ToString()),
                ("targetScale", targetPercent.ToString()),
                ("logicalWidthAtTarget", ScalePlanner.LogicalWidth(sourceWidth, targetPercent).ToString()));

            if (targetPercent == scale.Value.Current)
            {
                AppLog.Info("DPI: already at optimal; no change needed.", "Fix",
                    ("step", "dpi"),
                    ("noChangeNeeded", true.ToString()),
                    ("currentPercent", scale.Value.Current.ToString()));
                sw.Stop();
                return;
            }

            bool applied = DpiController.SetPercentageAndVerify(
                adapterId, sharedSource.SourceId, targetPercent, scale.Value);

            sw.Stop();
            AppLog.Info("DPI scale applied", "Fix",
                ("step", "dpi"),
                ("targetPercent", targetPercent.ToString()),
                ("verified", applied.ToString()),
                ("elapsedMs", sw.ElapsedMilliseconds.ToString()));
            _log($"[Fix] DPI: set to {targetPercent}% → {(applied ? "verified" : "NOT verified — check manually")}.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            AppLog.Exception(ex, "Fix", ("step", "dpi"));
            _log($"[Fix] DPI: exception: {ex.Message}");
        }
    }

    // ── Restore sequence ─────────────────────────────────────────────────────

    private void RequestRestore()
    {
        _session.RequestRestore();
        Task.Run(RunRestoreSequence);
    }

    private void RunRestoreSequence()
    {
        var overall = Stopwatch.StartNew();
        try
        {
            AppLog.Info("Restore sequence starting", "Restore");

            // ── Step 1: Display mode restore ────────────────────────────────────────
            AppLog.Info("Restore Step 1: Display mode", "Restore",
                ("step", "displayMode"));

            var topologyBefore = new DisplayInventory().Query();
            AppLog.Info("Topology before restore", "Restore",
                ("step", "displayMode"),
                ("topologyId", topologyBefore.CurrentTopologyId),
                ("isMirrored", topologyBefore.IsMirrored.ToString()),
                ("activePaths", topologyBefore.ActivePaths.Count),
                ("topologyDetail", topologyBefore.ToString()));

            string? modeError = _mode.Restore();

            var topologyAfter = new DisplayInventory().Query();
            AppLog.Info("Topology after restore", "Restore",
                ("step", "displayMode"),
                ("topologyId", topologyAfter.CurrentTopologyId),
                ("isMirrored", topologyAfter.IsMirrored.ToString()),
                ("activePaths", topologyAfter.ActivePaths.Count),
                ("topologyDetail", topologyAfter.ToString()),
                ("error", modeError ?? "none"));

            // ── Step 2: Night Light ON ───────────────────────────────────────────────
            AppLog.Info("Restore Step 2: Night Light", "Restore",
                ("step", "nightLight"));

            if (_nightLightSnapshot is not null && _nightLightSnapshot.Count > 0)
            {
                NightLightWriteResult nlResult = _nightLight.Restore(_nightLightSnapshot);
                AppLog.Info("Night Light restore result", "Restore",
                    ("step", "nightLight"),
                    ("success", nlResult.Success.ToString()),
                    ("message", nlResult.Message),
                    ("details", string.Join(" | ", nlResult.Details)));
                _nightLightSnapshot = null;
            }
            else
            {
                AppLog.Info("Restore: no Night Light snapshot to restore (none was captured).", "Restore",
                    ("step", "nightLight"),
                    ("hadSnapshot", (_nightLightSnapshot is not null).ToString()));
            }

            _session.MarkIdle();
            _journal.Clear();
            overall.Stop();
            AppLog.Info("Restore sequence complete", "Restore",
                ("totalMs", overall.ElapsedMilliseconds.ToString()));
        }
        catch (Exception ex)
        {
            overall.Stop();
            AppLog.Exception(ex, "Restore");
            _log($"[Restore] Unhandled exception: {ex}");
            _session.MarkIdle();
        }
    }

    /// <summary>Parses an adapter id string (from <c>DisplayPath.AdapterId</c>) to an <c>LUID</c>.</summary>
    private static LUID BeLuid(string s) =>
        LUID.TryParse(s, out LUID luid) ? luid : new LUID();

    /// <summary>
    /// Evaluates the current display state and applies the fix if the trigger condition
    /// is already met (startup case: beam launched while the external is already mirrored
    /// with bars). Safe to call repeatedly — it is a no-op when not in Idle state.
    /// </summary>
    public void CheckCurrentState()
    {
        if (_session.State != SessionState.Idle)
        {
            return;
        }

        DisplayInventory inventory = new();
        DisplayTopology topology = inventory.Query();
        bool triggerMet = IsTriggerConditionMet(topology);

        AppLog.Info("Startup state check", "Startup",
            ("triggerMet", triggerMet.ToString()),
            ("mirrored", topology.IsMirrored.ToString()),
            ("hasMismatch", topology.HasMirrorAspectMismatch.ToString()),
            ("externalCount", topology.ExternalPaths.Count),
            ("topologyDetail", topology.ToString()),
            ("topologyId", topology.CurrentTopologyId));

        if (triggerMet)
        {
            AppLog.Info("Startup trigger already met — applying fix.", "Startup");
            _log("[Startup] trigger already met — applying fix.");
            BeginFixSequence();
        }
        else
        {
            _log($"[Startup] state is safe: mirrored={topology.IsMirrored} mismatch={topology.HasMirrorAspectMismatch}");
        }
    }

    /// <summary>
    /// Recovers from a previous crash: if a fix was left half-applied (active journal),
    /// restore the original configuration (extended topology + Night Light ON). Called
    /// once at startup, before the change-monitor starts.
    /// </summary>
    public void TryRecover()
    {
        JournalStore.Journal? journal = _journal.Read();
        if (journal is null || !journal.IsActive)
        {
            AppLog.Info("No recovery needed — journal empty or idle.", "Recovery");
            return;
        }

        AppLog.Warn($"Recovering from previous interrupted session (appliedAt={journal.AppliedAt}, reason={journal.Reason}).",
            "Recovery");

        // Rehydrate the Night Light snapshot so restore can turn it back on.
        _nightLightSnapshot = JournalStore.RehydrateNightLight(journal);

        // Restore is intentionally run on a background thread; it touches display APIs
        // that may take seconds and must not block the UI thread.
        RequestRestore();
    }

    /// <summary>User-requested apply. Runs the fix sequence if idle.</summary>
    public void ApplyNow()
    {
        if (_session.State == SessionState.Paused)
        {
            _session.Resume();
            AppLog.Info("ApplyNow: session was paused — resuming.", "Manual");
        }

        if (_session.State == SessionState.Idle)
        {
            AppLog.Info("ApplyNow: running fix sequence on user request.", "Manual");
            _log("[Manual] apply requested — running fix.");
            BeginFixSequence();
        }
        else
        {
            AppLog.Info("ApplyNow: ignored — session not idle.", "Manual",
                ("state", _session.State.ToString()));
            _log($"[Manual] apply ignored (state={_session.State}).");
        }
    }

    /// <summary>User-requested restore. Runs the restore sequence if active.</summary>
    public void RestoreNow()
    {
        if (_session.State == SessionState.Paused)
        {
            AppLog.Info("RestoreNow: ignored — session is paused.", "Manual",
                ("state", SessionState.Paused.ToString()));
            return; // do not touch display while paused
        }

        if (_session.State is SessionState.Active or SessionState.Applying)
        {
            AppLog.Info("RestoreNow: running restore on user request.", "Manual",
                ("state", _session.State.ToString()));
            _log("[Manual] restore requested — running.");
            RequestRestore();
        }
        else
        {
            AppLog.Info("RestoreNow: ignored — session not active.", "Manual",
                ("state", _session.State.ToString()));
            _log($"[Manual] restore ignored (state={_session.State}).");
        }
    }

    public void Dispose()
    {
        _monitor.Changed -= OnDisplayChanged;
    }
}
