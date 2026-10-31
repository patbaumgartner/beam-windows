namespace Beam.Core.Display;

/// <summary>
/// The DPI scale ladder Windows uses for <c>DisplayConfigGetDeviceInfo(-3)</c> /
/// <c>DisplayConfigSetDeviceInfo(-4)</c>. Values are relative steps counted from the
/// recommended scale; this ladder maps a step back to a percentage.
/// </summary>
/// <remarks>
/// Observed (and extrapolated) from the Settings app and confirmed against
/// <c>lihas/windows-DPI-scaling-sample</c> (Unlicense). The list is not a pure
/// geometric sequence (250->300, 300->350) so it must be a table, not a formula.
/// </remarks>
public static class DpiScaleLadder
{
    /// <summary>The ladder, from 100% to 500%. The index is the step relative to 100%.</summary>
    public static readonly int[] Values = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

    /// <summary>Returns the index of a percentage in the ladder, or -1 when not representable.</summary>
    public static int IndexOf(int percent)
    {
        for (int i = 0; i < Values.Length; i++)
        {
            if (Values[i] == percent)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The percentage at a ladder index, clamped to the table bounds.</summary>
    public static int At(int index) => Values[Math.Clamp(index, 0, Values.Length - 1)];
}
