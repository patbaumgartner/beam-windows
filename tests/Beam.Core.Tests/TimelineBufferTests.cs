using Beam.Core.Diagnostics;

namespace Beam.Core.Tests;

/// <summary>C4: the diagnostics timeline ring buffer (UC-14 shows the last 200 events).</summary>
public class TimelineBufferTests
{
    private static TimelineEvent Ev(int i) => new(
        new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero).AddSeconds(i),
        LogLevel.Info, "Test", $"event {i}", Array.Empty<KeyValuePair<string, object?>>());

    [Fact]
    public void Add_ThenSnapshot_ReturnsOldestFirst()
    {
        var buffer = new TimelineBuffer(capacity: 5);
        buffer.Add(Ev(0));
        buffer.Add(Ev(1));
        buffer.Add(Ev(2));

        Assert.Equal(3, buffer.Count);
        Assert.Equal(new[] { 0, 1, 2 }, buffer.Snapshot().Select(e => e.Message).ToArray()
            .Select(m => int.Parse(m.Split(' ')[1])));
    }

    [Fact]
    public void WhenFull_OldestIsEvicted()
    {
        var buffer = new TimelineBuffer(capacity: 3);
        for (int i = 0; i < 10; i++)
        {
            buffer.Add(Ev(i));
        }

        Assert.Equal(3, buffer.Count);
        Assert.Equal(3, buffer.Capacity);
        Assert.Equal(new[] { "event 7", "event 8", "event 9" },
            buffer.Snapshot().Select(e => e.Message).ToArray());
    }

    [Fact]
    public void CapacityOne_AcceptsAndKeepsLatestEntry()
    {
        var buffer = new TimelineBuffer(capacity: 1);

        buffer.Add(Ev(0));
        buffer.Add(Ev(1));

        Assert.Equal(1, buffer.Count);
        Assert.Equal("event 1", buffer[0].Message);
    }

    [Fact]
    public void Clear_ThenAdd_StartsFreshAtBeginning()
    {
        var buffer = new TimelineBuffer(capacity: 3);
        buffer.Add(Ev(0));
        buffer.Add(Ev(1));
        buffer.Clear();

        buffer.Add(Ev(2));
        buffer.Add(Ev(3));

        Assert.Equal(new[] { "event 2", "event 3" }, buffer.Snapshot().Select(e => e.Message));
    }

    [Fact]
    public void DefaultCapacity_Is200()
    {
        Assert.Equal(200, TimelineBuffer.DefaultCapacity);
        Assert.Equal(200, new TimelineBuffer().Capacity);
    }

    [Fact]
    public void Clear_EmptiesTheBuffer()
    {
        var buffer = new TimelineBuffer(10);
        buffer.Add(Ev(0));
        buffer.Add(Ev(1));
        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.Snapshot());
    }

    [Fact]
    public void Indexer_IsOldestFirst_AndRangeChecked()
    {
        var buffer = new TimelineBuffer(10);
        for (int i = 0; i < 4; i++)
        {
            buffer.Add(Ev(i));
        }

        Assert.Equal("event 0", buffer[0].Message);
        Assert.Equal("event 3", buffer[3].Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[4]);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[-1]);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimelineBuffer(0));
    }

    [Fact]
    public void ToLine_FormatsTimestampLevelCategoryAndData()
    {
        var ev = new TimelineEvent(
            new DateTimeOffset(2026, 11, 14, 10, 1, 2, 345, TimeSpan.Zero),
            LogLevel.Warn, "Monitor", "hello",
            new[] { new KeyValuePair<string, object?>("k", "v") });

        Assert.Equal("10:01:02.345 [WARN] [Monitor] hello k=v", ev.ToLine());
    }

    [Fact]
    public void TimelineSink_RecordsAndForwards()
    {
        var inner = new RecordingSink();
        var sink = new TimelineSink(inner);

        sink.Write(LogLevel.Info, "m1", "Cat", new[] { new KeyValuePair<string, object?>("a", 1) });
        sink.WriteException(new InvalidOperationException("boom"), "Cat", Array.Empty<KeyValuePair<string, object?>>());

        Assert.Equal(2, sink.Buffer.Count);
        Assert.Equal(2, inner.Entries.Count);
        Assert.Equal("m1", sink.Buffer.Snapshot()[0].Message);
        Assert.Contains("EXCEPTION InvalidOperationException: boom", sink.Buffer.Snapshot()[1].Message);
        Assert.Equal(LogLevel.Error, sink.Buffer.Snapshot()[1].Level);
        Assert.Equal("boom-part", inner.Entries[1].Contains("boom") ? "boom-part" : "");
    }

    [Fact]
    public void TimelineSink_UsesProvidedBuffer()
    {
        var buffer = new TimelineBuffer(7);
        var sink = new TimelineSink(new RecordingSink(), buffer);
        sink.Write(LogLevel.Info, "x", null, Array.Empty<KeyValuePair<string, object?>>());

        Assert.Same(buffer, sink.Buffer);
        Assert.Equal(1, buffer.Count);
    }

    private sealed class RecordingSink : ILogSink
    {
        public List<string> Entries { get; } = new();

        public void Write(LogLevel level, string message, string? category, IReadOnlyList<KeyValuePair<string, object?>> data)
            => Entries.Add(message);

        public void WriteException(Exception exception, string? category, IReadOnlyList<KeyValuePair<string, object?>> data)
            => Entries.Add(exception.Message);
    }
}
