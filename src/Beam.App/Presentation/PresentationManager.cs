using System.Diagnostics;
using Beam.App.Journal;
using Beam.App.Sessions;
using Beam.Core.Diagnostics;
using Beam.Core.Display;
using Beam.Core.NightLight;
using Beam.Core.Ports;

namespace Beam.App.Presentation;

/// <summary>
/// The presentation engine. Listens for display changes, evaluates the trigger condition,
/// and sequences Night Light → display mode → DPI (and the reverse on restore).
/// </summary>
/// <remarks>
/// <para>
/// Every platform touch goes through a Core port (<see cref="IDisplayChangeSource"/>,
/// <see cref="ITopologySource"/>, <see cref="IDisplayModeController"/>,
/// <see cref="IDpiController"/>, <see cref="INightLightController"/>). This project targets
/// plain <c>net10.0</c> and contains no P/Invoke, no WinForms, and no Windows-only types,
/// so the whole orchestration is unit-testable with fakes.
/// </para>
/// <para>
/// Sequences are serialised through a semaphore: a disconnect that arrives while a fix is
/// running waits for it, then restores. Work is started on the thread pool, so callers on
/// the UI thread never block on display APIs.
/// </para>
/// </remarks>
public sealed class PresentationManager : IDisposable
{
    /// <summary>UC-12 rule 11: after this many consecutive failed fix sequences, enter safe mode.</summary>
    public const int MaxConsecutiveFailures = 2;

    /// <summary>UC-12 rule 8: ignore display-change events this long after our own mutation.</summary>
    public static readonly TimeSpan DefaultSelfChangeWindow = TimeSpan.FromSeconds(3);

    private readonly IDisplayChangeSource _changes;
    private readonly ITopologySource _topology;
    private readonly IDisplayModeController _mode;
    private readonly IDpiController _dpi;
    private readonly INightLightController _nightLight;
    private readonly IJournalStore _journal;
    private readonly SessionManager _session;
    private readonly TimeSpan _selfChangeWindow;
    private readonly Func<DateTimeOffset> _clock;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<NightLightSnapshot>? _nightLightSnapshot;
    private Task _pending = Task.CompletedTask;
    private DateTimeOffset _lastOwnMutationUtc = DateTimeOffset.MinValue;
    private int _consecutiveFailures;
    private bool _disposed;

    /// <summary>Raised when safe mode is entered or cleared (UC-12 AC5: must be visible).</summary>
    public event Action<bool>? SafeModeChanged;

    public PresentationManager(
        IDisplayChangeSource changes,
        ITopologySource topology,
        IDisplayModeController mode,
        IDpiController dpi,
        INightLightController nightLight,
        IJournalStore journal,
        SessionManager session,
        TimeSpan? selfChangeWindow = null,
        Func<DateTimeOffset>? clock = null)
    {
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _mode = mode ?? throw new ArgumentNullException(nameof(mode));
        _dpi = dpi ?? throw new ArgumentNullException(nameof(dpi));
        _nightLight = nightLight ?? throw new ArgumentNullException(nameof(nightLight));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _selfChangeWindow = selfChangeWindow ?? DefaultSelfChangeWindow;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        _changes.Changed += OnDisplayChanged;
    }

    /// <summary>UC-12 rule 11: true after <see cref="MaxConsecutiveFailures"/> consecutive failed fixes.</summary>
    public bool IsSafeMode => _consecutiveFailures >= MaxConsecutiveFailures;

    /// <summary>Diagnostics/manual-retry escape hatch: clears safe mode and the failure count.</summary>
    public void ClearSafeMode() => NoteSuccess("manual clear");

    /// <summary>The most recent sequence task. Tests await this for determinism.</summary>
    public Task PendingTask => _pending;

    /// <summary>
    /// True when the topology is a mirrored external display with an aspect mismatch.
    /// Kept as a static helper so callers (and older tests) have one obvious entry point.
    /// </summary>
    public static bool IsTriggerConditionMet(DisplayTopology topology) =>
        TriggerCondition.IsMet(topology);

    // ── Public operations ───────────────────────────────────────────────────────

    /// <summary>User-requested apply. Resumes a paused session, then runs the fix if idle.</summary>
    public Task ApplyNowAsync() => Track(RunExclusiveAsync(ApplyCoreAsync));

    /// <summary>User-requested restore. Ignored while paused or when nothing is applied.</summary>
    public Task RestoreNowAsync() => Track(RunExclusiveAsync(RestoreNowCoreAsync));

    /// <summary>
    /// Applies the fix when the trigger is already true (the app launched while a
    /// projector was already mirrored with bars). Safe to call repeatedly.
    /// </summary>
    public Task CheckCurrentStateAsync() => Track(RunExclusiveAsync(CheckCurrentStateCoreAsync));

    /// <summary>
    /// Recovers from a previous crash: if the journal says a fix was left applied, restore
    /// the original configuration before monitoring starts.
    /// </summary>
    public Task TryRecoverAsync() => Track(RunExclusiveAsync(TryRecoverCoreAsync));

    // ── Display-change handling ─────────────────────────────────────────────────

    private void OnDisplayChanged(ConnectionSnapshot? previous, ConnectionSnapshot current)
    {
        if (_disposed)
        {
            return;
        }

        Track(HandleDisplayChangedAsync(previous, current));
    }

    private async Task HandleDisplayChangedAsync(ConnectionSnapshot? previous, ConnectionSnapshot current)
    {
        var sw = Stopwatch.StartNew();
        SessionState state = _session.State;
        int prevCount = previous?.Displays.Count ?? 0;
        int currCount = current.Displays.Count;

        Log.Info("Display change detected", "Monitor",
            ("sessionState", state.ToString()),
            ("previousDisplays", prevCount),
            ("currentDisplays", currCount),
            ("disconnect", (prevCount > currCount).ToString()));

        if (state == SessionState.Paused)
        {
            Log.Info("Session is paused — ignoring change.", "Monitor");
            return;
        }

        bool displayDisconnected = previous is not null && currCount < prevCount;

        if (state == SessionState.Active && displayDisconnected)
        {
            Log.Info("External display disconnected while presenting — requesting restore.", "Monitor",
                ("previousDisplays", prevCount),
                ("currentDisplays", currCount));
            await RunExclusiveAsync(RestoreSequenceAsync).ConfigureAwait(false);
            return;
        }

        // UC-12 rule 8: a change we caused ourselves (mode/DPI write re-enumerates paths)
        // must not re-trigger the fix loop. The disconnect→restore path above is a safety
        // path and is deliberately never suppressed by this window.
        if (_clock() - _lastOwnMutationUtc < _selfChangeWindow)
        {
            Log.Info("Display change within self-change window — suppressed.", "Monitor",
                ("windowMs", _selfChangeWindow.TotalMilliseconds.ToString()));
            return;
        }

        DisplayTopology topology = _topology.Query();
        bool triggerMet = TriggerCondition.IsMet(topology);

        Log.Info("Trigger evaluation", "Monitor",
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
            // UC-12 rule 11: in safe mode no automatic mutations; manual retry stays available.
            if (IsSafeMode)
            {
                Log.Warn("Safe mode active — not auto-applying. Use ApplyNow/diagnostics to retry.", "Monitor",
                    ("consecutiveFailures", _consecutiveFailures.ToString()));
                return;
            }

            Log.Info("Trigger met and session idle — starting fix sequence.", "Monitor",
                ("evaluationMs", sw.ElapsedMilliseconds.ToString()));
            await RunExclusiveAsync(FixSequenceAsync).ConfigureAwait(false);
        }
    }

    // ── Core operations (caller holds the gate) ─────────────────────────────────

    private async Task ApplyCoreAsync()
    {
        if (_session.State == SessionState.Paused)
        {
            Log.Info("ApplyNow: session was paused — resuming.", "Manual");
            _session.Resume();
        }

        if (_session.State != SessionState.Idle)
        {
            Log.Info("ApplyNow: ignored — session not idle.", "Manual",
                ("state", _session.State.ToString()));
            return;
        }

        Log.Info("ApplyNow: running fix sequence on user request.", "Manual");
        await FixSequenceAsync().ConfigureAwait(false);
    }

    private Task RestoreNowCoreAsync()
    {
        if (_session.State == SessionState.Paused)
        {
            Log.Info("RestoreNow: ignored — session is paused.", "Manual",
                ("state", SessionState.Paused.ToString()));
            return Task.CompletedTask;
        }

        if (_session.State is not (SessionState.Active or SessionState.Applying))
        {
            Log.Info("RestoreNow: ignored — session not active.", "Manual",
                ("state", _session.State.ToString()));
            return Task.CompletedTask;
        }

        Log.Info("RestoreNow: running restore on user request.", "Manual",
            ("state", _session.State.ToString()));
        return RestoreSequenceAsync();
    }

    private Task CheckCurrentStateCoreAsync()
    {
        if (_session.State != SessionState.Idle)
        {
            return Task.CompletedTask;
        }

        // UC-12 rule 11: safe mode blocks automatic applies, including the startup check.
        if (IsSafeMode)
        {
            Log.Warn("Startup state check: safe mode active — not auto-applying. Use ApplyNow to retry.", "Startup",
                ("consecutiveFailures", _consecutiveFailures.ToString()));
            return Task.CompletedTask;
        }

        DisplayTopology topology = _topology.Query();
        bool triggerMet = TriggerCondition.IsMet(topology);

        Log.Info("Startup state check", "Startup",
            ("triggerMet", triggerMet.ToString()),
            ("mirrored", topology.IsMirrored.ToString()),
            ("hasMismatch", topology.HasMirrorAspectMismatch.ToString()),
            ("externalCount", topology.ExternalPaths.Count),
            ("topologyDetail", topology.ToString()),
            ("topologyId", topology.CurrentTopologyId));

        return triggerMet ? FixSequenceAsync() : Task.CompletedTask;
    }

    private async Task TryRecoverCoreAsync()
    {
        JournalRecord? journal = _journal.Read();
        if (journal is null || !journal.IsActive)
        {
            Log.Info("No recovery needed — journal empty or idle.", "Recovery");
            return;
        }

        Log.Warn(
            $"Recovering from previous interrupted session (appliedAt={journal.AppliedAt}, reason={journal.Reason}).",
            "Recovery");

        // Rehydrate the Night Light snapshot so restore can put it back exactly.
        _nightLightSnapshot = JournalStore.RehydrateNightLight(journal);
        await RestoreSequenceAsync().ConfigureAwait(false);
    }

    // ── Fix sequence (caller holds the gate) ────────────────────────────────────

    private async Task FixSequenceAsync()
    {
        if (!_session.TryBeginFix())
        {
            Log.Warn("Session refused BeginFix (not in Idle/Detecting state).", "Fix");
            return;
        }

        var overall = Stopwatch.StartNew();
        try
        {
            // Step 1: Night Light OFF.
            Log.Info("Fix Step 1: Night Light OFF", "Fix", ("step", "nightLight"));
            _nightLightSnapshot = _nightLight.SnapshotAll();

            Log.Info("Night Light snapshots captured", "Fix",
                ("count", _nightLightSnapshot.Count),
                ("snapshots", string.Join(",", _nightLightSnapshot.Select(s => s.KeyPath))));

            // Persist intent before any mutation: if Beam is killed later in the sequence,
            // the next launch sees an active journal and can recover.
            _journal.BeginApply("fix-sequence-started", _nightLightSnapshot);

            NightLightWriteResult nlResult = _nightLight.SetActive(active: false);
            MarkOwnMutation();
            Log.Info("Night Light set-off result", "Fix",
                ("step", "nightLight"),
                ("success", nlResult.Success.ToString()),
                ("message", nlResult.Message),
                ("details", string.Join(" | ", nlResult.Details)));

            // Step 2: clone/duplicate topology with a 16:9 shared source.
            Log.Info("Fix Step 2: Display mode — clone + 16:9", "Fix", ("step", "displayMode"));
            LogTopology("Topology before mode change", "Fix", _topology.Query());

            string? modeError = _mode.ApplyCloneWith16x9();
            MarkOwnMutation();

            // UC-12 rule 4: verify after the mode mutation — the trigger must be gone.
            if (modeError is null && TriggerCondition.IsMet(_topology.Query()))
            {
                modeError = "verify-after-mode failed: mirror aspect mismatch still present after apply";
            }

            LogTopology("Topology after mode change", "Fix", _topology.Query(), modeError);

            if (modeError is not null)
            {
                // UC-12 rules 4/13: never stay half-applied. Unwind everything via restore.
                Log.Warn("Fix failed at display mode — unwinding via restore.", "Fix",
                    ("step", "displayMode"),
                    ("error", modeError));
                NoteFailure(modeError);
                _session.RequestRestore();
                await RestoreSequenceAsync().ConfigureAwait(false);
                return;
            }

            Log.Info("Display mode applied and verified", "Fix", ("step", "displayMode"));

            // Step 3: readable DPI on the shared source.
            ApplyDpi();
            MarkOwnMutation();

            _session.MarkActive();
            NoteSuccess("fix sequence complete");
            overall.Stop();
            Log.Info("Fix sequence complete — session is now Active", "Fix",
                ("totalMs", overall.ElapsedMilliseconds.ToString()));
        }
        catch (Exception ex)
        {
            overall.Stop();
            Log.Exception(ex, "Fix", ("step", "unknown"));
            NoteFailure(ex.Message);
            _session.RequestRestore();
            await RestoreSequenceAsync().ConfigureAwait(false);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void ApplyDpi()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            DisplayTopology topology = _topology.Query();
            DisplayPath? sharedSource = topology.ActivePaths.FirstOrDefault();
            if (sharedSource is null)
            {
                Log.Warn("DPI: no active source found.", "Fix", ("step", "dpi"));
                return;
            }

            Log.Info("DPI step", "Fix",
                ("step", "dpi"),
                ("adapterId", sharedSource.AdapterId),
                ("sourceId", sharedSource.SourceId.ToString()),
                ("sourceMode", sharedSource.SourceMode?.Width.ToString() ?? "unknown"),
                ("sourceGdiName", sharedSource.SourceGdiName));

            DpiScale? scale = _dpi.Read(sharedSource);
            if (scale?.Value is null)
            {
                Log.Warn("DPI: could not read current scale from DisplayConfig API.", "Fix",
                    ("step", "dpi"),
                    ("adapterId", sharedSource.AdapterId),
                    ("sourceId", sharedSource.SourceId.ToString()),
                    ("scaleSupported", scale?.IsSupported.ToString() ?? "null"),
                    ("scaleValue", scale?.Value?.ToString() ?? "null"));
                return;
            }

            uint sourceWidth = sharedSource.SourceMode?.Width ?? 1920;
            int targetPercent = ScalePlanner.ChooseScale(sourceWidth, scale.Value);

            Log.Info("DPI scale analysis", "Fix",
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
                Log.Info("DPI: already at optimal; no change needed.", "Fix",
                    ("step", "dpi"),
                    ("noChangeNeeded", true.ToString()),
                    ("currentPercent", scale.Value.Current.ToString()));
                return;
            }

            bool applied = _dpi.SetPercentageAndVerify(sharedSource, targetPercent, scale.Value);

            sw.Stop();
            Log.Info("DPI scale applied", "Fix",
                ("step", "dpi"),
                ("targetPercent", targetPercent.ToString()),
                ("verified", applied.ToString()),
                ("elapsedMs", sw.ElapsedMilliseconds.ToString()));
        }
        catch (Exception ex)
        {
            Log.Exception(ex, "Fix", ("step", "dpi"));
        }
    }

    // ── Restore sequence (caller holds the gate) ────────────────────────────────

    private async Task RestoreSequenceAsync()
    {
        _session.RequestRestore();

        var overall = Stopwatch.StartNew();
        try
        {
            Log.Info("Restore sequence starting", "Restore");

            // Step 1: return to the extended topology and the captured source modes.
            Log.Info("Restore Step 1: Display mode", "Restore", ("step", "displayMode"));
            LogTopology("Topology before restore", "Restore", _topology.Query());

            string? modeError = _mode.Restore();

            LogTopology("Topology after restore", "Restore", _topology.Query(), modeError);

            // Step 2: put Night Light back exactly as it was.
            Log.Info("Restore Step 2: Night Light", "Restore", ("step", "nightLight"));
            if (_nightLightSnapshot is { Count: > 0 } snapshot)
            {
                NightLightWriteResult nlResult = _nightLight.Restore(snapshot);
                Log.Info("Night Light restore result", "Restore",
                    ("step", "nightLight"),
                    ("success", nlResult.Success.ToString()),
                    ("message", nlResult.Message),
                    ("details", string.Join(" | ", nlResult.Details)));
            }
            else
            {
                Log.Info("Restore: no Night Light snapshot to restore.", "Restore",
                    ("step", "nightLight"));
            }

            MarkOwnMutation();
            _nightLightSnapshot = null;
            _journal.Clear();
            _session.MarkIdle();
            overall.Stop();
            Log.Info("Restore sequence complete", "Restore",
                ("totalMs", overall.ElapsedMilliseconds.ToString()));
        }
        catch (Exception ex)
        {
            overall.Stop();
            Log.Exception(ex, "Restore");
            _nightLightSnapshot = null;
            _session.MarkIdle();
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>UC-12 rule 8: records that Beam itself just mutated the display stack.</summary>
    private void MarkOwnMutation() => _lastOwnMutationUtc = _clock();

    /// <summary>UC-12 rule 11: counts a failed fix; enters safe mode at the threshold.</summary>
    private void NoteFailure(string reason)
    {
        _consecutiveFailures++;
        bool safeMode = IsSafeMode;
        Log.Warn("Fix failure recorded", "Fix",
            ("reason", reason),
            ("consecutiveFailures", _consecutiveFailures.ToString()),
            ("safeMode", safeMode.ToString()));
        if (safeMode)
        {
            SafeModeChanged?.Invoke(true);
        }
    }

    /// <summary>Clears the failure streak; leaves safe mode if it was active.</summary>
    private void NoteSuccess(string reason)
    {
        bool wasSafeMode = IsSafeMode;
        if (_consecutiveFailures > 0)
        {
            Log.Info("Failure streak cleared", "Fix",
                ("reason", reason),
                ("wasSafeMode", wasSafeMode.ToString()));
        }

        _consecutiveFailures = 0;
        if (wasSafeMode)
        {
            SafeModeChanged?.Invoke(false);
        }
    }

    private static void LogTopology(
        string message,
        string category,
        DisplayTopology topology,
        string? error = null)
    {
        Log.Info(message, category,
            ("topologyId", topology.CurrentTopologyId),
            ("isMirrored", topology.IsMirrored.ToString()),
            ("activePaths", topology.ActivePaths.Count),
            ("topologyDetail", topology.ToString()),
            ("error", error ?? "none"));
    }

    private async Task RunExclusiveAsync(Func<Task> body)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await body().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task Track(Task task)
    {
        _pending = task;
        return task;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _changes.Changed -= OnDisplayChanged;
        _gate.Dispose();
    }
}
