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
    /// <summary>UC-12 rule 5: at most this many mode candidates are tried per apply.</summary>
    internal const int MaxCandidates = 3;

    private readonly ITopologySource _inventory = new DisplayInventory();
    private readonly List<DevModeSnapshot> _snapshots = new();
    private readonly Dictionary<string, DevModeSnapshot> _appliedModes = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasModeChanged;

    /// <inheritdoc />
    public bool HasModeChanged => _hasModeChanged;

    /// <inheritdoc />
    public IReadOnlyList<DisplayModeSnapshot> OriginalModes => _snapshots
        .Select(snapshot => new DisplayModeSnapshot(
            snapshot.GdiName, snapshot.Width, snapshot.Height, snapshot.Frequency))
        .ToArray();

    /// <inheritdoc />
    public void Abort()
    {
        _snapshots.Clear();
        _appliedModes.Clear();
        _hasModeChanged = false;
    }

    /// <inheritdoc />
    public IReadOnlyList<DisplayModeSnapshot> AppliedModes => _appliedModes.Values
        .Select(snapshot => new DisplayModeSnapshot(
            snapshot.GdiName, snapshot.Width, snapshot.Height, snapshot.Frequency))
        .ToArray();

    /// <inheritdoc />
    public void Prepare()
    {
        if (_snapshots.Count == 0)
        {
            CaptureSnapshots();
        }
    }

    /// <inheritdoc />
    public string? Restore(IReadOnlyList<DisplayModeSnapshot> snapshots) =>
        Restore(snapshots, Array.Empty<DisplayModeSnapshot>());

    /// <inheritdoc />
    public string? Restore(
        IReadOnlyList<DisplayModeSnapshot> snapshots,
        IReadOnlyList<DisplayModeSnapshot> appliedModes)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(appliedModes);
        _snapshots.Clear();
        _snapshots.AddRange(snapshots.Select(snapshot => new DevModeSnapshot(
            snapshot.GdiName, snapshot.Width, snapshot.Height, snapshot.Frequency)));
        _appliedModes.Clear();
        foreach (DisplayModeSnapshot snapshot in appliedModes)
        {
            _appliedModes[snapshot.GdiName] = new DevModeSnapshot(
                snapshot.GdiName, snapshot.Width, snapshot.Height, snapshot.Frequency);
        }

        return Restore();
    }

    /// <summary>A snapshot of one display's DEVMODE before Beam changed it.</summary>
    internal sealed record DevModeSnapshot(string GdiName, uint Width, uint Height, uint Frequency);

    /// <summary>Enters clone (duplicate) mode and switches the shared source to 16:9.</summary>
    /// <param name="preferNative">C1/B2: rank the EDID-native 16:9 mode first (default true).</param>
    /// <returns>Null on success; an error message if the mode change was rejected.</returns>
    public string? ApplyCloneWith16x9(bool preferNative = true)
    {
        var sw = Stopwatch.StartNew();
        _hasModeChanged = false;
        _appliedModes.Clear();

        // Capture the current DEVMODE for every active display before changing anything.
        Prepare();
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

        _hasModeChanged = true;
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

        // B2/V8: the mode we want on the shared source is the projector's EDID-native
        // 16:9 mode, so rank it first. First external target with a 16:9 native mode.
        DisplayMode? nativeMode = topology.ActivePaths
            .Where(p => !p.Target.IsInternal)
            .Select(p => p.Target.NativeMode)
            .FirstOrDefault(m => m is not null && AspectRatio.Classify(m) == AspectKind.SixteenNine);

        Log.Info("Found shared source after clone", "DisplayMode",
            ("gdiName", gdi),
            ("sourceId", primary.SourceId.ToString()),
            ("sourceMode", primary.SourceMode?.ToString() ?? "null"),
            ("nativeMode", nativeMode?.ToString() ?? "none"),
            ("topology", topology.ToString()));

        // 3) Find the best 16:9 mode the shared source supports. Native 16:9 first
        // (C1 policy toggle); then highest pixels, 60 Hz first, 30 Hz fallback.
        string? modeError = TrySet16x9(gdi, nativeMode, preferNative);
        if (modeError is null)
        {
            CaptureAppliedModes();
        }

        sw.Stop();
        Log.Info("ApplyCloneWith16x9 complete", "DisplayMode",
            ("gdiName", gdi),
            ("error", modeError ?? "none"),
            ("success", (modeError is null).ToString()),
            ("elapsedMs", sw.ElapsedMilliseconds.ToString()));

        return modeError;
    }

    /// <summary>
    /// Enters extend topology and switches the projector's own source to 16:9.
    /// <param name="preferNative">C1/B2: rank the EDID-native 16:9 mode first (default true).</param>
    /// <returns>Null on success; an error message if the change was rejected.</returns>
    /// </summary>
    public string? ApplyExtendWith16x9(bool preferNative = true)
    {
        var sw = Stopwatch.StartNew();
        _hasModeChanged = false;
        _appliedModes.Clear();

        // Capture the current DEVMODE for every active display before changing anything,
        // so Restore() can put each source back exactly.
        Prepare();
        Log.Info("Snapshots captured before extend", "DisplayMode",
            ("count", _snapshots.Count),
            ("snapshots", string.Join(" | ", _snapshots.Select(s => $"{s.GdiName}={s.Width}x{s.Height}@{s.Frequency}Hz"))));

        // 1) Put the OS into extend topology (each display gets its own source/desktop).
        int err = SetDisplayConfig(0, null, 0, null, SdcTopologyExtend | SdcApply);
        if (err != ErrorSuccess)
        {
            sw.Stop();
            Log.Error("SetDisplayConfig(TOPOLOGY_EXTEND) failed", "DisplayMode",
                ("win32Error", err.ToString()),
                ("elapsedMs", sw.ElapsedMilliseconds.ToString()));
            return $"SetDisplayConfig(TOPOLOGY_EXTEND) failed: Win32 error {err}.";
        }

        _hasModeChanged = true;
        Log.Info("Extend topology applied, waiting for OS to settle", "DisplayMode",
            ("win32Error", err.ToString()),
            ("settleMs", 1500.ToString()));
        Thread.Sleep(1500); // Let the OS settle.

        // 2) In extend mode every target has its own source; pick the external one and
        // set ITS source to the best 16:9 mode (the internal panel keeps its own mode).
        DisplayTopology topology = _inventory.Query();
        DisplayPath? external = SelectExternalPath(topology);
        if (external is null)
        {
            sw.Stop();
            Log.Error("No active external path found after extend.", "DisplayMode",
                ("elapsedMs", sw.ElapsedMilliseconds.ToString()),
                ("topology", topology.ToString()));
            return "No active external display found after extend.";
        }

        string gdi = external.SourceGdiName;

        // The projector's EDID-native 16:9 mode, ranked first when preferNative is on.
        DisplayMode? nativeMode = external.Target.NativeMode is { } m
            && AspectRatio.Classify(m) == AspectKind.SixteenNine
                ? m
                : null;

        Log.Info("Found external source after extend", "DisplayMode",
            ("gdiName", gdi),
            ("sourceId", external.SourceId.ToString()),
            ("sourceMode", external.SourceMode?.ToString() ?? "null"),
            ("nativeMode", nativeMode?.ToString() ?? "none"),
            ("topology", topology.ToString()));

        string? modeError = TrySet16x9(gdi, nativeMode, preferNative);
        if (modeError is null)
        {
            CaptureAppliedModes();
        }

        sw.Stop();
        Log.Info("ApplyExtendWith16x9 complete", "DisplayMode",
            ("gdiName", gdi),
            ("error", modeError ?? "none"),
            ("success", (modeError is null).ToString()),
            ("elapsedMs", sw.ElapsedMilliseconds.ToString()));

        return modeError;
    }

    /// <summary>
    /// The first active external path with a GDI name — in extend mode that is the
    /// projector's own source. Pure helper, unit tested.
    /// </summary>
    internal static DisplayPath? SelectExternalPath(DisplayTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        return topology.ActivePaths.FirstOrDefault(p =>
            !p.Target.IsInternal && !string.IsNullOrEmpty(p.SourceGdiName));
    }

    private string? TrySet16x9(string gdi, DisplayMode? nativeMode = null, bool preferNative = true)
    {
        // Pick the best 16:9 mode the display actually supports instead of hard-coding.
        // UC-12 rule 5: bounded to MaxCandidates attempts so a flaky driver cannot
        // keep us mutating forever.
        IReadOnlyList<DisplayMode> ranked = BoundedCandidates(SelectBest16x9(gdi, nativeMode, preferNative));

        Log.Info("Ranking 16:9 modes for shared source", "DisplayMode",
            ("gdiName", gdi),
            ("nativeMode", nativeMode?.ToString() ?? "none"),
            ("candidateCount", ranked.Count),
            ("candidates", string.Join(", ", ranked.Select(m => $"{m.Width}x{m.Height}@{m.RefreshHz}Hz"))));

        DisplayMode? applied = SelectFirstAccepted(ranked, mode => TryApplyMode(gdi, mode));

        if (applied is not null)
        {
            return null; // Success.
        }

        Log.Error("No supported 16:9 mode found for the shared source.", "DisplayMode",
            ("gdiName", gdi),
            ("triedModes", ranked.Count.ToString()));
        return "No supported 16:9 mode found for the shared source.";
    }

    /// <summary>
    /// Returns the first candidate the driver accepts, or null when none is accepted.
    /// The result is always null or a member of <paramref name="ranked"/> — never a mode
    /// outside the candidate list. Pure helper, property tested.
    /// </summary>
    internal static DisplayMode? SelectFirstAccepted(IReadOnlyList<DisplayMode> ranked, Func<DisplayMode, bool> tryApply)
    {
        ArgumentNullException.ThrowIfNull(ranked);
        ArgumentNullException.ThrowIfNull(tryApply);

        foreach (DisplayMode mode in ranked)
        {
            if (tryApply(mode))
            {
                return mode;
            }
        }

        return null;
    }

    /// <summary>
    /// Tries one candidate mode: CDS test first, then CDS apply with registry persist.
    /// Returns true when the driver accepted and applied the mode.
    /// </summary>
    private static bool TryApplyMode(string gdi, DisplayMode mode)
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
                return true;
            }

            // UC-12 rule 5: a rejected apply is not fatal — try the next candidate.
            Log.Warn("CDS apply failed — trying next candidate", "DisplayMode",
                ("gdiName", gdi),
                ("mode", $"{mode.Width}x{mode.Height}@{mode.RefreshHz}Hz"),
                ("result", cds.ToString()));
        }

        return false;
    }

    /// <summary>UC-12 rule 5: bounds the ranked candidate list. Pure helper, unit tested.</summary>
    internal static IReadOnlyList<DisplayMode> BoundedCandidates(IEnumerable<DisplayMode> ranked) =>
        ranked.Take(MaxCandidates).ToList();

    /// <summary>
    /// Picks the restore topology flag from the live active-path count: EXTEND with two or
    /// more displays, INTERNAL with a single one (the OS rejects EXTEND with error 31 there).
    /// Pure helper, unit tested.
    /// </summary>
    internal static uint RestoreTopologyFlag(int activePathCount) =>
        activePathCount > 1 ? SdcTopologyExtend : SdcTopologyInternal;

    /// <summary>
    /// Ranks 16:9 modes: the EDID-native 16:9 mode first (B2/V8, when
    /// <paramref name="preferNative"/> is on — C1 policy), then highest pixels,
    /// then 60 Hz over 30 Hz, then highest refresh. Pure helper so the ranking can be
    /// unit tested. The native boost is a reorder only — never invents or drops modes.
    /// </summary>
    internal static IReadOnlyList<DisplayMode> Rank16x9(
        IEnumerable<DisplayMode> modes, DisplayMode? nativeMode = null, bool preferNative = true)
    {
        ArgumentNullException.ThrowIfNull(modes);

        return modes
            .Where(m => AspectRatio.Classify(m) == AspectKind.SixteenNine)
            .OrderByDescending(m => preferNative && IsNativeMode(m, nativeMode) ? 1 : 0)
            .ThenByDescending(m => m.Pixels)
            .ThenBy(m => m.RefreshHz == 60 ? 0 : (m.RefreshHz == 30 ? 1 : 2))
            .ThenByDescending(m => m.RefreshHz)
            .ToList();
    }

    /// <summary>
    /// True when <paramref name="mode"/> is the display's EDID-native mode: same pixel
    /// size and a refresh rate within 0.5 Hz (absorbs 59.94-style rationals vs the
    /// driver's rounded 60).
    /// </summary>
    private static bool IsNativeMode(DisplayMode mode, DisplayMode? nativeMode) =>
        nativeMode is not null
        && mode.Width == nativeMode.Width
        && mode.Height == nativeMode.Height
        && Math.Abs(mode.RefreshHz - nativeMode.RefreshHz) <= 0.5;

    /// <summary>
    /// Ranks the 16:9 modes a display reports: native 16:9 first (when
    /// <paramref name="preferNative"/>), then highest pixels, 60 Hz over 30 Hz.
    /// </summary>
    internal static IReadOnlyList<DisplayMode> SelectBest16x9(
        string gdi, DisplayMode? nativeMode = null, bool preferNative = true) =>
        Rank16x9(DisplayInventory.GetAvailableModes(gdi), preferNative ? nativeMode : null);

    /// <summary>Restores the original display topology and source modes.</summary>
    /// <returns>An error message, or null on success.</returns>
    public string? Restore()
    {
        var sw = Stopwatch.StartNew();

        Log.Info("Restore: returning to the OS's default topology", "DisplayMode");

        List<DevModeSnapshot> candidates = _snapshots.Where(snapshot =>
        {
            _appliedModes.TryGetValue(snapshot.GdiName, out DevModeSnapshot? applied);
            DevModeSnapshot? current = ReadCurrentSnapshot(snapshot.GdiName);
            bool shouldRestore = ShouldRestoreMode(applied, current);
            if (!shouldRestore)
            {
                Log.Info("Skipping DEVMODE restore: user changed the mode after Beam applied it", "DisplayMode",
                    ("gdiName", snapshot.GdiName),
                    ("appliedMode", $"{applied!.Width}x{applied.Height}@{applied.Frequency}Hz"),
                    ("currentMode", $"{current!.Width}x{current.Height}@{current.Frequency}Hz"));
            }

            return shouldRestore;
        }).ToList();

        // With a single active display, EXTEND is rejected by the OS (Win32 error 31);
        // the correct topology for one display is INTERNAL. Pick the flag from the
        // live path count instead of assuming two displays.
        uint topologyFlag = RestoreTopologyFlag(_inventory.Query().ActivePaths.Count);
        int err = SetDisplayConfig(0, null, 0, null, topologyFlag | SdcApply);
        if (err != ErrorSuccess)
        {
            sw.Stop();
            Log.Error("SetDisplayConfig restore topology failed", "DisplayMode",
                ("topologyFlag", topologyFlag.ToString("X")),
                ("win32Error", err.ToString()),
                ("elapsedMs", sw.ElapsedMilliseconds.ToString()));
            return $"SetDisplayConfig(topology 0x{topologyFlag:X}) failed: Win32 error {err}.";
        }

        Log.Info("Restore topology applied, waiting for OS to settle", "DisplayMode",
            ("topologyFlag", topologyFlag.ToString("X")),
            ("settleMs", 1500.ToString()));
        Thread.Sleep(1500);

        DisplayTopology current = _inventory.Query();
        List<DevModeSnapshot> connected = SnapshotsForActiveDisplays(candidates, current).ToList();
        foreach (DevModeSnapshot disconnected in candidates.Except(connected))
        {
            Log.Info("Skipping DEVMODE restore for disconnected display", "DisplayMode",
                ("gdiName", disconnected.GdiName),
                ("targetMode", $"{disconnected.Width}x{disconnected.Height}@{disconnected.Frequency}Hz"));
        }

        Log.Info("Restoring captured DEVMODE snapshots", "DisplayMode",
            ("snapshotCount", connected.Count),
            ("disconnectedCount", (candidates.Count - connected.Count).ToString()));

        // Restore every connected DEVMODE. A disconnected target cannot accept a write
        // and must not keep the whole session/journal stuck in a permanent retry loop.
        // Keep only real write failures so a manager-level retry cannot lose their inverse.
        List<DevModeSnapshot> failed = RestoreSnapshots(connected, RestoreDevMode).ToList();
        _snapshots.Clear();
        _snapshots.AddRange(failed);
        sw.Stop();
        if (failed.Count > 0)
        {
            string displays = string.Join(", ", failed.Select(s => s.GdiName));
            Log.Error("Restore incomplete: one or more DEVMODE writes failed", "DisplayMode",
                ("failedDisplays", displays),
                ("elapsedMs", sw.ElapsedMilliseconds.ToString()));
            return $"Could not restore the original display mode for: {displays}.";
        }

        _hasModeChanged = false;
        _appliedModes.Clear();
        Log.Info("Restore complete", "DisplayMode",
            ("elapsedMs", sw.ElapsedMilliseconds.ToString()));
        return null;
    }

    /// <summary>True when restore metadata is absent or the live mode is still Beam's applied mode.</summary>
    internal static bool ShouldRestoreMode(DevModeSnapshot? applied, DevModeSnapshot? current) =>
        applied is null || current is null || applied == current;

    private static DevModeSnapshot? ReadCurrentSnapshot(string gdiName)
    {
        DEVMODE? current = DisplayInventory.GetCurrentDevMode(gdiName);
        return current is null
            ? null
            : new DevModeSnapshot(gdiName, current.Value.dmPelsWidth, current.Value.dmPelsHeight, current.Value.dmDisplayFrequency);
    }

    private void CaptureAppliedModes()
    {
        foreach (DisplayPath path in _inventory.Query().ActivePaths)
        {
            if (!string.IsNullOrEmpty(path.SourceGdiName) && ReadCurrentSnapshot(path.SourceGdiName) is { } current)
            {
                _appliedModes[path.SourceGdiName] = current;
            }
        }
    }

    /// <summary>Selects snapshots whose GDI source is still active after a topology restore.</summary>
    internal static IReadOnlyList<DevModeSnapshot> SnapshotsForActiveDisplays(
        IEnumerable<DevModeSnapshot> snapshots,
        DisplayTopology topology)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(topology);
        var activeNames = topology.ActivePaths
            .Select(p => p.SourceGdiName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return snapshots.Where(snapshot => activeNames.Contains(snapshot.GdiName)).ToList();
    }

    /// <summary>Attempts every snapshot and returns only those that still need a retry.</summary>
    internal static IReadOnlyList<DevModeSnapshot> RestoreSnapshots(
        IEnumerable<DevModeSnapshot> snapshots,
        Func<DevModeSnapshot, bool> tryRestore)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(tryRestore);
        return snapshots.Where(snap => !tryRestore(snap)).ToList();
    }

    /// <summary>Tries the original refresh rate, then 60/30 Hz fallbacks, without duplicates.</summary>
    internal static bool TryRestoreMode(DevModeSnapshot snap, Func<uint, bool> tryRestore)
    {
        ArgumentNullException.ThrowIfNull(snap);
        ArgumentNullException.ThrowIfNull(tryRestore);
        foreach (uint hz in new[] { snap.Frequency, 60u, 30u }.Distinct())
        {
            if (tryRestore(hz))
            {
                return true;
            }
        }

        return false;
    }

    private bool RestoreDevMode(DevModeSnapshot snap)
    {
        Log.Info("Restoring DEVMODE", "DisplayMode",
            ("gdiName", snap.GdiName),
            ("targetMode", $"{snap.Width}x{snap.Height}@{snap.Frequency}Hz"));

        bool restored = TryRestoreMode(snap, hz => TryRestoreDevModeAt(snap, hz));
        if (!restored)
        {
            Log.Warn("Could not restore DEVMODE for display (no compatible mode found).", "DisplayMode",
                ("gdiName", snap.GdiName),
                ("targetMode", $"{snap.Width}x{snap.Height}@{snap.Frequency}Hz"));
        }

        return restored;
    }

    private static bool TryRestoreDevModeAt(DevModeSnapshot snap, uint hz)
    {
        var devMode = DisplayInventory.NewDevMode();
        devMode.dmPelsWidth = snap.Width;
        devMode.dmPelsHeight = snap.Height;
        devMode.dmDisplayFrequency = hz;
        devMode.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;

        int cds = ChangeDisplaySettingsEx(snap.GdiName, ref devMode, IntPtr.Zero, CdsTest, IntPtr.Zero);
        if (cds != DispChangeSuccessful)
        {
            return false;
        }

        cds = ChangeDisplaySettingsEx(snap.GdiName, ref devMode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
        Log.Info("DEVMODE restore apply", "DisplayMode",
            ("gdiName", snap.GdiName),
            ("restoredMode", $"{snap.Width}x{snap.Height}@{hz}Hz"),
            ("result", cds.ToString()),
            ("success", (cds == DispChangeSuccessful).ToString()));
        return cds == DispChangeSuccessful;
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
