using System.IO;

namespace Beam.Tray;

/// <summary>
/// Structured application logger that writes timestamped, categorised log lines to
/// <c>%LOCALAPPDATA%\Beam\beam.log</c> with optional console output.
///
/// Format: <c>[HH:mm:ss.fff] [threadId] [category] message key1=value1 key2=value2</c>
///
/// Logs are also written to a machine-readable JSON file
/// <c>%LOCALAPPDATA%\Beam\beam-detailed.jsonl</c> that can be submitted for debugging.
/// </summary>
internal static class AppLog
{
    private static readonly string LogDir;
    private static readonly string LogFile;
    private static readonly string DetailedFile;
    private static readonly object Lock = new();

    static AppLog()
    {
        LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Beam");
        Directory.CreateDirectory(LogDir);
        LogFile = Path.Combine(LogDir, "beam.log");
        DetailedFile = Path.Combine(LogDir, "beam-detailed.jsonl");
    }

    /// <summary>Full path to the machine-readable JSONL log file. Attach this when reporting issues.</summary>
    public static string DetailedLogPath => DetailedFile;

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Logs an informational message. In debug/verbose mode logs to console too.
    /// </summary>
    public static void Info(string message, string? category = null, params (string Key, object? Value)[] data)
        => Write(LogLevel.Info, message, category, data);

    /// <summary>Logs a warning — something non-fatal that might indicate a problem.</summary>
    public static void Warn(string message, string? category = null, params (string Key, object? Value)[] data)
        => Write(LogLevel.Warn, message, category, data);

    /// <summary>Logs an error — something that failed but the app is still running.</summary>
    public static void Error(string message, string? category = null, params (string Key, object? Value)[] data)
        => Write(LogLevel.Error, message, category, data);

    /// <summary>
    /// Logs an exception including its type, message, and full stack trace.
    /// Use this in every catch block.
    /// </summary>
    public static void Exception(Exception ex, string? category = null, params (string Key, object? Value)[] data)
    {
        Write(LogLevel.Error,
            $"{ex.GetType().Name}: {ex.Message}",
            category,
            data.Append(("stack", ex.ToString())).ToArray());
    }

    // ── Internal helpers ───────────────────────────────────────────────────

    private static void Write(LogLevel level, string message, string? category, (string Key, object? Value)[] data)
    {
        lock (Lock)
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss.fff");
            int tid = Environment.CurrentManagedThreadId;
            string cat = category ?? "App";

            string dataStr = data.Length > 0
                ? " " + string.Join(" ", data.Select(d => $"{d.Key}={Esc(d.Value)}"))
                : "";

            // Plain-text line (human-readable)
            string line = $"[{stamp}] [tid={tid,3}] [{level.ToString().ToUpper()}] [{cat}] {message}{dataStr}";

            try
            {
                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the app down.
            }

            // JSON line (machine-readable — attach this when reporting issues)
            Dictionary<string, object?>? jsonData = null;
            if (data.Length > 0)
            {
                jsonData = new Dictionary<string, object?>();
                foreach ((string Key, object? Value) d in data)
                {
                    // Duplicate keys (e.g. 'step' used in multiple steps) collapse to the
                    // last value; numeric suffixes keep all occurrences distinguishable.
                    jsonData[d.Key] = d.Value;
                }
            }

            string json = System.Text.Json.JsonSerializer.Serialize(new LogEntry(
                DateTimeOffset.Now,
                level.ToString(),
                category ?? "App",
                message,
                jsonData));

            try
            {
                File.AppendAllText(DetailedFile, json + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the app down.
            }

            Console.WriteLine(line);
        }
    }

    private static string Esc(object? value) => value switch
    {
        null => "null",
        string s => s.Contains(' ') ? $"\"{s}\"" : s,
        _ => value.ToString() ?? "null",
    };

    private enum LogLevel { Info, Warn, Error }

    // Compact JSON record for the machine-readable log
    private record LogEntry(DateTimeOffset Timestamp, string Level, string Category, string Message, Dictionary<string, object?>? Data);
}
