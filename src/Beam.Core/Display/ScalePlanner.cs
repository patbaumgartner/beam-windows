namespace Beam.Core.Display;

/// <summary>
/// Picks the DPI percentage Beam should set for a source in duplicate mode.
/// </summary>
/// <remarks>
/// <para>
/// After the shared source is set to a 16:9 mode the old scale may still apply. At 200%
/// a 1920x1080 source becomes a 960x540 logical desktop — unusable on a wall. The
/// target rule from ADR-0013:
/// </para>
/// <code>
/// logicalW = width * 100 / scale   keep logicalW in [1280, 2560]
/// prefer 100% when logicalW would be below the range
/// </code>
/// <para>
/// In practice this means: pick the smallest scale that still yields a usable logical
/// width; for a 1080p source that is 100% (1920 logical). For a 4K source it is 200%
/// (1920 logical), the same on-screen footprint as a 1080p desktop.
/// </para>
/// </remarks>
public static class ScalePlanner
{
    /// <summary>Preferred logical desktop width in CSS/DIP pixel. 1920 is a comfortable, widely supported target.</summary>
    public const int PreferredLogicalWidth = 1920;

    /// <summary>Logical desktop width must stay within this band.</summary>
    public const int MinLogicalWidth = 1280;

    /// <summary>Logical desktop width must stay within this band.</summary>
    public const int MaxLogicalWidth = 2560;

    /// <summary>
    /// Chooses the target scale percentage for a source of the given pixel width and DPI
    /// capability. Returns the chosen percentage (already clamped to the ladder range).
    /// </summary>
    public static int ChooseScale(uint sourcePixelWidth, DpiScaleValue dpi)
    {
        ArgumentNullException.ThrowIfNull(dpi);

        // Order candidates so that we prefer the one whose resulting logical width is
        // closest to the preferred 1920, but never below the minimum logical width and
        // never beyond the display's maximum scale.
        var candidates = new List<(int Percent, int LogicalWidth)>();
        foreach (int percent in DpiScaleLadder.Values)
        {
            if (percent < dpi.Minimum || percent > dpi.Maximum)
            {
                continue;
            }

            int logical = LogicalWidth(sourcePixelWidth, percent);
            candidates.Add((percent, logical));
        }

        if (candidates.Count == 0)
        {
            return dpi.Minimum;
        }

        // Score: distance from preferred logical width, but strongly penalize logical
        // widths outside the allowed band.
        int best = dpi.Minimum;
        long bestScore = long.MaxValue;
        int bestLogical = int.MinValue;

        foreach ((int Percent, int LogicalWidth) c in candidates)
        {
            long score = Score(c.LogicalWidth);
            if (score < bestScore || (score == bestScore && c.LogicalWidth > bestLogical))
            {
                best = c.Percent;
                bestScore = score;
                bestLogical = c.LogicalWidth;
            }
        }

        return best;
    }

    /// <summary>Logical (DIP) width produced by a pixel width at a given scale percentage.</summary>
    public static int LogicalWidth(uint sourcePixelWidth, int scalePercent)
    {
        long logical = (long)sourcePixelWidth * 100 / scalePercent;
        return (int)Math.Clamp(logical, 0, int.MaxValue);
    }

    private static long Score(int logicalWidth)
    {
        if (logicalWidth < MinLogicalWidth)
        {
            // Far too small: penalise hard, smaller widths worse. Kept in long to avoid
            // the int overflow that once made a 1097px logical width score as the minimum.
            return long.MaxValue - logicalWidth;
        }

        if (logicalWidth > MaxLogicalWidth)
        {
            // Somewhat too large: penalise, larger widths worse.
            return 100_000 + logicalWidth;
        }

        return Math.Abs(logicalWidth - PreferredLogicalWidth);
    }
}
