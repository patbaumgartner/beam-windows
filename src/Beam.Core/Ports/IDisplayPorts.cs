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
    /// <summary>
    /// Enters clone/duplicate mode and switches the shared source to the best supported
    /// 16:9 mode.
    /// </summary>
    /// <returns><c>null</c> on success; an error message when the change was rejected.</returns>
    string? ApplyCloneWith16x9();

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
