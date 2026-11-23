using System.Collections;

namespace Beam.Core.Diagnostics;

/// <summary>
/// C4: one entry in the in-memory diagnostics timeline shown by the diagnostics window.
/// </summary>
/// <param name="Timestamp">UTC time the entry was recorded.</param>
/// <param name="Level">Severity.</param>
/// <param name="Category">Log category (Monitor, Fix, Restore, Tray, …) or null.</param>
/// <param name="Message">The message text.</param>
/// <param name="Data">Structured key/value pairs (may be empty).</param>
public sealed record TimelineEvent(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string? Category,
    string Message,
    IReadOnlyList<KeyValuePair<string, object?>> Data)
{
    /// <summary>One display line, e.g. <c>10:00:01.234 [WARN] [Monitor] message k=v</c>.</summary>
    public string ToLine()
    {
        string head = $"{Timestamp:HH:mm:ss.fff} [{Level.ToString().ToUpperInvariant()}]{(Category is null ? "" : $" [{Category}]")} {Message}";
        if (Data.Count == 0)
        {
            return head;
        }

        return head + " " + string.Join(" ", Data.Select(kv => $"{kv.Key}={kv.Value}"));
    }
}

/// <summary>
/// C4: a fixed-capacity ring buffer of the most recent <see cref="TimelineEvent"/>s
/// (UC-14 diagnostics window shows the last 200). Pure and thread-safe; the oldest
/// entries are overwritten once full.
/// </summary>
public sealed class TimelineBuffer : IReadOnlyList<TimelineEvent>
{
    /// <summary>UC-14: the diagnostics window shows at most this many recent events.</summary>
    public const int DefaultCapacity = 200;

    private readonly object _lock = new();
    private readonly TimelineEvent?[] _ring;
    private int _start; // index of the oldest entry
    private int _count;

    public TimelineBuffer(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _ring = new TimelineEvent?[capacity];
    }

    /// <summary>Maximum number of entries kept.</summary>
    public int Capacity => _ring.Length;

    /// <summary>Number of entries currently kept (≤ <see cref="Capacity"/>).</summary>
    public int Count
    {
        get { lock (_lock) { return _count; } }
    }

    /// <summary>Appends an entry, evicting the oldest when full.</summary>
    public void Add(TimelineEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            if (_count < _ring.Length)
            {
                _ring[(_start + _count) % _ring.Length] = entry;
                _count++;
            }
            else
            {
                _ring[_start] = entry;
                _start = (_start + 1) % _ring.Length;
            }
        }
    }

    /// <summary>All entries, oldest first.</summary>
    public IReadOnlyList<TimelineEvent> Snapshot()
    {
        lock (_lock)
        {
            var result = new TimelineEvent[_count];
            for (int i = 0; i < _count; i++)
            {
                result[i] = _ring[(_start + i) % _ring.Length]!;
            }

            return result;
        }
    }

    /// <summary>Removes all entries.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_ring);
            // With no entries, the next Add defines the oldest slot; no start reset is needed.
            _count = 0;
        }
    }

    // IReadOnlyList<TimelineEvent> — oldest first.
    public int Count_ => Count;
    int IReadOnlyCollection<TimelineEvent>.Count => Count;
    public TimelineEvent this[int index]
    {
        get
        {
            lock (_lock)
            {
                if (index < 0 || index >= _count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _ring[(_start + index) % _ring.Length]!;
            }
        }
    }

    public IEnumerator<TimelineEvent> GetEnumerator() => Snapshot().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// C4: an <see cref="ILogSink"/> decorator that records every entry into a
/// <see cref="TimelineBuffer"/> (for the diagnostics window) and forwards it unchanged
/// to an inner sink (the file log). Never throws — a timeline failure must not break logging.
/// </summary>
public sealed class TimelineSink : ILogSink
{
    private readonly ILogSink _inner;

    public TimelineSink(ILogSink inner, TimelineBuffer? buffer = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Buffer = buffer ?? new TimelineBuffer();
    }

    /// <summary>The ring buffer receiving every entry.</summary>
    public TimelineBuffer Buffer { get; }

    public void Write(LogLevel level, string message, string? category, IReadOnlyList<KeyValuePair<string, object?>> data)
    {
        Record(level, category, message, data);
        _inner.Write(level, message, category, data);
    }

    public void WriteException(Exception exception, string? category, IReadOnlyList<KeyValuePair<string, object?>> data)
    {
        Record(LogLevel.Error, category, $"EXCEPTION {exception.GetType().Name}: {exception.Message}", data);
        _inner.WriteException(exception, category, data);
    }

    private void Record(LogLevel level, string? category, string message, IReadOnlyList<KeyValuePair<string, object?>> data)
    {
        try
        {
            Buffer.Add(new TimelineEvent(DateTimeOffset.UtcNow, level, category, message, data));
        }
        catch
        {
            // Never let the diagnostics timeline break logging.
        }
    }
}
