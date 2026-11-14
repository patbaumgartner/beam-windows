namespace Beam.Core.Settings;

/// <summary>
/// Versioned user settings persisted at <c>%LOCALAPPDATA%\Beam\settings.json</c> (B4/V5).
/// Pure data + port: this file lives in Core, which stays free of any platform API.
///
/// <see cref="Version"/> is the settings schema version. Readers ignore files claiming a
/// newer version (without clobbering them); corrupt files are set aside as
/// <c>settings.json.bad</c> and defaults are used.
/// </summary>
/// <param name="Version">Schema version; see <see cref="CurrentVersion"/>.</param>
/// <param name="IsEnabled">Master switch (UC-15). While false, no automatic fixes run.</param>
/// <param name="UseMode">Policy: control the display mode in the fix sequence.</param>
/// <param name="UseDpi">Policy: control the DPI scale in the fix sequence.</param>
/// <param name="UseNightLight">Policy: turn Night Light off in the fix sequence.</param>
/// <param name="KeepAwake">B3 extra: hold the display awake while presenting (default on).</param>
/// <param name="DoNotDisturb">B3 extra: silence toast notifications while presenting (default on).</param>
/// <param name="AudioToHdmi">B3 extra: switch default audio to the HDMI/projector output (default on).</param>
/// <param name="IgnoredDisplays">B5: <see cref="Beam.Core.Display.DisplayTarget.Identity"/> values excluded from the trigger.</param>
/// <param name="Hotkey">B7: global hotkey combo string, e.g. "Win+Alt+P".</param>
/// <param name="TimedPauseMinutes">C1/C2: preferred pause duration in minutes (15/60), or null for indefinite.</param>
/// <param name="RestoreOnDisconnect">C1: restore the user's display settings when the projector disconnects (default on).</param>
/// <param name="DpiTargetPercent">C1: fixed DPI target for the fix (0 = automatic via <c>ScalePlanner</c>).</param>
/// <param name="PreferNativeMode">C1/B2: rank the EDID-native 16:9 mode first (default on).</param>
/// <param name="KnownDevices">C1: displays seen before, for the Devices table (auto-apply / ignore / forget).</param>
/// <param name="FirstRunToastShown">C3: whether the first-run toast was already shown (shown once per install).</param>
public sealed record BeamSettings(
    int Version,
    bool IsEnabled,
    bool UseMode,
    bool UseDpi,
    bool UseNightLight,
    bool KeepAwake,
    bool DoNotDisturb,
    bool AudioToHdmi,
    IReadOnlyList<string> IgnoredDisplays,
    string Hotkey,
    int? TimedPauseMinutes = null,
    bool RestoreOnDisconnect = true,
    int DpiTargetPercent = 0,
    bool PreferNativeMode = true,
    IReadOnlyList<DeviceProfile>? KnownDevices = null,
    bool FirstRunToastShown = false)
{
    /// <summary>
    /// Schema version written by this build. Bump on any breaking change. C1 fields are
    /// optional with defaults, so they round-trip through v1 files: readers ignore unknown
    /// keys and the store preserves unknown keys when writing (forward/backward compatible).
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>The defaults used on first run, when no settings file exists, or when it is unusable.</summary>
    public static BeamSettings Defaults { get; } = new(
        Version: CurrentVersion,
        IsEnabled: true,
        UseMode: true,
        UseDpi: true,
        UseNightLight: true,
        KeepAwake: true,
        DoNotDisturb: true,
        AudioToHdmi: true,
        IgnoredDisplays: Array.Empty<string>(),
        Hotkey: "Win+Alt+P",
        TimedPauseMinutes: null,
        RestoreOnDisconnect: true,
        DpiTargetPercent: 0,
        PreferNativeMode: true,
        KnownDevices: Array.Empty<DeviceProfile>(),
        FirstRunToastShown: false);
}

/// <summary>
/// Port for loading/saving <see cref="BeamSettings"/>. The application layer depends on
/// this interface; the Windows-independent implementation lives in Beam.App, tests use a fake.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Loads the settings. Never throws: a missing file returns <see cref="BeamSettings.Defaults"/>;
    /// a corrupt file is set aside (see <see cref="CorruptFileSetAside"/>) and defaults are returned;
    /// a file written by a newer version returns defaults without touching the file.
    /// </summary>
    BeamSettings Load();

    /// <summary>
    /// Atomically persists the settings, preserving any unknown keys already in the file
    /// (so newer Beam versions do not lose data when an older build writes the file).
    /// </summary>
    void Save(BeamSettings settings);

    /// <summary>
    /// Raised with the path of the set-aside file when the on-disk settings were corrupt
    /// and renamed to <c>*.bad</c>, so the UI can surface one toast (B4 exit criterion).
    /// </summary>
    event Action<string>? CorruptFileSetAside;
}
