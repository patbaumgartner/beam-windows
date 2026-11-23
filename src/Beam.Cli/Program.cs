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

// Mutating commands (apply/restore/dpi set/nightlight) take a short-lived operation
// semaphore shared with the tray. The tray's lifetime singleton never blocks the CLI.
// B6: `beam status` merges the running tray's live status file (status.json) when present.
var app = new CliApp(
    new DisplayInventory(),
    new DisplayModeController(),
    new WindowsDpiController(),
    new NightLightController(),
    new JournalStore(),
    lockFactory: () => DisplayOperationLock.TryAcquire(),
    statusReader: () => StatusFile.TryRead());

return app.Run(args);
