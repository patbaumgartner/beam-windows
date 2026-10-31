namespace Beam.Core.Diagnostics;

/// <summary>
/// Ambient logging façade used by the domain and adapters.
///
/// The composition root installs a sink once at startup (<see cref="SetSink"/>); the
/// default is a no-op, so unit tests and library consumers get silence unless they opt in.
/// </summary>
public static class Log
{
    private static ILogSink _sink = NullLogSink.Instance;

    /// <summary>Installs the sink that receives subsequent entries.</summary>
    public static void SetSink(ILogSink sink) =>
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));

    /// <summary>Logs an informational message.</summary>
    public static void Info(string message, string? category = null, params (string Key, object? Value)[] data) =>
        _sink.Write(LogLevel.Info, message, category, ToPairs(data));

    /// <summary>Logs a warning: something non-fatal that may indicate a problem.</summary>
    public static void Warn(string message, string? category = null, params (string Key, object? Value)[] data) =>
        _sink.Write(LogLevel.Warn, message, category, ToPairs(data));

    /// <summary>Logs an error: something failed but the app is still running.</summary>
    public static void Error(string message, string? category = null, params (string Key, object? Value)[] data) =>
        _sink.Write(LogLevel.Error, message, category, ToPairs(data));

    /// <summary>Logs an exception including its type, message, and full stack trace.</summary>
    public static void Exception(Exception exception, string? category = null, params (string Key, object? Value)[] data)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _sink.WriteException(exception, category, ToPairs(data));
    }

    private static IReadOnlyList<KeyValuePair<string, object?>> ToPairs((string Key, object? Value)[] data)
    {
        if (data.Length == 0)
        {
            return Array.Empty<KeyValuePair<string, object?>>();
        }

        var pairs = new KeyValuePair<string, object?>[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            pairs[i] = new KeyValuePair<string, object?>(data[i].Key, data[i].Value);
        }

        return pairs;
    }

    private sealed class NullLogSink : ILogSink
    {
        public static readonly NullLogSink Instance = new();

        public void Write(LogLevel level, string message, string? category, IReadOnlyList<KeyValuePair<string, object?>> data)
        {
        }

        public void WriteException(Exception exception, string? category, IReadOnlyList<KeyValuePair<string, object?>> data)
        {
        }
    }
}
