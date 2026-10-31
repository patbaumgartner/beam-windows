using Beam.Core.Display;
using Beam.Windows.Display;
using static Beam.Windows.Display.DisplayNative;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Beam.Tray.Tests")]

namespace Beam.Tray;

/// <summary>
/// Applies and restores the display mode for the presentation session.
/// Adapts the S6 spike's clone/fix/restore logic into a service.
/// </summary>
public sealed class DisplayModeController
{
    private readonly DisplayInventory _inventory = new();
    private readonly List<DevModeSnapshot> _snapshots = new();

    /// <summary>A snapshot of one display's DEVMODE before Beam changed it.</summary>
    private sealed record DevModeSnapshot(string GdiName, uint Width, uint Height, uint Frequency);

    /// <summary>Enters clone (duplicate) mode and switches the shared source to 16:9.</summary>
    /// <returns>Null on success; an error message if the mode change was rejected.</returns>
    public string? ApplyCloneWith16x9()
    {
        // Capture the current DEVMODE for every active display before changing anything.
        CaptureSnapshots();

        // 1) Put the OS into clone/duplicate topology.
        int err = SetDisplayConfig(0, null, 0, null, SdcTopologyClone | SdcApply);
        if (err != ErrorSuccess)
        {
            return $"SetDisplayConfig(TOPOLOGY_CLONE) failed: Win32 error {err}.";
        }

        Thread.Sleep(1500); // Let the OS settle.

        // 2) Find the shared source (any active path's GDI name works; they share one source
        // in clone mode). Pick the first one.
        DisplayTopology topology = _inventory.Query();
        DisplayPath? primary = topology.ActivePaths.FirstOrDefault(p => !string.IsNullOrEmpty(p.SourceGdiName));
        if (primary is null)
        {
            return "No active source GDI name found after clone.";
        }

        string gdi = primary.SourceGdiName;

        // 3) Find the best 16:9 mode the shared source supports. 60 Hz first; 30 Hz fallback.
        string? modeError = TrySet16x9(gdi);
        return modeError;
    }

    private string? TrySet16x9(string gdi)
    {
        // Pick the best 16:9 mode the display actually supports instead of hard-coding.
        foreach (DisplayMode mode in SelectBest16x9(gdi))
        {
            var devMode = DisplayInventory.NewDevMode();
            devMode.dmPelsWidth = mode.Width;
            devMode.dmPelsHeight = mode.Height;
            devMode.dmDisplayFrequency = (uint)Math.Round(mode.RefreshHz);
            devMode.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;

            int cds = ChangeDisplaySettingsEx(gdi, ref devMode, IntPtr.Zero, CdsTest, IntPtr.Zero);
            if (cds == DispChangeSuccessful)
            {
                cds = ChangeDisplaySettingsEx(gdi, ref devMode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
                if (cds == DispChangeSuccessful)
                {
                    return null; // Success.
                }

                return $"CDS apply at {mode} failed: code {cds}.";
            }
        }

        return "No supported 16:9 mode found for the shared source.";
    }

    /// <summary>
    /// Ranks 16:9 modes: highest pixels first, then 60 Hz over 30 Hz, then highest refresh.
    /// Pure helper so the ranking can be unit tested.
    /// </summary>
    internal static IReadOnlyList<DisplayMode> Rank16x9(IEnumerable<DisplayMode> modes) =>
        modes
            .Where(m => AspectRatio.Classify(m) == AspectKind.SixteenNine)
            .OrderByDescending(m => m.Pixels)
            .ThenBy(m => m.RefreshHz == 60 ? 0 : (m.RefreshHz == 30 ? 1 : 2))
            .ThenByDescending(m => m.RefreshHz)
            .ToList();

    /// <summary>
    /// Ranks the 16:9 modes a display reports, highest pixels first, 60 Hz over 30 Hz.
    /// </summary>
    internal static IReadOnlyList<DisplayMode> SelectBest16x9(string gdi) =>
        Rank16x9(DisplayInventory.GetAvailableModes(gdi));

    /// <summary>Restores the original display topology and source modes.</summary>
    /// <returns>An error message, or null on success.</returns>
    public string? Restore()
    {
        // Return to the OS's default extended topology. The OS preserves exact positions
        // for subsequent boots; this is the standard "best-effort" for a session restore.
        int err = SetDisplayConfig(0, null, 0, null, SdcTopologyExtend | SdcApply);
        if (err != ErrorSuccess)
        {
            return $"SetDisplayConfig(TOPOLOGY_EXTEND) failed: Win32 error {err}.";
        }

        Thread.Sleep(1500);

        // Restore each captured DEVMODE (the shared source's original resolution).
        foreach (DevModeSnapshot snap in _snapshots)
        {
            RestoreDevMode(snap);
        }

        _snapshots.Clear();
        return null;
    }

    private void RestoreDevMode(DevModeSnapshot snap)
    {
        // Try to restore the original resolution via CDS. Test first.
        foreach (uint hz in new[] { snap.Frequency, 60u, 30u })
        {
            var devMode = DisplayInventory.NewDevMode();
            devMode.dmPelsWidth = snap.Width;
            devMode.dmPelsHeight = snap.Height;
            devMode.dmDisplayFrequency = hz;
            devMode.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;

            int cds = ChangeDisplaySettingsEx(snap.GdiName, ref devMode, IntPtr.Zero, CdsTest, IntPtr.Zero);
            if (cds != DispChangeSuccessful)
            {
                continue;
            }

            cds = ChangeDisplaySettingsEx(snap.GdiName, ref devMode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
            return; // Best-effort; one try is enough.
        }
    }

    private void CaptureSnapshots()
    {
        _snapshots.Clear();
        DisplayTopology topology = _inventory.Query();
        foreach (DisplayPath path in topology.ActivePaths)
        {
            string gdi = path.SourceGdiName;
            if (string.IsNullOrEmpty(gdi))
            {
                continue;
            }

            DEVMODE? devMode = DisplayInventory.GetCurrentDevMode(gdi);
            if (devMode.HasValue)
            {
                _snapshots.Add(new DevModeSnapshot(
                    gdi,
                    devMode.Value.dmPelsWidth,
                    devMode.Value.dmPelsHeight,
                    devMode.Value.dmDisplayFrequency));
            }
        }
    }
}
