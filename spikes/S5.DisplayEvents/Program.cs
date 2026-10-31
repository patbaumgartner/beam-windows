using Beam.Core.Display;
using Beam.Windows.Display;

// S5 spike: display connect/disconnect detection with timing.
// Watch subcommand: polls when the user plugs/unplugs a display and reports detection
// latency (p95 <= 1.5s is the acceptance bar).

DisplayNative.SetProcessDpiAwarenessContext(DisplayNative.DpiAwarenessPerMonitorV2);

Func<ConnectionSnapshot> capture = () =>
{
    var inventory = new DisplayInventory();
    DisplayTopology topology = inventory.Query();
    return SnapshotOf(topology, DateTime.UtcNow);
};

Console.WriteLine("S5 display-event detection. Plug/unplug a display (or dock) to test.");
Console.WriteLine("Ctrl+C to stop. Writes a benchmark line per detected change.");
Console.WriteLine();

var monitor = new DisplayMonitor(capture, TimeSpan.FromMilliseconds(250));
monitor.LatencyUpdated += summary =>
    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] change -> p95 {summary.P95Ms?.ToString("0") ?? "-"} ms (n={summary.Count})");

monitor.Changed += (previous, current) =>
{
    Console.WriteLine("Display changed:");
    Console.WriteLine("  before:");
    PrintSnapshot(previous);
    Console.WriteLine("  after:");
    PrintSnapshot(current);
    Console.WriteLine();
};

monitor.Start();

// Keep the process alive until Ctrl+C.
var exitEvent = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, __) => exitEvent.Set();
exitEvent.Wait();

monitor.Dispose();
return 0;

static ConnectionSnapshot SnapshotOf(DisplayTopology topology, DateTimeOffset now)
{
    var displays = topology.ActivePaths.Select(p => new DisplaySnapshot(
        new DisplayId(p.AdapterId, p.SourceId),
        p.SourceMode?.Width ?? 0,
        p.SourceMode?.Height ?? 0,
        0,
        p.Target.FriendlyName)).ToArray();
    return new ConnectionSnapshot(displays, now);
}

static void PrintSnapshot(ConnectionSnapshot? s)
{
    if (s is null)
    {
        Console.WriteLine("    (none)");
        return;
    }

    foreach (var d in s.Displays)
    {
        Console.WriteLine($"    {d.FriendlyName ?? "unknown"} src={d.Id.SourceId} {d.Width}x{d.Height} scale={d.ScalePercent}% ar={d.AspectRatio():0.000}");
    }
}
