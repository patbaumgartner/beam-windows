using System.Diagnostics;
using Beam.App.Journal;
using Beam.App.Sessions;
using Beam.Core.Diagnostics;
using Beam.Core.Display;
using Beam.Core.Input;
using Beam.Core.NightLight;
using Beam.Core.Ports;
using Beam.Core.Settings;
using Beam.Core.Status;

namespace Beam.App.Presentation;

/// <summary>
/// The presentation engine. Listens for display changes, evaluates the trigger condition,
/// and sequences Night Light → display mode → DPI → extras (keep-awake / DND / audio),
/// and the reverse on restore.
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

    /// <summary>
    /// Issue #1: after a successful mode apply, the OS may still report the old topology
    /// for a moment, so the verify-after-mode check polls instead of reading once.
    /// This is the poll interval between those re-queries.
    /// </summary>
    public static readonly TimeSpan DefaultVerifySettleInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Issue #1: bound on the verify-after-mode re-queries (≈2 s at the default interval),
    /// so a genuinely still-mismatched topology still fails the verify instead of hanging.
    /// </summary>
    public const int DefaultVerifyMaxAttempts = 20;

    private readonly IDisplayChangeSource _changes;
    private readonly ITopologySource _topology;
    private readonly IDisplayModeController _mode;
    private readonly IDpiController _dpi;
    private readonly INightLightController _nightLight;
    private readonly IJournalStore _journal;
    private readonly SessionManager _session;
    private readonly TimeSpan _selfChangeWindow;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _verifySettleInterval;
    private readonly int _verifyMaxAttempts;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _operationLock = new();
    private readonly Func<IDisposable?>? _operationLockFactory;
    private Task _operationTail = Task.CompletedTask;
    private IReadOnlyList<NightLightSnapshot>? _nightLightSnapshot;
    private IReadOnlyList<JournalDpiEntry>? _dpiSnapshot;
    private bool _modeChangeAttempted;
    private bool _dpiChangeAttempted;
    private bool _nightLightChangeAttempted;
    private bool _simulatedPresentation;
    private Task _pending = Task.CompletedTask;
    private readonly object _displayEventLock = new();
    private Task _displayEventTail = Task.CompletedTask;
    private DateTimeOffset _lastOwnMutationUtc = DateTimeOffset.MinValue;
    private int _consecutiveFailures;
    private bool _disposed;

    // B5 "Ignore this display": target identities excluded from the trigger. Swapped
    // atomically (immutable list), so reads from the UI thread need no lock.
    private volatile IReadOnlyCollection<string> _ignoredDisplays = Array.Empty<string>();
    private DisplayTopology _lastTopology = DisplayTopology.Empty;
    private volatile IReadOnlyList<DisplayPath> _currentExternalPaths = Array.Empty<DisplayPath>();

    // B3 presentation extras. The ports are optional: when absent (e.g. a headless CLI
    // composition root) the corresponding extra is skipped entirely.
    private readonly IKeepAwake? _keepAwakePort;
    private readonly IDndController? _dnd;
    private readonly IAudioController? _audio;
    private JournalExtras? _extras;

    // B6 live status: published on every session state change so `beam status` can
    // report what the tray is doing. Optional; absent in the CLI composition root.
    private readonly IStatusWriter? _statusWriter;
    private string? _appliedSourceMode;
    private int? _appliedDpiPercent;
    private bool? _nightLightDisabled;

    // B4 persisted settings: loaded once in the ctor, saved on every change.
    private readonly ISettingsStore? _settingsStore;
    private BeamSettings _settings = BeamSettings.Defaults;
    private bool _isEnabled = true;
    private bool _useMode = true;
    private bool _useDpi = true;
    private bool _useNightLight = true;
    private bool _keepAwake = true;
    private bool _doNotDisturb = true;
    private bool _audioToHdmi = true;
    private string _hotkey = BeamSettings.Defaults.Hotkey;
    private PresentationTopology _projectorTopology = PresentationTopology.Mirror;

    // C1 settings window fields: pause preset, restore-on-disconnect, DPI target,
    // prefer-native ranking, known devices, first-run toast flag.
    private int? _timedPauseMinutes;
    private bool _restoreOnDisconnect = true;
    private int _dpiTargetPercent; // 0 = automatic
    private bool _preferNativeMode = true;
    private volatile IReadOnlyList<DeviceProfile> _knownDevices = Array.Empty<DeviceProfile>();

    /// <summary>Guards the check-then-act mutation of <see cref="_knownDevices"/>.
    /// QueryTopology runs on the display-change handler, the startup check and the tray UI
    /// thread at the same time; without this lock a connect-burst records the same projector
    /// twice (seen in the 2026-11-16 log: two "New external display recorded" 3 ms apart)
    /// and each recorder fires its own settings save.</summary>
    private readonly object _devicesLock = new();
    private bool _firstRunToastShown;
    private Timer? _pauseTimer;
    private DateTimeOffset _pauseUntilUtc = DateTimeOffset.MinValue;

    /// <summary>Raised when safe mode is entered or cleared (UC-12 AC5: must be visible).</summary>
    public event Action<bool>? SafeModeChanged;

    /// <summary>UC-15 minimal settings: master switch. While false, no automatic fixes run. Persisted (B4).</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        private set
        {
            _isEnabled = value;
            PersistSettings();
        }
    }

    /// <summary>Policy: control the display mode in the fix sequence (roadmap 1.13). Persisted (B4).</summary>
    public bool UseMode
    {
        get => _useMode;
        set
        {
            _useMode = value;
            PersistSettings();
        }
    }

    /// <summary>Policy: control the DPI scale in the fix sequence (roadmap 1.13). Persisted (B4).</summary>
    public bool UseDpi
    {
        get => _useDpi;
        set
        {
            _useDpi = value;
            PersistSettings();
        }
    }

    /// <summary>Policy: turn Night Light off in the fix sequence (roadmap 1.13). Persisted (B4).</summary>
    public bool UseNightLight
    {
        get => _useNightLight;
        set
        {
            _useNightLight = value;
            PersistSettings();
        }
    }

    /// <summary>B3 extra: hold the display awake while presenting (default on). Persisted (B4).</summary>
    public bool KeepAwake
    {
        get => _keepAwake;
        set
        {
            _keepAwake = value;
            PersistSettings();
        }
    }

    /// <summary>B3 extra: silence toast notifications while presenting (default on). Persisted (B4).</summary>
    public bool DoNotDisturb
    {
        get => _doNotDisturb;
        set
        {
            _doNotDisturb = value;
            PersistSettings();
        }
    }

    /// <summary>B3 extra: switch default audio to the projector/HDMI output while presenting (default on). Persisted (B4).</summary>
    public bool AudioToHdmi
    {
        get => _audioToHdmi;
        set
        {
            _audioToHdmi = value;
            PersistSettings();
        }
    }

    /// <summary>
    /// B7: global hotkey combo string, e.g. "Win+Alt+P". Persisted (B4). Win+P is reserved
    /// by Windows (the projector flyout) and never accepted — an unparsable or reserved
    /// value falls back to the default Win+Alt+P, so Beam can never hijack Win+P.
    /// </summary>
    public string Hotkey
    {
        get => _hotkey;
        set
        {
            string sanitized = SanitizeHotkey(value);
            bool changed = !string.Equals(_hotkey, sanitized, StringComparison.Ordinal);
            _hotkey = sanitized;
            PersistSettings();
            if (changed)
            {
                HotkeyChanged?.Invoke(_hotkey);
            }
        }
    }

    /// <summary>Raised after a new, sanitized hotkey value should be registered.</summary>
    public event Action<string>? HotkeyChanged;

    /// <summary>Returns the combo when it is parseable and not reserved by Windows; otherwise the default.</summary>
    private static string SanitizeHotkey(string? value) =>
        HotkeyParser.TryParse(value, out HotkeyCombo? combo) && combo is not null
            ? value!
            : BeamSettings.Defaults.Hotkey;

    /// <summary>
    /// What to do with the projector while presenting: <see cref="PresentationTopology.Mirror"/>
    /// (duplicate — the default, matching Win+P → Duplicate) or
    /// <see cref="PresentationTopology.Extend"/> (the projector becomes a second screen).
    /// Persisted (B4) as the <c>ProjectorTopology</c> setting ("Mirror" / "Extend").
    /// </summary>
    public PresentationTopology ProjectorTopology
    {
        get => _projectorTopology;
        set
        {
            _projectorTopology = value;
            PersistSettings();
        }
    }

    /// <summary>C1/C2: preferred pause duration in minutes (15/60), null = indefinite. Persisted.</summary>
    public int? TimedPauseMinutes
    {
        get => _timedPauseMinutes;
        set
        {
            _timedPauseMinutes = value;
            PersistSettings();
        }
    }

    /// <summary>C1: restore the user's display settings when the projector disconnects (default on). Persisted.</summary>
    public bool RestoreOnDisconnect
    {
        get => _restoreOnDisconnect;
        set
        {
            _restoreOnDisconnect = value;
            PersistSettings();
        }
    }

    /// <summary>C1: fixed DPI target percent for the fix sequence; 0 = automatic. Persisted.</summary>
    public int DpiTargetPercent
    {
        get => _dpiTargetPercent;
        set
        {
            _dpiTargetPercent = value < 0 ? 0 : value;
            PersistSettings();
        }
    }

    /// <summary>C1/B2: rank the EDID-native 16:9 mode first when choosing the projector mode. Persisted.</summary>
    public bool PreferNativeMode
    {
        get => _preferNativeMode;
        set
        {
            _preferNativeMode = value;
            PersistSettings();
        }
    }

    /// <summary>C1: displays seen before (Devices table in the settings window). Persisted.</summary>
    public IReadOnlyList<DeviceProfile> KnownDevices => _knownDevices;

    /// <summary>C3: true until the first-run toast has been shown once; then false forever.</summary>
    public bool ShouldShowFirstRunToast => !_firstRunToastShown;

    /// <summary>C3: marks the first-run toast as shown (persisted).</summary>
    public void MarkFirstRunToastShown()
    {
        _firstRunToastShown = true;
        PersistSettings();
    }

    /// <summary>B4: a snapshot of all current settings (exactly what would be persisted now).</summary>
    public BeamSettings CurrentSettings => _settings with
    {
        IsEnabled = _isEnabled,
        UseMode = _useMode,
        UseDpi = _useDpi,
        UseNightLight = _useNightLight,
        KeepAwake = _keepAwake,
        DoNotDisturb = _doNotDisturb,
        AudioToHdmi = _audioToHdmi,
        IgnoredDisplays = _ignoredDisplays.ToList(),
        Hotkey = _hotkey,
        ProjectorTopology = _projectorTopology.ToSettingString(),
        TimedPauseMinutes = _timedPauseMinutes,
        RestoreOnDisconnect = _restoreOnDisconnect,
        DpiTargetPercent = _dpiTargetPercent,
        PreferNativeMode = _preferNativeMode,
        KnownDevices = _knownDevices.ToList(),
        FirstRunToastShown = _firstRunToastShown,
    };

    /// <summary>B5: raised when the ignore-list changes (the tray rebuilds its submenu from it).</summary>
    public event Action<IReadOnlyCollection<string>>? IgnoredDisplaysChanged;

    /// <summary>C1: raised when the known-devices list changes (the settings Devices table rebinds).</summary>
    public event Action<IReadOnlyList<DeviceProfile>>? KnownDevicesChanged;

    /// <summary>B5: target identities the user excluded from the trigger ("Ignore this display").</summary>
    public IReadOnlyCollection<string> IgnoredDisplays => _ignoredDisplays;

    /// <summary>
    /// B5: all external active paths from the most recent topology query. Kept unfiltered so
    /// ignored devices remain visible/checkable in the tray; trigger filtering is separate.
    /// Cache only — safe to read from the UI thread (A6: never QueryDisplayConfig there).
    /// </summary>
    public IReadOnlyList<DisplayPath> CurrentExternalPaths => _currentExternalPaths;

    /// <summary>
    /// B1/B6: a snapshot of the live session status (same shape as the status.json file),
    /// safe to read from the UI thread — used by the tray for the rich tooltip. The applied
    /// fields are null while idle.
    /// </summary>
    public StatusFileRecord CurrentStatus => new(
        _session.State.ToString(),
        null,
        _clock().ToString("o"),
        Environment.ProcessId,
        _appliedSourceMode,
        _appliedDpiPercent,
        _nightLightDisabled);

    public PresentationManager(
        IDisplayChangeSource changes,
        ITopologySource topology,
        IDisplayModeController mode,
        IDpiController dpi,
        INightLightController nightLight,
        IJournalStore journal,
        SessionManager session,
        TimeSpan? selfChangeWindow = null,
        Func<DateTimeOffset>? clock = null,
        ISettingsStore? settings = null,
        IKeepAwake? keepAwake = null,
        IDndController? dnd = null,
        IAudioController? audio = null,
        IStatusWriter? statusWriter = null,
        TimeSpan? verifySettleInterval = null,
        int? verifyMaxAttempts = null,
        Func<IDisposable?>? operationLockFactory = null)
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
        _verifySettleInterval = verifySettleInterval ?? DefaultVerifySettleInterval;
        _verifyMaxAttempts = Math.Max(1, verifyMaxAttempts ?? DefaultVerifyMaxAttempts);
        _settingsStore = settings;
        _keepAwakePort = keepAwake;
        _dnd = dnd;
        _audio = audio;
        _statusWriter = statusWriter;
        _operationLockFactory = operationLockFactory;

        if (statusWriter is not null)
        {
            _session.StateChanged += OnSessionStateChanged;
            // Publish the initial state so `beam status` sees the tray even before the
            // first transition.
            WriteStatus(_session.State, "startup", _clock());
        }

        if (settings is not null)
        {
            // B4: load persisted settings. Backing fields are assigned directly so the
            // load itself never triggers a save or the IgnoredDisplaysChanged event.
            BeamSettings loaded = settings.Load();
            _settings = loaded;
            _isEnabled = loaded.IsEnabled;
            _useMode = loaded.UseMode;
            _useDpi = loaded.UseDpi;
            _useNightLight = loaded.UseNightLight;
            _keepAwake = loaded.KeepAwake;
            _doNotDisturb = loaded.DoNotDisturb;
            _audioToHdmi = loaded.AudioToHdmi;
            _hotkey = SanitizeHotkey(loaded.Hotkey);
            _projectorTopology = PresentationTopologyExtensions.FromSettingString(loaded.ProjectorTopology);
            _ignoredDisplays = (loaded.IgnoredDisplays ?? Array.Empty<string>()).ToList();
            _timedPauseMinutes = loaded.TimedPauseMinutes;
            _restoreOnDisconnect = loaded.RestoreOnDisconnect;
            _dpiTargetPercent = loaded.DpiTargetPercent < 0 ? 0 : loaded.DpiTargetPercent;
            _preferNativeMode = loaded.PreferNativeMode;
            _knownDevices = loaded.KnownDevices ?? Array.Empty<DeviceProfile>();
            _firstRunToastShown = loaded.FirstRunToastShown;
            Log.Info("Settings loaded from disk", "Settings",
                ("version", loaded.Version.ToString()),
                ("isEnabled", _isEnabled.ToString()),
                ("useMode", _useMode.ToString()),
                ("useDpi", _useDpi.ToString()),
                ("useNightLight", _useNightLight.ToString()),
                ("keepAwake", _keepAwake.ToString()),
                ("doNotDisturb", _doNotDisturb.ToString()),
                ("audioToHdmi", _audioToHdmi.ToString()),
                ("ignoredCount", _ignoredDisplays.Count.ToString()),
                ("hotkey", _hotkey),
                ("projectorTopology", _projectorTopology.ToSettingString()));
        }

        _changes.Changed += OnDisplayChanged;
    }

    /// <summary>
    /// B6: writes the live status record on every session transition. Never throws —
    /// a status write failure must not affect the session.
    /// </summary>
    private void OnSessionStateChanged(SessionEvent evt) =>
        WriteStatus(evt.To, string.IsNullOrEmpty(evt.Reason) ? null : evt.Reason, evt.Timestamp);

    private void WriteStatus(SessionState state, string? reason, DateTimeOffset timestamp)
    {
        if (_statusWriter is null)
        {
            return;
        }

        try
        {
            _statusWriter.Write(new StatusFileRecord(
                state.ToString(),
                reason,
                timestamp.ToString("o"),
                Environment.ProcessId,
                _appliedSourceMode,
                _appliedDpiPercent,
                _nightLightDisabled));
        }
        catch (Exception ex)
        {
            Log.Warn("Status write failed — continuing", "Status", ("error", ex.Message));
        }
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

    /// <summary>B5: same, honouring the per-display ignore list.</summary>
    public static bool IsTriggerConditionMet(DisplayTopology topology, IReadOnlyCollection<string>? ignoredIdentities) =>
        TriggerCondition.IsMet(topology, ignoredIdentities);

    // ── Public operations ───────────────────────────────────────────────────────

    /// <summary>User-requested apply. Resumes a paused session, then runs the fix if idle.</summary>
    public Task ApplyNowAsync() => Track(RunExclusiveAsync(ApplyCoreAsync));

    /// <summary>User-requested restore. Ignored while paused or when nothing is applied.</summary>
    public Task RestoreNowAsync() => Track(RunExclusiveAsync(RestoreNowCoreAsync));

    /// <summary>
    /// Resumes a pause and reconciles changes missed while paused: restore a real presentation
    /// whose projector disappeared, or evaluate a newly connected projector while idle.
    /// </summary>
    public Task ResumeAsync() => Track(RunExclusiveAsync(ResumeCoreAsync));

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

    /// <summary>
    /// UC-12 rule 12 kill switch: disabling Beam restores an active fix immediately and
    /// stops automatic applies; enabling resumes monitoring.
    /// </summary>
    public Task SetEnabledAsync(bool enabled) => Track(RunExclusiveAsync(() => SetEnabledCoreAsync(enabled)));

    /// <summary>Manual Night Light on/off toggle from the tray Settings menu (roadmap 1.13).</summary>
    public Task SetNightLightAsync(bool on) => Track(RunExclusiveAsync(() =>
    {
        Log.Info("Night Light manual toggle", "Manual", ("active", on.ToString()));
        _nightLight.SetActive(on);
        return Task.CompletedTask;
    }));

    /// <summary>B5: exclude a display (by <see cref="DisplayTarget.Identity"/>) from the trigger.</summary>
    public void IgnoreDisplay(string identity)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        IReadOnlyCollection<string> current = _ignoredDisplays;
        if (current.Contains(identity))
        {
            return;
        }

        IReadOnlyCollection<string> next = current.Concat(new[] { identity }).ToList();
        SetIgnoredDisplaysCore(next);
        Log.Info("Display ignored", "Settings", ("identity", identity), ("ignoredCount", next.Count.ToString()));
    }

    /// <summary>B5: re-include a previously ignored display.</summary>
    public void UnignoreDisplay(string identity)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        IReadOnlyCollection<string> current = _ignoredDisplays;
        if (!current.Contains(identity))
        {
            return;
        }

        IReadOnlyCollection<string> next = current
            .Where(i => !string.Equals(i, identity, StringComparison.Ordinal))
            .ToList();
        SetIgnoredDisplaysCore(next);
        Log.Info("Display un-ignored", "Settings", ("identity", identity), ("ignoredCount", next.Count.ToString()));
    }

    /// <summary>B5: replace the whole ignore list (settings load). Null clears it.</summary>
    public void SetIgnoredDisplays(IEnumerable<string>? identities)
    {
        IReadOnlyCollection<string> next = (identities ?? Enumerable.Empty<string>())
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        SetIgnoredDisplaysCore(next);
        Log.Info("Ignore list loaded", "Settings", ("ignoredCount", next.Count.ToString()));
    }

    private void SetIgnoredDisplaysCore(IReadOnlyCollection<string> next)
    {
        _ignoredDisplays = next;
        // Re-filter the cached topology — no platform query, so this is safe on the UI thread (A6).
        _currentExternalPaths = _lastTopology.ExternalPaths;
        IgnoredDisplaysChanged?.Invoke(next);
        PersistSettings(); // B4: the ignore list lives in settings.json
    }

    // ── C1: per-device profiles (Devices table in the settings window) ──────────

    /// <summary>
    /// C1: the identities that must not trigger the automatic fix — the explicit ignore
    /// list (B5) plus any known device whose auto-apply flag is off.
    /// </summary>
    private IReadOnlyCollection<string> EffectiveIgnoredIdentities()
    {
        IReadOnlyCollection<string> ignored = _ignoredDisplays;
        var noAuto = _knownDevices.Where(d => !d.AutoApply).Select(d => d.Identity).ToList();
        if (noAuto.Count == 0)
        {
            return ignored;
        }

        return ignored.Concat(noAuto).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// C1: records every external display of a topology as known (idempotent) so the
    /// settings window's Devices table can list name + identity. Persists only when the
    /// set actually grows.
    /// </summary>
    private void RecordKnownDevices(DisplayTopology topology)
    {
        List<DeviceProfile>? recorded = null;
        lock (_devicesLock)
        {
            List<DeviceProfile> devices = _knownDevices.ToList();
            bool changed = false;
            foreach (DisplayPath p in topology.ActivePaths)
            {
                if (p.Target.IsInternal)
                {
                    continue;
                }

                string identity = p.Target.Identity;
                if (devices.Any(d => string.Equals(d.Identity, identity, StringComparison.Ordinal)))
                {
                    continue;
                }

                devices.Add(new DeviceProfile(identity, p.Target.FriendlyName, AutoApply: true, _clock().ToString("o")));
                changed = true;
                Log.Info("New external display recorded in known devices", "Settings",
                    ("identity", identity), ("name", p.Target.FriendlyName));
            }

            if (changed)
            {
                _knownDevices = devices;
                recorded = devices;
            }
        }

        // Event + persist stay outside the lock: PersistSettings does file I/O (its own
        // lock in the store serializes the writes), and handlers must not run under it.
        if (recorded is not null)
        {
            KnownDevicesChanged?.Invoke(recorded);
            PersistSettings();
        }
    }

    /// <summary>C1: sets the per-device auto-apply flag (false = this display never triggers the fix).</summary>
    public void SetDeviceAutoApply(string identity, bool autoApply)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        List<DeviceProfile> updated;
        lock (_devicesLock)
        {
            List<DeviceProfile> devices = _knownDevices.ToList();
            int i = devices.FindIndex(d => string.Equals(d.Identity, identity, StringComparison.Ordinal));
            if (i < 0)
            {
                devices.Add(new DeviceProfile(identity, identity, autoApply, _clock().ToString("o")));
            }
            else
            {
                devices[i] = devices[i] with { AutoApply = autoApply };
            }

            _knownDevices = devices;
            updated = devices;
        }

        KnownDevicesChanged?.Invoke(updated);
        PersistSettings();
        Log.Info("Device auto-apply changed", "Settings",
            ("identity", identity), ("autoApply", autoApply.ToString()));
    }

    /// <summary>C1: removes a device from the known list and clears any ignore for it.</summary>
    public void ForgetDevice(string identity)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        List<DeviceProfile>? removed = null;
        lock (_devicesLock)
        {
            List<DeviceProfile> devices = _knownDevices
                .Where(d => !string.Equals(d.Identity, identity, StringComparison.Ordinal))
                .ToList();
            if (devices.Count != _knownDevices.Count)
            {
                _knownDevices = devices;
                removed = devices;
            }
        }

        if (removed is not null)
        {
            KnownDevicesChanged?.Invoke(removed);
        }

        UnignoreDisplay(identity); // forget also clears the ignore flag
        PersistSettings();
        Log.Info("Device forgotten", "Settings", ("identity", identity));
    }

    /// <summary>
    /// C1/C2: pauses the session. Auto-resumes after <paramref name="duration"/>;
    /// <paramref name="duration"/> ≤ 0 means indefinite (resume manually).
    /// </summary>
    public void PauseFor(TimeSpan duration)
    {
        if (_session.State is SessionState.Detecting or SessionState.Applying or SessionState.Restoring)
        {
            // Pausing a transition would hide Applying/Restoring from MarkActive/MarkIdle and
            // could strand an owned fix. The tray disables these actions during transitions.
            Log.Warn("Pause ignored while a presentation transition is in progress", "Manual",
                ("state", _session.State.ToString()));
            return;
        }

        CancelPauseTimer();
        _session.Pause();
        Log.Info("Pause requested", "Manual",
            ("durationMs", duration.TotalMilliseconds.ToString()),
            ("indefinite", (duration <= TimeSpan.Zero).ToString()));
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        _pauseUntilUtc = _clock() + duration;
        _pauseTimer = new Timer(_ => OnPauseTimerElapsed(), null, duration, Timeout.InfiniteTimeSpan);
    }

    private void CancelPauseTimer()
    {
        _pauseTimer?.Dispose();
        _pauseTimer = null;
        _pauseUntilUtc = DateTimeOffset.MinValue;
    }

    private void OnPauseTimerElapsed()
    {
        if (_disposed)
        {
            return;
        }

        if (_session.State == SessionState.Paused && _clock() >= _pauseUntilUtc)
        {
            Log.Info("Timed pause elapsed — resuming", "Manual", ("pausedUntil", _pauseUntilUtc.ToString("o")));
            _ = ResumeAsync();
        }
    }

    /// <summary>
    /// B5/A6: re-queries the topology and refreshes <see cref="CurrentExternalPaths"/>.
    /// Calls the platform display query — must run off the UI thread.
    /// </summary>
    public void RefreshExternalPaths() => QueryTopology();

    // ── Display-change handling ─────────────────────────────────────────────────

    private void OnDisplayChanged(ConnectionSnapshot? previous, ConnectionSnapshot current)
    {
        if (_disposed)
        {
            return;
        }

        Task task;
        lock (_displayEventLock)
        {
            // The serialization gate protects native mutations, but state evaluation before
            // that gate must be ordered too. Chaining also makes PendingTask cover earlier
            // display work rather than only the newest event in a hot-plug burst.
            task = HandleDisplayChangedAfterAsync(_displayEventTail, previous, current);
            _displayEventTail = task;
        }

        Track(task);
    }

    private async Task HandleDisplayChangedAfterAsync(
        Task previousTask,
        ConnectionSnapshot? previous,
        ConnectionSnapshot current)
    {
        try
        {
            await previousTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Exception(ex, "Monitor", ("step", "previous-display-change"));
        }

        if (!_disposed)
        {
            await HandleDisplayChangedAsync(previous, current).ConfigureAwait(false);
        }
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

        if (!IsEnabled)
        {
            Log.Info("Beam is disabled — ignoring change.", "Monitor");
            return;
        }

        bool displayDisconnected = previous is not null && currCount < prevCount;

        if (state == SessionState.Active && displayDisconnected)
        {
            if (!_restoreOnDisconnect)
            {
                // C1: user turned "restore on disconnect" off — keep presenting on the
                // remaining display(s) and just re-evaluate below.
                Log.Info("External display disconnected while presenting — restore on disconnect is OFF, continuing.", "Monitor",
                    ("previousDisplays", prevCount),
                    ("currentDisplays", currCount));
            }
            else
            {
                Log.Info("External display disconnected while presenting — requesting restore.", "Monitor",
                    ("previousDisplays", prevCount),
                    ("currentDisplays", currCount));
                // Mark the transition before dispatching native work so a subsequent display
                // event cannot observe Active and re-apply while this restore is queued.
                _session.RequestRestore();
                await RunExclusiveAsync(RestoreSequenceAsync).ConfigureAwait(false);
                return;
            }
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

        DisplayTopology topology = QueryTopology();
        // C1: devices with auto-apply off act like ignored displays for the trigger.
        IReadOnlyCollection<string> effectiveIgnored = EffectiveIgnoredIdentities();
        bool triggerMet = TriggerCondition.IsMet(topology, effectiveIgnored);

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
            await RunExclusiveAsync(() => FixSequenceAsync()).ConfigureAwait(false);
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
        await FixSequenceAsync(manual: true).ConfigureAwait(false);
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

    private async Task ResumeCoreAsync()
    {
        if (_session.State != SessionState.Paused)
        {
            return;
        }

        CancelPauseTimer();
        _session.Resume();
        if (_session.State == SessionState.Active
            && _restoreOnDisconnect
            && !_simulatedPresentation
            && QueryTopology().ExternalPaths.Count == 0)
        {
            // The disconnect event was deliberately ignored while paused. Do not require a
            // second physical event to restore the user's settings on manual/timed resume.
            await RestoreSequenceAsync().ConfigureAwait(false);
            return;
        }

        // When idle, a projector connected during the pause is evaluated exactly once.
        await CheckCurrentStateCoreAsync().ConfigureAwait(false);
    }

    private Task SetEnabledCoreAsync(bool enabled)
    {
        if (IsEnabled == enabled)
        {
            return Task.CompletedTask;
        }

        bool pausedFromActive = _session.PausedFromActive;
        IsEnabled = enabled;
        Log.Info(enabled
                ? "Beam enabled by user — monitoring resumed."
                : "Beam disabled by user — restoring now if a fix is active.",
            "Manual", ("enabled", enabled.ToString()));

        // UC-12 rule 12: ownership survives Paused state. Resume only to make the normal
        // restore path available, then finish disabled and Idle when restoration succeeds.
        if (!enabled && (_session.State is SessionState.Active or SessionState.Applying || pausedFromActive))
        {
            if (pausedFromActive)
            {
                _session.Resume();
            }

            return RestoreSequenceAsync();
        }

        // Re-enabling must reconcile a projector connected while Beam was disabled instead
        // of waiting for another physical display event.
        return enabled ? CheckCurrentStateCoreAsync() : Task.CompletedTask;
    }

    private Task CheckCurrentStateCoreAsync()
    {
        if (_session.State != SessionState.Idle)
        {
            return Task.CompletedTask;
        }

        if (!IsEnabled)
        {
            Log.Info("Startup state check: Beam is disabled — not auto-applying.", "Startup");
            return Task.CompletedTask;
        }

        // UC-12 rule 11: safe mode blocks automatic applies, including the startup check.
        if (IsSafeMode)
        {
            Log.Warn("Startup state check: safe mode active — not auto-applying. Use ApplyNow to retry.", "Startup",
                ("consecutiveFailures", _consecutiveFailures.ToString()));
            return Task.CompletedTask;
        }

        DisplayTopology topology = QueryTopology();
        // C1: devices with auto-apply off act like ignored displays for the trigger.
        IReadOnlyCollection<string> effectiveIgnored = EffectiveIgnoredIdentities();
        bool triggerMet = TriggerCondition.IsMet(topology, effectiveIgnored);

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

        // Rehydrate the Night Light snapshot and the original DPI scales so restore can
        // put both back exactly (roadmap 1.9: DPI restore from the crash journal).
        _nightLightSnapshot = JournalStore.RehydrateNightLight(journal);
        _dpiSnapshot = JournalStore.RehydrateDpi(journal);
        // Legacy journals have no per-step phase information. Preserve their established
        // recovery behavior; normal in-process restores use the more precise attempted flags.
        _modeChangeAttempted = true;
        _dpiChangeAttempted = _dpiSnapshot is { Count: > 0 };
        _nightLightChangeAttempted = _nightLightSnapshot is { Count: > 0 };

        // B3: rehydrate the extras previous-state so crash recovery also undoes
        // keep-awake / DND / audio exactly as they were before the interrupted fix.
        _extras = journal.Extras;
        await RestoreSequenceAsync().ConfigureAwait(false);
    }

    // ── Fix sequence (caller holds the gate) ────────────────────────────────────

    /// <summary>
    /// Runs the fix sequence. <paramref name="manual"/> is true for a user-requested
    /// apply (tray menu / hotkey): when no external display is connected, the
    /// display-mode step is skipped and the presentation is simulated (Night Light,
    /// DPI and extras still apply) instead of failing and unwinding immediately.
    /// </summary>
    private async Task FixSequenceAsync(bool manual = false)
    {
        if (!_session.TryBeginFix())
        {
            Log.Warn("Session refused BeginFix (not in Idle/Detecting state).", "Fix");
            return;
        }

        var overall = Stopwatch.StartNew();
        _modeChangeAttempted = false;
        _dpiChangeAttempted = false;
        _nightLightChangeAttempted = false;
        _simulatedPresentation = false;
        try
        {
            // Step 1: Night Light OFF (policy-gated: UseNightLight).
            if (UseNightLight)
            {
                Log.Info("Fix Step 1: Night Light OFF", "Fix", ("step", "nightLight"));
                _nightLightSnapshot = _nightLight.SnapshotAll();

                Log.Info("Night Light snapshots captured", "Fix",
                    ("count", _nightLightSnapshot.Count),
                    ("snapshots", string.Join(",", _nightLightSnapshot.Select(s => s.KeyPath))));
            }
            else
            {
                Log.Info("Fix Step 1 skipped: policy says do not control Night Light.", "Fix", ("step", "nightLight"));
                _nightLightSnapshot = null;
            }

            // Roadmap 1.9: capture the original DPI scale only when Beam will control it.
            // A disabled policy must not make Undo write a value Beam never changed.
            _dpiSnapshot = UseDpi ? CaptureDpi() : null;

            // B3: capture the previous extras state (toasts, default audio endpoint) BEFORE
            // any mutation, so the journal — and therefore crash recovery — can undo each
            // extra exactly.
            _extras = CaptureExtras();

            // Persist intent before any mutation: if Beam is killed later in the sequence,
            // the next launch sees an active journal and can recover. Without a durable
            // journal, changing the machine would be unsafe, so abort before the first write.
            try
            {
                _journal.BeginApply("fix-sequence-started", _nightLightSnapshot, _dpiSnapshot, _extras);
            }
            catch (Exception ex)
            {
                overall.Stop();
                Log.Exception(ex, "Fix", ("step", "journal"));
                NoteFailure("recovery journal could not be written; apply aborted");
                _nightLightSnapshot = null;
                _dpiSnapshot = null;
                _extras = null;
                _session.RequestRestore();
                _session.MarkIdle();
                return;
            }

            if (UseNightLight)
            {
                _nightLightChangeAttempted = true;
                NightLightWriteResult nlResult = _nightLight.SetActive(active: false);
                MarkOwnMutation();
                _nightLightDisabled = nlResult.Success; // B6: reported in the live status
                Log.Info("Night Light set-off result", "Fix",
                    ("step", "nightLight"),
                    ("success", nlResult.Success.ToString()),
                    ("message", nlResult.Message),
                    ("details", string.Join(" | ", nlResult.Details)));
            }

            // Step 2: display mode — mirror (clone + 16:9 shared source) or extend
            // (projector as a second screen), per the persisted ProjectorTopology
            // setting (policy-gated: UseMode).
            string? modeError = null;
            if (UseMode)
            {
                DisplayTopology beforeMode = QueryTopology();
                if (manual && beforeMode.ActivePaths.Count < 2)
                {
                    // Manual "Apply projector mode" with no external display connected:
                    // a clone/extend has nothing to mirror onto and would fail, and the
                    // unwind below would switch straight back. Simulate the presentation
                    // instead — Night Light, DPI and the extras still apply; only the
                    // display-mode step is skipped.
                    _simulatedPresentation = true;
                    Log.Info("Fix Step 2 skipped: manual apply with a single display — simulating presentation mode.", "Fix",
                        ("step", "displayMode"),
                        ("activePaths", beforeMode.ActivePaths.Count.ToString()),
                        ("projectorTopology", _projectorTopology.ToString()));
                }
                else
                {
                    bool extend = _projectorTopology == PresentationTopology.Extend;
                    Log.Info(extend
                            ? "Fix Step 2: Display mode — extend + 16:9 on the external source"
                            : "Fix Step 2: Display mode — clone + 16:9",
                        "Fix", ("step", "displayMode"), ("projectorTopology", _projectorTopology.ToString()));
                    LogTopology("Topology before mode change", "Fix", beforeMode);

                    _modeChangeAttempted = true;
                    modeError = extend
                        ? _mode.ApplyExtendWith16x9(_preferNativeMode) // C1: prefer-native policy
                        : _mode.ApplyCloneWith16x9(_preferNativeMode);
                    MarkOwnMutation();

                    // UC-12 rule 4: verify after the mode mutation — the trigger must be gone.
                    // Issue #1: ChangeDisplaySettingsEx returns before the OS has
                    // re-enumerated the display paths, so a single immediate query can read
                    // a stale topology and false-fail the verify. Poll for a bounded settle
                    // window instead of reading once. (After an extend the topology is no
                    // longer mirrored, so the trigger is gone by construction.)
                    DisplayTopology afterMode = QueryTopology();
                    if (modeError is null)
                    {
                        (modeError, afterMode) = await VerifyTriggerClearedAsync(afterMode).ConfigureAwait(false);
                    }

                    LogTopology("Topology after mode change", "Fix", afterMode, modeError);

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

                    // B6: remember what the fix applied so the live status can report it.
                    // Reuse the settled topology from the verify — no extra query needed.
                    DisplayPath? appliedShared = SelectPresentationSource(afterMode);
                    _appliedSourceMode = appliedShared?.SourceMode is { } sm
                        ? $"{sm.Width}x{sm.Height}@{sm.RefreshHz:0}"
                        : null;
                }
            }
            else
            {
                Log.Info("Fix Step 2 skipped: policy says do not control display mode.", "Fix", ("step", "displayMode"));
            }

            // Step 3: readable DPI on the shared source (policy-gated: UseDpi).
            if (UseDpi)
            {
                ApplyDpi();
                MarkOwnMutation();
            }
            else
            {
                Log.Info("Fix Step 3 skipped: policy says do not control DPI.", "Fix", ("step", "dpi"));
            }

            // Step 4: presentation extras (keep-awake, DND, audio-to-HDMI). Best-effort:
            // a failure here must never fail the session.
            ApplyExtras();

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

    /// <summary>
    /// UC-12 rule 4 verify (issue #1): returns null when the trigger is gone from
    /// <paramref name="first"/> or becomes gone within the bounded settle window,
    /// otherwise the verify-failed error. Each re-query goes through
    /// <see cref="QueryTopology"/>, so the external-paths cache stays fresh too.
    /// </summary>
    private async Task<(string? Error, DisplayTopology Topology)> VerifyTriggerClearedAsync(DisplayTopology first)
    {
        const string verifyError = "verify-after-mode failed: mirror aspect mismatch still present after apply";

        DisplayTopology topology = first;
        for (int attempt = 1; ; attempt++)
        {
            if (!TriggerCondition.IsMet(topology, _ignoredDisplays))
            {
                return (null, topology);
            }

            if (attempt >= _verifyMaxAttempts)
            {
                Log.Warn("Verify-after-mode: trigger still met after the settle window — verify failed", "Fix",
                    ("attempts", attempt.ToString()),
                    ("maxAttempts", _verifyMaxAttempts.ToString()),
                    ("pollMs", _verifySettleInterval.TotalMilliseconds.ToString()));
                return (verifyError, topology);
            }

            if (attempt == 1)
            {
                Log.Info("Verify-after-mode: trigger still met right after apply — polling for the OS to settle", "Fix",
                    ("maxAttempts", _verifyMaxAttempts.ToString()),
                    ("pollMs", _verifySettleInterval.TotalMilliseconds.ToString()));
            }

            await Task.Delay(_verifySettleInterval).ConfigureAwait(false);
            topology = QueryTopology();
        }
    }

    private void ApplyDpi()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            DisplayTopology topology = QueryTopology();
            DisplayPath? sharedSource = SelectPresentationSource(topology);
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
            // C1: a fixed DPI target from settings wins over the automatic recommendation
            // (clamped to the adapter's supported range); 0 = automatic.
            int targetPercent = _dpiTargetPercent > 0
                ? Math.Clamp(_dpiTargetPercent, scale.Value.Minimum, scale.Value.Maximum)
                : ScalePlanner.ChooseScale(sourceWidth, scale.Value);

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
                _appliedDpiPercent = scale.Value.Current; // B6: reported in the live status
                Log.Info("DPI: already at optimal; no change needed.", "Fix",
                    ("step", "dpi"),
                    ("noChangeNeeded", true.ToString()),
                    ("currentPercent", scale.Value.Current.ToString()));
                return;
            }

            _dpiChangeAttempted = true;
            bool applied = _dpi.SetPercentageAndVerify(sharedSource, targetPercent, scale.Value);
            if (applied)
            {
                _appliedDpiPercent = targetPercent; // B6: reported in the live status
            }

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

            // Step 1: restore display mode only when Beam actually attempted to change it.
            // Simulated presentation, UseMode=false, and pre-mutation failures must not
            // unexpectedly switch the user's topology during Undo/error unwinding.
            string? modeError = null;
            if (_modeChangeAttempted)
            {
                Log.Info("Restore Step 1: Display mode", "Restore", ("step", "displayMode"));
                LogTopology("Topology before restore", "Restore", QueryTopology());
                modeError = _mode.Restore();
                LogTopology("Topology after restore", "Restore", QueryTopology(), modeError);
            }
            else
            {
                Log.Info("Restore Step 1 skipped: Beam did not change display mode.", "Restore",
                    ("step", "displayMode"));
            }

            // Step 2: put back DPI only when Beam attempted a scale write.
            bool dpiRestored = true;
            if (_dpiChangeAttempted && _dpiSnapshot is { Count: > 0 } dpiEntries)
            {
                Log.Info("Restore Step 2: DPI", "Restore", ("step", "dpi"));
                dpiRestored = RestoreDpi(dpiEntries);
            }

            // Step 3: put Night Light back exactly as it was.
            bool nightLightRestored = true;
            Log.Info("Restore Step 3: Night Light", "Restore", ("step", "nightLight"));
            if (_nightLightChangeAttempted && _nightLightSnapshot is { Count: > 0 } snapshot)
            {
                NightLightWriteResult nlResult = _nightLight.Restore(snapshot);
                nightLightRestored = nlResult.Success;
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

            // Step 4: undo the B3 presentation extras (best-effort, failure-isolated).
            RestoreExtras();
            MarkOwnMutation();

            bool restored = modeError is null && dpiRestored && nightLightRestored;
            overall.Stop();
            if (!restored)
            {
                // Keep every inverse and the crash journal. Returning to Active leaves Undo
                // armed for an in-process retry and prevents a new apply replacing them.
                _session.MarkRestoreFailed();
                Log.Warn("Restore incomplete — recovery journal retained; Undo can be retried.", "Restore",
                    ("modeError", modeError ?? "none"),
                    ("dpiRestored", dpiRestored.ToString()),
                    ("nightLightRestored", nightLightRestored.ToString()),
                    ("totalMs", overall.ElapsedMilliseconds.ToString()));
                return;
            }

            // B6: the final Idle status must not report a stale "applied" fix. Only clear
            // recovery state after every required restoration was verified.
            _journal.Clear();
            _nightLightSnapshot = null;
            _dpiSnapshot = null;
            _modeChangeAttempted = false;
            _dpiChangeAttempted = false;
            _nightLightChangeAttempted = false;
            _simulatedPresentation = false;
            _extras = null;
            _appliedSourceMode = null;
            _appliedDpiPercent = null;
            _nightLightDisabled = null;
            _session.MarkIdle();
            Log.Info("Restore sequence complete", "Restore",
                ("totalMs", overall.ElapsedMilliseconds.ToString()));
        }
        catch (Exception ex)
        {
            overall.Stop();
            Log.Exception(ex, "Restore");
            _session.MarkRestoreFailed();
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// B4: persists the current settings through the store when one was provided.
    /// Never throws — a settings write failure must not affect the fix sequence.
    /// </summary>
    private void PersistSettings()
    {
        if (_settingsStore is null)
        {
            return;
        }

        try
        {
            BeamSettings snapshot = CurrentSettings;
            _settings = snapshot;
            _settingsStore.Save(snapshot);
        }
        catch (Exception ex)
        {
            Log.Warn("Settings persist failed — continuing with in-memory settings", "Settings",
                ("error", ex.Message));
        }
    }

    /// <summary>UC-12 rule 8: records that Beam itself just mutated the display stack.</summary>
    private void MarkOwnMutation() => _lastOwnMutationUtc = _clock();

    /// <summary>
    /// Queries the topology and refreshes the <see cref="CurrentExternalPaths"/> cache, so
    /// the tray menu can read the cache on the UI thread without a display query (A6).
    /// </summary>
    private DisplayTopology QueryTopology()
    {
        DisplayTopology topology = _topology.Query();
        _lastTopology = topology;
        _currentExternalPaths = topology.ExternalPaths;
        RecordKnownDevices(topology); // C1: feed the settings Devices table
        return topology;
    }

    /// <summary>
    /// Selects the source whose mode/DPI Beam owns. Extend has independent desktops, so the
    /// external projector must be used rather than the first (usually internal) active path.
    /// Mirror shares one source; single-display simulation keeps the sole internal source.
    /// </summary>
    private DisplayPath? SelectPresentationSource(DisplayTopology topology)
    {
        IReadOnlyList<DisplayPath> active = topology.ActivePaths;
        if (_projectorTopology == PresentationTopology.Extend)
        {
            return active.FirstOrDefault(p => !p.Target.IsInternal) ?? active.FirstOrDefault();
        }

        return active.FirstOrDefault();
    }

    /// <summary>
    /// Roadmap 1.9: captures the current DPI percent of the shared source before any
    /// mutation, so restore can put it back and the journal can persist it for crash recovery.
    /// </summary>
    private IReadOnlyList<JournalDpiEntry>? CaptureDpi()
    {
        try
        {
            DisplayPath? sharedSource = SelectPresentationSource(QueryTopology());
            if (sharedSource is null)
            {
                return null;
            }

            DpiScale? scale = _dpi.Read(sharedSource);
            if (scale?.Value is null)
            {
                return null;
            }

            return new[]
            {
                new JournalDpiEntry(sharedSource.AdapterId, sharedSource.SourceId, scale.Value.Current),
            };
        }
        catch (Exception ex)
        {
            Log.Exception(ex, "Fix", ("step", "dpiCapture"));
            return null;
        }
    }

    /// <summary>Roadmap 1.9: puts the captured original DPI scales back, verifying each write.</summary>
    private bool RestoreDpi(IReadOnlyList<JournalDpiEntry> entries)
    {
        bool allRestored = true;
        foreach (JournalDpiEntry entry in entries)
        {
            try
            {
                DisplayPath? path = QueryTopology().Paths.FirstOrDefault(p =>
                    p.AdapterId == entry.AdapterId && p.SourceId == entry.SourceId);
                if (path is null)
                {
                    // UC-12 edge case: unplugged during restore — skip, finish the rest.
                    Log.Warn("DPI restore: source no longer present — skipping.", "Restore",
                        ("adapterId", entry.AdapterId),
                        ("sourceId", entry.SourceId.ToString()));
                    continue;
                }

                DpiScale? scale = _dpi.Read(path);
                bool ok = _dpi.SetPercentageAndVerify(path, entry.Percent, scale?.Value);
                allRestored &= ok;
                Log.Info("DPI restored", "Restore",
                    ("step", "dpi"),
                    ("adapterId", entry.AdapterId),
                    ("sourceId", entry.SourceId.ToString()),
                    ("percent", entry.Percent.ToString()),
                    ("verified", ok.ToString()));
            }
            catch (Exception ex)
            {
                allRestored = false;
                Log.Exception(ex, "Restore", ("step", "dpi"));
            }
        }

        return allRestored;
    }

    /// <summary>
    /// B3: captures the previous extras state (toasts enabled, current default audio
    /// endpoint) before any mutation, so restore — including crash recovery from the
    /// journal — can put each one back exactly. Never throws; an extra whose previous
    /// state cannot be read is simply absent from the result.
    /// </summary>
    private JournalExtras? CaptureExtras()
    {
        bool? toasts = null;
        string? previousAudio = null;

        if (_doNotDisturb && _dnd is not null)
        {
            try
            {
                toasts = _dnd.SnapshotToastsEnabled();
            }
            catch (Exception ex)
            {
                Log.Warn("Extras capture: toasts snapshot failed — skipping the DND extra", "Fix",
                    ("step", "extrasCapture"), ("extra", "dnd"), ("error", ex.Message));
            }
        }

        if (_audioToHdmi && _audio is not null)
        {
            try
            {
                previousAudio = _audio.GetDefaultRenderEndpoint();
            }
            catch (Exception ex)
            {
                Log.Warn("Extras capture: default audio endpoint read failed — skipping the audio extra", "Fix",
                    ("step", "extrasCapture"), ("extra", "audio"), ("error", ex.Message));
            }
        }

        return toasts is null && previousAudio is null ? null : new JournalExtras(toasts, previousAudio);
    }

    /// <summary>
    /// B3: engages the presentation extras (keep-awake hold, toasts off, audio on HDMI).
    /// Every extra is policy-gated and individually failure-isolated: a failure is logged
    /// and never fails the session.
    /// </summary>
    private void ApplyExtras()
    {
        if (_keepAwake && _keepAwakePort is not null)
        {
            try
            {
                _keepAwakePort.Hold();
                Log.Info("Extra applied: keep-awake hold engaged", "Fix",
                    ("step", "extras"), ("extra", "keepAwake"));
            }
            catch (Exception ex)
            {
                Log.Warn("Extra failed: keep-awake hold — continuing", "Fix",
                    ("step", "extras"), ("extra", "keepAwake"), ("error", ex.Message));
            }
        }

        if (_doNotDisturb && _dnd is not null)
        {
            try
            {
                if (_extras?.ToastsWereEnabled is true)
                {
                    bool ok = _dnd.SetToastsEnabled(false);
                    Log.Info("Extra applied: toast notifications silenced", "Fix",
                        ("step", "extras"), ("extra", "dnd"), ("verified", ok.ToString()));
                }
                else
                {
                    Log.Info("Extra skipped: toasts already off or unknown — nothing to silence", "Fix",
                        ("step", "extras"), ("extra", "dnd"),
                        ("toastsWereEnabled", _extras?.ToastsWereEnabled?.ToString() ?? "unknown"));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Extra failed: DND — continuing", "Fix",
                    ("step", "extras"), ("extra", "dnd"), ("error", ex.Message));
            }
        }

        if (_audioToHdmi && _audio is not null)
        {
            try
            {
                string? switched = _audio.SwitchToHdmi();
                Log.Info(switched is null
                        ? "Extra skipped: no HDMI/projector audio endpoint found"
                        : "Extra applied: default audio switched to the HDMI endpoint",
                    "Fix",
                    ("step", "extras"), ("extra", "audio"),
                    ("endpoint", switched ?? "none"),
                    ("previous", _extras?.PreviousAudioEndpoint ?? "unknown"));
            }
            catch (Exception ex)
            {
                Log.Warn("Extra failed: audio switch — continuing", "Fix",
                    ("step", "extras"), ("extra", "audio"), ("error", ex.Message));
            }
        }
    }

    /// <summary>
    /// B3: undoes the presentation extras using the captured previous state. Best-effort:
    /// each extra is individually failure-isolated and never fails the restore.
    /// </summary>
    private void RestoreExtras()
    {
        if (_keepAwakePort is not null)
        {
            try
            {
                _keepAwakePort.Release();
                Log.Info("Extra restored: keep-awake hold released", "Restore",
                    ("step", "extras"), ("extra", "keepAwake"));
            }
            catch (Exception ex)
            {
                Log.Warn("Extra restore failed: keep-awake release — continuing", "Restore",
                    ("step", "extras"), ("extra", "keepAwake"), ("error", ex.Message));
            }
        }

        if (_dnd is not null && _extras?.ToastsWereEnabled is bool wasEnabled)
        {
            try
            {
                bool ok = _dnd.SetToastsEnabled(wasEnabled);
                Log.Info("Extra restored: toast notifications setting restored", "Restore",
                    ("step", "extras"), ("extra", "dnd"),
                    ("enabled", wasEnabled.ToString()), ("verified", ok.ToString()));
            }
            catch (Exception ex)
            {
                Log.Warn("Extra restore failed: DND — continuing", "Restore",
                    ("step", "extras"), ("extra", "dnd"), ("error", ex.Message));
            }
        }

        if (_audio is not null && _extras?.PreviousAudioEndpoint is string previous)
        {
            try
            {
                bool ok = _audio.Restore(previous);
                Log.Info("Extra restored: default audio endpoint restored", "Restore",
                    ("step", "extras"), ("extra", "audio"),
                    ("endpoint", previous), ("stillExists", ok.ToString()));
            }
            catch (Exception ex)
            {
                Log.Warn("Extra restore failed: audio — continuing", "Restore",
                    ("step", "extras"), ("extra", "audio"), ("error", ex.Message));
            }
        }
    }

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

    private Task RunExclusiveAsync(Func<Task> body)
    {
        lock (_operationLock)
        {
            // Assign the tail before any task runs so public operations execute in invocation
            // order (Apply then Restore cannot become Restore-then-no-op). Task.Run provides
            // the hard UI/thread-pool boundary; the previous-task await provides FIFO order.
            Task previous = _operationTail;
            Task task = Task.Run(() => RunExclusiveAfterAsync(previous, body));
            _operationTail = task;
            return task;
        }
    }

    private async Task RunExclusiveAfterAsync(Task previous, Func<Task> body)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A failed earlier command must not prevent later Undo/recovery commands.
            Log.Exception(ex, "Sequence", ("step", "previous-operation"));
        }

        IDisposable? operationLease = null;
        if (_operationLockFactory is not null)
        {
            operationLease = _operationLockFactory();
            if (operationLease is null)
            {
                Log.Warn("Another Beam process is mutating display state — operation not run", "Sequence");
                if (_session.State == SessionState.Restoring)
                {
                    _session.MarkRestoreFailed();
                }

                return;
            }
        }

        try
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
        finally
        {
            operationLease?.Dispose();
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
        _pauseTimer?.Dispose(); // C1/C2: stop a pending timed-pause resume
        _pauseTimer = null;

        // B3: always drop the keep-awake hold when the manager goes away.
        try
        {
            _keepAwakePort?.Release();
        }
        catch
        {
            // Best-effort: the process is shutting down anyway.
        }

        // B6: the tray is gone — remove its status file so `beam status` reports no session.
        if (_statusWriter is not null)
        {
            _session.StateChanged -= OnSessionStateChanged;
            try
            {
                _statusWriter.Clear();
            }
            catch
            {
                // Best-effort.
            }
        }

        _gate.Dispose();
    }
}
