using Beam.Core.Display;
using Beam.Windows.Display;
using static Beam.Windows.Display.DisplayNative;

// S4 + S3 spike: inventory of active display paths, clone groups, modes and DPI state.
// Read-only. Nothing here changes the system.

DisplayNative.SetProcessDpiAwarenessContext(DpiAwarenessPerMonitorV2);

bool all = args.Contains("--all");
var inventory = new DisplayInventory();
DisplayTopology topology = inventory.Query(includeInactive: all);

Console.WriteLine("=== Active display paths ===");
Console.WriteLine();

foreach (DisplayPath path in topology.ActivePaths.OrderBy(p => p.SourceGdiName))
{
    Console.WriteLine($"  {path.SourceGdiName}  src={path.SourceId}  target={path.TargetId}  rot={path.Rotation}deg  adapter={path.AdapterId}");
    Console.WriteLine($"    display:  {path.Target.FriendlyName} ({path.Target.Manufacturer}:{path.Target.ProductCode:X4}) "
                      + $"[{path.Target.OutputTechnology}]{(path.Target.IsInternal ? "  internal panel" : string.Empty)}");
    Console.WriteLine($"    desktop:  {Describe(path.SourceMode)}  at ({path.DesktopX},{path.DesktopY})  aspect {DescribeAspect(path.SourceMode)}");
    Console.WriteLine($"    signal:   {Describe(path.TargetMode)}  aspect {DescribeAspect(path.TargetMode)}");
    Console.WriteLine($"    dpi:      curRel={path.DpiCurRel?.ToString() ?? "?"} minRel={path.DpiMinRel?.ToString() ?? "?"} maxRel={path.DpiMaxRel?.ToString() ?? "?"}");
    Console.WriteLine($"    edid:     {path.Target.Identity}");
    Console.WriteLine();
}

Console.WriteLine("=== Clone groups (duplicate mode) ===");
Console.WriteLine();

foreach (CloneGroup group in topology.ActiveCloneGroups)
{
    string kind = group.IsClone ? "CLONE" : "single";
    Console.WriteLine($"  {kind}  {group.SourceGdiName}  desktop {Describe(group.Mode)}");
    foreach (DisplayPath path in group.Paths)
    {
        Console.WriteLine($"    -> {path.Target.FriendlyName} ({path.Target.OutputTechnology}) signal {Describe(path.TargetMode)}");
    }
}

Console.WriteLine();
Console.WriteLine($"  internal panel: {topology.InternalPanel?.Target.FriendlyName ?? "(none active)"}");
Console.WriteLine($"  external active: {topology.ExternalPaths.Count}");
Console.WriteLine($"  mirror aspect mismatch: {(topology.HasMirrorAspectMismatch ? "YES (black bars)" : "no")}");

Console.WriteLine();
Console.WriteLine("=== Available modes (active displays) ===");

foreach (DisplayPath path in topology.ActivePaths)
{
    if (string.IsNullOrEmpty(path.SourceGdiName))
    {
        continue;
    }

    IReadOnlyList<DisplayMode> modes = DisplayInventory.GetAvailableModes(path.SourceGdiName);
    Console.WriteLine();
    Console.WriteLine($"  {path.SourceGdiName} / {path.Target.FriendlyName}: {modes.Count} mode(s)");

    foreach (IGrouping<(uint, uint), DisplayMode> size in modes.GroupBy(m => (m.Width, m.Height)))
    {
        string rates = string.Join(", ", size.Select(m => $"{m.RefreshHz:0.##}"));
        Console.WriteLine($"    {size.Key.Item1,5}x{size.Key.Item2,-5} {AspectRatio.Describe(size.Key.Item1, size.Key.Item2),-7} @ {rates}");
    }
}

Console.WriteLine();
Console.WriteLine("=== Current DEVMODE (GDI) ===");
Console.WriteLine();

foreach (DisplayPath path in topology.ActivePaths)
{
    DEVMODE? devMode = DisplayInventory.GetCurrentDevMode(path.SourceGdiName);
    if (devMode is null)
    {
        continue;
    }

    DEVMODE value = devMode.Value;
    Console.WriteLine($"  {path.SourceGdiName}: {value.dmPelsWidth}x{value.dmPelsHeight} @ {value.dmDisplayFrequency}Hz, " +
                      $"{value.dmBitsPerPel}bpp, pos ({value.dmPositionX},{value.dmPositionY}), " +
                      $"orientation {value.dmDisplayOrientation}");
}

return 0;

static string Describe(DisplayMode? mode) => mode is null ? "(none)" : mode.ToString();

static string DescribeAspect(DisplayMode? mode) =>
    mode is null ? "?" : AspectRatio.Describe(mode.Width, mode.Height);
