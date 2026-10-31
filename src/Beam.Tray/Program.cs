using Beam.Windows.Display;
using System.Windows.Forms;

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
/// </summary>

/// <summary>
/// ApplicationContext that owns the hidden message-loop window and the tray icon.
/// Disposing this context ends the Application.Run() message loop, cleanly shutting down.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly TrayService _tray;
    private Form? _hostForm;

    public event Action? ApplyNowRequested;
    public event Action? RestoreRequested;

    public TrayApplicationContext(SessionManager session, Action<string> log, bool startHidden)
    {
        _tray = new TrayService(session, log, startHidden);

        _tray.ApplyNowRequested += () => ApplyNowRequested?.Invoke();
        _tray.RestoreRequested += () => RestoreRequested?.Invoke();

        // A hidden form provides the HWND / message loop that NotifyIcon needs to
        // dispatch WM_COMMAND for the context menu and other shell messages.
        // Without this, the menu appears but clicks are silently dropped.
        _hostForm = new Form
        {
            ShowInTaskbar = false,
            Visible = false,
            Opacity = 0,
            Size = new System.Drawing.Size(1, 1),
            FormBorderStyle = FormBorderStyle.None,
            Text = "Beam hidden host"
        };
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
        ApplicationConfiguration.Initialize();

        // Parse --hidden flag: autostart from the installer uses this to suppress the
        // initial balloon when nothing is presenting at boot time.
        bool hidden = args.Any(a => a.Equals("--hidden", StringComparison.OrdinalIgnoreCase) ||
                                    a.Equals("-hidden", StringComparison.OrdinalIgnoreCase) ||
                                    a.Equals("/hidden", StringComparison.OrdinalIgnoreCase));

        AppLog.Info("Beam process starting",
            "Startup",
            ("args", string.Join(",", args)),
            ("hidden", hidden),
            ("dpiAware", true),
            ("logFile", AppLog.DetailedLogPath));

        // Single instance per user. Without this, an autostart instance and a manual
        // launch would run two monitor loops and could double-apply the display fix.
        bool isFirstInstance;
        using (var singleInstance = new Mutex(initiallyOwned: true, @"Local\Beam.SingleInstance", out isFirstInstance))
        {
            if (!isFirstInstance)
            {
                AppLog.Warn("Another Beam instance is already running — exiting.", "Startup");
                return;
            }

            var session = new SessionManager();

            // Action<string> wrapper: bridges components' Action<string> API onto AppLog.
            Action<string> log = msg => AppLog.Info(msg);

            using var monitor = new DisplayMonitor(
                DisplayInventory.Capture,
                TimeSpan.FromMilliseconds(250));

            // TrayApplicationContext owns the hidden host form and the tray icon.
            // Application.Run(context) enters the WinForms message loop. It returns only
            // when ExitThread() is called — i.e. when the user clicks "Exit Beam".
            using var context = new TrayApplicationContext(session, log, hidden);

            var presentation = new PresentationManager(monitor, session, log);
            context.ApplyNowRequested += presentation.ApplyNow;
            context.RestoreRequested += presentation.RestoreNow;

            // Recover first: if a previous Beam was killed mid-fix, restore the original
            // display configuration before we start watching for new changes.
            presentation.TryRecover();

            monitor.Start();

            // Apply fix immediately if mismatch is already present at startup.
            presentation.CheckCurrentState();

            AppLog.Info("Beam started. Watching for external displays.", "Startup",
                ("appVersion", typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"),
                ("osVersion", Environment.OSVersion.ToString()),
                ("machineName", Environment.MachineName),
                ("userName", Environment.UserName));

            session.StateChanged += evt =>
                AppLog.Info($"[State] {evt.From} → {evt.To} ({evt.Reason})", "State");

            Application.Run(context);

            AppLog.Info("Application.Run returned — cleaning up.", "Shutdown");
            session.Dispose();
        }
    }
}
