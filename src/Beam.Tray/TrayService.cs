using System.Drawing;
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
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // P/Invoke for system DPI — used to pick the right icon size for the shell.
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private readonly NotifyIcon _icon = new();
    private readonly SessionManager _session;
    private readonly Action<string> _log;
    private readonly bool _startHidden;

    private ToolStripMenuItem? _pauseItem;
    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _startupItem;

    public event Action? ExitRequested;

    public TrayService(SessionManager session, Action<string>? log = null, bool startHidden = false)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _log = log ?? (_ => { });
        _startHidden = startHidden;
        _session.StateChanged += OnStateChanged;
    }

    public void Show()
    {
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

        // Icon.FromHandle creates an icon from a GDI bitmap; the shell takes ownership
        // and will scale it correctly within the DPI context of the system tray.
        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    /// <summary>
    /// Suggests an icon size in pixels based on the current system DPI.
    /// GetDpiForSystem() returns raw DPI: 96 (100%), 144 (150%), 192 (200%), etc.
    /// Small (24 px) on 100% DPI, medium (32 px) on 150%, large (48 px) on 200%+.
    /// </summary>
    private static int SuggestIconSize()
    {
        uint dpi = GetDpiForSystem();
        return dpi switch
        {
            >= 192 => 48, // 192+ DPI  →  200 %+ scaling  →  48 px (sharpest at high DPI)
            >= 144 => 32, // 144–191 DPI → 150 % scaling  →  32 px
            _      => 24, //  96–143 DPI → 100–150 %     →  24 px
        };
    }

    /// <summary>Draws a simple projector-beam icon into the given graphics context.</summary>
    private static void DrawProjectorIcon(Graphics g, int size)
    {
        // Projector body — rounded rectangle
        var bodyRect = new RectangleF(size * 0.1f, size * 0.3f, size * 0.8f, size * 0.55f);
        using var bodyBrush = new SolidBrush(Color.FromArgb(80, 130, 200));
        using var bodyPen = new Pen(Color.FromArgb(30, 80, 180), size * 0.05f);
        g.FillEllipse(bodyBrush, bodyRect);
        g.DrawEllipse(bodyPen, bodyRect);

        // Lens (inner circle)
        float lensR = size * 0.15f;
        float lensX = size * 0.85f;
        float lensY = size * 0.575f;
        using var lensBrush = new SolidBrush(Color.FromArgb(180, 220, 255));
        g.FillEllipse(lensBrush, lensX - lensR, lensY - lensR, lensR * 2, lensR * 2);
        using var lensPen = new Pen(Color.FromArgb(20, 60, 150), size * 0.04f);
        g.DrawEllipse(lensPen, lensX - lensR, lensY - lensR, lensR * 2, lensR * 2);

        // Light beam (lines going to the left)
        var beamColor = Color.FromArgb(255, 240, 160);
        var beamPen = new Pen(beamColor, size * 0.08f);
        beamPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
        beamPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
        beamPen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;

        // Three beam lines diverging to the left
        g.DrawLine(beamPen, size * 0.1f, lensY, size * 0.0f, size * 0.08f);
        g.DrawLine(beamPen, size * 0.1f, lensY, size * 0.0f, lensY);
        g.DrawLine(beamPen, size * 0.1f, lensY, size * 0.0f, size * 0.92f);

        // Status dot (green = active, gray = idle — always render as "running" here)
        using var dotBrush = new SolidBrush(Color.FromArgb(80, 200, 100));
        float dotR = size * 0.07f;
        float dotX = size * 0.2f;
        float dotY = size * 0.15f;
        g.FillEllipse(dotBrush, dotX - dotR, dotY - dotR, dotR * 2, dotR * 2);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.ShowImageMargin = false;

        // Status (read-only header).
        _statusItem = new ToolStripMenuItem(StatusText(_session.State)) { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        // Pause / resume.
        _pauseItem = new ToolStripMenuItem("Pause Beam");
        _pauseItem.Click += (_, _) =>
        {
            if (_session.State == SessionState.Paused)
            {
                _session.Resume();
                _pauseItem.Text = "Pause Beam";
                AppLog.Info("Beam resumed by user", "Tray");
            }
            else
            {
                _session.Pause();
                _pauseItem.Text = "Resume Beam";
                AppLog.Info("Beam paused by user", "Tray");
            }
        };
        menu.Items.Add(_pauseItem);

        // Launch at sign-in (registry HKCU Run key). Matches Windows Settings wording.
        _startupItem = new ToolStripMenuItem("Launch at sign-in")
        {
            CheckOnClick = true,
            Checked = IsStartupEnabled(),
        };
        _startupItem.Click += (_, _) => SetStartupEnabled(_startupItem!.Checked);
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

        if (_statusItem is null)
        {
            return;
        }

        _statusItem.Text = StatusText(evt.To);
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

    private static string StartupCommand =>
        $"\"{Environment.ProcessPath}\"";

    private bool IsStartupEnabled()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath)!;
            return key?.GetValue("Beam") is string v && v.Equals(StartupCommand, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void SetStartupEnabled(bool enable)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enable)
            {
                key.SetValue("Beam", StartupCommand);
                AppLog.Info("Added Beam to Windows login (registry Run key)", "Startup");
                _log("[Startup] added Beam to Windows login.");
            }
            else
            {
                key.DeleteValue("Beam", throwOnMissingValue: false);
                AppLog.Info("Removed Beam from Windows login (registry Run key)", "Startup");
                _log("[Startup] removed Beam from Windows login.");
            }
        }
        catch (Exception ex)
        {
            AppLog.Exception(ex, "Startup",
                ("operation", enable ? "enable" : "disable"));
            _log($"[Startup] failed to update login entry: {ex.Message}");
            _startupItem!.Checked = !enable; // revert the toggle
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

    public void Dispose()
    {
        _session.StateChanged -= OnStateChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }
}