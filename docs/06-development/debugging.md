# Debugging

Everything Beam knows lands in two log files under `%LOCALAPPDATA%\Beam`. Start there before
touching the debugger.

## 1. The logs

| File | Purpose |
|---|---|
| `beam.log` | Human-readable line log: `[HH:mm:ss.fff] [tid=42] [INFO] [Category] message key=value` |
| `beam-detailed.jsonl` | One JSON object per line, machine-readable; **attach this to bug reports** |
| `journal.json` | Crash journal: the intent to change display state, active until a verified restore |

The log sink is installed by the composition root (`Beam.Tray.Program`, `Beam.Cli.Program`)
before anything else can log. Code in the portable layers never opens these files directly —
they log through the `Log` facade, so they work in tests too.

Tail the detailed log while you reproduce:

```powershell
Get-Content "$env:LOCALAPPDATA\Beam\beam.log" -Tail 50 -Wait
```

## 2. Quick checks without a debugger

```powershell
# What does Beam see right now?
beam status --json

# Is a fix currently triggered?
beam devices --json

# What would apply do, without touching anything?
beam apply --dry-run

# Read/write the real Night Light toggle (writes are verified by re-read)
beam nightlight

# Is the process running, and which build?
Get-Process Beam | Select-Object Id, Path
Get-Item "$env:LOCALAPPDATA\Programs\Beam\Beam.exe" | Select-Object LastWriteTime, Length
```

Single-instance is enforced with a named mutex `Local\Beam.SingleInstance`. If a second
launch appears to "do nothing", it is almost always the mutex — the running instance is the
real one.

## 3. Common symptoms

| Symptom | Cause | Action |
|---|---|---|
| `MSB3027: file is locked` on build | Beam.exe is running | `Stop-Process -Name Beam -Force`, then rebuild |
| Icon missing / second icon | Single-instance mutex held by a zombie | Kill all `Beam` processes, restart from Explorer |
| App exits immediately at sign-in | Another instance already running | Expected; check the instance's `beam.log` for "Another Beam instance" |
| Menu looks frozen on open | Registry/`Run`-key read on the UI thread | Fixed in 2026-11-01; verify with the cursor probe (below) |
| Sticky busy cursor over the menu | Latched wait cursor + null WinForms class cursor | Fixed in 2026-11-01; repro target below |
| Restore ran but Night Light is still off | Restore failed or the journal is stale | Check `beam-detailed.jsonl` for the Night Light write error |
| Display not restored after a crash | Journal was cleared before a *verified* restore | Check journal presence; the journal survives until restore completes |
| No trigger fired with a projector attached | Extended mode, matching aspects, or paused | `beam status --json`: `mirrored`, `aspectMismatch`, `needsFix` |
| CLI returns 4 | Feature unsupported by this display/Windows | Expected on adapters that cannot report the needed values |

## 4. Reproducing the sticky-cursor bug

The 2026-10-31 build could leave the tray context menu wearing the shell's busy cursor:

1. **Root cause:** WinForms registers its window classes with a null class cursor, so once a
   wait cursor latched (a transient UI-thread stall or shell contention), nothing cleared it
   while the pointer was over the menu — the app looked hung even though the message loop was
   answering in ~1 ms.
2. **Fix:** `TrayService` pins the arrow cursor when the menu opens and re-asserts it for as
   long as the drop-down is displayed.

### 4.1 Automated A/B: `spikes/S7.BugRepro`

`S7.BugRepro` runs the real `TrayService` with a real WinForms message loop, opens/closes the
context menu through `NotifyIcon`'s shell path, and drives the session state from a
background thread so changes land while the menu is open. A 20 Hz UI pulse detects stalls.

Run it against the **old** and **new** builds and compare:

```powershell
# Either build of Beam, whichever is installed:
cd spikes\S7.BugRepro
dotnet run -c Release
# RESULT: total suspected stall = N ms over 10 opens
```

Known results: the 10-31 build reproduced the bug (stall > 1500 ms, exit code 1); the 11-01
build never stalls (exit code 0). Do not let a green S7 run substitute for section 4.2.

### 4.2 Manual real-mouse protocol

Automated S7 proves the *mechanism*; the user-visible bug was real mouse input, so the
acceptance check uses a real pointer:

1. Start the app fresh.
2. Right-click the icon to open the menu; hover each item for ~1 s.
3. Close (click away or ESC), re-open, repeat **ten times** — including while a session
   change is in flight if you can provoke one.
4. The cursor must stay an arrow for the entire hover, every time.

During the old build this reproduced 18/18 samples with a wait cursor; the fixed build is
20/20 clean. If you see the busy cursor even once, stop and re-open an issue.

## 5. Debugging in an IDE

`Beam.sln` opens in Visual Studio / Rider. Breakpoints in `Beam.Tray` need the message loop:

```powershell
dotnet run --project src\Beam.Tray
```

For the CLI, run with fake ports instead of the real display — `CliApp` is unit-testable via
`test` | `tests\Beam.Cli.Tests\CliFakes.cs`. The tray is the only part that genuinely needs a
shell; everything interesting lives below it.

## 6. Collecting a bug report

Prefer a `beam status --json` blob plus the tail of `beam-detailed.jsonl`:

```powershell
beam status --json
Get-Content "$env:LOCALAPPDATA\Beam\beam-detailed.jsonl" -Tail 200
```

Also record: Windows build (`winver`), DPI scaling in effect, the display setup, and the
`Beam.exe` hash so old/new builds are distinguishable:

```powershell
Get-FileHash "$env:LOCALAPPDATA\Programs\Beam\Beam.exe"
```
