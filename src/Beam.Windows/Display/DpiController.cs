using Beam.Core.Display;
using System.Runtime.InteropServices;
using static Beam.Windows.Display.DisplayNative;

namespace Beam.Windows.Display;

/// <summary>
/// Reads and sets the DPI scale of a display <em>source</em> through the undocumented
/// <c>DisplayConfigGetDeviceInfo(-3)</c> / <c>DisplayConfigSetDeviceInfo(-4)</c> API that
/// the Settings app itself uses.
/// </summary>
/// <remarks>
/// DPI belongs to the source, never the target. In duplicate mode the shared source owns
/// the scale for every mirrored target, which is exactly what Beam needs.
/// </remarks>
public static class DpiController
{
    /// <summary>
    /// Reads the DPI scale of a source. Returns null when the OS cannot answer or the
    /// data does not fit the ladder (never guess).
    /// </summary>
    public static DpiScale? Read(LUID adapterId, uint sourceId)
    {
        var request = new DISPLAYCONFIG_GET_DPI_SCALE
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DeviceInfoGetDpiScale,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_DPI_SCALE>(),
                adapterId = adapterId,
                id = sourceId,
            },
        };

        if (request.header.size != 32)
        {
            // OS changed the struct; do not touch anything.
            return null;
        }

        if (DisplayConfigGetDeviceInfo(ref request) != ErrorSuccess)
        {
            return null;
        }

        return DpiScale.From(request.minScaleRel, request.curScaleRel, request.maxScaleRel);
    }

    /// <summary>
    /// Writes a relative DPI step for a source. Returns true when accepted.
    /// </summary>
    public static bool SetRelative(LUID adapterId, uint sourceId, int scaleRel)
    {
        var request = new DISPLAYCONFIG_SET_DPI_SCALE
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DeviceInfoSetDpiScale,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SET_DPI_SCALE>(),
                adapterId = adapterId,
                id = sourceId,
            },
            scaleRel = scaleRel,
        };

        if (request.header.size != 24)
        {
            // OS changed the struct; do not touch anything.
            return false;
        }

        return DisplayConfigSetDeviceInfo(ref request) == ErrorSuccess;
    }

    /// <summary>
    /// Sets a source to a percentage on the ladder, clamped to the supported range.
    /// Verifies by re-reading.
    /// </summary>
    public static bool SetPercentageAndVerify(LUID adapterId, uint sourceId, int percent, DpiScaleValue? info = null)
    {
        DpiScale? scale = Read(adapterId, sourceId);
        DpiScaleValue? value = scale?.Value;
        if (value is null)
        {
            return false;
        }

        // info may be null; flow analysis needs the intermediate local above (value) so
        // CS8604 does not fire on the ??= below.
        info ??= value;

        // Clamp to the display's supported range, then to the ladder.
        int target = Math.Clamp(percent, info.Minimum, info.Maximum);
        if (DpiScaleLadder.IndexOf(target) < 0)
        {
            return false;
        }

        int? relative = info.RelativeFor(target);
        if (relative is null)
        {
            return false;
        }

        if (!SetRelative(adapterId, sourceId, relative.Value))
        {
            return false;
        }

        // Verify by re-read; give the driver a beat to apply.
        DpiScale? after = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Thread.Sleep(250);
            after = Read(adapterId, sourceId);
            if (after?.Value is not null && after.Value.Current == target)
            {
                return true;
            }
        }

        return after?.Value?.Current == target;
    }
}
