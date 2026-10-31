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
internal static class Program
{
    private static readonly object LogLock = new();

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Autostart from the installer uses "--hidden" so no balloon is shown on boot
        // when nothing is presenting. The tray icon still appears so the user can see
        // Beam is running and change its behaviour.
        bool hidden = args.Any(a => a.Equals("--hidden", StringComparison.OrdinalIgnoreCase) ||
                                    a.Equals("-hidden", StringComparison.OrdinalIgnoreCase) ||
                                    a.Equals("/hidden", StringComparison.OrdinalIgnoreCase));

        var session = new SessionManager();
        Action<string> log = WriteLog;

        using var monitor = new DisplayMonitor(
            DisplayInventory.Capture,
            TimeSpan.FromMilliseconds(250));

        using var presentation = new PresentationManager(monitor, session, log);
        using var tray = new TrayService(session, log, startHidden: hidden);

        var exit = new ManualResetEventSlim(false);
        tray.ExitRequested += () => exit.Set();

        tray.ApplyNowRequested += presentation.ApplyNow;
        tray.RestoreRequested += presentation.RestoreNow;

        tray.Show();
        monitor.Start();

        presentation.CheckCurrentState(); // Apply fix immediately if mismatch is already present at startup.

        log("Beam started. Watching for external displays.");
        session.StateChanged += evt =>
            log($"[State] {evt.From} → {evt.To} ({evt.Reason})");

        exit.Wait();
    }

    private static void WriteLog(string message)
    {
        lock (LogLock)
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"[{stamp}] {message}";

            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Beam");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "beam.log"), line + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the app down.
            }

            Console.WriteLine(line);
        }
    }
}
