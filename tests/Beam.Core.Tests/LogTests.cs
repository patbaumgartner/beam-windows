using Beam.Core.Diagnostics;

namespace Beam.Core.Tests;

/// <summary>
/// The ambient <see cref="Log"/> façade must route entries to the installed sink
/// unchanged — level, message, category, and structured data.
/// </summary>
public class LogTests
{
    [Fact]
    public void Info_PassesEverythingThroughUnchanged()
    {
        var sink = new RecordingSink();
        try
        {
            Log.SetSink(sink);

            Log.Info("hello", "Cat", ("count", 3), ("name", "beam"));

            Entry entry = Assert.Single(sink.Entries, e => e.Message == "hello");
            Assert.Equal(LogLevel.Info, entry.Level);
            Assert.Equal("Cat", entry.Category);
            Assert.Equal(new[] { "count", "name" }, entry.Data.Select(d => d.Key).ToArray());
            Assert.Equal(3, entry.Data[0].Value);
            Assert.Equal("beam", entry.Data[1].Value);
        }
        finally
        {
            Log.SetSink(NoopSink.Instance);
        }
    }

    [Fact]
    public void WarnAndError_PassTheirLevelThrough()
    {
        var sink = new RecordingSink();
        try
        {
            Log.SetSink(sink);

            Log.Warn("careful");
            Log.Error("broken");

            Assert.Equal(LogLevel.Warn, Assert.Single(sink.Entries, e => e.Message == "careful").Level);
            Assert.Equal(LogLevel.Error, Assert.Single(sink.Entries, e => e.Message == "broken").Level);
        }
        finally
        {
            Log.SetSink(NoopSink.Instance);
        }
    }

    [Fact]
    public void WithoutData_TheSinkGetsAnEmptyList()
    {
        var sink = new RecordingSink();
        try
        {
            Log.SetSink(sink);

            Log.Info("no data");

            Assert.Empty(Assert.Single(sink.Entries, e => e.Message == "no data").Data);
        }
        finally
        {
            Log.SetSink(NoopSink.Instance);
        }
    }

    [Fact]
    public void Exception_RoutesToWriteException_WithDataUnchanged()
    {
        var sink = new RecordingSink();
        try
        {
            Log.SetSink(sink);

            Log.Exception(new InvalidOperationException("bad"), "Cat", ("step", "dpi"));

            ExceptionEntry entry = Assert.Single(sink.Exceptions);
            Assert.IsType<InvalidOperationException>(entry.Exception);
            Assert.Equal("Cat", entry.Category);
            Assert.Equal(new[] { "step" }, entry.Data.Select(d => d.Key).ToArray());
            Assert.Equal("dpi", entry.Data[0].Value);
        }
        finally
        {
            Log.SetSink(NoopSink.Instance);
        }
    }

    [Fact]
    public void SetSink_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Log.SetSink(null!));
    }

    private sealed record Entry(LogLevel Level, string Message, string? Category, IReadOnlyList<KeyValuePair<string, object?>> Data);

    private sealed record ExceptionEntry(Exception Exception, string? Category, IReadOnlyList<KeyValuePair<string, object?>> Data);

    private sealed class RecordingSink : ILogSink
    {
        public List<Entry> Entries { get; } = new();
        public List<ExceptionEntry> Exceptions { get; } = new();

        public void Write(LogLevel level, string message, string? category, IReadOnlyList<KeyValuePair<string, object?>> data) =>
            Entries.Add(new Entry(level, message, category, data));

        public void WriteException(Exception exception, string? category, IReadOnlyList<KeyValuePair<string, object?>> data) =>
            Exceptions.Add(new ExceptionEntry(exception, category, data));
    }

    private sealed class NoopSink : ILogSink
    {
        public static readonly NoopSink Instance = new();

        public void Write(LogLevel level, string message, string? category, IReadOnlyList<KeyValuePair<string, object?>> data)
        {
        }

        public void WriteException(Exception exception, string? category, IReadOnlyList<KeyValuePair<string, object?>> data)
        {
        }
    }
}
