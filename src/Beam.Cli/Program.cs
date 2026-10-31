using Beam.App.Diagnostics;
using Beam.App.Journal;
using Beam.Cli;
using Beam.Core.Diagnostics;
using Beam.Windows.Display;
using Beam.Windows.NightLight;
using static Beam.Windows.Display.DisplayNative;

// Composition root for the scriptable CLI. All command handling lives in CliApp so it
// can be unit tested; this file only wires the real Windows adapters.

DisplayNative.SetProcessDpiAwarenessContext(DpiAwarenessPerMonitorV2);

// Diagnostics go to the log file, never to stdout/stderr: those belong to the command's
// output (and may be JSON that another tool parses).
Log.SetSink(new FileLogSink());

var app = new CliApp(
    new DisplayInventory(),
    new DisplayModeController(),
    new WindowsDpiController(),
    new NightLightController(),
    new JournalStore());

return app.Run(args);
