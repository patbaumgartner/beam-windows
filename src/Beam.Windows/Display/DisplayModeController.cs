using System.Diagnostics;
using Beam.Core.Diagnostics;
using Beam.Core.Display;
using Beam.Core.Ports;
using static Beam.Windows.Display.DisplayNative;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Beam.Windows.Tests")]

namespace Beam.Windows.Display;

/// <summary>
/// Applies and restores the display mode for the presentation session.
/// Adapts the S6 spike's clone/fix/restore logic into a Windows adapter implementing
/// <see cref="IDisplayModeController"/>.
/// </summary>
public sealed class DisplayModeController : IDisplayModeController
{
    private readonly ITopologySource _inventory = new DisplayInventory();
    private readonly List<DevModeSnapshot> _snapshots = new();

    /// <summary>A snapshot of one display's DEVMODE before Beam changed it.</summary>
    private sealed record DevModeSnapshot(string GdiName, uint Width, uint Height, uint Frequency);

    /// <summary>Enters clone (duplicate) mode and switches the shared source to 16:9.</summary>
    /// <returns>Null on success; an error message if the mode change was rejected.</returns>
    public string? ApplyCloneWith16x9()
    {
        var sw = Stopwatch.StartNew();

        // Capture the current DEVMODE for every active display before changing anything.
        CaptureSnapshots();
        Log.Info("Snapshots captured before clone", "DisplayMode",
            ("count", _snapshots.Count),
            ("snapshots", string.Join(" | ", _snapshots.Select(s => $"{s.GdiName}={s.Width}x{s.Height}@{s.Frequency}Hz"))));

        // 1) Put the OS into clone/duplicate topology.
        int err = SetDisplayConfig(0, null, 0, null, SdcTopologyClone | SdcApply);
        if (err != ErrorSuccess)
        {
            sw.Stop();
            Log.Error("SetDisplayConfig(TOPOLOGY_CLONE) failed", "DisplayMode",
                ("win32Error", err.ToString()),
                ("elapsedMs", sw.ElapsedMilliseconds.ToString()));
            return $"SetDisplayConfig(TOPOLOGY_CLONE) failed: Win32 error {err}.";
        }

        Log.Info("Clone topology applied, waiting for OS to settle", "DisplayMode",
            ("win32Error", err.ToString()),
            ("settleMs", 1500.ToString()));
        Thread.Sleep(1500); // Let the OS settle.

        // 2) Find the shared source (any active path's GDI name works; they share one source
        // in clone mode). Pick the first one.
        DisplayTopology topology = _inventory.Query();
        DisplayPath? primary = topology.ActivePaths.FirstOrDefault(p => !string.IsNullOrEmpty(p.SourceGdiName));
        if (primary is null)
        {
            sw.Stop();
            Log.Error("No active source GDI name found after clone.", "DisplayMode",
                ("elapsedMs", sw.ElapsedMilliseconds.ToString()),
                ("topology", topology.ToString()));
            return "No active source GDI name found after clone.";
        }

        string gdi = primary.SourceGdiName;

        Log.Info("Found shared source after clone", "DisplayMode",
            ("gdiName", gdi),
            ("sourceId", primary.SourceId.ToString()),
            ("sourceMode", primary.SourceMode?.ToString() ?? "null"),
            ("topology", topology.ToString()));

        // 3) Find the best 16:9 mode the shared source supports. 60 Hz first; 30 Hz fallback.
        string? modeError = TrySet16x9(gdi);

        sw.Stop();
        Log.Info("ApplyCloneWith16x9 complete", "DisplayMode",
            ("gdiName", gdi),
            ("error", modeError ?? "none"),
            ("success", (modeError is null).ToString()),
            ("elapsedMs", sw.ElapsedMilliseconds.ToString()));

        return modeError;
    }

    private string? TrySet16x9(string gdi)
    {
        // Pick the best 16:9 mode the display actually supports instead of hard-coding.
        IReadOnlyList<DisplayMode> ranked = SelectBest16x9(gdi);

        Log.Info("Ranking 16:9 modes for shared source", "DisplayMode",
            ("gdiName", gdi),
            ("candidateCount", ranked.Count),
            ("candidates", string.Join(", ", ranked.Select(m => $"{m.Width}x{m.Height}@{m.RefreshHz}Hz"))));

        foreach (DisplayMode mode in ranked)
        {
            var devMode = DisplayInventory.NewDevMode();
            devMode.dmPelsWidth = mode.Width;
            devMode.dmPelsHeight = mode.Height;
            devMode.dmDisplayFrequency = (uint)Math.Round(mode.RefreshHz);
            devMode.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;

            int cds = ChangeDisplaySettingsEx(gdi, ref devMode, IntPtr.Zero, CdsTest, IntPtr.Zero);
            Log.Info("CDS test", "DisplayMode",
                ("gdiName", gdi),
                ("mode", $"{mode.Width}x{mode.Height}@{mode.RefreshHz}Hz"),
                ("result", cds.ToString()));

            if (cds == DispChangeSuccessful)
            {
                cds = ChangeDisplaySettingsEx(gdi, ref devMode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
                Log.Info("CDS apply", "DisplayMode",
                    ("gdiName", gdi),
                    ("mode", $"{mode.Width}x{mode.Height}@{mode.RefreshHz}Hz"),
                    ("result", cds.ToString()),
                    ("registry", "updated"));

                if (cds == DispChangeSuccessful)
                {
                    Log.Info("16:9 mode applied successfully", "DisplayMode",
                        ("gdiName", gdi),
                        ("appliedMode", $"{mode.Width}x{mode.Height}@{mode.RefreshHz}Hz"));
                    return null; // Success.
                }

                Log.Warn("CDS apply failed", "DisplayMode",
                    ("gdiName", gdi),
                    ("mode", $"{mode.Width}x{mode.Height}@{mode.RefreshHz}Hz"),
                    ("result", cds.ToString()));
                return $"CDS apply at {mode} failed: code {cds}.";
            }
        }

        Log.Error("No supported 16:9 mode found for the shared source.", "DisplayMode",
            ("gdiName", gdi),
            ("triedModes", ranked.Count.ToString()));
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
        var sw = Stopwatch.StartNew();

        Log.Info("Restore: returning to extended topology", "DisplayMode");

        // Return to the OS's default extended topology.
        int err = SetDisplayConfig(0, null, 0, null, SdcTopologyExtend | SdcApply);
        if (err != ErrorSuccess)
        {
            sw.Stop();
            Log.Error("SetDisplayConfig(TOPOLOGY_EXTEND) failed", "DisplayMode",
                ("win32Error", err.ToString()),
                ("elapsedMs", sw.ElapsedMilliseconds.ToString()));
            return $"SetDisplayConfig(TOPOLOGY_EXTEND) failed: Win32 error {err}.";
        }

        Log.Info("Extended topology applied, waiting for OS to settle", "DisplayMode",
            ("settleMs", 1500.ToString()));
        Thread.Sleep(1500);

        Log.Info("Restoring captured DEVMODE snapshots", "DisplayMode",
            ("snapshotCount", _snapshots.Count));

        // Restore each captured DEVMODE (the shared source's original resolution).
        foreach (DevModeSnapshot snap in _snapshots)
        {
            RestoreDevMode(snap);
        }

        _snapshots.Clear();
        sw.Stop();
        Log.Info("Restore complete", "DisplayMode",
            ("elapsedMs", sw.ElapsedMilliseconds.ToString()));
        return null;
    }

    private void RestoreDevMode(DevModeSnapshot snap)
    {
        Log.Info("Restoring DEVMODE", "DisplayMode",
            ("gdiName", snap.GdiName),
            ("targetMode", $"{snap.Width}x{snap.Height}@{snap.Frequency}Hz"));

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
            Log.Info("DEVMODE restored", "DisplayMode",
                ("gdiName", snap.GdiName),
                ("restoredMode", $"{snap.Width}x{snap.Height}@{hz}Hz"),
                ("result", cds.ToString()));
            return; // Best-effort; one try is enough.
        }

        Log.Warn("Could not restore DEVMODE for display (no compatible mode found).", "DisplayMode",
            ("gdiName", snap.GdiName),
            ("targetMode", $"{snap.Width}x{snap.Height}@{snap.Frequency}Hz"));
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
