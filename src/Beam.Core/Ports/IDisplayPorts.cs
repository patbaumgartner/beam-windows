using Beam.Core.Display;

namespace Beam.Core.Ports;

/// <summary>
/// Reads the current display topology. Implemented by the Windows adapter; replaced by a
/// fake in tests.
/// </summary>
public interface ITopologySource
{
    /// <summary>Queries the current display paths, including inactive ones.</summary>
    DisplayTopology Query();
}

/// <summary>
/// Applies and restores the presentation display mode. The implementation owns the
/// platform calls; the application layer only sequences it.
/// </summary>
public interface IDisplayModeController
{
    /// <summary>Captures restorable source modes before the recovery journal is written.</summary>
    void Prepare()
    {
    }

    /// <summary>Discards prepared state after an apply aborts before any mutation.</summary>
    void Abort()
    {
    }

    /// <summary>Original modes captured by <see cref="Prepare"/>, when supported.</summary>
    IReadOnlyList<DisplayModeSnapshot> OriginalModes => Array.Empty<DisplayModeSnapshot>();

    /// <summary>True after the controller actually changed topology or a source mode.</summary>
    bool HasModeChanged => true;

    /// <summary>Verified modes applied by this controller, when supported.</summary>
    IReadOnlyList<DisplayModeSnapshot> AppliedModes => Array.Empty<DisplayModeSnapshot>();

    /// <summary>Rehydrates original modes from another process's recovery journal.</summary>
    string? Restore(IReadOnlyList<DisplayModeSnapshot> snapshots) => Restore();

    /// <summary>Rehydrates original and applied modes so restore can preserve user drift.</summary>
    string? Restore(
        IReadOnlyList<DisplayModeSnapshot> snapshots,
        IReadOnlyList<DisplayModeSnapshot> appliedModes) => Restore(snapshots);

    /// <summary>
    /// Enters clone/duplicate mode and switches the shared source to the best supported
    /// 16:9 mode.
    /// </summary>
    /// <param name="preferNative">
    /// C1/B2: when true, the projector's EDID-native 16:9 mode is ranked first; when false,
    /// ranking falls back to highest pixels, then 60 Hz over 30 Hz.
    /// </param>
    /// <returns><c>null</c> on success; an error message when the change was rejected.</returns>
    string? ApplyCloneWith16x9(bool preferNative = true);

    /// <summary>
    /// Enters extend mode (the projector becomes a second screen with its own source)
    /// and switches that external source to the best supported 16:9 mode, so the
    /// projector shows a clean 16:9 desktop of its own.
    /// </summary>
    /// <param name="preferNative">
    /// C1/B2: when true, the projector's EDID-native 16:9 mode is ranked first; when false,
    /// ranking falls back to highest pixels, then 60 Hz over 30 Hz.
    /// </param>
    /// <returns><c>null</c> on success; an error message when the change was rejected.</returns>
    string? ApplyExtendWith16x9(bool preferNative = true);

    /// <summary>Restores the original topology and captured source modes.</summary>
    /// <returns><c>null</c> on success; an error message otherwise.</returns>
    string? Restore();
}

/// <summary>
/// Reads and writes the DPI scale of a display source.
/// </summary>
public interface IDpiController
{
    /// <summary>Reads the scale for a path's source, or <c>null</c> when unavailable.</summary>
    DpiScale? Read(DisplayPath path);

    /// <summary>Writes a percentage (clamped to the supported range) and verifies it.</summary>
    bool SetPercentageAndVerify(DisplayPath path, int percent, DpiScaleValue? current = null);
}
