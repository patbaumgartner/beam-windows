using System.Windows.Forms;
using Beam.App.Diagnostics;
using Beam.App.Journal;
using Beam.App.Presentation;
using Beam.App.Sessions;
using Beam.Core.Diagnostics;
using Beam.Windows.Display;
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
    private readonly Form? _hostForm;

    public event Action? ApplyNowRequested;
    public event Action? RestoreRequested;

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
        _hostForm = new Form
        {
            ShowInTaskbar = false,
            Visible = false,
            Opacity = 0,
            Size = new System.Drawing.Size(1, 1),
            FormBorderStyle = FormBorderStyle.None,
            Text = "Beam hidden host",
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

        // Install the production log sink before anything else can log.
        var sink = new FileLogSink();
        Log.SetSink(sink);

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
        bool isFirstInstance;
        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\Beam.SingleInstance", out isFirstInstance);
        if (!isFirstInstance)
        {
            Log.Warn("Another Beam instance is already running — exiting.", "Startup");
            return;
        }

        using var session = new SessionManager();

        // Action<string> wrapper: bridges components' Action<string> API onto the log sink.
        Action<string> log = msg => Log.Info(msg, "Tray");

        using var monitor = new DisplayMonitor(
            DisplayInventory.Capture,
            TimeSpan.FromMilliseconds(250));
        using var presentation = new PresentationManager(
            monitor,
            new DisplayInventory(),
            new Beam.Windows.Display.DisplayModeController(),
            new WindowsDpiController(),
            new NightLightController(),
            new JournalStore(),
            session);

        // TrayApplicationContext owns the hidden host form and the tray icon.
        // Application.Run(context) enters the WinForms message loop. It returns only
        // when ExitThread() is called — i.e. when the user clicks "Exit Beam".
        using var context = new TrayApplicationContext(session, log, hidden, presentation);
        context.ApplyNowRequested += () => _ = presentation.ApplyNowAsync();
        context.RestoreRequested += () => _ = presentation.RestoreNowAsync();

        // Recover first: if a previous Beam was killed mid-fix, restore the original
        // display configuration before we start watching for new changes.
        presentation.TryRecoverAsync().GetAwaiter().GetResult();

        monitor.Start();

        // Apply the fix immediately if a mismatch is already present at startup.
        _ = presentation.CheckCurrentStateAsync();

        Log.Info("Beam started. Watching for external displays.", "Startup",
            ("appVersion", typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"),
            ("osVersion", Environment.OSVersion.ToString()),
            ("machineName", Environment.MachineName),
            ("userName", Environment.UserName));

        Application.Run(context);

        Log.Info("Application.Run returned — cleaning up.", "Shutdown");
    }
}
