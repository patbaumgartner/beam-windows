using Beam.Core.Display;
using Beam.Core.Ports;
using static Beam.Windows.Display.DisplayNative;

namespace Beam.Windows.Display;

/// <summary>
/// Windows adapter for <see cref="IDpiController"/>. Parses the adapter LUID out of a
/// <see cref="DisplayPath"/> and delegates to the static <see cref="DpiController"/>.
/// </summary>
public sealed class WindowsDpiController : IDpiController
{
    public DpiScale? Read(DisplayPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return LUID.TryParse(path.AdapterId, out LUID adapter)
            ? DpiController.Read(adapter, path.SourceId)
            : null;
    }

    public bool SetPercentageAndVerify(DisplayPath path, int percent, DpiScaleValue? current = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        return LUID.TryParse(path.AdapterId, out LUID adapter)
            && DpiController.SetPercentageAndVerify(adapter, path.SourceId, percent, current);
    }
}
