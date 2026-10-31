using Beam.Core.Display;
using Beam.Core.Ports;
using static Beam.Windows.Display.DisplayNative;

namespace Beam.Windows.Display;

/// <summary>Reads the current display configuration and the modes each display supports.</summary>
/// <remarks>
/// Read-only. Nothing in this class changes the system. Implements
/// <see cref="ITopologySource"/> so the application layer can be tested with a fake.
/// </remarks>
public sealed class DisplayInventory : ITopologySource
{
    /// <summary>Queries the current display paths, including inactive ones.</summary>
    public DisplayTopology Query()
    {
        return Query(includeInactive: true);
    }

    /// <summary>Queries the display paths.</summary>
    /// <param name="includeInactive">
    /// When true, inactive targets are returned too (they still have EDID identity and are
    /// useful for "what was plugged in before").
    /// </param>
    public DisplayTopology Query(bool includeInactive)
    {
        uint flags = QdcAllPaths | QdcVirtualModeAware;
        if (!includeInactive)
        {
            flags = QdcOnlyActivePaths | QdcVirtualModeAware;
        }

        for (int attempt = 0; attempt < 4; attempt++)
        {
            int error = GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);
            if (error != ErrorSuccess)
            {
                throw new InvalidOperationException(
                    $"GetDisplayConfigBufferSizes failed with Win32 error {error}.");
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            // currentTopologyId must be NULL unless QDC_DATABASE_CURRENT is set, otherwise
            // QueryDisplayConfig returns ERROR_INVALID_PARAMETER. Read it separately below.
            error = QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (error == ErrorInsufficientBuffer)
            {
                // The configuration changed between the two calls; retry.
                continue;
            }

            if (error != ErrorSuccess)
            {
                throw new InvalidOperationException($"QueryDisplayConfig failed with Win32 error {error}.");
            }

            return Build(paths, modes, pathCount, modeCount, QueryTopologyId());
        }

        throw new InvalidOperationException("The display configuration kept changing; gave up after 4 attempts.");
    }

    /// <summary>
    /// Builds a <see cref="ConnectionSnapshot"/> of the currently active displays, using
    /// the same source identity the monitor diffs on.
    /// </summary>
    public static ConnectionSnapshot Capture()
    {
        var inventory = new DisplayInventory();
        DisplayTopology topology = inventory.Query(includeInactive: false);
        var displays = topology.ActivePaths.Select(p => new DisplaySnapshot(
            new DisplayId(p.AdapterId, p.SourceId),
            p.SourceMode?.Width ?? 0,
            p.SourceMode?.Height ?? 0,
            p.DpiPercent ?? 0,
            p.Target.FriendlyName)).ToArray();
        return new ConnectionSnapshot(displays, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Reads the active topology id from the CCD database.
    /// </summary>
    /// <remarks>
    /// <c>currentTopologyId</c> is only valid with <c>QDC_DATABASE_CURRENT</c>; with
    /// <c>QDC_ALL_PATHS</c>/<c>QDC_ONLY_ACTIVE_PATHS</c> it must be NULL or the call fails
    /// with <c>ERROR_INVALID_PARAMETER</c>. Returns 0 when the topology cannot be read.
    /// </remarks>
    public static uint QueryTopologyId()
    {
        uint flags = QdcDatabaseCurrent | QdcVirtualModeAware;

        for (int attempt = 0; attempt < 4; attempt++)
        {
            int error = GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);
            if (error != ErrorSuccess)
            {
                return 0;
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            uint topologyId = 0;

            error = QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, ref topologyId);
            if (error == ErrorInsufficientBuffer)
            {
                continue;
            }

            return error == ErrorSuccess ? topologyId : 0;
        }

        return 0;
    }

    private static DisplayTopology Build(
        DISPLAYCONFIG_PATH_INFO[] paths,
        DISPLAYCONFIG_MODE_INFO[] modes,
        uint pathCount,
        uint modeCount,
        uint currentTopologyId)
    {
        var result = new List<DisplayPath>((int)pathCount);

        // With QDC_VIRTUAL_MODE_AWARE the modeInfoIdx is a packed encoding (high word
        // may be 0xFFFF for a virtual path). The correct mode for a given source or target
        // is the array entry whose infoType matches and whose id matches the source/target
        // id from the path structure. This is also the only reliable way when
        // QDC_ALL_PATHS returns more targets than the mode array is sized for.
        static DISPLAYCONFIG_MODE_INFO? FindMode(uint infoType, uint id, LUID adapterId,
            DISPLAYCONFIG_MODE_INFO[] modes, uint modeCount)
        {
            for (uint i = 0; i < modeCount; i++)
            {
                if (modes[i].infoType == (int)infoType && modes[i].id == id && modes[i].adapterId.adapterIdEquals(adapterId))
                {
                    return modes[i];
                }
            }

            return null;
        }

        for (int i = 0; i < pathCount; i++)
        {
            DISPLAYCONFIG_PATH_INFO path = paths[i];
            string sourceName = GetSourceGdiName(path.sourceInfo);
            DisplayTarget target = GetTarget(path.targetInfo);
            bool active = (path.flags & PathActive) != 0;

            // Source mode: infoType=1 (source), id = sourceInfo.id
            DISPLAYCONFIG_MODE_INFO? srcMode = FindMode(ModeInfoTypeSource, path.sourceInfo.id,
                path.sourceInfo.adapterId, modes, modeCount);
            DisplayMode? sourceMode = null;
            if (srcMode.HasValue)
            {
                DISPLAYCONFIG_SOURCE_MODE m = srcMode.Value.sourceMode;
                sourceMode = new DisplayMode(m.width, m.height, 0);
            }

            // Target mode: infoType=2 (target), id = targetInfo.id
            DISPLAYCONFIG_MODE_INFO? tgtMode = FindMode(ModeInfoTypeTarget, path.targetInfo.id,
                path.targetInfo.adapterId, modes, modeCount);
            DisplayMode? targetMode = null;
            if (tgtMode.HasValue)
            {
                DISPLAYCONFIG_VIDEO_SIGNAL_INFO signal = tgtMode.Value.targetMode.targetVideoSignalInfo;
                targetMode = new DisplayMode(
                    signal.activeSize.cx,
                    signal.activeSize.cy,
                    signal.vSyncFreq.ToDouble());
            }

            int desktopX = srcMode?.sourceMode.position.x ?? 0;
            int desktopY = srcMode?.sourceMode.position.y ?? 0;

            (int? percent, int? minRel, int? curRel, int? maxRel) =
                ReadDpi(path.sourceInfo.adapterId, path.sourceInfo.id);

            result.Add(new DisplayPath(
                sourceName,
                path.sourceInfo.id,
                path.sourceInfo.adapterId.ToString(),
                path.targetInfo.id,
                target,
                active,
                sourceMode,
                targetMode,
                desktopX,
                desktopY,
                RotationName(path.targetInfo.rotation),
                percent,
                minRel,
                curRel,
                maxRel));
        }

        return new DisplayTopology(result, currentTopologyId);
    }

    private static string GetSourceGdiName(DISPLAYCONFIG_PATH_SOURCE_INFO source)
    {
        var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DeviceInfoGetSourceName,
                size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                adapterId = source.adapterId,
                id = source.id,
            },
            viewGdiDeviceName = string.Empty,
        };

        return DisplayConfigGetDeviceInfo(ref request) == ErrorSuccess
            ? request.viewGdiDeviceName
            : string.Empty;
    }

    private static DisplayTarget GetTarget(DISPLAYCONFIG_PATH_TARGET_INFO target)
    {
        var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DeviceInfoGetTargetName,
                size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = target.adapterId,
                id = target.id,
            },
            monitorFriendlyDeviceName = string.Empty,
            monitorDevicePath = string.Empty,
        };

        if (DisplayConfigGetDeviceInfo(ref request) != ErrorSuccess)
        {
            return new DisplayTarget(
                $"target {target.id}",
                string.Empty,
                0,
                OutputTechnologyName(target.outputTechnology),
                string.Empty,
                target.outputTechnology == 18,
                target.targetAvailable != 0);
        }

        return new DisplayTarget(
            request.monitorFriendlyDeviceName,
            DecodeEdidManufacturer(request.edidManufactureId),
            request.edidProductCodeId,
            OutputTechnologyName(target.outputTechnology),
            request.monitorDevicePath,
             target.outputTechnology is 18 or OutputTechnologyInternalVirtual,
            target.targetAvailable != 0);
    }

    private static (int? Percent, int? MinRel, int? CurRel, int? MaxRel) ReadDpi(LUID adapterId, uint sourceId)
    {
        var request = new DISPLAYCONFIG_GET_DPI_SCALE
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DeviceInfoGetDpiScale,
                size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_GET_DPI_SCALE>(),
                adapterId = adapterId,
                id = sourceId,
            },
        };

        if (DisplayConfigGetDeviceInfo(ref request) != ErrorSuccess)
        {
            return (null, null, null, null);
        }

        return (null, request.minScaleRel, request.curScaleRel, request.maxScaleRel);
    }

    /// <summary>Reads the current DEVMODE for a GDI device name.</summary>
    public static DEVMODE? GetCurrentDevMode(string gdiDeviceName)
    {
        var devMode = NewDevMode();
        return EnumDisplaySettingsEx(gdiDeviceName, EnumCurrentSettings, ref devMode, 0)
            ? devMode
            : null;
    }

    /// <summary>Enumerates every mode a display reports, de-duplicated and sorted.</summary>
    public static IReadOnlyList<DisplayMode> GetAvailableModes(string gdiDeviceName)
    {
        var seen = new HashSet<(uint, uint, uint)>();
        var modes = new List<DisplayMode>();

        for (int index = 0; index < 4096; index++)
        {
            var devMode = NewDevMode();
            if (!EnumDisplaySettingsEx(gdiDeviceName, index, ref devMode, 0))
            {
                break;
            }

            uint width = devMode.dmPelsWidth;
            uint height = devMode.dmPelsHeight;
            uint refresh = devMode.dmDisplayFrequency;

            if (width == 0 || height == 0)
            {
                continue;
            }

            if (!seen.Add((width, height, refresh)))
            {
                continue;
            }

            modes.Add(new DisplayMode(width, height, refresh));
        }

        return modes
            .OrderByDescending(m => m.Pixels)
            .ThenByDescending(m => m.RefreshHz)
            .ThenByDescending(m => m.Width)
            .ToList();
    }

    /// <summary>Creates a zeroed DEVMODE with <c>dmSize</c> initialised.</summary>
    public static DEVMODE NewDevMode()
    {
        var devMode = new DEVMODE
        {
            dmDeviceName = string.Empty,
            dmFormName = string.Empty,
        };

        devMode.dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>();
        return devMode;
    }
}
