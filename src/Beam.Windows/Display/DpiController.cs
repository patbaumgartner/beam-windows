using System.Runtime.InteropServices;
using Beam.Core.Display;
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
    public static bool SetPercentageAndVerify(LUID adapterId, uint sourceId, int percent, DpiScaleValue? info = null) =>
        SetPercentageAndVerifyCore(adapterId, sourceId, percent, Read, SetRelative);

    /// <summary>
    /// Testable implementation. The fresh read is authoritative; caller-supplied metadata may
    /// be stale after a mode change. Failed verification writes the original relative step back
    /// and verifies that rollback best-effort instead of leaving an unverified scale in place.
    /// </summary>
    internal static bool SetPercentageAndVerifyCore(
        LUID adapterId,
        uint sourceId,
        int percent,
        Func<LUID, uint, DpiScale?> read,
        Func<LUID, uint, int, bool> write,
        Action<TimeSpan>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        delay ??= _ => Thread.Sleep(250);

        DpiScaleValue? original = read(adapterId, sourceId)?.Value;
        if (original is null)
        {
            return false;
        }

        int target = Math.Clamp(percent, original.Minimum, original.Maximum);
        if (DpiScaleLadder.IndexOf(target) < 0)
        {
            return false;
        }

        if (target == original.Current)
        {
            return true;
        }

        int? relative = original.RelativeFor(target);
        if (relative is null || !write(adapterId, sourceId, relative.Value))
        {
            return false;
        }

        if (Verify(read, adapterId, sourceId, target, delay))
        {
            return true;
        }

        int? originalRelative = original.RelativeFor(original.Current);
        if (originalRelative is not null)
        {
            _ = write(adapterId, sourceId, originalRelative.Value);
            _ = Verify(read, adapterId, sourceId, original.Current, delay);
        }

        return false;
    }

    private static bool Verify(
        Func<LUID, uint, DpiScale?> read,
        LUID adapterId,
        uint sourceId,
        int target,
        Action<TimeSpan> delay)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            delay(TimeSpan.FromMilliseconds(250));
            DpiScale? after = read(adapterId, sourceId);
            if (after?.Value is not null && after.Value.Current == target)
            {
                return true;
            }
        }

        return false;
    }
}
