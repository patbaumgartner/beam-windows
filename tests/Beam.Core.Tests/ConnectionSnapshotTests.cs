using Beam.Core.Display;

namespace Beam.Core.Tests;

public class ConnectionSnapshotTests
{
    [Fact]
    public void DisplaySnapshot_AspectRatio_Computes()
    {
        var d = new DisplaySnapshot(default, 1920, 1080, 100, "Test");
        Assert.Equal(16.0 / 9.0, d.AspectRatio(), 3);
    }

    [Theory]
    [InlineData(1920, 1080, true)]   // 16:9 exactly
    [InlineData(2560, 1440, true)]   // 16:9
    [InlineData(3840, 2160, true)]   // 16:9
    [InlineData(3840, 2400, false)]  // 16:10 — wider but NOT >= 16:9
    [InlineData(1280, 1024, false)]  // 5:4
    public void DisplaySnapshot_HasWiderOrEqualAspect(uint w, uint h, bool expected)
    {
        var d = new DisplaySnapshot(default, w, h, 100, null);
        Assert.Equal(expected, d.HasWiderOrEqualAspect());
    }

    [Fact]
    public void ConnectionSnapshot_SameDisplayIds_Empty_BothEmpty()
    {
        var a = new ConnectionSnapshot(Array.Empty<DisplaySnapshot>(), DateTimeOffset.UtcNow);
        var b = new ConnectionSnapshot(Array.Empty<DisplaySnapshot>(), DateTimeOffset.UtcNow);
        Assert.True(a.SameDisplayIds(b));
    }

    [Fact]
    public void ConnectionSnapshot_SameDisplayIds_DifferentCount()
    {
        var a = new ConnectionSnapshot(
            new[] { new DisplaySnapshot(default, 1920, 1080, 100, "A") }, DateTimeOffset.UtcNow);
        var b = new ConnectionSnapshot(
            new[] { new DisplaySnapshot(default, 1920, 1080, 100, "A"),
                    new DisplaySnapshot(default, 1920, 1080, 100, "B") }, DateTimeOffset.UtcNow);
        Assert.False(a.SameDisplayIds(b));
    }

    [Fact]
    public void ConnectionSnapshot_SameDisplayIds_SameIdsDifferentOrder()
    {
        var id1 = new DisplayId("0:1", 0);
        var id2 = new DisplayId("0:2", 0);
        var a = new ConnectionSnapshot(
            new[] { new DisplaySnapshot(id1, 1920, 1080, 100, "A"),
                    new DisplaySnapshot(id2, 1920, 1080, 100, "B") },
            DateTimeOffset.UtcNow);
        var b = new ConnectionSnapshot(
            new[] { new DisplaySnapshot(id2, 1920, 1080, 100, "B"),
                    new DisplaySnapshot(id1, 1920, 1080, 100, "A") },
            DateTimeOffset.UtcNow);
        Assert.True(a.SameDisplayIds(b));
    }

    [Fact]
    public void ConnectionSnapshot_SameDisplayIds_NullOther()
    {
        var a = new ConnectionSnapshot(Array.Empty<DisplaySnapshot>(), DateTimeOffset.UtcNow);
        ConnectionSnapshot? other = null;
        Assert.False(a.SameDisplayIds(other));
    }

    [Fact]
    public void ConnectionSnapshot_AgeSince_Computes()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var snapshot = new ConnectionSnapshot(Array.Empty<DisplaySnapshot>(), start);
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 3, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(3), snapshot.AgeSince(now));
    }
}
