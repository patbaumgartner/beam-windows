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
        SessionState state = _session.State;
        _log($"[Monitor] change detected; session={state} ({previous?.Displays.Count ?? 0} -> {current.Displays.Count} displays)");

        if (state == SessionState.Paused)
        {
            _log("[Monitor] paused — ignoring change.");
            return;
        }

        bool displayDisconnected = previous is not null
            && current.Displays.Count < previous.Displays.Count;

        if (state == SessionState.Active && displayDisconnected)
        {
            _log("[Monitor] a display disconnected while presenting — restoring.");
            RequestRestore();
            return;
        }

        // No disconnect while active: re-query topology for the trigger condition.
        DisplayInventory inventory = new();
        DisplayTopology topology = inventory.Query();
        bool triggerMet = IsTriggerConditionMet(topology);

        if (triggerMet && state is SessionState.Idle)
        {
            BeginFixSequence();
        }
    }

    // ── Fix sequence ─────────────────────────────────────────────────────────

    private void BeginFixSequence()
    {
        if (!_session.TryBeginFix())
        {
            _log("[Fix] session refused BeginFix (not in Idle/Detecting).");
            return;
        }

        Task.Run(() => RunFixSequence());
    }

    private void RunFixSequence()
    {
        try
        {
            // Step 1 — Night Light OFF
            _log("[Fix] Step 1: Night Light OFF");
            _nightLightSnapshot = _nightLight.SnapshotAll();
            NightLightWriteResult nlResult = _nightLight.SetActive(active: false);
            _log($"[Fix] Night Light: {nlResult.Message}");
            if (!nlResult.Success)
            {
                _log($"[Fix] Night Light write had issues: {string.Join("; ", nlResult.Details)}");
            }

            // Step 2 — Display mode: enter clone, set shared source to 16:9
            _log("[Fix] Step 2: Display mode (clone + 16:9)");
            string? modeError = _mode.ApplyCloneWith16x9();
            if (modeError is not null)
            {
                _log($"[Fix] Display mode error: {modeError}");
                // Best-effort; proceed to DPI even if mode failed.
            }
            else
            {
                _log("[Fix] Display mode: OK");
            }

            // Step 3 — DPI: pick and apply readable scale for the shared source
            ApplyDpi();

            // Done — arm restore for when the external display disconnects.
            _session.MarkActive();
        }
        catch (Exception ex)
        {
            _log($"[Fix] Unhandled exception in fix sequence: {ex}");
            RequestRestore();
        }
    }

    private void ApplyDpi()
    {
        try
        {
            DisplayInventory inventory = new();
            DisplayTopology topology = inventory.Query();

            // The shared source is the first active path's source.
            DisplayPath? sharedSource = topology.ActivePaths.FirstOrDefault();
            if (sharedSource is null)
            {
                _log("[Fix] DPI: no active source found.");
                return;
            }

            LUID adapterId = BeLuid(sharedSource.AdapterId);

            DpiScale? scale = DpiController.Read(adapterId, sharedSource.SourceId);
            if (scale?.Value is null)
            {
                _log("[Fix] DPI: could not read current scale.");
                return;
            }

            DisplayMode? sourceMode = sharedSource.SourceMode;
            uint sourceWidth = sourceMode?.Width ?? 1920;

            int targetPercent = ScalePlanner.ChooseScale(sourceWidth, scale.Value);
            _log($"[Fix] DPI: source={sourceWidth}px, current={scale.Value.Current}%, "
                 + $"recommended={scale.Value.Current}%, target={targetPercent}%");

            if (targetPercent == scale.Value.Current)
            {
                _log("[Fix] DPI: already at optimal; no change needed.");
                return;
            }

            bool applied = DpiController.SetPercentageAndVerify(adapterId, sharedSource.SourceId, targetPercent, scale.Value);
            _log($"[Fix] DPI: set to {targetPercent}% → {(applied ? "verified" : "NOT verified — check manually")}.");
        }
        catch (Exception ex)
        {
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
        try
        {
            // Step 1 — DPI: restore is best-effort; DPI was set per-source, no explicit
            // snapshot needed because the mode-change path resets it. Log anyway.
            _log("[Restore] DPI: restoring via mode change (no separate snapshot).");

            // Step 2 — Display mode: restore topology and source resolutions.
            _log("[Restore] Step 2: Display mode restore");
            string? modeError = _mode.Restore();
            if (modeError is not null)
            {
                _log($"[Restore] Display mode: {modeError}");
            }
            else
            {
                _log("[Restore] Display mode: restored");
            }

            // Step 3 — Night Light ON
            _log("[Restore] Step 3: Night Light restore");
            if (_nightLightSnapshot is not null && _nightLightSnapshot.Count > 0)
            {
                NightLightWriteResult nlResult = _nightLight.Restore(_nightLightSnapshot);
                _log($"[Restore] Night Light: {nlResult.Message}");
                _nightLightSnapshot = null;
            }
            else
            {
                _log("[Restore] Night Light: no snapshot to restore.");
            }

            _session.MarkIdle();
        }
        catch (Exception ex)
        {
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
        if (IsTriggerConditionMet(topology))
        {
            _log("[Startup] trigger already met — applying fix.");
            BeginFixSequence();
        }
        else
        {
            _log($"[Startup] state is safe: mirrored={topology.IsMirrored} mismatch={topology.HasMirrorAspectMismatch}");
        }
    }

    /// <summary>User-requested apply. Runs the fix sequence if idle.</summary>
    public void ApplyNow()
    {
        if (_session.State == SessionState.Paused)
        {
            _session.Resume();
        }

        if (_session.State == SessionState.Idle)
        {
            _log("[Manual] apply requested — running fix.");
            BeginFixSequence();
        }
        else
        {
            _log($"[Manual] apply ignored (state={_session.State}).");
        }
    }

    /// <summary>User-requested restore. Runs the restore sequence if active.</summary>
    public void RestoreNow()
    {
        if (_session.State == SessionState.Paused)
        {
            return; // do not touch display while paused
        }

        if (_session.State is SessionState.Active or SessionState.Applying)
        {
            _log("[Manual] restore requested — running.");
            RequestRestore();
        }
        else
        {
            _log($"[Manual] restore ignored (state={_session.State}).");
        }
    }

    public void Dispose()
    {
        _monitor.Changed -= OnDisplayChanged;
    }
}
