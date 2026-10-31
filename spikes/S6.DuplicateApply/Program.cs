using Beam.Core.Display;
using Beam.Windows.Display;
using static Beam.Windows.Display.DisplayNative;

// S6 spike: duplicate-mode apply + 16:9 shared source (the "remove black bars" fix).
// Operations are destructive to the desktop: each mutating op is gated behind --yes
// and prints the exact topology before/after. Mind the black-out; it lasts a second.

DisplayNative.SetProcessDpiAwarenessContext(DpiAwarenessPerMonitorV2);

string command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
bool yes = args.Contains("--yes");

var inventory = new DisplayInventory();

switch (command)
{
    case "snapshot":
        Print(inventory.Query());
        return 0;

    case "clone":
        if (!yes)
        {
            Console.Error.WriteLine("This flips to duplicate mode and switches the shared source to 16:9 (3840x2160).");
            Console.Error.WriteLine("Your screen will flash black for a second. Run again with --yes to proceed.");
            return 2;
        }

        return ApplyClone(inventory);

    case "restore":
        if (!yes)
        {
            Console.Error.WriteLine("This returns the desktop to extended mode at the previous arrangements.");
            Console.Error.WriteLine("Run again with --yes to proceed.");
            return 2;
        }

        return Restore(inventory);

    case "mode":
        if (args.Length < 3 || !TryParseResolution(args[2], out uint rw, out uint rh))
        {
            Console.Error.WriteLine("Usage: mode <gdiName> <width>x<height> [--yes]");
            return 2;
        }

        return ApplyMode(inventory, args[1], rw, rh, yes);

    default:
        Console.Error.WriteLine("Usage: S6.DuplicateApply <snapshot|clone|restore|mode <gdi> <WxH>> [--yes]");
        return 2;
}

static int ApplyMode(DisplayInventory inventory, string gdi, uint width, uint height, bool yes)
{
    Console.WriteLine($"Target: {gdi} -> {width}x{height} ({(width == 0 || height == 0 ? "?" : AspectRatio.Describe(width, height))})");
    Console.WriteLine("Before:");
    Print(inventory.Query());

    var target = DisplayInventory.NewDevMode();
    target.dmPelsWidth = width;
    target.dmPelsHeight = height;
    target.dmFields = DmPelsWidth | DmPelsHeight;

    int cds = ChangeDisplaySettingsEx(gdi, ref target, IntPtr.Zero, CdsTest, IntPtr.Zero);
    Console.WriteLine($"  CDS test {width}x{height} on {gdi}: {CdsName(cds)}");
    if (cds != DispChangeSuccessful || !yes)
    {
        Console.Error.WriteLine(cds == DispChangeSuccessful
            ? "Mode is valid; re-run with --yes to apply."
            : $"Mode rejected ({CdsName(cds)}); no change made.");
        return 2;
    }

    cds = ChangeDisplaySettingsEx(gdi, ref target, IntPtr.Zero, 0, IntPtr.Zero);
    Console.WriteLine($"  CDS apply on {gdi}: {CdsName(cds)}");

    Thread.Sleep(1500);
    Console.WriteLine("\nAfter:");
    Print(inventory.Query());
    return 0;
}

static int ApplyClone(DisplayInventory inventory)
{
    Console.WriteLine("Before:");
    Print(inventory.Query());

    // 1) Enter duplicate mode with OS-picked topology.
    int err = SetDisplayConfig(0, null, 0, null, SdcTopologyClone | SdcApply);    if (err != ErrorSuccess)
    {
        Console.Error.WriteLine($"SetDisplayConfig(TOPOLOGY_CLONE) failed: error {err}");
        return 1;
    }

    // 2) Let the OS settle, then force the shared source to 16:9 3840x2160.
    Thread.Sleep(1500);
    var shared = inventory.Query().ActivePaths.FirstOrDefault(p => !string.IsNullOrEmpty(p.SourceGdiName));
    if (shared is null)
    {
        Console.Error.WriteLine("No active source GDI name found after clone.");
        return 1;
    }

    string gdi = shared.SourceGdiName;
    var devMode = DisplayInventory.NewDevMode();
    bool has = EnumDisplaySettingsEx(gdi, EnumCurrentSettings, ref devMode, 0);
    if (has)
    {
        Console.WriteLine($"  current after clone: {devMode.dmPelsWidth}x{devMode.dmPelsHeight}@{devMode.dmDisplayFrequency}");
    }

    // Build a DEVMODE for 3840x2160@60 (falls back gracefully if the monitor won't do 60).
    var target = DisplayInventory.NewDevMode();
    target.dmPelsWidth = 3840;
    target.dmPelsHeight = 2160;
    target.dmDisplayFrequency = 60;
    target.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;

    int cds = ChangeDisplaySettingsEx(gdi, ref target, IntPtr.Zero, CdsTest, IntPtr.Zero);
    Console.WriteLine($"  CDS test 3840x2160@60 on {gdi}: {CdsName(cds)}");
    if (cds != DispChangeSuccessful)
    {
        target.dmDisplayFrequency = 30;
        cds = ChangeDisplaySettingsEx(gdi, ref target, IntPtr.Zero, CdsTest, IntPtr.Zero);
        Console.WriteLine($"  CDS test 3840x2160@30 on {gdi}: {CdsName(cds)}");
    }

    if (cds != DispChangeSuccessful)
    {
        Console.Error.WriteLine("Monitor refused the 16:9 shared source; leaving clone mode as-is.");
        return 1;
    }

    // Persist to registry so the OS does not revert the change moments later.
    cds = ChangeDisplaySettingsEx(gdi, ref target, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
    Console.WriteLine($"  CDS apply (persist) on {gdi}: {CdsName(cds)}");

    Thread.Sleep(1500);
    Console.WriteLine("\nAfter:");
    Print(inventory.Query());
    return 0;
}

static int Restore(DisplayInventory inventory)
{
    Console.WriteLine("Before restore:");
    Print(inventory.Query());

    // Return to whatever topology the OS considers default (extend). The exact prior
    // positions are normally preserved by the OS on the next boot; this is a best-effort
    // for the spike.
    int err = SetDisplayConfig(0, null, 0, null, SdcTopologyExtend | SdcApply);
    if (err != ErrorSuccess)
    {
        Console.Error.WriteLine($"SetDisplayConfig(TOPOLOGY_EXTEND) failed: error {err}");
        return 1;
    }

    Thread.Sleep(1500);
    Console.WriteLine("\nAfter restore:");
    Print(inventory.Query());
    return 0;
}

static void Print(DisplayTopology topology)
{
    foreach (DisplayPath path in topology.ActivePaths.OrderBy(p => p.SourceGdiName))
    {
        DisplayMode? sm = path.SourceMode;
        DisplayMode? tm = path.TargetMode;
        Console.WriteLine($"  {path.SourceGdiName} src={path.SourceId} -> {path.Target.FriendlyName} [{path.Target.OutputTechnology}]");
        Console.WriteLine($"      desktop {sm?.Width ?? 0}x{sm?.Height ?? 0}  signal {tm?.Width ?? 0}x{tm?.Height ?? 0}  " +
                          $"aspect {(sm is null ? "?" : AspectRatio.Describe(sm.Width, sm.Height))}");
    }

    Console.WriteLine($"  clone={topology.ActiveCloneGroups.Count(g => g.IsClone)}  " +
                      $"topology={DisplayTopologyId.Describe(topology.CurrentTopologyId)}  " +
                      $"mirrored={(topology.IsMirrored ? "YES" : "no")}  " +
                      $"aspectMismatch={(topology.HasMirrorAspectMismatch ? "YES" : "no")}");
}

static string CdsName(int code) => code switch
{
    DispChangeSuccessful => "OK",
    DispChangeRestart => "restart required",
    DispChangeBadMode => "bad mode",
    DispChangeFailed => "failed",
    _ => $"code {code}",
};

static bool TryParseResolution(string s, out uint width, out uint height)
{
    width = height = 0;
    int x = s.IndexOf('x');
    if (x <= 0 || x == s.Length - 1)
    {
        return false;
    }

    return uint.TryParse(s[..x], out width) && uint.TryParse(s[(x + 1)..], out height)
           && width > 0 && height > 0;
}
