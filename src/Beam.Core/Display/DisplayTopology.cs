namespace Beam.Core.Display;

/// <summary>
/// Values of <c>DISPLAYCONFIG_TOPOLOGY_ID</c>: the topology id reported by
/// <c>QueryDisplayConfig</c> when queried with <c>QDC_DATABASE_CURRENT</c>. The same
/// values are used as <c>SetDisplayConfig</c>'s <c>SDC_TOPOLOGY_*</c> flags.
/// </summary>
/// <remarks>
/// <c>DISPLAYCONFIG_TOPOLOGY_INTERNAL = 0x1</c>, <c>CLONE = 0x2</c>, <c>EXTEND = 0x4</c>,
/// <c>EXTERNAL = 0x8</c>. The gaps are intentional (each is a single bit).
/// </remarks>
public static class DisplayTopologyId
{
    public const uint Internal = 0x00000001;
    public const uint Clone = 0x00000002;
    public const uint Extend = 0x00000004;
    public const uint External = 0x00000008;

    /// <summary>True when the id is one of the known topology kinds.</summary>
    public static bool IsKnown(uint id) => id is Internal or Clone or Extend or External;

    /// <summary>A readable name for diagnostics.</summary>
    public static string Describe(uint id) => id switch
    {
        Internal => "internal",
        Clone => "clone",
        Extend => "extend",
        External => "external",
        0 => "unknown",
        _ => $"unknown({id})",
    };
}

/// <summary>A physical display attached to a target, identified by EDID.</summary>
/// <param name="FriendlyName">The name Windows shows, e.g. "DELL U2720Q".</param>
/// <param name="Manufacturer">Three-letter EDID manufacturer code, e.g. "DEL".</param>
/// <param name="ProductCode">EDID product code.</param>
/// <param name="OutputTechnology">Connector type, e.g. "HDMI" or "DisplayPort external".</param>
/// <param name="DevicePath">The monitor device interface path (stable across reconnects).</param>
/// <param name="IsInternal">True for the built-in panel.</param>
/// <param name="IsAvailable">False when the target exists in the path list but has no display.</param>
public sealed record DisplayTarget(
    string FriendlyName,
    string Manufacturer,
    ushort ProductCode,
    string OutputTechnology,
    string DevicePath,
    bool IsInternal,
    bool IsAvailable)
{
    /// <summary>A stable identity for profiles, independent of connector instance.</summary>
    public string Identity => string.IsNullOrEmpty(DevicePath)
        ? $"{Manufacturer}:{ProductCode:X4}"
        : DevicePath;
}

/// <summary>
/// One path in the display configuration: a source driving a target.
/// </summary>
/// <remarks>
/// In duplicate (clone) mode two <see cref="DisplayPath"/> entries share one
/// <see cref="SourceId"/>. The source owns the desktop size and the DPI scale; the target
/// owns the signal mode.
/// </remarks>
/// <param name="SourceGdiName">GDI device name of the source, e.g. <c>\\.\DISPLAY1</c>.</param>
/// <param name="SourceId">DisplayConfig source id.</param>
/// <param name="AdapterId">Adapter LUID as a string.</param>
/// <param name="TargetId">DisplayConfig target id.</param>
/// <param name="Target">The physical display.</param>
/// <param name="IsActive">Whether this path is currently active.</param>
/// <param name="SourceMode">Desktop mode of the shared source.</param>
/// <param name="TargetMode">Signal mode actually sent to this target.</param>
/// <param name="DesktopX">Desktop X position of the source.</param>
/// <param name="DesktopY">Desktop Y position of the source.</param>
/// <param name="Rotation">Rotation in degrees: "0", "90", "180", "270".</param>
/// <param name="DpiPercent">Current scale percentage, when it could be read.</param>
/// <param name="DpiMinRel">Relative minimum scale step, when it could be read.</param>
/// <param name="DpiCurRel">Relative current scale step, when it could be read.</param>
/// <param name="DpiMaxRel">Relative maximum scale step, when it could be read.</param>
public sealed record DisplayPath(
    string SourceGdiName,
    uint SourceId,
    string AdapterId,
    uint TargetId,
    DisplayTarget Target,
    bool IsActive,
    DisplayMode? SourceMode,
    DisplayMode? TargetMode,
    int DesktopX,
    int DesktopY,
    string Rotation,
    int? DpiPercent,
    int? DpiMinRel,
    int? DpiCurRel,
    int? DpiMaxRel)
{
    /// <summary>The size that matters for black bars in duplicate mode.</summary>
    public DisplayMode? EffectiveMode => SourceMode ?? TargetMode;
}

/// <summary>Two or more targets driven by one source (duplicate/clone mode).</summary>
public sealed record CloneGroup(
    string SourceGdiName,
    uint SourceId,
    DisplayMode? Mode,
    IReadOnlyList<DisplayPath> Paths)
{
    public bool IsClone => Paths.Count > 1;
}

/// <summary>A snapshot of the current display configuration.</summary>
/// <param name="Paths">All display paths returned by the query.</param>
/// <param name="CurrentTopologyId">
/// The canonical topology id reported by <c>QueryDisplayConfig</c>.
/// <see cref="DisplayTopologyId.Clone"/> when the OS is in duplicate mode.
/// </param>
public sealed record DisplayTopology(IReadOnlyList<DisplayPath> Paths, uint CurrentTopologyId)
{
    public static DisplayTopology Empty { get; } = new(Array.Empty<DisplayPath>(), 0);

    public IReadOnlyList<DisplayPath> ActivePaths =>
        Paths.Where(p => p.IsActive).ToList();

    /// <summary>
    /// True when the OS reports the current topology as duplicate/clone. On some graphics
    /// stacks "Duplicate" is implemented as two sources rendering the same DWM content, so
    /// this is the canonical signal — grouping by source id alone is not enough.
    /// </summary>
    /// <remarks>
    /// When <see cref="CurrentTopologyId"/> could not be read (0), falls back to the
    /// source-id grouping heuristic so a failed topology read never silently disables the
    /// presentation trigger.
    /// </remarks>
    public bool IsMirrored => CurrentTopologyId == 0
        ? ActiveCloneGroups.Any(g => g.IsClone)
        : CurrentTopologyId == DisplayTopologyId.Clone;

    /// <summary>Groups active paths by source. Groups with more than one path are clones.</summary>
    public IReadOnlyList<CloneGroup> ActiveCloneGroups =>
        ActivePaths
            .GroupBy(p => p.SourceId)
            .Select(g => new CloneGroup(
                g.First().SourceGdiName,
                g.Key,
                g.First().SourceMode,
                g.ToList()))
            .ToList();

    /// <summary>The internal laptop panel, if one is active.</summary>
    public DisplayPath? InternalPanel =>
        ActivePaths.FirstOrDefault(p => p.Target.IsInternal);

    /// <summary>Active paths that are not the internal panel.</summary>
    public IReadOnlyList<DisplayPath> ExternalPaths =>
        ActivePaths.Where(p => !p.Target.IsInternal).ToList();

    /// <summary>
    /// True when an external display is mirrored (loop-back sources share one desktop
    /// width/height) and its owned target signal differs from the shared source aspect —
    /// the condition that produces black bars.
    /// </summary>
    public bool HasMirrorAspectMismatch
    {
        get
        {
            if (!IsMirrored)
            {
                // Two separate sources, each driving one target: extend-mode "same shape"
                // coincidences must not count. The OS topology id is the only reliable
                // signal for "duplicate".
                return false;
            }

            // Group by desktop identity (source size + position), not source id: on this
            // machine Windows 11 represents "Duplicate" as two sources sharing a desktop.
            var groups = ActivePaths
                .GroupBy(p => (p.SourceMode?.Width ?? 0, p.SourceMode?.Height ?? 0, p.DesktopX, p.DesktopY))
                .Where(g => g.Count() > 1)
                .ToList();

            if (groups.Count == 0)
            {
                return false;
            }

            foreach (var group in groups)
            {
                DisplayMode? mode = group.First().SourceMode;
                if (mode is null)
                {
                    continue;
                }

                uint width = mode.Width;
                uint height = mode.Height;

                foreach (DisplayPath path in group)
                {
                    DisplayMode? targetMode = path.TargetMode;
                    if (targetMode is null)
                    {
                        continue;
                    }

                    if (AspectRatio.Mismatch(width, height, targetMode.Width, targetMode.Height))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    public override string ToString() =>
        $"{ActivePaths.Count} active path(s), {ExternalPaths.Count} external, " +
        $"{ActiveCloneGroups.Count(g => g.IsClone)} clone group(s)";
}
