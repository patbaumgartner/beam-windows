using System.Text.Json;
using Beam.App.Diagnostics;
using Beam.Core.Diagnostics;

namespace Beam.App.Tests;

/// <summary>
/// The production log sink is diagnostic infrastructure, but a broken logger is a real
/// support problem: if the JSONL file cannot be parsed, bug reports are useless. These
/// tests pin the file names, both formats, and the no-throw guarantee.
/// </summary>
public class FileLogSinkTests : IDisposable
{
    private readonly string _dir;

    public FileLogSinkTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "beam-log-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Best effort; a leftover temp directory must not fail the suite.
        }
    }

    private static KeyValuePair<string, object?> Pair(string key, object? value) => new(key, value);

    [Fact]
    public void Paths_AreInsideTheGivenDirectory()
    {
        var sink = new FileLogSink(_dir);

        Assert.Equal(Path.Combine(_dir, "beam.log"), sink.LogPath);
        Assert.Equal(Path.Combine(_dir, "beam-detailed.jsonl"), sink.DetailedLogPath);
    }

    [Fact]
    public void Constructor_CreatesMissingDirectory()
    {
        string nested = Path.Combine(_dir, "a", "b");

        var sink = new FileLogSink(nested);

        Assert.True(Directory.Exists(nested));
        Assert.StartsWith(nested, sink.LogPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ProducesReadableLineWithCategoryAndData()
    {
        var sink = new FileLogSink(_dir);

        sink.Write(LogLevel.Info, "hello world", "Test", new[] { Pair("count", 3) });

        string text = File.ReadAllText(sink.LogPath);
        Assert.Contains("[INFO]", text);
        Assert.Contains("[Test]", text);
        Assert.Contains("hello world", text);
        Assert.Contains("count=3", text);
    }

    [Fact]
    public void Write_WithoutCategory_UsesApp()
    {
        var sink = new FileLogSink(_dir);

        sink.Write(LogLevel.Warn, "no category", null, Array.Empty<KeyValuePair<string, object?>>());

        Assert.Contains("[WARN] [App]", File.ReadAllText(sink.LogPath));
    }

    [Fact]
    public void Write_StringWithSpaces_IsQuoted()
    {
        var sink = new FileLogSink(_dir);

        sink.Write(LogLevel.Info, "quoted", "Test", new[] { Pair("name", "Dell U2720Q") });

        Assert.Contains("name=\"Dell U2720Q\"", File.ReadAllText(sink.LogPath));
    }

    [Fact]
    public void Write_NullValue_IsRendered()
    {
        var sink = new FileLogSink(_dir);

        sink.Write(LogLevel.Info, "nulls", "Test", new[] { Pair("missing", null) });

        Assert.Contains("missing=null", File.ReadAllText(sink.LogPath));
    }

    [Fact]
    public void Write_AppendsRatherThanTruncates()
    {
        var sink = new FileLogSink(_dir);

        sink.Write(LogLevel.Info, "first", "Test", Array.Empty<KeyValuePair<string, object?>>());
        sink.Write(LogLevel.Info, "second", "Test", Array.Empty<KeyValuePair<string, object?>>());

        string text = File.ReadAllText(sink.LogPath);
        Assert.Contains("first", text);
        Assert.Contains("second", text);
    }

    [Fact]
    public void Write_DetailedFile_IsOneJsonObjectPerLine()
    {
        var sink = new FileLogSink(_dir);

        sink.Write(LogLevel.Error, "boom", "Test", new[] { Pair("code", 42) });

        string[] lines = File.ReadAllLines(sink.DetailedLogPath);
        Assert.Single(lines);

        using JsonDocument doc = JsonDocument.Parse(lines[0]);
        Assert.Equal("Error", doc.RootElement.GetProperty("Level").GetString());
        Assert.Equal("Test", doc.RootElement.GetProperty("Category").GetString());
        Assert.Equal("boom", doc.RootElement.GetProperty("Message").GetString());
        Assert.Equal(42, doc.RootElement.GetProperty("Data").GetProperty("code").GetInt32());
    }

    [Fact]
    public void Write_WithoutData_OmitsDataObject()
    {
        var sink = new FileLogSink(_dir);

        sink.Write(LogLevel.Info, "plain", "Test", Array.Empty<KeyValuePair<string, object?>>());

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(sink.DetailedLogPath));
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public void WriteException_IncludesTypeMessageAndStack()
    {
        var sink = new FileLogSink(_dir);

        sink.WriteException(
            new InvalidOperationException("nope"),
            "Test",
            new[] { Pair("step", "dpi") });

        string plain = File.ReadAllText(sink.LogPath);
        Assert.Contains("InvalidOperationException: nope", plain);
        Assert.Contains("step=dpi", plain);

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(sink.DetailedLogPath));
        Assert.Equal("Error", doc.RootElement.GetProperty("Level").GetString());
        Assert.Contains(
            "InvalidOperationException",
            doc.RootElement.GetProperty("Data").GetProperty("stack").GetString());
    }

    [Fact]
    public void WriteException_Null_Throws()
    {
        var sink = new FileLogSink(_dir);

        Assert.Throws<ArgumentNullException>(() =>
            sink.WriteException(null!, "Test", Array.Empty<KeyValuePair<string, object?>>()));
    }

    [Fact]
    public void Write_WhenFileIsLocked_DoesNotThrow()
    {
        var sink = new FileLogSink(_dir);

        using (new FileStream(sink.LogPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            // Logging must never take the app down, even when the log file is held open.
            sink.Write(LogLevel.Info, "locked", "Test", Array.Empty<KeyValuePair<string, object?>>());
        }
    }

    [Fact]
    public void LogFacade_RoutesToInstalledSink()
    {
        var sink = new RecordingSink();
        try
        {
            Log.SetSink(sink);
            Log.Info("one", "Cat", ("key", "value"));
            Log.Warn("two");
            Log.Error("three");
            Log.Exception(new InvalidOperationException("bad"));

            Assert.Equal(new[] { LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Error }, sink.Levels);
            Assert.Equal("one", sink.Messages[0]);
            Assert.Equal("value", sink.Entries[0][0].Value);
            Assert.Equal("bad", sink.Messages[3]);
        }
        finally
        {
            // Restore the library default (silence) so later tests are not affected.
            Log.SetSink(NullSink.Instance);
        }
    }

    [Fact]
    public void LogFacade_RejectsNullSink()
    {
        Assert.Throws<ArgumentNullException>(() => Log.SetSink(null!));
    }

    private sealed class RecordingSink : ILogSink
    {
        public List<LogLevel> Levels { get; } = new();
        public List<string> Messages { get; } = new();
        public List<IReadOnlyList<KeyValuePair<string, object?>>> Entries { get; } = new();

        public void Write(LogLevel level, string message, string? category, IReadOnlyList<KeyValuePair<string, object?>> data)
        {
            Levels.Add(level);
            Messages.Add(message);
            Entries.Add(data);
        }

        public void WriteException(Exception exception, string? category, IReadOnlyList<KeyValuePair<string, object?>> data) =>
            Write(LogLevel.Error, exception.Message, category, data);
    }

    private sealed class NullSink : ILogSink
    {
        public static readonly NullSink Instance = new();

        public void Write(LogLevel level, string message, string? category, IReadOnlyList<KeyValuePair<string, object?>> data)
        {
        }

        public void WriteException(Exception exception, string? category, IReadOnlyList<KeyValuePair<string, object?>> data)
        {
        }
    }
}
