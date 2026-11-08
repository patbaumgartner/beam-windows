using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Beam.App.Sessions;
using Beam.Core.Diagnostics;
using Beam.Core.Display;
using Beam.Core.Startup;
using Microsoft.Win32;

namespace Beam.Tray;

/// <summary>
/// Owns the NotifyIcon and its context menu, and wires it to the session.
/// </summary>
public sealed class TrayService : IDisposable
{
    // P/Invoke for system DPI — used to pick the right icon size for the shell.
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private readonly NotifyIcon _icon = new();
    private readonly SessionManager _session;
    private readonly Action<string> _log;
    private readonly bool _startHidden;
    private SynchronizationContext? _ui;

    private ToolStripMenuItem? _pauseItem;
    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _startupItem;
    private ToolStripMenuItem? _applyItem;
    private ToolStripMenuItem? _restoreItem;
    private ContextMenuStrip? _menu;
    private System.Windows.Forms.Timer? _cursorGuard;

    // Cached HKCU Run-key state. Refreshed off the UI thread (see
    // RefreshStartupStateInBackground) so opening the menu never blocks on registry I/O.
    private bool _startupEnabled;
    private int _startupStateVersion;
    private int _startupRefreshRunning;

    // A state change that arrived while the context menu was open. The shell calls it would
    // trigger (tooltip text + balloon) are deferred until the menu closes; see ApplyStateToUi.
    private SessionState? _deferredShellState;

    public event Action? ExitRequested;

    public TrayService(SessionManager session, Action<string>? log = null, bool startHidden = false)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _log = log ?? (_ => { });
        _startHidden = startHidden;

        // Captured on the UI thread (TrayService is constructed from Program's thread).
        // State-change callbacks can arrive from the background display-monitor loop, so
        // all WinForms control access must be marshalled back here or the message pump
        // stalls (busy cursor, dead context menu).
        _ui = SynchronizationContext.Current;

        _session.StateChanged += OnStateChanged;
    }

    public void Show()
    {
        // (Re)capture the UI thread's SynchronizationContext. At construction time this is
        // still null because no control (and therefore no WindowsFormsSynchronizationContext)
        // exists yet — the host form is created right before Show() is called. Recapturing
        // here guarantees StateChanged callbacks can be marshalled onto the message loop.
        _ui ??= SynchronizationContext.Current;

        _icon.Icon = CreateTrayIcon();
        _icon.Text = "Beam — Projector mode";
        _icon.Visible = true;

        // Read the Run key once here, on the startup path, then serve it from the cache.
        // BuildMenu and the menu's Opening handler must not touch the registry (see
        // RefreshStartupStateInBackground for why).
        _startupEnabled = IsStartupEnabled();
        _menu = BuildMenu();
        _icon.ContextMenuStrip = _menu;

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowStatus();
                _log("[Tray] Left-click on icon — showing status balloon.");
            }
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowStatus();
                _log("[Tray] Left-doubleclick on icon — showing status balloon.");
            }
        };

        Log.Info("TrayService initialised",
            "Tray",
            ("hidden", _startHidden),
            ("startupEnabled", _startupEnabled),
            ("systemDpi", GetDpiForSystem()),
            ("iconSizeHint", SuggestIconSize()));
    }

    /// <summary>
    /// Creates the tray icon at the correct size for the current system DPI.
    ///
    /// The shell renders the icon at the appropriate size based on the taskbar's DPI
    /// context. Providing a properly-sized BITMAP (rather than a tiny 16x16 one)
    /// avoids the blurry upscaling you see with small icons on high-DPI displays.
    /// </summary>
    private static Icon CreateTrayIcon()
    {
        int sizePx = SuggestIconSize();
        using var bmp = new Bitmap(sizePx, sizePx);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        DrawBeamIcon(g, sizePx);

        // Icon.FromHandle creates an icon that wraps the GDI handle without owning it.
        // Clone so the NotifyIcon holds its own managed icon, then destroy the temporary
        // GDI handle via P/Invoke — otherwise each Show() leaks an icon handle.
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using Icon temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Suggests an icon size in pixels based on the current system DPI.
    /// GetDpiForSystem() returns raw DPI: 96 (100%), 144 (150%), 192 (200%), etc.
    /// Source bitmaps are deliberately large (2–3× the rendered shell slot) so the shell
    /// downscales a rich image rather than upscaling a blurry one.
    /// Small (48 px) on 100% DPI, medium (64 px) on 150%, large (96 px) on 200%+.
    /// </summary>
    private static int SuggestIconSize()
    {
        uint dpi = GetDpiForSystem();
        return dpi switch
        {
            >= 192 => 96, // 192+ DPI  →  200 %+ scaling  →  96 px (sharpest at high DPI)
            >= 144 => 64, // 144–191 DPI → 150 % scaling  →  64 px
            _ => 48, //  96–143 DPI → 100–150 %     →  48 px
        };
    }

    /// <summary>
    /// The Beam brand mark — the "high beam" headlight icon by I Create Stuff from
    /// The Noun Project (CC BY 3.0; see assets/ATTRIBUTION.md) — as a black silhouette
    /// with an alpha channel, loaded once from the embedded resources.
    /// </summary>
    private static readonly Lazy<Bitmap> BrandMark = new(LoadBrandMark);

    private static Bitmap LoadBrandMark()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Beam.Tray.Assets.high-beam.png")
            ?? throw new InvalidOperationException(
                "Embedded resource 'Beam.Tray.Assets.high-beam.png' is missing.");
        return new Bitmap(stream);
    }

    // Solid-tint attributes for recoloring the black silhouette while keeping its alpha:
    // the colour matrix outputs a constant RGB per channel and passes alpha through.
    private static readonly ImageAttributes BrandTint =
        TintAttributes(Color.FromArgb(0x00, 0xAE, 0xD8)); // Pantone 638C
    private static readonly ImageAttributes HaloTint =
        TintAttributes(Color.FromArgb(0x00, 0x80, 0xB4)); // deeper 638C-tinted edge

    private static ImageAttributes TintAttributes(Color color)
    {
        var matrix = new ColorMatrix
        {
            Matrix04 = color.R / 255f,
            Matrix14 = color.G / 255f,
            Matrix24 = color.B / 255f,
            Matrix33 = 1f,
            Matrix44 = 1f,
        };
        var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        return attributes;
    }

    /// <summary>
    /// Draws the Beam high-beam icon into the given graphics context.
    ///
    /// The mark (see <see cref="BrandMark"/>) is tinted at draw time: a deep-blue halo —
    /// the silhouette scaled up slightly — sits behind the Pantone 638C fill so the mark
    /// separates from pale (light-mode) taskbars, while the bright fill carries it on dark
    /// ones.
    /// </summary>
    private static void DrawBeamIcon(Graphics g, int size)
    {
        var mark = BrandMark.Value;

        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Halo — the silhouette drawn ~8% larger in the deep edge colour, centred.
        float haloSize = size * 1.08f;
        var haloRect = new Rectangle(
            (int)((size - haloSize) / 2f), (int)((size - haloSize) / 2f), (int)haloSize, (int)haloSize);
        g.DrawImage(mark, haloRect, 0, 0, mark.Width, mark.Height, GraphicsUnit.Pixel, HaloTint);

        // Brand fill — Pantone 638C.
        var rect = new Rectangle(0, 0, size, size);
        g.DrawImage(mark, rect, 0, 0, mark.Width, mark.Height, GraphicsUnit.Pixel, BrandTint);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.ShowImageMargin = false;
        menu.Opening += (_, _) =>
        {
            NormaliseCursor(menu);
            RefreshMenuState();
        };
        menu.Opened += (_, _) =>
        {
            NormaliseCursor(menu);
            _cursorGuard?.Start();
        };
        // Apply anything that arrived while the menu was up as soon as it goes away.
        menu.Closed += (_, _) =>
        {
            _cursorGuard?.Stop();
            FlushDeferredShellState();
        };

        // Status (read-only header).
        _statusItem = new ToolStripMenuItem(StatusText(_session.State)) { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        // Pause / resume — the label is kept in sync with the session state in
        // ApplyStateToUi, so it always reflects whether Beam is running or paused.
        _pauseItem = new ToolStripMenuItem(PauseItemText(_session.State));
        _pauseItem.Click += (_, _) =>
        {
            if (_session.State == SessionState.Paused)
            {
                _session.Resume();
                Log.Info("Beam resumed by user", "Tray");
            }
            else
            {
                _session.Pause();
                Log.Info("Beam paused by user", "Tray");
            }
        };
        menu.Items.Add(_pauseItem);

        // Launch at sign-in (registry HKCU Run key). Matches Windows Settings wording.
        // CheckOnClick is left off so we control the check state: the toggle performs the
        // registry write, shows a balloon confirmation, and reverts on failure.
        _startupItem = new ToolStripMenuItem("Launch at sign-in")
        {
            Checked = _startupEnabled,
        };
        _startupItem.Click += (_, _) => ToggleStartup();
        menu.Items.Add(_startupItem);

        menu.Items.Add(new ToolStripSeparator());

        // Manual actions. Enabled state is derived from the session state so a click is
        // never a no-op (see TrayPresentation.CanApplyNow / CanRestore).
        _applyItem = new ToolStripMenuItem("Apply projector mode now")
        {
            Enabled = TrayPresentation.CanApplyNow(_session.State),
        };
        _applyItem.Click += (_, _) =>
        {
            Log.Info("Apply-now requested from tray menu", "Tray",
                ("state", _session.State.ToString()));
            ApplyNowRequested?.Invoke();
        };
        menu.Items.Add(_applyItem);

        _restoreItem = new ToolStripMenuItem("Restore display")
        {
            Enabled = TrayPresentation.CanRestore(_session.State),
        };
        _restoreItem.Click += (_, _) =>
        {
            Log.Info("Restore requested from tray menu", "Tray",
                ("state", _session.State.ToString()));
            RestoreRequested?.Invoke();
        };
        menu.Items.Add(_restoreItem);

        menu.Items.Add(new ToolStripSeparator());

        // Supportability: open the log folder so beam-detailed.jsonl can be attached to a
        // bug report.
        var logsItem = new ToolStripMenuItem("Open log folder");
        logsItem.Click += (_, _) =>
        {
            Log.Info("Open log folder requested from tray menu", "Tray",
                ("dir", AppLog.LogDirectory));
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", AppLog.LogDirectory);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not open log folder", "Tray", ("error", ex.Message));
                _icon.ShowBalloonTip(3000, "Beam", "Could not open the log folder.", ToolTipIcon.Warning);
            }
        };
        menu.Items.Add(logsItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit Beam");
        exitItem.Click += (_, _) =>
        {
            Log.Info("Exit requested from tray menu", "Tray",
                ("state", _session.State.ToString()));
            ExitRequested?.Invoke();
        };
        menu.Items.Add(exitItem);

        // Keep the arrow pinned for as long as the drop-down is up. A busy cursor latched during
        // a transient stall is re-applied by WinForms on every WM_SETCURSOR, so resetting once
        // when the menu opens is not enough. The menu does no real work, so a wait cursor over
        // it is always wrong — clear it within a quarter second, however it got there.
        _cursorGuard = new System.Windows.Forms.Timer { Interval = 250 };
        _cursorGuard.Tick += (_, _) =>
        {
            if (menu.Visible)
            {
                NormaliseCursor(menu);
            }
            else
            {
                _cursorGuard?.Stop();
            }
        };

        return menu;
    }

    /// <summary>Raised when the user picks "Apply projector mode now".</summary>
    public event Action? ApplyNowRequested;

    /// <summary>Raised when the user picks "Restore display".</summary>
    public event Action? RestoreRequested;

    private void OnStateChanged(SessionEvent evt)
    {
        // The transition itself is logged once by SessionManager. This handler only
        // updates the shell UI.

        // UI updates must run on the UI thread. If we're already there, apply directly;
        // otherwise hand off to the captured SynchronizationContext. WinForms controls are
        // not thread-safe — touching them from the monitor loop is what makes the tray
        // menu show a busy cursor and ignore clicks.
        if (_ui is not null && _ui != SynchronizationContext.Current)
        {
            _ui.Post(_ => ApplyStateToUi(evt), null);
            return;
        }

        ApplyStateToUi(evt);
    }

    private void ApplyStateToUi(SessionEvent evt)
    {
        if (_statusItem is null)
        {
            return;
        }

        _statusItem.Text = StatusText(evt.To);
        if (_pauseItem is not null)
        {
            _pauseItem.Text = PauseItemText(evt.To);
        }
        if (_applyItem is not null)
        {
            _applyItem.Enabled = TrayPresentation.CanApplyNow(evt.To);
        }
        if (_restoreItem is not null)
        {
            _restoreItem.Enabled = TrayPresentation.CanRestore(evt.To);
        }

        // Tooltip text and balloons both go through Shell_NotifyIcon, which is serviced by
        // this same UI thread — and while the context menu is open that thread is parked
        // inside the drop-down's modal message loop. Re-entering the shell from there is what
        // leaves the tray wedged with a busy cursor and an unresponsive menu, so hold the
        // state change until the menu closes.
        if (IsContextMenuOpen)
        {
            _deferredShellState = evt.To;
            Log.Info("[Tray] deferring shell update until the menu closes", "Tray",
                ("state", evt.To.ToString()));
            return;
        }

        ApplyShellState(evt.To);
    }

    /// <summary>
    /// Applies a session state to the shell-facing parts of the icon: the tooltip text and
    /// the transition balloon. Must only be called when no context menu is displayed.
    /// </summary>
    private void ApplyShellState(SessionState state)
    {
        _icon.Text = state == SessionState.Idle
            ? "Beam — Projector mode"
            : $"Beam — {StatusText(state)}";

        // Notify only on transitions that matter to the user, not every micro-state.
        // When started via autostart (--hidden) the initial idle balloon is suppressed.
        if (!(_startHidden && state == SessionState.Idle)
            && state is SessionState.Active or SessionState.Restoring or SessionState.Paused or SessionState.Idle)
        {
            _icon.ShowBalloonTip(700, "Beam", $"Beam is now {StatusText(state).ToLowerInvariant()}.", ToolTipIcon.Info);
        }
    }

    private bool IsContextMenuOpen => _menu is { Visible: true };

    /// <summary>
    /// Pins the drop-down to the normal arrow cursor.
    ///
    /// WinForms registers its window classes with a NULL class cursor, so whatever cursor the
    /// shell last displayed stays latched while the pointer is over a WinForms window. If the
    /// shell paints its busy/starting cursor (a transient UI-thread stall, an AV scan, shell
    /// contention while the tray overflow animates), the hourglass sticks to the context menu
    /// even though the app is responding — the menu then looks hung. Setting the cursor
    /// explicitly makes the drop-down re-assert the arrow on WM_SETCURSOR, and assigning
    /// <see cref="Cursor.Current"/> clears it immediately for a pointer that is already
    /// resting on the menu (moving the mouse would otherwise be required).
    /// </summary>
    private static void NormaliseCursor(ContextMenuStrip menu)
    {
        menu.Cursor = Cursors.Default;
        Cursor.Current = Cursors.Default;
    }

    /// <summary>Replays a shell update that was held back while the menu was open.</summary>
    private void FlushDeferredShellState()
    {
        if (_deferredShellState is not { } state)
        {
            return;
        }

        _deferredShellState = null;
        ApplyShellState(state);
    }

    private void ShowStatus()
    {
        string display = DescribeDisplays();
        string note = $"Status: {StatusText(_session.State)}\n{display}";
        _icon.ShowBalloonTip(3000, "Beam", note, ToolTipIcon.Info);
        Log.Info("Status balloon shown", "Tray",
            ("state", _session.State.ToString()),
            ("displays", display.Replace("\n", " | ")));
    }

    /// <summary>
    /// Refreshes the menu's dynamic items each time it opens. The pause label and the
    /// sign-in checkbox are live state (session + registry), so re-reading them here keeps
    /// the menu honest even when the state changed behind its back (external registry edit,
    /// auto-transition, restore triggered by a display change mid-menu).
    /// </summary>
    private void RefreshMenuState()
    {
        if (_statusItem is not null)
        {
            _statusItem.Text = StatusText(_session.State);
        }
        if (_pauseItem is not null)
        {
            _pauseItem.Text = PauseItemText(_session.State);
        }
        if (_applyItem is not null)
        {
            _applyItem.Enabled = TrayPresentation.CanApplyNow(_session.State);
        }
        if (_restoreItem is not null)
        {
            _restoreItem.Enabled = TrayPresentation.CanRestore(_session.State);
        }
        if (_startupItem is not null)
        {
            // Serve the cached value — Opening runs on the UI thread on the way into the
            // drop-down's modal loop, so a synchronous registry read here would block the
            // menu from appearing. Kick off a refresh and apply it when it lands.
            _startupItem.Checked = _startupEnabled;
            RefreshStartupStateInBackground();
        }
    }

    /// <summary>
    /// Re-reads the HKCU Run key on the thread pool and publishes the result to the UI
    /// thread. Used instead of reading the registry inline when the menu opens: registry
    /// reads can block (profile load, AV filter, a concurrent writer) and any blocking call
    /// on the UI thread here freezes the menu, which the shell paints as a wait cursor.
    /// </summary>
    private void RefreshStartupStateInBackground()
    {
        if (Interlocked.Exchange(ref _startupRefreshRunning, 1) == 1)
        {
            return;
        }

        int version = Volatile.Read(ref _startupStateVersion);
        Task.Run(() =>
        {
            bool enabled;
            try
            {
                enabled = IsStartupEnabled();
            }
            catch
            {
                enabled = _startupEnabled;
            }
            finally
            {
                Volatile.Write(ref _startupRefreshRunning, 0);
            }

            _ui?.Post(_ => PublishStartupState(enabled, version), null);
        });
    }

    private void PublishStartupState(bool enabled, int version)
    {
        // Drop the result if the user toggled the setting while the read was in flight —
        // otherwise a stale read would flip the checkbox back and fight the click handler.
        if (version != _startupStateVersion)
        {
            return;
        }

        _startupEnabled = enabled;
        if (_startupItem is not null && _startupItem.Checked != enabled)
        {
            _startupItem.Checked = enabled;
        }
    }

    private static string DescribeDisplays()
    {
        try
        {
            DisplayTopology topology = new Beam.Windows.Display.DisplayInventory().Query();
            var list = topology.ActivePaths
                .Select(p => $"{p.Target.FriendlyName} {p.SourceMode?.Width ?? 0}×{p.SourceMode?.Height ?? 0}")
                .ToList();
            return list.Count == 0 ? "No active displays." : string.Join("\n", list);
        }
        catch
        {
            return "Could not read displays.";
        }
    }

    // ── Startup (registry Run key) ──────────────────────────────────────────

    private bool IsStartupEnabled()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (exePath is null)
            {
                return false;
            }
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupCommand.RunKeyPath);
            object? value = key?.GetValue(StartupCommand.ValueName);
            return value is string v && StartupCommand.IsAutoStartValue(exePath, v);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Flips the HKCU Run key entry. The checkbox is authoritative: the new value is the
    /// logical opposite of the current check, we persist it, and we revert the check on
    /// failure (with a balloon telling the user why).
    /// </summary>
    private void ToggleStartup()
    {
        bool enable = !(_startupItem?.Checked ?? false);
        string? exePath = Environment.ProcessPath;
        if (exePath is null)
        {
            _log("[Startup] cannot resolve the Beam executable path.");
            Log.Warn("Canceled sign-in change — could not resolve the executable path.", "Startup");
            _icon.ShowBalloonTip(3000, "Beam", "Could not update the sign-in setting.", ToolTipIcon.Warning);
            return;
        }

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupCommand.RunKeyPath);
            if (enable)
            {
                key.SetValue(StartupCommand.ValueName, StartupCommand.ValueFor(exePath, launchHidden: true));
                Log.Info("Added Beam to Windows login (registry Run key)", "Startup");
                _log("[Startup] added Beam to Windows login.");
            }
            else
            {
                key.DeleteValue(StartupCommand.ValueName, throwOnMissingValue: false);
                Log.Info("Removed Beam from Windows login (registry Run key)", "Startup");
                _log("[Startup] removed Beam from Windows login.");
            }

            // Persisted state is now the source of truth — reflect it in the checkbox and
            // confirm to the user (the one place the app gives direct feedback on the toggle).
            // Bump the version so any refresh started before this write is discarded.
            _startupEnabled = enable;
            Interlocked.Increment(ref _startupStateVersion);
            if (_startupItem is not null)
            {
                _startupItem.Checked = enable;
            }
            _icon.ShowBalloonTip(
                2500,
                "Beam",
                enable ? "Beam will start automatically at sign-in." : "Beam will no longer start automatically at sign-in.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Exception(ex, "Startup",
                ("operation", enable ? "enable" : "disable"));
            _log($"[Startup] failed to update login entry: {ex.Message}");

            if (_startupItem is not null)
            {
                _startupItem.Checked = !enable; // revert the toggle
            }
            _startupEnabled = !enable;
            Interlocked.Increment(ref _startupStateVersion);

            _icon.ShowBalloonTip(
                3000,
                "Beam",
                "Could not update the sign-in setting.",
                ToolTipIcon.Warning);
        }
    }

    private static string StatusText(SessionState state) => TrayPresentation.StatusText(state);

    private static string PauseItemText(SessionState state) => TrayPresentation.PauseItemText(state);

    public void Dispose()
    {
        _session.StateChanged -= OnStateChanged;
        _cursorGuard?.Stop();
        _cursorGuard?.Dispose();
        _cursorGuard = null;
        _icon.Visible = false;
        _icon.Dispose();
        _menu?.Dispose();
        _menu = null;
    }
}
