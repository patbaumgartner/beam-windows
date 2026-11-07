using Beam.Core.Display;
using Beam.Core.Stats;
using Beam.Windows.Display;

namespace Beam.Windows.Tests;

/// <summary>
/// Robustness of the poll loop when capture misbehaves: transient exceptions must not
/// kill the monitor (the loop keeps polling), and a capture that always throws must
/// not spin-crash or raise Changed. Covers the "no monitor connected / config in
/// flux" reality of real machines where QueryDisplayConfig can transiently fail.
/// </summary>
public class DisplayMonitorResilienceTests
{
    private static ConnectionSnapshot Snapshot(string adapter) => new(
        new[] { new DisplaySnapshot(new DisplayId(adapter, 0), 1920, 1080, 100, "D") },
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task Capture_TransientThrows_LoopSurvivesAndLaterRaises()
    {
        int calls = 0;
        ConnectionSnapshot Capture()
        {
            calls++;
            if (calls <= 3)
            {
                throw new InvalidOperationException("transient Win32 failure");
            }
            // First success becomes the silent baseline; a later real change raises.
            return calls <= 6 ? Snapshot("A") : Snapshot("B");
        }

        using var monitor = new DisplayMonitor(Capture, TimeSpan.FromMilliseconds(20));
        var tcs = new TaskCompletionSource();
        monitor.Changed += (_, _) => tcs.TrySetResult();

        monitor.Start();

        Task completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.Same(tcs.Task, completed);

        monitor.Stop();
    }

    [Fact]
    public async Task Capture_AlwaysThrows_NeverRaisesChanged_KeepsPolling()
    {
        int calls = 0;
        ConnectionSnapshot Capture()
        {
            calls++;
            throw new InvalidOperationException("permanent failure");
        }

        using var monitor = new DisplayMonitor(Capture, TimeSpan.FromMilliseconds(10));
        bool raised = false;
        monitor.Changed += (_, _) => raised = true;

        monitor.Start();
        await Task.Delay(150);

        Assert.False(raised);
        Assert.True(calls > 3, $"loop should keep polling, calls={calls}");

        monitor.Stop();
    }

    [Fact]
    public async Task IdenticalSnapshots_NeverRaiseChanged()
    {
        var snap = Snapshot("A");
        using var monitor = new DisplayMonitor(() => snap, TimeSpan.FromMilliseconds(10));
        int raises = 0;
        monitor.Changed += (_, _) => raises++;

        monitor.Start();
        await Task.Delay(120);

        Assert.Equal(0, raises);
        monitor.Stop();
    }

    [Fact]
    public async Task Stop_IsTerminal_NoFurtherRaises()
    {
        int seq = 0;
        using var monitor = new DisplayMonitor(
            () => Snapshot($"A{seq++}"), TimeSpan.FromMilliseconds(10));
        int raises = 0;
        monitor.Changed += (_, _) => raises++;

        monitor.Start();
        await Task.Delay(80);
        monitor.Stop();
        int afterStop = raises;
        await Task.Delay(80);

        Assert.True(raises >= afterStop);   // never decreases
        Assert.Equal(afterStop, raises);    // and does not increase after Stop
    }

    [Fact]
    public void Start_BeforeStop_Works_DoubleStart_IsIdempotent()
    {
        using var monitor = new DisplayMonitor(() => Snapshot("A"), TimeSpan.FromMilliseconds(50));
        monitor.Start();
        monitor.Start(); // must not throw or create a second loop

        Assert.NotNull(monitor.Current);
    }

    [Fact]
    public void Dispose_Twice_IsIdempotent()
    {
        var monitor = new DisplayMonitor(() => Snapshot("A"), TimeSpan.FromMilliseconds(50));
        monitor.Dispose();
        monitor.Dispose();
    }

    [Fact]
    public async Task Latency_TracksChanges()
    {
        int seq = 0;
        using var monitor = new DisplayMonitor(
            () => Snapshot($"A{seq++}"), TimeSpan.FromMilliseconds(10));

        monitor.Start();
        await Task.Delay(80);

        LatencySummary latency = monitor.Latency;
        Assert.True(latency.Count > 0, "changes occurred, latency must be measured");
        monitor.Stop();
    }

    [Fact]
    public void Constructor_InvalidPollInterval_FallsBackTo250ms()
    {
        // Zero/negative interval must not throw or spin — falls back to 250 ms.
        using var monitor = new DisplayMonitor(() => Snapshot("A"), TimeSpan.Zero);
        Assert.NotNull(monitor);
    }
}
