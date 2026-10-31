using Beam.Core.Stats;

namespace Beam.Core.Tests;

public class PercentileTests
{
    [Fact]
    public void Compute_Empty_ReturnsNull()
    {
        Assert.Null(Percentile.Compute(Array.Empty<double>(), 0.95));
    }

    [Theory]
    [InlineData(0.0, 10)]   // min
    [InlineData(1.0, 100)]   // max
    [InlineData(0.5, 55)]   // median
    [InlineData(0.95, 100)]  // p95 of 10 samples = 10th sample (last)
    public void Compute_KnownSamples(double p, double expected)
    {
        var samples = new double[] { 10, 20, 30, 40, 50, 55, 60, 70, 80, 90, 100 };
        var result = Percentile.Compute(samples, p);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Compute_OutOfRange_Throws()
    {
        var samples = new double[] { 1, 2, 3 };
        Assert.Throws<ArgumentOutOfRangeException>(() => Percentile.Compute(samples, -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Percentile.Compute(samples, 1.1));
    }
}

public class LatencySummaryTests
{
    [Fact]
    public void Of_Empty_ReturnsEmptySummary()
    {
        var s = LatencySummary.Of(Array.Empty<double>());
        Assert.Equal(0, s.Count);
        Assert.Null(s.MinMs);
        Assert.Equal(0, s.MaxMs);
        Assert.Null(s.P95Ms);
    }

    [Fact]
    public void Of_SingleSample_ReturnsThatValue()
    {
        var s = LatencySummary.Of(new[] { 42.0 });
        Assert.Equal(1, s.Count);
        Assert.Equal(42, s.MinMs);
        Assert.Equal(42, s.MaxMs);
        Assert.Equal(42, s.P95Ms);
    }

    [Theory]
    [InlineData(new[] { 1.0, 2.0, 3.0 }, 3.0)]   // p95 of 3 samples = 3rd = max
    [InlineData(new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0 }, 10.0)] // p95 of 10
    [InlineData(new[] { 1.0, 2.0, 3.0, 4.0, 5.0 }, 5.0)] // p95 of 5 = 5th (max)
    public void Of_P95MatchesExpected(double[] samples, double expectedP95)
    {
        var s = LatencySummary.Of(samples);
        Assert.Equal(expectedP95, s.P95Ms);
    }
}
