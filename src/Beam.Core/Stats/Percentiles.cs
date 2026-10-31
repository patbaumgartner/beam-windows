namespace Beam.Core.Stats;

/// <summary>Percentile computation over a sample of measured durations.</summary>
public static class Percentile
{
    /// <summary>
    /// Computes the <paramref name="p"/>th percentile (0..1) of <paramref name="values"/>.
    /// Returns null when the sample is empty. Uses nearest-rank.
    /// </summary>
    public static double? Compute(IReadOnlyCollection<double> values, double p)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        if (values.Count == 0)
        {
            return null;
        }

        if (p < 0 || p > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(p), "Percentile must be in [0,1].");
        }

        var sorted = values.OrderBy(x => x).ToArray();
        int rank = (int)Math.Ceiling(p * sorted.Length);   // nearest-rank
        return sorted[Math.Max(0, Math.Min(rank, sorted.Length) - 1)];
    }
}

/// <summary>A summary of detection-latency samples.</summary>
public sealed record LatencySummary(int Count, double? MinMs, double MaxMs, double? P95Ms)
{
    public static LatencySummary Of(IEnumerable<double> samplesMs)
    {
        var arr = samplesMs.ToArray();
        if (arr.Length == 0)
        {
            return new LatencySummary(0, null, 0, null);
        }

        return new LatencySummary(
            arr.Length,
            arr.Min(),
            arr.Max(),
            Percentile.Compute(arr, 0.95));
    }
}
