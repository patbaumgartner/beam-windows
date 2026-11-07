namespace Beam.Core.Display;

/// <summary>
/// The parsed result of <c>DisplayConfigGetDeviceInfo(-3)</c>: raw relative steps plus
/// the resolved percentages.
/// </summary>
/// <remarks>
/// <para>
/// Windows reports DPI as three relative steps from the recommended scale. The source
/// (not the target) owns DPI, so these numbers belong to a <see cref="DisplayPath"/>'s
/// source. <see cref="Resolve"/> maps the steps to percentages using
/// <see cref="DpiScaleLadder"/>, <c>recommended = ladder[abs(minRel)]</c>.
/// </para>
/// </remarks>
public sealed class DpiScale
{
    private DpiScale(DpiScaleValue? value)
    {
        Value = value;
    }

    /// <summary>
    /// True when the OS answered the <c>-3</c> query, the struct size matched, the raw
    /// steps are consistent, and the steps map onto the ladder. Callers gate DPI control
    /// on this single flag.
    /// </summary>
    public bool IsSupported => Value is not null;

    /// <summary>The resolved value, or <see langword="null"/> when <see cref="IsSupported"/> is false.</summary>
    public DpiScaleValue? Value { get; }

    /// <summary>An "unsupported" result: the API answered, but the data does not fit the ladder.</summary>
    public static DpiScale Unsupported() => new(null);

    /// <summary>Parses the raw <c>-3</c> values. Null when they do not fit the ladder.</summary>
    public static DpiScale? From(int? minRel, int? curRel, int? maxRel)
    {
        if (minRel is null || curRel is null || maxRel is null)
        {
            return null;
        }

        int min = minRel.Value;
        int cur = curRel.Value;
        int max = maxRel.Value;

        // minScaleRel is a count of steps below the recommended scale, so it must be <= 0.
        // int.MinValue cannot be negated (two's complement), so reject it up front.
        if (min == int.MinValue || min > 0 || max < min || cur < min || cur > max)
        {
            return null;
        }

        int minStep = -min;            // ladder index of 100%
        long maxIndex = (long)minStep + max;  // ladder index of the maximum scale
        if (minStep >= DpiScaleLadder.Values.Length
            || maxIndex >= DpiScaleLadder.Values.Length)
        {
            return null;
        }

        long curIndex = (long)minStep + cur;
        if (curIndex < 0 || curIndex >= DpiScaleLadder.Values.Length)
        {
            return null;
        }

        var value = new DpiScaleValue(
            DpiScaleLadder.Values[0],
            DpiScaleLadder.Values[minStep],
            DpiScaleLadder.Values[curIndex],
            DpiScaleLadder.Values[maxIndex],
            min,
            cur,
            max);

        return new DpiScale(value);
    }
}

/// <summary>Resolved DPI percentages for one source.</summary>
public sealed record DpiScaleValue(
    int Minimum,
    int Recommended,
    int Current,
    int Maximum,
    int MinRel,
    int CurRel,
    int MaxRel)
{
    /// <summary>True when the current percentage equals the recommended percentage.</summary>
    public bool IsAtRecommended => Current == Recommended;

    /// <summary>
    /// The relative step to write via <c>DisplayConfigSetDeviceInfo(-4)</c> to reach
    /// <paramref name="targetPercent"/>. Steps are measured from the recommended scale:
    /// <c>index(target) - index(recommended)</c>. Returns null when the target or the
    /// recommended percentage is not on the ladder.
    /// </summary>
    public int? RelativeFor(int targetPercent)
    {
        int target = DpiScaleLadder.IndexOf(targetPercent);
        int recommended = DpiScaleLadder.IndexOf(Recommended);
        if (target < 0 || recommended < 0)
        {
            return null;
        }

        return target - recommended;
    }

    public override string ToString() =>
        $"current={Current}% recommended={Recommended}% range=[{Minimum}..{Maximum}] rel=({MinRel},{CurRel},{MaxRel})";
}
