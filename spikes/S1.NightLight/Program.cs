using System.Text.Json;
using Beam.Core.NightLight;
using Beam.Windows.NightLight;

// S1 spike harness: prove that the real Windows Night Light toggle can be read,
// written, verified and restored through the CloudStore value.
//
// Read-only commands are safe. Mutating commands are explicit and print what they do.

var controller = new NightLightController();
string command = args.Length > 0 ? args[0].ToLowerInvariant() : "dump";
bool dryRun = args.Contains("--dry-run");
bool confirmed = args.Contains("--yes");

switch (command)
{
    case "dump":
        Dump(controller);
        return 0;

    case "status":
        return Status(controller);

    case "off":
        return Apply(controller, active: false, dryRun);

    case "on":
        return Apply(controller, active: true, dryRun);

    case "snapshot":
        Snapshot(controller);
        return 0;

    case "restore":
        return Restore(controller, dryRun);

    case "cycle":
        return Cycle(controller, args, confirmed);

    default:
        Console.Error.WriteLine("Usage: S1.NightLight <dump|status|off|on|snapshot|restore|cycle N --yes> [--dry-run]");
        return 2;
}

static void Dump(NightLightController controller)
{
    Console.WriteLine("Night Light state values (CloudStore):");
    Console.WriteLine();

    foreach (NightLightEntry entry in controller.ReadAll())
    {
        Console.WriteLine($"  {entry.KeyPath}");
        Console.WriteLine($"    value: {entry.ValueName}");

        if (entry.RawBytes is null || entry.State is null)
        {
            Console.WriteLine($"    ERROR: {entry.Error}");
            Console.WriteLine();
            continue;
        }

        Console.WriteLine($"    length: {entry.RawBytes.Length} bytes");
        Console.WriteLine($"    hex:    {NightLightBlobCodec.ToHex(entry.RawBytes)}");
        Console.WriteLine($"    active: {entry.State.Active}");
        Console.WriteLine($"    cause:  {entry.State.TransitionCause}");
        Console.WriteLine($"    epoch:  {entry.State.PrologueEpochSeconds} ({DateTimeOffset.FromUnixTimeSeconds(entry.State.PrologueEpochSeconds):u})");
        Console.WriteLine($"    filetime: {entry.State.ModifiedFileTime} ({DateTime.FromFileTimeUtc(entry.State.ModifiedFileTime):u})");
        Console.WriteLine();
    }
}

static int Status(NightLightController controller)
{
    var entries = controller.ReadAll();
    var valid = entries.Where(e => e.State is not null).ToList();

    Console.WriteLine($"State values found: {entries.Count} ({valid.Count} parsed)");

    foreach (NightLightEntry entry in entries)
    {
        string name = entry.KeyPath.Contains("perdevice", StringComparison.OrdinalIgnoreCase)
            ? "per-device"
            : "default";

        Console.WriteLine(entry.State is null
            ? $"  {name,-12} UNPARSABLE: {entry.Error}"
            : $"  {name,-12} active={entry.State.Active,-5} cause={entry.State.TransitionCause}");
    }

    if (valid.Count == 0)
    {
        Console.WriteLine("No parsable Night Light state. Beam must not write anything.");
        return 1;
    }

    bool active = valid.Any(e => e.State!.Active);
    Console.WriteLine();
    Console.WriteLine($"Night Light is {(active ? "ON" : "OFF")}.");
    return 0;
}

static int Apply(NightLightController controller, bool active, bool dryRun)
{
    Console.WriteLine($"Requested: turn Night Light {(active ? "ON" : "OFF")}");

    foreach (NightLightEntry entry in controller.ReadAll())
    {
        string name = entry.KeyPath.Contains("perdevice", StringComparison.OrdinalIgnoreCase) ? "per-device" : "default";
        Console.WriteLine(entry.State is null
            ? $"  {name,-12} skipped ({entry.Error})"
            : $"  {name,-12} current active={entry.State.Active} cause={entry.State.TransitionCause}");
    }

    if (dryRun)
    {
        Console.WriteLine("Dry run: no writes performed.");
        return 0;
    }

    NightLightWriteResult result = controller.SetActive(active);
    Console.WriteLine();
    Console.WriteLine(result.Message);

    foreach (string detail in result.Details)
    {
        Console.WriteLine($"  {detail}");
    }

    return result.Success ? 0 : 1;
}

static void Snapshot(NightLightController controller)
{
    var snapshots = controller.SnapshotAll();
    string path = SnapshotPath();

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

    var dto = snapshots.Select(s => new SnapshotDto(
        s.KeyPath,
        s.ValueName,
        Convert.ToBase64String(s.RawBytes))).ToList();

    File.WriteAllText(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Snapshot written: {path} ({dto.Count} value(s))");
}

static int Restore(NightLightController controller, bool dryRun)
{
    string path = SnapshotPath();
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"No snapshot at {path}. Run 'snapshot' before changing anything.");
        return 1;
    }

    var dto = JsonSerializer.Deserialize<List<SnapshotDto>>(File.ReadAllText(path)) ?? new();
    var snapshots = new List<NightLightSnapshot>();

    foreach (SnapshotDto item in dto)
    {
        byte[] raw = Convert.FromBase64String(item.RawBase64);
        snapshots.Add(new NightLightSnapshot(item.KeyPath, item.ValueName, raw, NightLightBlobCodec.Parse(raw)));
    }

    Console.WriteLine($"Restoring {snapshots.Count} value(s) from {path}");

    if (dryRun)
    {
        Console.WriteLine("Dry run: no writes performed.");
        return 0;
    }

    NightLightWriteResult result = controller.Restore(snapshots);
    Console.WriteLine(result.Message);
    foreach (string detail in result.Details)
    {
        Console.WriteLine($"  {detail}");
    }

    return result.Success ? 0 : 1;
}

static int Cycle(NightLightController controller, string[] args, bool confirmed)
{
    if (!confirmed)
    {
        Console.Error.WriteLine("This toggles the real Night Light repeatedly and will flash the screen.");
        Console.Error.WriteLine("Re-run with: cycle <n> --yes");
        return 2;
    }

    if (args.Length < 2 || !int.TryParse(args[1], out int count) || count < 1 || count > 500)
    {
        Console.Error.WriteLine("Usage: cycle <n> --yes   (1..500)");
        return 2;
    }

    var snapshots = controller.SnapshotAll();
    if (snapshots.Count == 0)
    {
        Console.Error.WriteLine("No parsable state values; refusing to write.");
        return 1;
    }

    Console.WriteLine($"Snapshot: {snapshots.Count} value(s), original active={snapshots[0].Blob.State.Active}");
    int failures = 0;

    for (int i = 1; i <= count; i++)
    {
        NightLightWriteResult off = controller.SetActive(false);
        NightLightWriteResult on = controller.SetActive(true);

        bool ok = off.Success && on.Success;
        if (!ok)
        {
            failures++;
        }

        Console.WriteLine($"  cycle {i,3}/{count}: off={(off.Success ? "ok" : "FAIL")} on={(on.Success ? "ok" : "FAIL")}");
        Thread.Sleep(100);
    }

    NightLightWriteResult restore = controller.Restore(snapshots);
    Console.WriteLine();
    Console.WriteLine($"Cycles: {count}, failures: {failures}");
    Console.WriteLine($"Restore: {restore.Message}");

    return failures == 0 && restore.Success ? 0 : 1;
}

static string SnapshotPath() =>
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Beam",
        "spikes",
        "s1-nightlight-snapshot.json");

internal sealed record SnapshotDto(string KeyPath, string ValueName, string RawBase64);
