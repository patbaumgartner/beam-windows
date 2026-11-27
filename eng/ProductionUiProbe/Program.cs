using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Beam.App.Diagnostics;
using Beam.App.Journal;
using Beam.App.Presentation;
using Beam.App.Sessions;
using Beam.App.Settings;
using Beam.App.Status;
using Beam.Core.Diagnostics;
using Beam.Core.Startup;
using Beam.Tray;
using Beam.Windows.Display;
using Beam.Windows.Extras;
using Beam.Windows.NightLight;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        DisplayNative.SetProcessDpiAwarenessContext(DisplayNative.DpiAwarenessPerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        string command = args.SingleOrDefault() ?? throw new ArgumentException("Expected a UI probe command.");
        var sink = new FileLogSink();
        var timeline = new TimelineBuffer();
        Log.SetSink(new TimelineSink(sink, timeline));
        using var session = new SessionManager();
        using var monitor = new DisplayMonitor(DisplayInventory.Capture, TimeSpan.FromMilliseconds(250));
        using var keepAwake = new KeepAwakeAdapter();
        using var presentation = new PresentationManager(
            monitor,
            new DisplayInventory(),
            new DisplayModeController(),
            new WindowsDpiController(),
            new NightLightController(),
            new JournalStore(),
            session,
            settings: new SettingsStore(),
            keepAwake: keepAwake,
            dnd: new DndController(),
            audio: new AudioController(),
            statusWriter: new StatusFile(),
            operationLockFactory: () => DisplayOperationLock.TryAcquire());

        switch (command)
        {
            case "--settings":
                new SettingsWindow(new SettingsViewModel(presentation)).ShowDialog();
                return 0;

            case "--diagnostics":
                string logDir = FileLogSink.DefaultDirectory;
                new DiagnosticsWindow(new DiagnosticsViewModel(
                    presentation,
                    session,
                    timeline,
                    typeof(TrayService).Assembly.GetName().Version?.ToString() ?? "unknown",
                    logDir,
                    Path.Combine(logDir, "status.json"),
                    Environment.MachineName,
                    Environment.UserName)).ShowDialog();
                return 0;

            case "--startup-toggle":
            case "--startup-check":
                var tray = new TrayService(session, presentation: presentation);
                tray.Show();
                try
                {
                    string methodName = command == "--startup-toggle" ? "ToggleStartup" : "IsStartupEnabled";
                    object? result = typeof(TrayService)
                        .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.Invoke(tray, null);
                    if (command == "--startup-check")
                    {
                        Console.WriteLine($"STARTUP_ENABLED={result}");
                    }

                    return 0;
                }
                finally
                {
                    tray.Dispose();
                }

            case "--pause-cycle":
                presentation.PauseFor(Timeout.InfiniteTimeSpan);
                Console.WriteLine($"PAUSED={session.State}");
                presentation.ResumeAsync().GetAwaiter().GetResult();
                Console.WriteLine($"RESUMED={session.State}");
                return session.State == SessionState.Idle ? 0 : 1;

            default:
                throw new ArgumentException($"Unknown UI probe command: {command}");
        }
    }
}
