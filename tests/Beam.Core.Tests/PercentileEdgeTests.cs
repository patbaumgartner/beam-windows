using Beam.Core.Stats;

namespace Beam.Core.Tests;

/// <summary>
/// Adversarial percentile cases: NaN/Infinity samples, extreme p, single/dupe values.
/// A crash or nonsense result here poisons the latency stats shown in logs.
/// </summary>
public class PercentileEdgeTests
{
    [Fact]
    public void Compute_NaN_PropagatesOrSorts()
    {
        // NaN sorts unpredictably but must not throw.
        var ex = Record.Exception(() => Percentile.Compute([1.0, double.NaN, 3.0], 0.5));
        Assert.Null(ex);
    }

    [Fact]
    public void Compute_AllInfinity_ReturnsInfinity()
    {
        double? r = Percentile.Compute([double.PositiveInfinity, double.PositiveInfinity], 0.5);
        Assert.Equal(double.PositiveInfinity, r);
    }

    [Fact]
    public void Compute_SingleValue_ReturnsIt()
    {
        Assert.Equal(42.0, Percentile.Compute([42.0], 0.0));
        Assert.Equal(42.0, Percentile.Compute([42.0], 1.0));
    }

    [Fact]
    public void Compute_Duplicates_CountedIndividually()
    {
        double? r = Percentile.Compute([5, 5, 5, 5], 0.95);
        Assert.Equal(5, r);
    }

    [Fact]
    public void Compute_BoundaryP_ReturnsExtremes()
    {
        double? r = Percentile.Compute([1, 2, 3, 4], 0.0);
        Assert.Equal(1, r);
        double? r2 = Percentile.Compute([1, 2, 3, 4], 1.0);
        Assert.Equal(4, r2);
    }

    [Fact]
    public void Compute_NegativeValues_Work()
    {
        double? r = Percentile.Compute([-10, -5, 0], 0.5);
        Assert.Equal(-5, r);
    }

    [Fact]
    public void Compute_VeryLargeSample_NeverCrashes()
    {
        var values = new double[10_000];
        Array.Fill(values, 3.14);
        Assert.Equal(3.14, Percentile.Compute(values, 0.99));
    }

    [Fact]
    public void LatencySummary_NegativeLatencies_Preserved()
    {
        // Clock skew could produce negative durations; the summary must not crash.
        var s = LatencySummary.Of([-1.0, -2.0, -3.0]);
        Assert.Equal(3, s.Count);
        Assert.Equal(-3, s.MinMs);
        Assert.Equal(-1, s.MaxMs);
    }
}
