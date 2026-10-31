using Beam.Core.Display;
using Beam.Core.Stats;
using Beam.Windows.Display;

namespace Beam.Windows.Tests;

/// <summary>
/// The monitor is a thin polling adapter, but its contract matters: exactly one event per
/// real change, none for repeated polls, and a capture failure must not stop the loop.
/// </summary>
public class DisplayMonitorTests
{
    private static ConnectionSnapshot Snapshot(params string[] adapters) =>
        new(
            adapters.Select((a, i) => new DisplaySnapshot(new DisplayId(a, (uint)i), 1920, 1080, 100, a)).ToArray(),
            DateTimeOffset.UtcNow);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("Condition was not met within the timeout.");
    }

    [Fact]
    public void Constructor_RejectsNullCapture()
    {
        Assert.Throws<ArgumentNullException>(
            () => new DisplayMonitor(null!, TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void Constructor_UsesDefaultIntervalForNonPositiveValues()
    {
        // A zero interval would busy-spin; the constructor should clamp it.
        using var monitor = new DisplayMonitor(() => Snapshot("A"), TimeSpan.Zero);

        Assert.NotNull(monitor);
    }

    [Fact]
    public void Start_CapturesCurrentAndIsIdempotent()
    {
        int captures = 0;
        using var monitor = new DisplayMonitor(
            () =>
            {
                Interlocked.Increment(ref captures);
                return Snapshot("A");
            },
            TimeSpan.FromMinutes(5));

        monitor.Start();
        monitor.Start();

        Assert.NotNull(monitor.Current);
        Assert.Equal(1, captures);
    }

    [Fact]
    public async Task Change_RaisesExactlyOneEvent()
    {
        ConnectionSnapshot current = Snapshot("A");
        using var monitor = new DisplayMonitor(() => current, TimeSpan.FromMilliseconds(10));

        int events = 0;
        ConnectionSnapshot? previous = null;
        ConnectionSnapshot? latest = null;
        monitor.Changed += (p, c) =>
        {
            Interlocked.Increment(ref events);
            previous = p;
            latest = c;
        };

        monitor.Start();
        current = Snapshot("A", "B");

        await WaitUntilAsync(() => Volatile.Read(ref events) == 1);

        // Poll a little longer: repeated identical captures must not re-raise.
        await Task.Delay(60);
        Assert.Equal(1, Volatile.Read(ref events));
        Assert.NotNull(previous);
        Assert.Single(previous!.Displays);
        Assert.Equal(2, latest!.Displays.Count);
    }

    [Fact]
    public async Task Change_UpdatesLatencyAndRaisesLatencyUpdated()
    {
        ConnectionSnapshot current = Snapshot("A");
        using var monitor = new DisplayMonitor(() => current, TimeSpan.FromMilliseconds(10));

        var latencyReported = new TaskCompletionSource<LatencySummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.LatencyUpdated += summary => latencyReported.TrySetResult(summary);

        monitor.Start();
        current = Snapshot("A", "B");

        LatencySummary summary = await latencyReported.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, summary.Count);
        Assert.NotNull(summary.P95Ms);
        Assert.Equal(1, monitor.Latency.Count);
    }

    [Fact]
    public async Task CaptureFailure_DoesNotStopMonitoring()
    {
        ConnectionSnapshot current = Snapshot("A");
        int calls = 0;
        using var monitor = new DisplayMonitor(
            () =>
            {
                // Fail once after the initial capture, then recover.
                if (Interlocked.Increment(ref calls) == 2)
                {
                    throw new InvalidOperationException("transient");
                }

                return current;
            },
            TimeSpan.FromMilliseconds(10));

        int events = 0;
        monitor.Changed += (_, _) => Interlocked.Increment(ref events);

        monitor.Start();
        await Task.Delay(40);
        current = Snapshot("A", "B");

        await WaitUntilAsync(() => Volatile.Read(ref events) == 1);
    }

    [Fact]
    public void Stop_IsIdempotent_AndSafeWithoutStart()
    {
        var monitor = new DisplayMonitor(() => Snapshot("A"), TimeSpan.FromMilliseconds(10));

        monitor.Stop();
        monitor.Stop();
        monitor.Dispose();
        monitor.Dispose();
        Assert.Throws<ObjectDisposedException>(monitor.Start);
    }

    [Fact]
    public async Task Stop_EndsPolling()
    {
        int captures = 0;
        var monitor = new DisplayMonitor(
            () =>
            {
                Interlocked.Increment(ref captures);
                return Snapshot("A");
            },
            TimeSpan.FromMilliseconds(10));

        monitor.Start();
        await WaitUntilAsync(() => Volatile.Read(ref captures) >= 3);
        monitor.Stop();
        await Task.Delay(40);
        int afterStop = Volatile.Read(ref captures);
        await Task.Delay(60);

        Assert.Equal(afterStop, Volatile.Read(ref captures));
    }
}
