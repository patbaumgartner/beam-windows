using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Beam.App.Diagnostics;
using Beam.App.Journal;
using Beam.App.Presentation;
using Beam.App.Sessions;
using Beam.App.Settings;
using Beam.App.Startup;
using Beam.App.Status;
using Beam.Core.Diagnostics;
using Beam.Core.Input;
using Beam.Core.Startup;
using Beam.Windows.Display;
using Beam.Windows.Extras;
using Beam.Windows.NightLight;

namespace Beam.Tray;

/// <summary>
/// Beam tray application entry point.
///
/// Runs a per-user background process:
///   1. Polls for display changes (DisplayMonitor, 250 ms).
///   2. When an external 16:9 display is mirrored with an aspect mismatch, applies the
///      presentation fix: Night Light off → duplicate/16:9 shared source → readable DPI.
///   3. When the external display disconnects, restores the original topology, DPI, and
///      Night Light state.
///
/// This file is the composition root: it is the only place that knows both the ports
/// (Core) and the concrete Windows adapters.
/// </summary>

/// <summary>
/// ApplicationContext that owns the hidden message-loop window and the tray icon.
/// Disposing this context ends the Application.Run() message loop, cleanly shutting down.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly TrayService _tray;
    private readonly HostForm _hostForm;

    /// <summary>
    /// C5: the hidden host form. Besides providing the HWND/message loop for the
    /// NotifyIcon, it watches for the shell's <c>TaskbarCreated</c> broadcast — sent when
    /// Explorer restarts and the taskbar is recreated — and tells the tray to re-register
    /// its icon so it does not vanish from the notification area.
    /// </summary>
    private sealed class HostForm : Form
    {
        private const string TaskbarCreatedMessageName = "TaskbarCreated";

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegisterWindowMessage(string lpString);

        private readonly TrayService _tray;
        private readonly int _taskbarCreatedMsg;

        public HostForm(TrayService tray)
        {
            _tray = tray;
            _taskbarCreatedMsg = RegisterWindowMessage(TaskbarCreatedMessageName);

            ShowInTaskbar = false;
            Visible = false;
            Opacity = 0;
            Size = new System.Drawing.Size(1, 1);
            FormBorderStyle = FormBorderStyle.None;
            Text = "Beam hidden host";
        }

        protected override void WndProc(ref Message m)
        {
            if (_taskbarCreatedMsg != 0 && m.Msg == _taskbarCreatedMsg)
            {
                // C5: taskbar recreated (Explorer restart) — re-register the tray icon.
                _tray.OnTaskbarCreated();
            }

            base.WndProc(ref m);
        }
    }

    public event Action? ApplyNowRequested;
    public event Action? RestoreRequested;

    /// <summary>B7: the tray service, so the composition root can register the global hotkey.</summary>
    public TrayService Tray => _tray;

    public TrayApplicationContext(
        SessionManager session,
        Action<string> log,
        bool startHidden,
        PresentationManager? presentation = null)
    {
        _tray = new TrayService(session, log, startHidden, presentation);

        _tray.ApplyNowRequested += () => ApplyNowRequested?.Invoke();
        _tray.RestoreRequested += () => RestoreRequested?.Invoke();

        // A hidden form provides the HWND / message loop that NotifyIcon needs to
        // dispatch WM_COMMAND for the context menu and other shell messages.
        // Without this, the menu appears but clicks are silently dropped.
        // HostForm additionally re-registers the tray icon on TaskbarCreated (C5).
        _hostForm = new HostForm(_tray);
        _hostForm.FormClosed += (_, _) => ExitThread();

        _tray.ExitRequested += () =>
        {
            log("[Tray] Exit requested — shutting down.");
            _hostForm?.Close(); // triggers ThreadExit → Application.Run returns
        };

        _hostForm.Show(); // must be shown (not just created) to start the message pump
        _tray.Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _hostForm?.Close();
            _hostForm?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Set DPI awareness before any window is created — required for the shell to
        // report the correct icon size in the system tray on high-DPI displays.
        DisplayNative.SetProcessDpiAwarenessContext(DisplayNative.DpiAwarenessPerMonitorV2);

        // Install the production log sink before anything else can log. C4: a
        // TimelineSink decorates the file sink and records every entry into a
        // 200-event ring buffer that the diagnostics window shows live.
        var sink = new FileLogSink();
        var timeline = new TimelineBuffer();
        var timelineSink = new TimelineSink(sink, timeline);
        Log.SetSink(timelineSink);

        ApplicationConfiguration.Initialize();

        // Parse --hidden flag: autostart from the installer uses this to suppress the
        // initial balloon when nothing is presenting at boot time.
        bool hidden = args.Any(a => a.Equals("--hidden", StringComparison.OrdinalIgnoreCase) ||
                                    a.Equals("-hidden", StringComparison.OrdinalIgnoreCase) ||
                                    a.Equals("/hidden", StringComparison.OrdinalIgnoreCase));

        Log.Info("Beam process starting",
            "Startup",
            ("args", string.Join(",", args)),
            ("hidden", hidden),
            ("dpiAware", true),
            ("logFile", sink.DetailedLogPath));

        // Single instance per user. Without this, an autostart instance and a manual
        // launch would run two monitor loops and could double-apply the display fix.
        // This lifetime singleton is separate from DisplayOperationLock, which the tray
        // and CLI acquire only around individual display mutations.
        bool isFirstInstance;
        using var singleInstance = new Mutex(initiallyOwned: true, SingleInstanceLock.MutexName, out isFirstInstance);
        if (!isFirstInstance)
        {
            // A5: name the holder when the lock file knows it, so a mutex squat is
            // diagnosable from the log instead of a bare "already running".
            int? holderPid = InstanceLockFile.ReadPid();
            Log.Warn("Another Beam instance is already running — exiting.", "Startup",
                ("holderPid", holderPid ?? -1));
            return;
        }

        // A5: record which process holds the mutex (PID + start time).
        InstanceLockFile.Write();

        using var session = new SessionManager();

        // Action<string> wrapper: bridges components' Action<string> API onto the log sink.
        Action<string> log = msg => Log.Info(msg, "Tray");

        using var monitor = new DisplayMonitor(
            DisplayInventory.Capture,
            TimeSpan.FromMilliseconds(250));

        // B4: persisted settings (%LOCALAPPDATA%\Beam\settings.json). A corrupt file is set
        // aside as .bad and defaults are used; that is logged once here (A4: the log records
        // the file name only, never the user-profile path — Phase C turns this into a toast).
        var settingsStore = new SettingsStore();
        settingsStore.CorruptFileSetAside += badPath => Log.Warn(
            "Corrupt settings file set aside — defaults in use",
            "Settings",
            ("badFile", Path.GetFileName(badPath)));

        // B3 presentation extras: keep-awake (owned, disposed with the process),
        // DND/toasts, and audio-to-HDMI. All best-effort; failures never fail a session.
        using var keepAwake = new KeepAwakeAdapter();
        using var presentation = new PresentationManager(
            monitor,
            new DisplayInventory(),
            new Beam.Windows.Display.DisplayModeController(),
            new WindowsDpiController(),
            new NightLightController(),
            new JournalStore(),
            session,
            settings: settingsStore,
            keepAwake: keepAwake,
            dnd: new DndController(),
            audio: new AudioController(),
            statusWriter: new StatusFile(),
            operationLockFactory: () => DisplayOperationLock.TryAcquire());

        // TrayApplicationContext owns the hidden host form and the tray icon.
        // Application.Run(context) enters the WinForms message loop. It returns only
        // when ExitThread() is called — i.e. when the user clicks "Exit Beam".
        using var context = new TrayApplicationContext(session, log, hidden, presentation);
        context.ApplyNowRequested += () => _ = presentation.ApplyNowAsync();
        context.RestoreRequested += () => _ = presentation.RestoreNowAsync();

        // C1/C4: the tray menu's "Settings…" / "Diagnostics…" open the WPF windows
        // hosted inside this single exe (no MSIX packaging needed).
        string appVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        string statusPath = Path.Combine(FileLogSink.DefaultDirectory, "status.json");
        string[] diagSecrets = { Environment.MachineName, Environment.UserName }; // A4: redacted from exports
        context.Tray.SettingsRequested += () =>
        {
            try
            {
                var window = new SettingsWindow(new SettingsViewModel(presentation)
                {
                    OpenLogFolder = () => OpenLogFolder(),
                });
                window.Show();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "Tray", ("operation", "open-settings"));
            }
        };
        context.Tray.DiagnosticsRequested += () =>
        {
            try
            {
                var window = new DiagnosticsWindow(new DiagnosticsViewModel(
                    presentation, session, timeline, appVersion,
                    FileLogSink.DefaultDirectory, statusPath, diagSecrets));
                window.Show();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "Tray", ("operation", "open-diagnostics"));
            }
        };

        // C3: first-run toast, shown once per install (UC-15): "Beam is running. Plug in
        // a projector — I'll handle the rest." Skipped for --hidden autostart launches.
        if (!hidden && presentation.ShouldShowFirstRunToast)
        {
            context.Tray.ShowBalloon(TrayStrings.FirstRunToastTitle, TrayStrings.FirstRunToastText);
            presentation.MarkFirstRunToastShown();
            Log.Info("First-run toast shown", "Tray");
        }

        // B7: global hotkey (default Win+Alt+P, configurable in settings). A parse or
        // registration failure is logged as a warning and never blocks startup.
        if (HotkeyParser.TryParse(presentation.Hotkey, out HotkeyCombo? hotkey) && hotkey is not null)
        {
            context.Tray.EnableHotkey(hotkey);
        }
        else
        {
            Log.Warn("Configured hotkey is invalid — continuing without it", "Tray",
                ("hotkey", presentation.Hotkey));
        }

        // Recover first: if a previous Beam was killed mid-fix, restore the original
        // display configuration before we start watching for new changes.
        presentation.TryRecoverAsync().GetAwaiter().GetResult();

        monitor.Start();

        // B5/A6: warm the "Ignore this display" menu cache off the startup thread — the
        // tray menu reads this cache on the UI thread and must never query displays there.
        _ = Task.Run(() => presentation.RefreshExternalPaths());

        // Apply the fix immediately if a mismatch is already present at startup.
        _ = presentation.CheckCurrentStateAsync();

        // A4: no machine/user names in the log — logs are what users attach to bug
        // reports, so they must not carry PII. OS + app version are enough to triage.
        Log.Info("Beam started. Watching for external displays.", "Startup",
            ("appVersion", typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"),
            ("osVersion", Environment.OSVersion.ToString()));

        Application.Run(context);

        Log.Info("Application.Run returned — cleaning up.", "Shutdown");
    }

    /// <summary>A7: opens the log folder in Explorer (explicit shell launch).</summary>
    private static void OpenLogFolder()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("explorer.exe", FileLogSink.DefaultDirectory)
            {
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open log folder", "Tray", ("error", ex.Message));
        }
    }
}
