using System.Diagnostics;
using Beam.Core.Display;
using Beam.Core.Ports;
using Beam.Core.Stats;

namespace Beam.Windows.Display;

/// <summary>
/// Background monitor that detects display connect/disconnect/topology changes and raises
/// <see cref="Changed"/> within about one poll interval. Production capture is the display
/// inventory; tests inject a scripted capture to measure detection latency.
/// </summary>
/// <remarks>
/// The detection-latency samples are exposed via <see cref="Latency"/> as a running p95 —
/// the S5 acceptance bar is p95 ≤ 1.5s. Worst-case latency is one poll interval plus the
/// cost of a capture, so a 250 ms cadence comfortably clears that bar for a tray app.
/// </remarks>
public sealed class DisplayMonitor : IDisplayChangeSource, IDisposable
{
    private readonly Func<ConnectionSnapshot> _capture;
    private readonly TimeSpan _pollInterval;
    private readonly List<double> _latencySamplesMs = new();
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cts = new();

    private ConnectionSnapshot? _last;
    private long _lastChangeTimestamp;
    private Task? _loop;
    private bool _disposed;
    private LatencySummary _latency = new(0, null, 0, null);

    /// <summary>Fired when a change is detected. First raise has <paramref name="previous"/> null.</summary>
    public event Action<ConnectionSnapshot?, ConnectionSnapshot>? Changed;

    /// <summary>Raises with the latest latency summary after each change.</summary>
    public event Action<LatencySummary>? LatencyUpdated;

    public DisplayMonitor(Func<ConnectionSnapshot> capture, TimeSpan pollInterval)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _pollInterval = pollInterval > TimeSpan.Zero ? pollInterval : TimeSpan.FromMilliseconds(250);
    }

    /// <summary>Current snapshot captured at construction.</summary>
    public ConnectionSnapshot? Current { get; private set; }

    /// <summary>Runs until <see cref="Dispose"/>. Idempotent.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            if (_loop is not null)
            {
                return;
            }

            var sw = Stopwatch.StartNew();
            _last = _capture();
            _lastChangeTimestamp = sw.ElapsedTicks;
            Current = _last;

            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, ct).ConfigureAwait(false);

                // Capture state under the lock, then invoke callbacks outside it so a
                // callback that reads Latency/Current (or captures again) cannot deadlock.
                ConnectionSnapshot? previous;
                ConnectionSnapshot? current;
                LatencySummary latency;
                Action<ConnectionSnapshot?, ConnectionSnapshot>? onChanged;
                Action<LatencySummary>? onLatency;
                lock (_sync)
                {
                    current = _capture();
                    if (_last is null || current.SameDisplayIds(_last))
                    {
                        _last = current;
                        continue;
                    }

                    long nowTicks = sw.ElapsedTicks;
                    double ms = (nowTicks - _lastChangeTimestamp) * 1000.0 / Stopwatch.Frequency;
                    _latencySamplesMs.Add(ms);
                    _latency = LatencySummary.Of(_latencySamplesMs);
                    _lastChangeTimestamp = nowTicks;

                    previous = Current;
                    _last = current;
                    Current = current;
                    latency = _latency;
                    onChanged = Changed;
                    onLatency = LatencyUpdated;
                }

                // Stop() may have been called while this capture was in flight. Once it has
                // returned, no further callbacks may fire.
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                onChanged?.Invoke(previous, current);
                onLatency?.Invoke(latency);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // A transient capture failure must not kill the monitor; keep polling.
            }
        }
    }

    /// <summary>Latest latency summary.</summary>
    public LatencySummary Latency
    {
        get { lock (_sync) return _latency; }
    }

    /// <summary>Stops watching. Idempotent and terminal: a stopped monitor cannot restart.</summary>
    public void Stop()
    {
        if (_disposed || _cts.IsCancellationRequested)
        {
            return;
        }

        _cts.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        _cts.Dispose();
    }
}
