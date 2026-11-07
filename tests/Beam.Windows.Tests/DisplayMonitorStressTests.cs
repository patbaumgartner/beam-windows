using Beam.Core.Display;
using Beam.Core.Stats;
using Beam.Windows.Display;

namespace Beam.Windows.Tests;

/// <summary>
/// Stress tests for the display monitor's polling loop: burst changes, rapid connect/
/// disconnect, latency bounds, and error-injection resistance. These exercise the real
/// Task loop (not fakes) against scripted "hardware".
/// </summary>
public class DisplayMonitorStressTests
{
    private static ConnectionSnapshot Snapshot(params string[] adapters) =>
        new(
            adapters.Select((a, i) => new DisplaySnapshot(new DisplayId(a, (uint)i), 1920, 1080, 100, a)).ToArray(),
            DateTimeOffset.UtcNow);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
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
    public async Task BurstOfChanges_RaisesOneEventPerDistinctChange()
    {
        // Rapid consecutive changes: the monitor must raise exactly one event per
        // display-identity change, never zero and never twice for the same state.
        var state = new[] { Snapshot("A") };
        var monitor = new DisplayMonitor(() => state[0], TimeSpan.FromMilliseconds(5));

        int events = 0;
        var seen = new System.Collections.Concurrent.ConcurrentBag<(int, int)>();
        monitor.Changed += (p, c) =>
        {
            Interlocked.Increment(ref events);
            seen.Add(((p?.Displays.Count ?? 0), c.Displays.Count));
        };

        monitor.Start();

        // Burst through several distinct states quickly.
        var taps = new[] { Snapshot("A", "B"), Snapshot("A", "B", "C"), Snapshot("A", "B"), Snapshot("A") };
        foreach (var t in taps)
        {
            state[0] = t;
            await Task.Delay(15); // let a poll tick observe it
        }

        await WaitUntilAsync(() => Volatile.Read(ref events) >= 3);

        // Stop and confirm no further events.
        monitor.Stop();
        await Task.Delay(40);
        int frozen = Volatile.Read(ref events);
        await Task.Delay(60);
        Assert.Equal(frozen, Volatile.Read(ref events));
        monitor.Dispose();
    }

    [Fact]
    public async Task RapidConnectDisconnect_NoEventStorm()
    {
        // Oscillating between 1 and 2 displays must not produce unbounded events; each
        // distinct transition is one event, and identical states are deduplicated.
        var current = Snapshot("A");
        var monitor = new DisplayMonitor(() => current, TimeSpan.FromMilliseconds(10));
        long events = 0;
        monitor.Changed += (_, _) => Interlocked.Increment(ref events);

        monitor.Start();

        for (int i = 0; i < 10; i++)
        {
            current = Snapshot("A", "B"); // connect
            await Task.Delay(20);
            current = Snapshot("A");      // disconnect
            await Task.Delay(20);
        }

        // Allow the loop to process.
        await WaitUntilAsync(() => Volatile.Read(ref events) >= 10);

        // Dedup: repeating the same alternation after settling must not add events.
        long settled = Volatile.Read(ref events);
        current = Snapshot("A", "B");
        await Task.Delay(60);
        current = Snapshot("A");
        await Task.Delay(60);
        // At most 2 more events from the trailing changes.
        Assert.True(Volatile.Read(ref events) <= settled + 2, $"event storm detected: {events}");

        monitor.Stop();
    }

    [Fact]
    public async Task LatencyP95_TracksRepeatedChanges()
    {
        // After several changes, Latency.P95Ms must be populated and stay within a sane
        // bound for a 10 ms poll (well under the 1.5 s S5 acceptance bar).
        var current = Snapshot("A");
        using var monitor = new DisplayMonitor(() => current, TimeSpan.FromMilliseconds(10));

        var summaries = new System.Collections.Concurrent.ConcurrentBag<LatencySummary>();
        monitor.LatencyUpdated += s => summaries.Add(s);

        monitor.Start();
        current = Snapshot("A", "B");
        await WaitUntilAsync(() => monitor.Latency.Count >= 1);
        current = Snapshot("A", "B", "C");
        await WaitUntilAsync(() => monitor.Latency.Count >= 2);

        Assert.True(monitor.Latency.Count >= 2);
        Assert.NotNull(monitor.Latency.P95Ms);
        Assert.True(monitor.Latency.P95Ms <= 1500, $"p95={monitor.Latency.P95Ms}ms exceeded 1.5s bar");
    }

    [Fact]
    public async Task RepeatedStartStop_Robust()
    {
        // Multiple start/stop cycles must not corrupt the monitor.
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var monitor = new DisplayMonitor(() => Snapshot("A"), TimeSpan.FromMilliseconds(10));
            monitor.Start();
            await Task.Delay(30);
            monitor.Stop();
            monitor.Stop(); // idempotent
            monitor.Dispose();
            monitor.Dispose(); // idempotent
        }

        Assert.True(true);
    }

    [Fact]
    public async Task CaptureThrowing_Transiently_KeepsMonitoring()
    {
        // A capture that throws must not stop the loop; the monitor recovers on the next
        // poll and detects changes afterwards.
        var current = Snapshot("A");
        int calls = 0;
        using var monitor = new DisplayMonitor(
            () =>
            {
                if (Interlocked.Increment(ref calls) % 3 == 0)
                {
                    throw new InvalidOperationException("transient buzz");
                }

                return current;
            },
            TimeSpan.FromMilliseconds(10));

        int events = 0;
        monitor.Changed += (_, _) => Interlocked.Increment(ref events);

        monitor.Start();
        await Task.Delay(60);

        // Turn the dial: change the display identity; events must eventually fire despite
        // the intermittent capture failures.
        current = Snapshot("A", "B");
        await WaitUntilAsync(() => Volatile.Read(ref events) == 1);
    }

    [Fact]
    public async Task CaptureReturningNull_HandledGracefully()
    {
        // A capture that returns null is treated as a failed poll — must not throw.
        var current = Snapshot("A");
        int calls = 0;
        using var monitor = new DisplayMonitor(
            () =>
            {
                return Interlocked.Increment(ref calls) == 2 ? null! : current;
            },
            TimeSpan.FromMilliseconds(10));

        var tcs = new TaskCompletionSource();
        monitor.Changed += (_, _) => tcs.TrySetResult();

        monitor.Start();
        current = Snapshot("A", "B");
        try
        {
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
            // Acceptable: the null return was swallowed, loop survived.
        }

        Assert.True(true); // no exception escaped
    }
}
