using System.Text.Json;
using Beam.Core.Display;
using Beam.Tray;
using Beam.Windows.Display;
using Beam.Windows.NightLight;
using static Beam.Windows.Display.DisplayNative;

// Beam CLI � scriptable front end (UC-16).
//
// Exit codes:
//   0 success
//   1 generic failure
//   2 invalid arguments
//   3 no projector / no trigger condition
//   4 unsupported operation
//
// Commands:
//   beam status [--json]
//   beam devices [--json]
//   beam apply  [--dry-run] [--json]
//   beam restore [--json]
//   beam dpi set <percent> [--device <gdi>] [--verify] [--json]
//   beam nightlight on|off [--json]

DisplayNative.SetProcessDpiAwarenessContext(DpiAwarenessPerMonitorV2);

string[] cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
if (cliArgs.Length == 0)
{
    PrintHelp();
    return 2;
}

string command = cliArgs[0].ToLowerInvariant();
bool json = cliArgs.Contains("--json");
bool dryRun = cliArgs.Contains("--dry-run");
try
{
    return command switch
    {
        "help" or "-h" or "--help" => Help(json),
        "status" => Status(json),
        "devices" => Devices(json),
        "apply" => Apply(dryRun, json),
        "restore" => Restore(json),
        "dpi" => Dpi(args, json),
        "nightlight" => NightLight(args, json),
        _ when command.StartsWith('-') => InvalidCommand(command),
        _ => InvalidCommand(command),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

// -- Commands ----------------------------------------------------------------

static int Status(bool json)
{
    var inventory = new DisplayInventory();
    DisplayTopology topology = inventory.Query();

    var status = new
    {
        version = "0.1.0",
        success = true,
        state = "session",
        mirrored = topology.IsMirrored,
        aspectMismatch = topology.HasMirrorAspectMismatch,
        displays = topology.ActivePaths.Select(p => new
        {
            gdi = p.SourceGdiName,
            name = p.Target.FriendlyName,
            internalPanel = p.Target.IsInternal,
            source = $"{p.SourceMode?.Width ?? 0}x{p.SourceMode?.Height ?? 0}",
            signal = $"{p.TargetMode?.Width ?? 0}x{p.TargetMode?.Height ?? 0}",
            aspect = p.SourceMode is null ? "?" : AspectRatio.Describe(p.SourceMode.Width, p.SourceMode.Height),
            dpi = p.DpiCurRel is null ? null : (object?)new { currentRel = p.DpiCurRel, minRel = p.DpiMinRel, maxRel = p.DpiMaxRel },
        }).ToArray(),
        needsFix = PresentationTrigger(topology),
    };

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(status, CliJson.Options));
        return 0;
    }

    Console.WriteLine($"Mirrored: {(topology.IsMirrored ? "YES" : "no")}  Aspect mismatch: {(topology.HasMirrorAspectMismatch ? "YES" : "no")}  Needs fix: {(status.needsFix ? "YES" : "no")}");
    Console.WriteLine();
    Console.WriteLine("Active displays:");
    foreach (var d in status.displays)
    {
        Console.WriteLine($"  {d.gdi}  {d.name}  {(d.internalPanel ? "(internal)" : string.Empty)}  {d.source}  aspect={d.aspect}  signal={d.signal}");
    }
    return 0;
}

static int Devices(bool json)
{
    var inventory = new DisplayInventory();
    DisplayTopology topology = inventory.Query();

    var devices = topology.Paths.Select(p => new
    {
        gdi = p.SourceGdiName,
        sourceId = p.SourceId,
        targetId = p.TargetId,
        name = p.Target.FriendlyName,
        manufacturer = p.Target.Manufacturer,
        active = p.IsActive,
        internalPanel = p.Target.IsInternal,
        output = p.Target.OutputTechnology,
    }).ToArray();

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { version = "0.1.0", success = true, devices }, CliJson.Options));
        return 0;
    }

    foreach (var dev in devices)
    {
        Console.WriteLine($"{(dev.active ? "[active] " : "[inactive] ")}{dev.gdi}  {dev.name}  ({dev.manufacturer})  {dev.output}{(dev.internalPanel ? "  internal" : string.Empty)}");
    }

    return 0;
}

static int Apply(bool dryRun, bool json)
{
    var inventory = new DisplayInventory();
    DisplayTopology before = inventory.Query();

    if (!PresentationTrigger(before))
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { version = "0.1.0", success = false, error = "No projector detected (no mirrored external with aspect mismatch)." }, CliJson.Options));
        }
        else
        {
            Console.Error.WriteLine("No projector detected (need a mirrored external display with an aspect mismatch).");
        }
        return 3;
    }

    if (dryRun)
    {
        var plan = new
        {
            action = "duplicate + 16:9 shared source",
            target = "best supported 16:9",
            nightLight = "off",
            dpi = "optimal per ScalePlanner",
            before = TopologySummary(before),
        };
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { version = "0.1.0", success = true, plan }, CliJson.Options));
        }
        else
        {
            Console.WriteLine("Dry run � would apply:");
            Console.WriteLine("  mode:      duplicate + shared source 16:9 (best supported)");
            Console.WriteLine("  NightLight: off");
            Console.WriteLine("  dpi:       optimal per ScalePlanner");
        }
        return 0;
    }

    // Actual apply.
    var nightLight = new NightLightController();
    var snapshots = nightLight.SnapshotAll();
    NightLightWriteResult nl = nightLight.SetActive(false);

    var modeController = new DisplayModeController();
    string? modeError = modeController.ApplyCloneWith16x9();

    var result = new
    {
        version = "0.1.0",
        success = modeError is null && nl.Success,
        nightLight = nl.Message,
        modeError,
        after = TopologySummary(new DisplayInventory().Query()),
    };

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, CliJson.Options));
    }
    else
    {
        Console.WriteLine($"Night Light: {nl.Message}");
        Console.WriteLine($"Display mode: {(modeError is null ? "OK" : modeError)}");
    }

    // Keep the night light snapshot so it can be restored later by 'restore'? This is a
    // one-shot CLI; the tray app owns persistence. We still restore defensively is not
    // possible without state � so we just report.
    return modeError is null && nl.Success ? 0 : 1;
}

static int Restore(bool json)
{
    var nightLight = new NightLightController();
    // The CLI has no persisted snapshot from a prior 'apply'; best-effort: restore to
    // extended topology. NightLight is left as-is unless --restore-nightlight is given.
    bool restoreNight = args_Contains("--nightlight");
    NightLightWriteResult nl = restoreNight
        ? nightLight.SetActive(true)
        : new NightLightWriteResult(true, "Night Light left unchanged.", Array.Empty<string>());

    var modeController = new DisplayModeController();
    string? modeError = modeController.Restore();

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { version = "0.1.0", success = modeError is null && nl.Success, modeError }, CliJson.Options));
    }
    else
    {
        Console.WriteLine($"Display mode: {(modeError is null ? "restored" : modeError)}");
        Console.WriteLine($"Night Light: {nl.Message}");
    }
    return modeError is null ? 0 : 1;
}

// The Restore needs access to args for --nightlight; we approximate using a static.
static bool args_Contains(string flag)
{
    return Environment.GetCommandLineArgs().Skip(1).Contains(flag);
}

static int Dpi(string[] args, bool json)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: beam dpi <read|set> [<percent>] [--device <gdi>] [--json]");
        return 2;
    }

    string action = args[1].ToLowerInvariant();
    var inventory = new DisplayInventory();
    DisplayPath? path = inventory.Query().ActivePaths.FirstOrDefault(p => p.Target.IsInternal || !p.Target.IsInternal);
    if (path is null)
    {
        Console.Error.WriteLine("No active display found.");
        return 1;
    }

    LUID adapter = LUID.TryParse(path.AdapterId, out LUID l) ? l : new LUID();

    if (action == "read")
    {
        DpiScale? scale = DpiController.Read(adapter, path.SourceId);
        if (scale?.Value is null)
        {
            Console.Error.WriteLine("Could not read DPI.");
            return 1;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { version = "0.1.0", success = true, current = scale.Value.Current, minimum = scale.Value.Minimum, maximum = scale.Value.Maximum }, CliJson.Options));
        }
        else
        {
            Console.WriteLine($"DPI current={scale.Value.Current}%  range=[{scale.Value.Minimum}..{scale.Value.Maximum}]");
        }
        return 0;
    }

    if (action == "set")
    {
        if (args.Length < 4 || !int.TryParse(args[2], out int percent))
        {
            Console.Error.WriteLine("Usage: beam dpi set <percent>");
            return 2;
        }

        bool ok = DpiController.SetPercentageAndVerify(adapter, path.SourceId, percent);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { version = "0.1.0", success = ok, action = "dpi.set", percent }, CliJson.Options));
        }
        else
        {
            Console.WriteLine($"DPI {percent}% ? {(ok ? "applied and verified" : "FAILED")}");
        }
        return ok ? 0 : 1;
    }

    Console.Error.WriteLine("Unknown dpi action: " + action);
    return 2;
}

static int NightLight(string[] args, bool json)
{
    if (args.Length < 2 || args[1].ToLowerInvariant() is not ("on" or "off"))
    {
        Console.Error.WriteLine("Usage: beam nightlight <on|off> [--json]");
        return 2;
    }

    bool on = args[1].ToLowerInvariant() == "on";
    var controller = new NightLightController();
    NightLightWriteResult result = controller.SetActive(on);

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { version = "0.1.0", success = result.Success, message = result.Message }, CliJson.Options));
    }
    else
    {
        Console.WriteLine($"Night Light: {result.Message}");
    }
    return result.Success ? 0 : 1;
}

// -- Helpers -----------------------------------------------------------------

static int Help(bool json)
{
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { version = "0.1.0", success = true, commands = new[] { "status", "devices", "apply", "restore", "dpi", "nightlight" } }, CliJson.Options));
        return 0;
    }

    PrintHelp();
    return 0;
}

static int InvalidCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    Console.Error.WriteLine("Run 'beam help' for usage.");
    return 2;
}

static void PrintHelp()
{
    Console.WriteLine("Beam � projector mode label. Usage:");
    Console.WriteLine();
    Console.WriteLine("  beam status [--json]");
    Console.WriteLine("  beam devices [--json]");
    Console.WriteLine("  beam apply [--dry-run] [--json]");
    Console.WriteLine("  beam restore [--nightlight] [--json]");
    Console.WriteLine("  beam dpi read [--json]");
    Console.WriteLine("  beam dpi set <percent> [--json]");
    Console.WriteLine("  beam nightlight <on|off> [--json]");
    Console.WriteLine();
    Console.WriteLine("Exit codes: 0 success, 1 failure, 2 bad arguments, 3 no projector, 4 unsupported.");
}

static string? TopologySummary(DisplayTopology topology) =>
    string.Join(", ", topology.ActivePaths.Select(p => $"{p.SourceGdiName}:{p.SourceMode?.Width ?? 0}x{p.SourceMode?.Height ?? 0}"));

static bool PresentationTrigger(DisplayTopology topology) =>
    topology.IsMirrored && topology.HasMirrorAspectMismatch && topology.ExternalPaths.Count > 0;

/// <summary>JSON serialisation options shared by all commands (no global state).</summary>
static class CliJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}
