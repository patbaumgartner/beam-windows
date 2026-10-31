namespace Beam.Core.Display;

/// <summary>
/// The rule that decides whether Beam should enter presentation mode.
/// </summary>
/// <remarks>
/// A fix is only useful when an external display is part of a mirrored (duplicate)
/// topology and the mirrored desktop does not match the display's native aspect ratio
/// (the classic black-bar case). The rule lives in the domain so the tray app, the CLI,
/// and the tests all agree on exactly one definition.
/// </remarks>
public static class TriggerCondition
{
    /// <summary>
    /// True when the topology is mirrored, contains at least one external path, and the
    /// shared desktop has a different aspect ratio than a mirrored display expects.
    /// </summary>
    public static bool IsMet(DisplayTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);

        return topology.IsMirrored
            && topology.HasMirrorAspectMismatch
            && topology.ExternalPaths.Count > 0;
    }
}
