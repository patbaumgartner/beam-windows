using System.Drawing;
using System.Reflection;
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
        _icon.Icon = LoadAppIcon();
        _icon.Text = "Beam — Projector mode";
        _icon.Visible = true;
        _icon.ContextMenuStrip = BuildMenu();

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowStatus();
            }
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowStatus();
            }
        };
    }

    /// <summary>Loads the embedded 9-frame app icon.</summary>
    private static Icon LoadAppIcon()
    {
        Assembly asm = typeof(TrayService).Assembly;
        using Stream? stream = asm.GetManifestResourceStream("Beam.Tray.Assets.app.ico");
        return stream is not null ? new Icon(stream) : SystemIcons.Application;
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
            }
            else
            {
                _session.Pause();
                _pauseItem.Text = "Resume Beam";
            }
        };
        menu.Items.Add(_pauseItem);

        // Launch at sign-in (registry HKCU Run key). Matches Windows Settings wording.
        _startupItem = new ToolStripMenuItem("Launch at sign-in")
        {
            CheckOnClick = true,
            Checked = IsStartupEnabled(),
        };
        _startupItem.Click += (_, _) => SetStartupEnabled(_startupItem.Checked);
        menu.Items.Add(_startupItem);

        menu.Items.Add(new ToolStripSeparator());

        // Manual actions.
        var applyNow = new ToolStripMenuItem("Apply projector mode now");
        applyNow.Click += (_, _) => ApplyNowRequested?.Invoke();
        menu.Items.Add(applyNow);

        var restoreItem = new ToolStripMenuItem("Restore display");
        restoreItem.Click += (_, _) => RestoreRequested?.Invoke();
        menu.Items.Add(restoreItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit Beam");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>Raised when the user picks "Apply projector mode now".</summary>
    public event Action? ApplyNowRequested;

    /// <summary>Raised when the user picks "Restore display".</summary>
    public event Action? RestoreRequested;

    private void OnStateChanged(SessionEvent evt)
    {
        _log($"[Tray] {evt.From} → {evt.To} ({evt.Reason})");
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
                _log("[Startup] added Beam to Windows login.");
            }
            else
            {
                key.DeleteValue("Beam", throwOnMissingValue: false);
                _log("[Startup] removed Beam from Windows login.");
            }
        }
        catch (Exception ex)
        {
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
