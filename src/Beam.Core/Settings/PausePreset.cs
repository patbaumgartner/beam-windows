namespace Beam.Core.Settings;

/// <summary>
/// C1/C2: the pause durations offered in the tray menu and the settings window.
/// <see cref="Indefinite"/> pauses until the user resumes; the others auto-resume.
/// </summary>
public enum PausePreset
{
    FifteenMinutes,
    OneHour,
    Indefinite,
}

/// <summary>
/// C1/C2: parsing and display of <see cref="PausePreset"/> — pure logic, unit tested.
/// Stored in settings as <see cref="BeamSettings.TimedPauseMinutes"/> (null = indefinite).
/// </summary>
public static class PausePresetExtensions
{
    /// <summary>The preset's duration, or null for <see cref="PausePreset.Indefinite"/>.</summary>
    public static TimeSpan? Duration(this PausePreset preset) => preset switch
    {
        PausePreset.FifteenMinutes => TimeSpan.FromMinutes(15),
        PausePreset.OneHour => TimeSpan.FromHours(1),
        _ => null,
    };

    /// <summary>Menu label, e.g. "Pause for 15 min" / "Pause until I resume".</summary>
    public static string MenuText(this PausePreset preset) => preset switch
    {
        PausePreset.FifteenMinutes => "Pause for 15 min",
        PausePreset.OneHour => "Pause for 1 h",
        _ => "Pause until I resume",
    };

    /// <summary>Maps a persisted minutes value back to a preset (null → indefinite).</summary>
    public static PausePreset FromMinutes(int? minutes) => minutes switch
    {
        15 => PausePreset.FifteenMinutes,
        60 => PausePreset.OneHour,
        _ => PausePreset.Indefinite,
    };

    /// <summary>The persisted minutes value for a preset (null = indefinite).</summary>
    public static int? ToMinutes(this PausePreset preset) =>
        preset.Duration() is { } d ? (int)d.TotalMinutes : null;
}
