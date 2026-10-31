using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Beam.Core.Display;
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
        _icon.ContextMenuStrip = BuildMenu();

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

        AppLog.Info("TrayService initialised",
            "Tray",
            ("hidden", _startHidden),
            ("startupEnabled", IsStartupEnabled()),
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

        DrawProjectorIcon(g, sizePx);

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
            _      => 48, //  96–143 DPI → 100–150 %     →  48 px
        };
    }

    /// <summary>
    /// Draws the Beam laser icon into the given graphics context.
    ///
    /// The mark is a stylised laser: a compact emitter on the right whose beam widens into
    /// a cone to the left — the classic laser hazard mark, restyled in the brand colour. It
    /// reads on both light and dark taskbars: the Pantone 638C cone with a deep-blue edge
    /// carries the shape on light backgrounds, and the bright white core carries it on dark
    /// ones. A filled cone (rather than thin lines) survives downscaling to shell sizes.
    /// </summary>
    private static void DrawProjectorIcon(Graphics g, int size)
    {
        // Brand palette — Pantone 638C (#00AED8) with a deeper 638C-tinted outline so the
        // mark stays legible on light backgrounds, plus a white hot core for dark ones.
        using var brand = new SolidBrush(Color.FromArgb(0x00, 0xAE, 0xD8));
        using var brandDeep = new SolidBrush(Color.FromArgb(0x00, 0x80, 0xB4));

        // Beam cone — a filled triangle from the emitter (right) widening to the left.
        var cone = new[]
        {
            new PointF(size * 0.80f, size * 0.42f), // emitter top
            new PointF(size * 0.80f, size * 0.58f), // emitter bottom
            new PointF(size * 0.04f, size * 0.88f), // far bottom
            new PointF(size * 0.04f, size * 0.12f), // far top
        };
        g.FillPolygon(brand, cone);

        // Deep outline on the trailing interior so the cone separates from pale taskbars.
        using var outline = new Pen(Color.FromArgb(0x00, 0x80, 0xB4), Math.Max(1f, size * 0.05f))
        {
            LineJoin = LineJoin.Round,
        };
        g.DrawPolygon(outline, cone);

        // White hot core — the inner bright ray that carries the mark on dark taskbars.
        var core = new[]
        {
            new PointF(size * 0.60f, size * 0.47f),
            new PointF(size * 0.60f, size * 0.53f),
            new PointF(size * 0.06f, size * 0.68f),
            new PointF(size * 0.06f, size * 0.32f),
        };
        g.FillPolygon((Brush)Brushes.White, core);

        // Hotspot at the emitter — the lens.
        float lensR = size * 0.11f;
        float lensX = size * 0.82f;
        float lensY = size * 0.5f;
        g.FillEllipse(brandDeep, lensX - lensR, lensY - lensR, lensR * 2, lensR * 2);
        g.FillEllipse(Brushes.White, lensX - lensR * 0.45f, lensY - lensR * 0.45f, lensR * 0.9f, lensR * 0.9f);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.ShowImageMargin = false;
        menu.Opening += (_, _) => RefreshMenuState();

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
                AppLog.Info("Beam resumed by user", "Tray");
            }
            else
            {
                _session.Pause();
                AppLog.Info("Beam paused by user", "Tray");
            }
        };
        menu.Items.Add(_pauseItem);

        // Launch at sign-in (registry HKCU Run key). Matches Windows Settings wording.
        // CheckOnClick is left off so we control the check state: the toggle performs the
        // registry write, shows a balloon confirmation, and reverts on failure.
        _startupItem = new ToolStripMenuItem("Launch at sign-in")
        {
            Checked = IsStartupEnabled(),
        };
        _startupItem.Click += (_, _) => ToggleStartup();
        menu.Items.Add(_startupItem);

        menu.Items.Add(new ToolStripSeparator());

        // Manual actions.
        var applyNow = new ToolStripMenuItem("Apply projector mode now");
        applyNow.Click += (_, _) =>
        {
            AppLog.Info("Apply-now requested from tray menu", "Tray",
                ("state", _session.State.ToString()));
            ApplyNowRequested?.Invoke();
        };
        menu.Items.Add(applyNow);

        var restoreItem = new ToolStripMenuItem("Restore display");
        restoreItem.Click += (_, _) =>
        {
            AppLog.Info("Restore requested from tray menu", "Tray",
                ("state", _session.State.ToString()));
            RestoreRequested?.Invoke();
        };
        menu.Items.Add(restoreItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit Beam");
        exitItem.Click += (_, _) =>
        {
            AppLog.Info("Exit requested from tray menu", "Tray",
                ("state", _session.State.ToString()));
            ExitRequested?.Invoke();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>Raised when the user picks "Apply projector mode now".</summary>
    public event Action? ApplyNowRequested;

    /// <summary>Raised when the user picks "Restore display".</summary>
    public event Action? RestoreRequested;

    private void OnStateChanged(SessionEvent evt)
    {
        AppLog.Info($"[State] {evt.From} → {evt.To} ({evt.Reason})", "State",
            ("from", evt.From.ToString()),
            ("to", evt.To.ToString()),
            ("reason", evt.Reason));

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
        _icon.Text = evt.To == SessionState.Idle
            ? "Beam — Projector mode"
            : $"Beam — {StatusText(evt.To)}";

        // Notify only on transitions that matter to the user, not every micro-state.
        // When started via autostart (--hidden) the initial idle balloon is suppressed.
        if (!(_startHidden && evt.To == SessionState.Idle)
            && evt.To is SessionState.Active or SessionState.Restoring or SessionState.Paused or SessionState.Idle)
        {
            _icon.ShowBalloonTip(700, "Beam", $"Beam is now {StatusText(evt.To).ToLowerInvariant()}.", ToolTipIcon.Info);
        }
    }

    private void ShowStatus()
    {
        string display = DescribeDisplays();
        string note = $"Status: {StatusText(_session.State)}\n{display}";
        _icon.ShowBalloonTip(3000, "Beam", note, ToolTipIcon.Info);
        AppLog.Info("Status balloon shown", "Tray",
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
        if (_startupItem is not null)
        {
            _startupItem.Checked = IsStartupEnabled();
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
            AppLog.Warn("Canceled sign-in change — could not resolve the executable path.", "Startup");
            _icon.ShowBalloonTip(3000, "Beam", "Could not update the sign-in setting.", ToolTipIcon.Warning);
            return;
        }

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupCommand.RunKeyPath);
            if (enable)
            {
                key.SetValue(StartupCommand.ValueName, StartupCommand.ValueFor(exePath, launchHidden: true));
                AppLog.Info("Added Beam to Windows login (registry Run key)", "Startup");
                _log("[Startup] added Beam to Windows login.");
            }
            else
            {
                key.DeleteValue(StartupCommand.ValueName, throwOnMissingValue: false);
                AppLog.Info("Removed Beam from Windows login (registry Run key)", "Startup");
                _log("[Startup] removed Beam from Windows login.");
            }

            // Persisted state is now the source of truth — reflect it in the checkbox and
            // confirm to the user (the one place the app gives direct feedback on the toggle).
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
            AppLog.Exception(ex, "Startup",
                ("operation", enable ? "enable" : "disable"));
            _log($"[Startup] failed to update login entry: {ex.Message}");

            if (_startupItem is not null)
            {
                _startupItem.Checked = !enable; // revert the toggle
            }

            _icon.ShowBalloonTip(
                3000,
                "Beam",
                "Could not update the sign-in setting.",
                ToolTipIcon.Warning);
        }
    }

    private static string StatusText(SessionState state) => state switch
    {
        SessionState.Idle => "Monitoring",
        SessionState.Detecting => "Detecting…",
        SessionState.Applying => "Applying…",
        SessionState.Active => "Presenting",
        SessionState.Restoring => "Restoring…",
        SessionState.Paused => "Paused",
        _ => state.ToString(),
    };

    private static string PauseItemText(SessionState state) =>
        state == SessionState.Paused ? "Resume Beam" : "Pause Beam";

    public void Dispose()
    {
        _session.StateChanged -= OnStateChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }
}