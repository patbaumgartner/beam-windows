using Beam.App.Diagnostics;
using Beam.App.Journal;
using Beam.App.Status;
using Beam.Cli;
using Beam.Core.Diagnostics;
using Beam.Core.Startup;
using Beam.Windows.Display;
using Beam.Windows.NightLight;
using static Beam.Windows.Display.DisplayNative;

// Composition root for the scriptable CLI. All command handling lives in CliApp so it
// can be unit tested; this file only wires the real Windows adapters.

DisplayNative.SetProcessDpiAwarenessContext(DpiAwarenessPerMonitorV2);

// Diagnostics go to the log file, never to stdout/stderr: those belong to the command's
// output (and may be JSON that another tool parses).
Log.SetSink(new FileLogSink());

// A1: mutating commands (apply/restore/dpi set/nightlight) take the same per-user
// single-instance mutex as the tray app, so CLI and tray can never race each other.
// B6: `beam status` merges the running tray's live status file (status.json) when present.
var app = new CliApp(
    new DisplayInventory(),
    new DisplayModeController(),
    new WindowsDpiController(),
    new NightLightController(),
    new JournalStore(),
    lockFactory: () => SingleInstanceLock.TryAcquire(),
    statusReader: () => StatusFile.TryRead());

return app.Run(args);
