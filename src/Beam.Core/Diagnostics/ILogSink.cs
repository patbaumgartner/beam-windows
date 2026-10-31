namespace Beam.Core.Diagnostics;

/// <summary>Severity of a log entry.</summary>
public enum LogLevel
{
    Info,
    Warn,
    Error,
}

/// <summary>
/// Destination for application diagnostics. Implemented by the host (file/console,
/// in-memory for tests). The domain only knows this interface, so <c>Beam.Core</c> and
/// <c>Beam.App</c> never take a dependency on a logging framework.
/// </summary>
public interface ILogSink
{
    /// <summary>Writes one structured entry.</summary>
    void Write(
        LogLevel level,
        string message,
        string? category,
        IReadOnlyList<KeyValuePair<string, object?>> data);

    /// <summary>Writes an exception with its full stack trace.</summary>
    void WriteException(Exception exception, string? category, IReadOnlyList<KeyValuePair<string, object?>> data);
}
