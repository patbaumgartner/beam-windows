using Beam.Core.Display;
using Beam.Windows.Display;
using static Beam.Windows.Display.DisplayNative;

// S4 spike: DPI get/set/restore through DisplayConfigGetDeviceInfo(-3/-4).
// Default (no arg) is a READ-ONLY report. Mutating commands are explicit.

DisplayNative.SetProcessDpiAwarenessContext(DpiAwarenessPerMonitorV2);

string command = args.Length > 0 ? args[0].ToLowerInvariant() : "report";
bool confirmed = args.Contains("--yes");

var inventory = new DisplayInventory();
DisplayTopology topology = inventory.Query();

switch (command)
{
    case "report":
        foreach (DisplayPath path in topology.ActivePaths)
        {
            DpiScale? scale = DpiController.Read(ParseLuid(path.AdapterId), path.SourceId);
            Console.WriteLine($"{path.SourceGdiName}  src={path.SourceId}  {path.Target.FriendlyName}");
            if (scale?.Value is not null)
            {
                Console.WriteLine($"    {scale.Value}");
                int chosen = ScalePlanner.ChooseScale(path.SourceMode?.Width ?? 0, scale.Value);
                Console.WriteLine($"    planner -> {chosen}%  setRel={scale.Value.RelativeFor(chosen)}");
            }
            else
            {
                string reason = scale is null ? "read failed" : "ladder miss";
                Console.WriteLine($"    NOT SUPPORTED ({reason})");
            }

            Console.WriteLine();
        }

        return 0;

    case "set":
        if (args.Length < 4 || !int.TryParse(args[2], out int percent))
        {
            Console.Error.WriteLine("Usage: set <gdiName> <percent> [--yes]");
            return 2;
        }

        return ApplySet(topology, args[1], percent, confirmed);

    case "list":
        foreach (DisplayPath path in topology.ActivePaths)
        {
            Console.WriteLine($"{path.SourceGdiName}  {path.Target.FriendlyName}");
        }

        return 0;

    default:
        Console.Error.WriteLine("Usage: S4.Dpi <report|list|set <gdi> <percent> [--yes]>");
        return 2;
}

static int ApplySet(DisplayTopology topology, string gdi, int percent, bool confirmed)
{
    DisplayPath? path = topology.ActivePaths.FirstOrDefault(p =>
        string.Equals(p.SourceGdiName, gdi, StringComparison.OrdinalIgnoreCase));
    if (path is null)
    {
        Console.Error.WriteLine($"No active path named '{gdi}'.");
        return 1;
    }

    DpiScale? before = DpiController.Read(ParseLuid(path.AdapterId), path.SourceId);
    if (before?.Value is null)
    {
        Console.Error.WriteLine("DPI not supported on this source; refusing to write.");
        return 1;
    }

    Console.WriteLine($"Source {path.SourceGdiName}: before = {before.Value}");
    if (!confirmed)
    {
        Console.Error.WriteLine("Re-run with --yes to apply. (This changes the real desktop scale and will resize windows.)");
        return 2;
    }

    bool ok = DpiController.SetPercentageAndVerify(ParseLuid(path.AdapterId), path.SourceId, percent, before.Value);
    DpiScale? after = DpiController.Read(ParseLuid(path.AdapterId), path.SourceId);

    Console.WriteLine($"Set {percent}% -> {(ok ? "VERIFIED" : "FAILED")}; after = {after?.Value}");

    if (!confirmed || !ok)
    {
        return ok ? 0 : 1;
    }

    // Keep a snapshot for manual restore. Snapshot path is printed; Beam restores on exit.
    return ok ? 0 : 1;
}

static LUID ParseLuid(string s)
{
    string[] parts = s.Split(':');
    int high = Convert.ToInt32(parts[0], 16);
    uint low = uint.Parse(parts[1], System.Globalization.NumberStyles.HexNumber);
    return new LUID { HighPart = high, LowPart = low };
}
