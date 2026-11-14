using System.IO;
using System.Text;
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

    /// <summary>Default rotation threshold per log file: 5 MB.</summary>
    public const long DefaultMaxFileBytes = 5L * 1024 * 1024;

    /// <summary>Default number of rotated backups kept per log file (beam.log.1 … beam.log.3).</summary>
    public const int DefaultMaxBackups = 3;

    private readonly string _logDir;
    private readonly string _logFile;
    private readonly string _detailedFile;
    private readonly bool _alsoWriteToConsole;
    private readonly long _maxFileBytes;
    private readonly int _maxBackups;

    /// <summary>Default log directory: <c>%LOCALAPPDATA%\Beam</c>.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Beam");

    public FileLogSink(
        string? dir = null,
        bool alsoWriteToConsole = false,
        long maxFileBytes = DefaultMaxFileBytes,
        int maxBackups = DefaultMaxBackups)
    {
        _logDir = dir ?? DefaultDirectory;
        Directory.CreateDirectory(_logDir);

        _logFile = Path.Combine(_logDir, "beam.log");
        _detailedFile = Path.Combine(_logDir, "beam-detailed.jsonl");
        _alsoWriteToConsole = alsoWriteToConsole;
        _maxFileBytes = Math.Max(1, maxFileBytes);
        _maxBackups = Math.Max(1, maxBackups);
    }

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

            string line = $"[{stamp}] [tid={tid,3}] [{level.ToString().ToUpperInvariant()}] [{cat}] {EscapeText(message)}{dataStr}";
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

    private void Append(string path, string text)
    {
        try
        {
            RotateIfNeeded(path);
            File.AppendAllText(path, text);
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    /// <summary>
    /// Rotates <paramref name="path"/> once it has reached the size cap: the oldest
    /// backup (<c>path.N</c>) is deleted, each <c>path.i</c> moves to <c>path.i+1</c>,
    /// and the current file becomes <c>path.1</c>. Best-effort — a rotation failure
    /// just means the file keeps growing until the next append.
    /// </summary>
    private void RotateIfNeeded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < _maxFileBytes)
        {
            return;
        }

        string oldest = path + "." + _maxBackups;
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (int i = _maxBackups - 1; i >= 1; i--)
        {
            string src = path + "." + i;
            if (File.Exists(src))
            {
                File.Move(src, path + "." + (i + 1), overwrite: true);
            }
        }

        File.Move(path, path + ".1", overwrite: true);
    }

    private static string Escape(object? value)
    {
        string s = value switch
        {
            null => "null",
            string str => str,
            _ => value.ToString() ?? "null",
        };

        string escaped = EscapeText(s);
        return escaped.Contains(' ') ? $"\"{escaped}\"" : escaped;
    }

    /// <summary>
    /// Escapes control characters that would forge extra log lines (\r, \n, \t) into
    /// literal backslash sequences. The plain log is line-oriented: an unescaped newline
    /// in an exception message or an EDID display name could inject fake "[INFO] …"
    /// lines into a log users attach to bug reports. The JSONL file needs no escaping —
    /// System.Text.Json already encodes control characters.
    /// </summary>
    private static string EscapeText(string s)
    {
        if (s.IndexOfAny(EscapableChars) < 0)
        {
            return s;
        }

        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    private static readonly char[] EscapableChars = { '\r', '\n', '\t' };

    private sealed record LogEntry(
        DateTimeOffset Timestamp,
        string Level,
        string Category,
        string Message,
        Dictionary<string, object?>? Data);
}
