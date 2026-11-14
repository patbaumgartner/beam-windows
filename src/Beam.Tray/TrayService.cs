using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Beam.App.Diagnostics;
using Beam.App.Presentation;
using Beam.App.Sessions;
using Beam.Core.Diagnostics;
using Beam.Core.Display;
using Beam.Core.Input;
using Beam.Core.Settings;
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
    private ToolStripMenuItem? _undoItem;
    private ToolStripMenuItem? _enableItem;
    private ToolStripMenuItem? _nightLightItem;
    private ToolStripMenuItem? _policyModeItem;
    private ToolStripMenuItem? _policyDpiItem;
    private ToolStripMenuItem? _policyNightLightItem;
    private ToolStripMenuItem? _ignoreMenu;
    private readonly List<ToolStripMenuItem> _pausePresetItems = new();
    private ContextMenuStrip? _menu;
    private System.Windows.Forms.Timer? _cursorGuard;
    private int _displaysRefreshRunning;

    // Optional engine reference for the Settings submenu and the safe-mode balloon
    // (roadmap 1.10/1.12/1.13). Null in tests that only exercise session-state mapping.
    private readonly PresentationManager? _presentation;

    // Cached HKCU Run-key state. Refreshed off the UI thread (see
    // RefreshStartupStateInBackground) so opening the menu never blocks on registry I/O.
    private bool _startupEnabled;
    private int _startupStateVersion;
    private int _startupRefreshRunning;

    // A state change that arrived while the context menu was open. The shell calls it would
    // trigger (icon + tooltip text + balloon) are deferred until the menu closes; see ApplyStateToUi.
    private SessionState? _deferredShellState;

    // B1: current shell-facing state — drives the icon badge and the rich tooltip.
    private SessionState _currentState = SessionState.Idle;
    private bool _safeMode;
    private Icon? _currentIcon;

    // B7: global hotkey host (hidden form owning the RegisterHotKey HWND). Null when no
    // hotkey is configured or registration failed — a hotkey failure never blocks startup.
    private HotkeyHostForm? _hotkeyHost;

    public event Action? ExitRequested;

    public TrayService(
        SessionManager session,
        Action<string>? log = null,
        bool startHidden = false,
        PresentationManager? presentation = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _log = log ?? (_ => { });
        _startHidden = startHidden;
        _presentation = presentation;

        // Captured on the UI thread (TrayService is constructed from Program's thread).
        // State-change callbacks can arrive from the background display-monitor loop, so
        // all WinForms control access must be marshalled back here or the message pump
        // stalls (busy cursor, dead context menu).
        _ui = SynchronizationContext.Current;

        _session.StateChanged += OnStateChanged;
        if (_presentation is not null)
        {
            // UC-12 AC5: safe mode must be visible — surface it with a warning balloon.
            _presentation.SafeModeChanged += OnSafeModeChanged;
            // B5: keep the "Ignore this display" submenu in sync with the engine's list.
            _presentation.IgnoredDisplaysChanged += OnIgnoredDisplaysChanged;
        }
    }

    public void Show()
    {
        // (Re)capture the UI thread's SynchronizationContext. At construction time this is
        // still null because no control (and therefore no WindowsFormsSynchronizationContext)
        // exists yet — the host form is created right before Show() is called. Recapturing
        // here guarantees StateChanged callbacks can be marshalled onto the message loop.
        _ui ??= SynchronizationContext.Current;

        _currentIcon = CreateTrayIcon(TrayPresentation.BadgeFor(_currentState, _safeMode));
        _icon.Icon = _currentIcon;
        _icon.Text = TrayPresentation.TooltipText(_currentState, _safeMode, CurrentExternalName(), _presentation?.CurrentStatus);
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
    private static Icon CreateTrayIcon() => CreateTrayIcon(TrayBadge.None);

    /// <summary>
    /// Creates the tray icon at the correct size for the current system DPI, with the
    /// state badge for the given badge kind (B1).
    ///
    /// The shell renders the icon at the appropriate size based on the taskbar's DPI
    /// context. Providing a properly-sized BITMAP (rather than a tiny 16x16 one)
    /// avoids the blurry upscaling you see with small icons on high-DPI displays.
    /// </summary>
    private static Icon CreateTrayIcon(TrayBadge badge)
    {
        int sizePx = SuggestIconSize();
        using var bmp = new Bitmap(sizePx, sizePx);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        TrayIconRenderer.Draw(g, sizePx, badge);

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
    /// Draws the plain Beam high-beam brand mark into the given graphics context
    /// (no state badge). The rendering lives in <see cref="TrayIconRenderer"/>; this
    /// entry point is kept for the brand-colour pin tests.
    /// </summary>
    internal static void DrawBeamIcon(Graphics g, int size) => TrayIconRenderer.DrawBrand(g, size);

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

        // C2: timed pause presets next to the indefinite pause ("Pause for 15 min",
        // "Pause for 1 h"). Only meaningful when the engine is wired in and not already
        // paused; each item auto-resumes via the manager's pause timer.
        if (_presentation is not null)
        {
            foreach (PausePreset preset in TrayPresentation.TimedPausePresets())
            {
                PausePreset captured = preset;
                var item = new ToolStripMenuItem(TrayPresentation.PausePresetItemText(captured))
                {
                    Enabled = TrayPresentation.CanPauseFor(_session.State),
                };
                item.Click += (_, _) =>
                {
                    if (captured.Duration() is { } duration)
                    {
                        Log.Info("Timed pause requested from tray menu", "Tray",
                            ("preset", captured.ToString()), ("minutes", duration.TotalMinutes.ToString()));
                        _presentation.PauseFor(duration);
                    }
                };
                _pausePresetItems.Add(item);
                menu.Items.Add(item);
            }
        }

        // Launch at sign-in (registry HKCU Run key). Matches Windows Settings wording.
        // CheckOnClick is left off so we control the check state: the toggle performs the
        // registry write, shows a balloon confirmation, and reverts on failure.
        _startupItem = new ToolStripMenuItem("Launch at sign-in")
        {
            Checked = _startupEnabled,
        };
        _startupItem.Click += (_, _) => ToggleStartup();
        menu.Items.Add(_startupItem);

        // Settings submenu (roadmap 1.13): master switch, manual Night Light toggle, and
        // the policy toggles that gate the fix sequence. Only present when the engine is
        // wired in (always true in the tray app; null in tests).
        if (_presentation is not null)
        {
            var settings = new ToolStripMenuItem("Settings");

            _enableItem = new ToolStripMenuItem("Enable Beam")
            {
                Checked = _presentation.IsEnabled,
                CheckOnClick = true,
            };
            _enableItem.Click += (_, _) =>
            {
                bool enabled = _enableItem.Checked;
                Log.Info("Enable Beam toggled from tray menu", "Tray", ("enabled", enabled.ToString()));
                _ = _presentation.SetEnabledAsync(enabled);
            };
            settings.DropDownItems.Add(_enableItem);

            _nightLightItem = new ToolStripMenuItem("Night Light")
            {
                Checked = true,
                CheckOnClick = true,
            };
            _nightLightItem.Click += (_, _) =>
            {
                bool on = _nightLightItem.Checked;
                Log.Info("Night Light toggled manually from tray menu", "Tray", ("on", on.ToString()));
                _presentation.SetNightLight(on);
            };
            settings.DropDownItems.Add(_nightLightItem);

            var policy = new ToolStripMenuItem("Policy");
            _policyModeItem = new ToolStripMenuItem("Control display mode")
            {
                Checked = _presentation.UseMode,
                CheckOnClick = true,
            };
            _policyModeItem.Click += (_, _) => _presentation.UseMode = _policyModeItem.Checked;
            policy.DropDownItems.Add(_policyModeItem);

            _policyDpiItem = new ToolStripMenuItem("Control DPI")
            {
                Checked = _presentation.UseDpi,
                CheckOnClick = true,
            };
            _policyDpiItem.Click += (_, _) => _presentation.UseDpi = _policyDpiItem.Checked;
            policy.DropDownItems.Add(_policyDpiItem);

            _policyNightLightItem = new ToolStripMenuItem("Turn Night Light off in fix")
            {
                Checked = _presentation.UseNightLight,
                CheckOnClick = true,
            };
            _policyNightLightItem.Click += (_, _) => _presentation.UseNightLight = _policyNightLightItem.Checked;
            policy.DropDownItems.Add(_policyNightLightItem);
            settings.DropDownItems.Add(policy);

            menu.Items.Add(settings);

            // B5: per-display "Ignore this display" submenu (UC-08/UC-10 subset, V5).
            // Rebuilt from the engine's cached external paths — never a live display
            // query on the UI thread (A6).
            _ignoreMenu = new ToolStripMenuItem("Ignore this display");
            menu.Items.Add(_ignoreMenu);
            RebuildIgnoreItems();
        }

        menu.Items.Add(new ToolStripSeparator());

        // Manual actions. Enabled state is derived from the session state so a click is
        // never a no-op (see TrayPresentation.CanApplyNow / CanUndo). C2: the label
        // carries the global-hotkey shortcut hint when one is configured.
        _applyItem = new ToolStripMenuItem(TrayPresentation.ApplyItemText(HotkeyDisplay()))
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

        // UC-09: one-click undo of an active fix (roadmap 1.12). Wired to RestoreNowAsync
        // in the composition root; enabled only while a fix is actually active (AC3).
        _undoItem = new ToolStripMenuItem("Undo projector mode")
        {
            Enabled = TrayPresentation.CanUndo(_session.State),
        };
        _undoItem.Click += (_, _) =>
        {
            Log.Info("Undo requested from tray menu", "Tray",
                ("state", _session.State.ToString()));
            RestoreRequested?.Invoke();
        };
        menu.Items.Add(_undoItem);

        // C1/C4: open the settings and diagnostics windows (WPF, hosted by the
        // composition root). Only present when the engine is wired in.
        if (_presentation is not null)
        {
            var settingsItem = new ToolStripMenuItem(TrayStrings.SettingsMenuItem);
            settingsItem.Click += (_, _) =>
            {
                Log.Info("Settings window requested from tray menu", "Tray");
                SettingsRequested?.Invoke();
            };
            menu.Items.Add(settingsItem);

            var diagnosticsItem = new ToolStripMenuItem(TrayStrings.DiagnosticsMenuItem);
            diagnosticsItem.Click += (_, _) =>
            {
                Log.Info("Diagnostics window requested from tray menu", "Tray");
                DiagnosticsRequested?.Invoke();
            };
            menu.Items.Add(diagnosticsItem);
        }

        menu.Items.Add(new ToolStripSeparator());

        // Supportability: open the log folder so beam-detailed.jsonl can be attached to a
        // bug report.
        var logsItem = new ToolStripMenuItem("Open log folder");
        logsItem.Click += (_, _) =>
        {
            Log.Info("Open log folder requested from tray menu", "Tray",
                ("dir", FileLogSink.DefaultDirectory));
            try
            {
                // A7: explicit shell launch — explorer.exe is resolved via the shell.
                var startInfo = new System.Diagnostics.ProcessStartInfo("explorer.exe", FileLogSink.DefaultDirectory)
                {
                    UseShellExecute = true,
                };
                System.Diagnostics.Process.Start(startInfo);
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

    /// <summary>Raised when the user picks "Undo projector mode" (UC-09 one-click revert).</summary>
    public event Action? RestoreRequested;

    /// <summary>C1: raised when the user picks "Settings…" — the composition root opens the settings window.</summary>
    public event Action? SettingsRequested;

    /// <summary>C4: raised when the user picks "Diagnostics…" — the composition root opens the diagnostics window.</summary>
    public event Action? DiagnosticsRequested;

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
        _currentState = evt.To;
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
        if (_undoItem is not null)
        {
            _undoItem.Enabled = TrayPresentation.CanUndo(evt.To);
        }
        foreach (ToolStripMenuItem item in _pausePresetItems)
        {
            item.Enabled = TrayPresentation.CanPauseFor(evt.To);
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
    /// Applies a session state to the shell-facing parts of the icon: the state badge, the
    /// rich tooltip (full status line, capped at the NotifyIcon.Text limit), and the
    /// transition balloon. Must only be called when no context menu is displayed.
    /// </summary>
    private void ApplyShellState(SessionState state)
    {
        _currentState = state;

        // B1: state badge on the icon (idle = plain mark, Active = green, Paused = amber,
        // safe mode = red). The old icon is disposed after the swap so no GDI handle leaks.
        Icon fresh = CreateTrayIcon(TrayPresentation.BadgeFor(state, _safeMode));
        Icon? previous = _currentIcon;
        _currentIcon = fresh;
        _icon.Icon = fresh;
        previous?.Dispose();

        // B1: the tooltip is the full status line while presenting
        // (e.g. "Presenting — Epson EB-2250U · 1920×1080@60 · 100% · Night Light off").
        _icon.Text = TrayPresentation.TooltipText(state, _safeMode, CurrentExternalName(), _presentation?.CurrentStatus);

        // Notify only when the fix lands; errors/safe mode raise their own warning
        // balloon via OnSafeModeChanged. 5 s so it is seen but not nagging.
        if (TrayPresentation.ShouldBalloon(state))
        {
            _icon.ShowBalloonTip(5000, "Beam", $"Beam is now {StatusText(state).ToLowerInvariant()}.", ToolTipIcon.Info);
        }
    }

    /// <summary>
    /// B1: the friendly name of the first connected (non-ignored) external display,
    /// for the rich tooltip. Cache only — never queries the display config on the UI thread.
    /// </summary>
    private string? CurrentExternalName() =>
        _presentation?.CurrentExternalPaths.FirstOrDefault(p => !p.Target.IsInternal)?.Target.FriendlyName;

    // ── B7: global hotkey ──────────────────────────────────────────────────────

    /// <summary>Whether a global hotkey is currently registered with the OS.</summary>
    public bool HotkeyRegistered { get; private set; }

    /// <summary>
    /// B7: registers the global hotkey (default Win+Alt+P, configurable in settings).
    /// Pressing it toggles apply/undo, exactly like the menu items. A registration
    /// failure (e.g. another app owns the combo) is logged as a warning and never
    /// blocks startup. Null disables the hotkey.
    /// </summary>
    public void EnableHotkey(HotkeyCombo? combo)
    {
        DisableHotkey();
        if (combo is null)
        {
            return;
        }

        try
        {
            var host = new HotkeyHostForm();
            if (!host.TryRegister(combo))
            {
                int error = Marshal.GetLastWin32Error();
                Log.Warn("Global hotkey registration failed — continuing without it", "Tray",
                    ("combo", combo.DisplayString), ("win32Error", error));
                _log($"[Hotkey] could not register {combo.DisplayString} (Win32 error {error}); continuing without it.");
                host.Dispose();
                return;
            }

            host.Pressed += OnHotkeyPressed;
            _hotkeyHost = host;
            HotkeyRegistered = true;
            Log.Info("Global hotkey registered", "Tray", ("combo", combo.DisplayString));
            _log($"[Hotkey] {combo.DisplayString} registered — toggles projector mode.");
        }
        catch (Exception ex)
        {
            Log.Exception(ex, "Hotkey",
                ("operation", "register"), ("combo", combo.DisplayString));
            _log($"[Hotkey] failed to register {combo.DisplayString}: {ex.Message}");
        }
    }

    /// <summary>Unregisters the hotkey, if any. Idempotent.</summary>
    public void DisableHotkey()
    {
        if (_hotkeyHost is null)
        {
            return;
        }

        _hotkeyHost.Pressed -= OnHotkeyPressed;
        _hotkeyHost.Dispose();
        _hotkeyHost = null;
        HotkeyRegistered = false;
    }

    /// <summary>B7: hotkey pressed — toggle apply/undo per the current session state.</summary>
    private void OnHotkeyPressed()
    {
        void Toggle()
        {
            switch (TrayPresentation.ActionFor(_session.State))
            {
                case HotkeyAction.Apply:
                    _log("[Hotkey] pressed — applying projector mode.");
                    ApplyNowRequested?.Invoke();
                    break;
                case HotkeyAction.Undo:
                    _log("[Hotkey] pressed — undoing projector mode.");
                    RestoreRequested?.Invoke();
                    break;
            }
        }

        if (_ui is not null && _ui != SynchronizationContext.Current)
        {
            _ui.Post(_ => Toggle(), null);
        }
        else
        {
            Toggle();
        }
    }

    private bool IsContextMenuOpen => _menu is { Visible: true };

    /// <summary>
    /// C3: shows an info balloon on the tray icon (used for the first-run toast).
    /// Marshalled to the UI thread; safe to call from any thread.
    /// </summary>
    public void ShowBalloon(string title, string text, int timeoutMs = 5000)
    {
        void Show()
        {
            if (!IsContextMenuOpen)
            {
                _icon.ShowBalloonTip(timeoutMs, title, text, ToolTipIcon.Info);
            }
        }

        if (_ui is not null && _ui != SynchronizationContext.Current)
        {
            _ui.Post(_ => Show(), null);
        }
        else
        {
            Show();
        }
    }

    /// <summary>
    /// C5: the taskbar was recreated (explorer restart) — re-register the tray icon so it
    /// does not vanish from the notification area. Re-applies the current shell state
    /// (badge + tooltip) afterwards.
    /// </summary>
    public void OnTaskbarCreated()
    {
        void ReRegister()
        {
            Log.Info("Taskbar recreated — re-registering tray icon", "Tray");
            _icon.Visible = false;
            _icon.Visible = true;
            ApplyShellState(_currentState);
        }

        if (_ui is not null && _ui != SynchronizationContext.Current)
        {
            _ui.Post(_ => ReRegister(), null);
        }
        else
        {
            ReRegister();
        }
    }

    /// <summary>
    /// C2: the display string of the configured global hotkey (e.g. "Win+Alt+P") for the
    /// shortcut hint on the Apply item, or null when none is configured/parseable.
    /// </summary>
    private string? HotkeyDisplay() =>
        _presentation is not null && HotkeyParser.TryParse(_presentation.Hotkey, out HotkeyCombo? combo) && combo is not null
            ? combo.DisplayString
            : null;

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
        // A6: QueryDisplayConfig can stall on a busy shell/driver — never run it on the
        // UI thread (the same class of stall the cursor-guard exists for). Query on the
        // thread pool, then marshal the balloon back to the UI thread.
        Task.Run(() =>
        {
            string display = DescribeDisplays();

            void Show()
            {
                string note = $"Status: {StatusText(_session.State)}\n{display}";
                _icon.ShowBalloonTip(3000, "Beam", note, ToolTipIcon.Info);
                Log.Info("Status balloon shown", "Tray",
                    ("state", _session.State.ToString()),
                    ("displays", display.Replace("\n", " | ")));
            }

            if (_ui is not null && _ui != SynchronizationContext.Current)
            {
                _ui.Post(_ => Show(), null);
            }
            else
            {
                Show();
            }
        });
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
            _applyItem.Text = TrayPresentation.ApplyItemText(HotkeyDisplay()); // C2: shortcut hint
        }
        if (_undoItem is not null)
        {
            _undoItem.Enabled = TrayPresentation.CanUndo(_session.State);
        }
        foreach (ToolStripMenuItem item in _pausePresetItems)
        {
            item.Enabled = TrayPresentation.CanPauseFor(_session.State);
        }
        if (_startupItem is not null)
        {
            // Serve the cached value — Opening runs on the UI thread on the way into the
            // drop-down's modal loop, so a synchronous registry read here would block the
            // menu from appearing. Kick off a refresh and apply it when it lands.
            _startupItem.Checked = _startupEnabled;
            RefreshStartupStateInBackground();
        }
        if (_ignoreMenu is not null)
        {
            // B5: rebuild from the cache, then refresh the cache off-thread (A6).
            RebuildIgnoreItems();
            RefreshDisplaysInBackground();
        }
    }

    /// <summary>
    /// B5: rebuilds the "Ignore this display" submenu from the engine's cached external
    /// paths. Reads only the cache — a live QueryDisplayConfig never runs on the UI
    /// thread (A6); <see cref="RefreshDisplaysInBackground"/> refreshes the cache off-thread.
    /// </summary>
    private void RebuildIgnoreItems()
    {
        if (_ignoreMenu is null || _presentation is null)
        {
            return;
        }

        IReadOnlyList<DisplayPath> externals = _presentation.CurrentExternalPaths;
        _ignoreMenu.DropDownItems.Clear();
        _ignoreMenu.Enabled = TrayPresentation.CanIgnoreDisplay(externals.Count);

        if (externals.Count == 0)
        {
            _ignoreMenu.DropDownItems.Add(new ToolStripMenuItem(TrayPresentation.IgnoreMenuEmptyText) { Enabled = false });
            return;
        }

        IReadOnlyCollection<string> ignored = _presentation.IgnoredDisplays;
        foreach (DisplayPath path in externals)
        {
            string identity = path.Target.Identity;
            string name = path.Target.FriendlyName;
            bool isIgnored = ignored.Contains(identity);
            var item = new ToolStripMenuItem(TrayPresentation.IgnoreItemText(name, isIgnored))
            {
                Checked = isIgnored,
                CheckOnClick = true,
            };
            item.Click += (_, _) =>
            {
                // CheckOnClick flips Checked before Click fires, so this is the new state.
                bool nowIgnored = item.Checked;
                Log.Info(nowIgnored ? "Display ignored from tray menu" : "Display un-ignored from tray menu",
                    "Tray", ("identity", identity), ("name", name));
                if (nowIgnored)
                {
                    _presentation.IgnoreDisplay(identity);
                }
                else
                {
                    _presentation.UnignoreDisplay(identity);
                }

                RebuildIgnoreItems();
            };
            _ignoreMenu.DropDownItems.Add(item);
        }
    }

    private void OnIgnoredDisplaysChanged(IReadOnlyCollection<string> ignored)
    {
        void Update()
        {
            Log.Info("Ignored-displays list changed", "Tray", ("count", ignored.Count.ToString()));
            RebuildIgnoreItems();
        }

        if (_ui is not null && _ui != SynchronizationContext.Current)
        {
            _ui.Post(_ => Update(), null);
        }
        else
        {
            Update();
        }
    }

    /// <summary>
    /// A6: the ignore submenu reads the engine's cached topology. When the menu opens we
    /// refresh that cache on the thread pool (QueryDisplayConfig can stall on a busy
    /// shell/driver), then rebuild the items on the UI thread when it lands.
    /// </summary>
    private void RefreshDisplaysInBackground()
    {
        if (_presentation is null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _displaysRefreshRunning, 1) == 1)
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                _presentation.RefreshExternalPaths();
            }
            catch (Exception ex)
            {
                Log.Warn("Display refresh for the ignore menu failed", "Tray", ("error", ex.Message));
            }
            finally
            {
                Volatile.Write(ref _displaysRefreshRunning, 0);
            }

            _ui?.Post(_ => RebuildIgnoreItems(), null);
        });
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

    /// <summary>
    /// UC-12 AC5: safe mode must be visible. Entering safe mode shows a warning balloon;
    /// leaving it does not (the next successful fix is itself visible via the status line).
    /// Marshalled to the UI thread because the event fires on the monitor loop.
    /// </summary>
    public void OnSafeModeChanged(bool active)
    {
        // B1: the red safe-mode badge and tooltip must reflect this immediately, on both
        // entering and leaving safe mode.
        _safeMode = active;
        if (IsContextMenuOpen)
        {
            _deferredShellState = _currentState; // re-applied (badge + tooltip) when the menu closes
        }
        else
        {
            ApplyShellState(_currentState);
        }

        if (!active)
        {
            return;
        }

        void Show()
        {
            // Shell calls while the context menu is open are deferred elsewhere; the
            // balloon is less urgent than the state line, so skip it there.
            if (IsContextMenuOpen)
            {
                return;
            }

            _icon.ShowBalloonTip(
                5000,
                "Beam — safe mode",
                "Beam could not apply the projector fix twice in a row, so it paused itself to protect your display. Choose 'Apply projector mode now' to retry.",
                ToolTipIcon.Warning);
        }

        if (_ui is not null && _ui != SynchronizationContext.Current)
        {
            _ui.Post(_ => Show(), null);
        }
        else
        {
            Show();
        }
    }

    public void Dispose()
    {
        _session.StateChanged -= OnStateChanged;
        if (_presentation is not null)
        {
            _presentation.SafeModeChanged -= OnSafeModeChanged;
            _presentation.IgnoredDisplaysChanged -= OnIgnoredDisplaysChanged;
        }
        _cursorGuard?.Stop();
        _cursorGuard?.Dispose();
        _cursorGuard = null;
        DisableHotkey(); // B7: unregister the global hotkey
        _icon.Visible = false;
        _icon.Dispose();
        _currentIcon?.Dispose(); // B1: the last badge icon (NotifyIcon.Dispose does not own it)
        _currentIcon = null;
        _menu?.Dispose();
        _menu = null;
    }
}
