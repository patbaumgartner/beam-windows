using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using Beam.Tray;

namespace S7.BugRepro;

/// <summary>
/// Faithful reproduction of "wait cursor after opening the tray context menu a second time".
///
/// Mirrors the real app: TrayService + hidden host on the STA thread with a real
/// Application.Run message loop, plus a DisplayMonitor-style background thread that fires
/// SessionManager state changes. A timer opens/closes the tray context menu repeatedly via
/// NotifyIcon's internal ShowContextMenu (the same path the shell uses on right-click) and
/// a 20 Hz UI pulse detects UI-thread stalls.
///
/// Detection: we post to the UI thread every 50 ms. If the UI thread stalls (winforms busy
/// cursor / frozen menu), the "pulses" counter stops advancing, and the reported stall delay
/// grows.
/// </summary>
internal static class Program
{
    private static int _pulses;          // incremented on the UI thread; a stall stops it.
    private static int _cycle;
    private static bool _menuOpen;

    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        var session = new SessionManager();

        using var hostForm = new Form
        {
            ShowInTaskbar = false,
            Text = "S7 repro host",
            Opacity = 0,
            Size = new System.Drawing.Size(1, 1),
            FormBorderStyle = FormBorderStyle.None,
        };

        // Give the UI thread a WindowsFormsSynchronizationContext (real app gets one when
        // the hidden host form is shown in Program.cs).
        var tray = new TrayService(session, _ => { }, startHidden: true);
        tray.Show();
        Application.DoEvents();

        object trayIcon = typeof(TrayService)
            .GetField("_icon", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(tray)!;
        MethodInfo showCtx = typeof(NotifyIcon)
            .GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Background churn — same as DisplayMonitor raising changes while the user is at the menu.
        using var cts = new CancellationTokenSource();
        var churn = new Thread(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                session.Pause();
                Thread.Sleep(650);
                session.Resume();
                Thread.Sleep(650);
            }
        }) { IsBackground = true };
        churn.Start();

        // High-frequency UI pulse (20 Hz).
        var uiTimer = new System.Windows.Forms.Timer { Interval = 50 };
        uiTimer.Tick += (_, _) => Interlocked.Increment(ref _pulses);
        uiTimer.Start();

        // Menu open/close driver.
        const int cycles = 10;
        var menu = new System.Windows.Forms.Timer { Interval = 1600 };
        var lastPulses = 0;
        var stallMs = 0L;
        var sw = Stopwatch.StartNew();

        menu.Tick += (_, _) =>
        {
            int pulsesNow = Volatile.Read(ref _pulses);
            int delta = pulsesNow - lastPulses;
            lastPulses = pulsesNow;

            _cycle++;
            if (!_menuOpen)
            {
                sw.Restart();
                showCtx.Invoke(trayIcon, null);      // the shell equivalent of right-click
                _menuOpen = true;
            }
            else
            {
                SendKeys.SendWait("{ESC}");          // click away / close
                _menuOpen = false;
                long openMs = sw.ElapsedMilliseconds;
                Console.WriteLine($"Cycle {_cycle / 2,2}: menu open+close took {openMs,4} ms | pulsesSinceLast={delta,3}");
                if (delta < 12)
                {
                    stallMs += openMs;
                    Console.WriteLine($"       >>> suspected UI stall: only {delta} pulses while menu was open");
                }
            }

            if (_cycle >= cycles * 2)
            {
                menu.Stop();
                uiTimer.Stop();
                cts.Cancel();
                Console.WriteLine();
                Console.WriteLine($"RESULT: total suspected stall = {stallMs} ms over {cycles} opens");
                Console.WriteLine(stallMs > 1500 ? "BUG REPRODUCED (UI thread stalled)" : "No stall reproduced");
                hostForm.Close();
            }
        };
        menu.Start();

        Application.Run(hostForm);
        return stallMs > 1500 ? 1 : 0;
    }
}
