using System.IO;
using System.Text.Json;
using Beam.Core.Diagnostics;

namespace Beam.App.Diagnostics;

/// <summary>
/// Structured application logger that writes timestamped, categorised log lines to
/// <c>%LOCALAPPDATA%\Beam\beam.log</c>, plus a machine-readable JSONL file at
/// <c>%LOCALAPPDATA%\Beam\beam-detailed.jsonl</c> that users can attach to bug reports.
///
/// Format: <c>[HH:mm:ss.fff] [tid=42] [INFO] [Category] message key1=value1 key2=value2</c>
/// </summary>
/// <remarks>
/// This is the production <see cref="ILogSink"/>. The domain never references it: the
/// composition root installs it once via <see cref="Log.SetSink"/>.
/// </remarks>
public sealed class FileLogSink : ILogSink
{
    private static readonly object WriteLock = new();

    private readonly string _logDir;
    private readonly string _logFile;
    private readonly string _detailedFile;
    private readonly bool _alsoWriteToConsole;

    /// <summary>Default log directory: <c>%LOCALAPPDATA%\Beam</c>.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Beam");

    public FileLogSink(string? dir = null, bool alsoWriteToConsole = false)
    {
        _logDir = dir ?? DefaultDirectory;
        Directory.CreateDirectory(_logDir);

        _logFile = Path.Combine(_logDir, "beam.log");
        _detailedFile = Path.Combine(_logDir, "beam-detailed.jsonl");
        _alsoWriteToConsole = alsoWriteToConsole;
    }

    /// <summary>Directory holding <c>beam.log</c> and <c>beam-detailed.jsonl</c>.</summary>
    public string LogDirectory => _logDir;

    /// <summary>Full path to the human-readable log file.</summary>
    public string LogPath => _logFile;

    /// <summary>Full path to the machine-readable JSONL log file. Attach this when reporting issues.</summary>
    public string DetailedLogPath => _detailedFile;

    public void Write(
        LogLevel level,
        string message,
        string? category,
        IReadOnlyList<KeyValuePair<string, object?>> data)
    {
        lock (WriteLock)
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss.fff");
            int tid = Environment.CurrentManagedThreadId;
            string cat = category ?? "App";

            string dataStr = data.Count > 0
                ? " " + string.Join(" ", data.Select(d => $"{d.Key}={Escape(d.Value)}"))
                : "";

            string line = $"[{stamp}] [tid={tid,3}] [{level.ToString().ToUpperInvariant()}] [{cat}] {message}{dataStr}";
            Append(_logFile, line + Environment.NewLine);

            Dictionary<string, object?>? jsonData = null;
            if (data.Count > 0)
            {
                jsonData = new Dictionary<string, object?>(data.Count, StringComparer.Ordinal);
                foreach (KeyValuePair<string, object?> item in data)
                {
                    // Duplicate keys collapse to the last value; callers use distinct keys.
                    jsonData[item.Key] = item.Value;
                }
            }

            string json = JsonSerializer.Serialize(new LogEntry(
                DateTimeOffset.Now,
                level.ToString(),
                cat,
                message,
                jsonData));
            Append(_detailedFile, json + Environment.NewLine);

            if (_alsoWriteToConsole)
            {
                Console.WriteLine(line);
            }
        }
    }

    public void WriteException(
        Exception exception,
        string? category,
        IReadOnlyList<KeyValuePair<string, object?>> data)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var pairs = new List<KeyValuePair<string, object?>>(data.Count + 1);
        pairs.AddRange(data);
        pairs.Add(new KeyValuePair<string, object?>("stack", exception.ToString()));

        Write(
            LogLevel.Error,
            $"{exception.GetType().Name}: {exception.Message}",
            category,
            pairs);
    }

    private static void Append(string path, string text)
    {
        try
        {
            File.AppendAllText(path, text);
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private static string Escape(object? value) => value switch
    {
        null => "null",
        string s => s.Contains(' ') ? $"\"{s}\"" : s,
        _ => value.ToString() ?? "null",
    };

    private sealed record LogEntry(
        DateTimeOffset Timestamp,
        string Level,
        string Category,
        string Message,
        Dictionary<string, object?>? Data);
}
