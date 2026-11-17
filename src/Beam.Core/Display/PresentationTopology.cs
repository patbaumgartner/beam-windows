namespace Beam.Core.Display;

/// <summary>
/// What Beam does with the projector while presenting: <see cref="Mirror"/> (duplicate —
/// the same desktop on every screen, the default, matching what Win+P → Duplicate gives)
/// or <see cref="Extend"/> (the projector becomes a second screen with its own desktop).
/// Persisted in settings as <c>ProjectorTopology</c> ("Mirror" / "Extend").
/// </summary>
public enum PresentationTopology
{
    Mirror = 0,
    Extend = 1,
}

/// <summary>Serialization and display helpers for <see cref="PresentationTopology"/>.</summary>
public static class PresentationTopologyExtensions
{
    public const string MirrorSetting = "Mirror";
    public const string ExtendSetting = "Extend";

    /// <summary>The persisted settings value for a topology ("Mirror" / "Extend").</summary>
    public static string ToSettingString(this PresentationTopology topology) => topology switch
    {
        PresentationTopology.Extend => ExtendSetting,
        _ => MirrorSetting,
    };

    /// <summary>
    /// Parses a persisted setting value. Anything unknown or missing falls back to
    /// <see cref="PresentationTopology.Mirror"/> — mirroring is the default.
    /// </summary>
    public static PresentationTopology FromSettingString(string? value) =>
        string.Equals(value, ExtendSetting, StringComparison.OrdinalIgnoreCase)
            ? PresentationTopology.Extend
            : PresentationTopology.Mirror;

    /// <summary>Menu/settings label for a topology.</summary>
    public static string MenuText(this PresentationTopology topology) => topology switch
    {
        PresentationTopology.Extend => "Extend — projector as a second screen",
        _ => "Mirror (duplicate) — same image on all screens",
    };
}
