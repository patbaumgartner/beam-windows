# Architecture

Beam is a per-user Windows tray app. It watches the display topology, enters a
presentation state when a projector is mirrored with an aspect mismatch, and restores the
previous state when the projector goes away.

The goal of the layering is simple: **the decision and sequencing logic must be testable
without a projector, and the platform code must be small enough to read in one sitting.**

## Dependency rule

```
Beam.Core          (net10.0)            domain + ports + logging abstraction
   ▲
   ├── Beam.App    (net10.0)            orchestration, journal, file logging
   │      ▲
   ├── Beam.Windows (net10.0-windows)   Win32/WinRT adapters
   │      ▲
   └──────┴── Beam.Tray / Beam.Cli      composition roots (no inbound references)
```

| Project | May reference | Must not reference |
|---|---|---|
| `Beam.Core` | BCL only | any other Beam project, WinForms, Win32 |
| `Beam.App` | `Beam.Core` | `Beam.Windows`, `Beam.Tray`, WinForms, P/Invoke |
| `Beam.Windows` | `Beam.Core` | WinForms, `Beam.App`, `Beam.Tray` |
| `Beam.Tray` | Core, App, Windows | — |
| `Beam.Cli` | Core, App, Windows | `Beam.Tray` |

`Beam.Architecture.Tests` reads the `.csproj` files and scans the portable layers'
source for `System.Windows.Forms`, `[DllImport]`, `[LibraryImport]`, and
`Microsoft.Win32`. If someone adds a reference or a P/Invoke in the wrong place, CI fails
with a message pointing at the rule.

## Ports

The application layer never calls Windows directly. It depends on five interfaces in
`Beam.Core.Ports`:

| Port | Purpose | Windows adapter |
|---|---|---|
| `ITopologySource` | Read the current display paths | `DisplayInventory` |
| `IDisplayChangeSource` | Raise connect/disconnect/topology changes | `DisplayMonitor` |
| `IDisplayModeController` | Apply clone + 16:9, restore | `DisplayModeController` |
| `IDpiController` | Read/write the source DPI scale | `WindowsDpiController` |
| `INightLightController` | Snapshot, disable, restore Night Light | `NightLightController` |

`IJournalStore` (in `Beam.App.Journal`) stores the intent to change display state so a
crash can be recovered on the next launch.

## Responsibility map

| Area | Where | Why there |
|---|---|---|
| Trigger rule (`IsMet`) | `Beam.Core.Display.TriggerCondition` | One definition shared by tray and CLI |
| Session state machine | `Beam.App.Sessions.SessionManager` | Host-independent; the only place transitions are logged |
| Apply/restore sequence | `Beam.App.Presentation.PresentationManager` | Serialises work, owns the journal handshake |
| Crash journal | `Beam.App.Journal.JournalStore` | Atomic file I/O, no Windows dependency |
| Logging | `Beam.Core.Diagnostics.Log` + `Beam.App.Diagnostics.FileLogSink` | Domain logs to an abstraction; production sink is installed by the root |
| Display/topology Win32 | `Beam.Windows.Display` | The only place CCD/P/Invoke lives |
| Night Light CloudStore | `Beam.Windows.NightLight` | The only place registry/blob writes live |
| Tray UI | `Beam.Tray` | WinForms message loop, menu, icon |
| CLI parsing/commands | `Beam.Cli` (`CliApp`, `CommandLineParser`) | Testable without a process |
| Process wiring | `Beam.Tray.Program`, `Beam.Cli.Program` | Composition roots |

## Runtime sequence

```
Monitor (250 ms poll)
  └─ Changed ─► PresentationManager.OnDisplayChanged
                   │  paused?            ignore
                   │  disconnect while Active? ─► RestoreSequence
                   └─ trigger met & Idle?      ─► FixSequence
                                                  1. Snapshot Night Light
                                                  2. Journal.BeginApply   ← before mutation
                                                  3. Night Light OFF
                                                  4. Clone + best 16:9
                                                  5. Optimal DPI
                                                  6. Session.Active
```

Restore runs steps in reverse: extended topology, captured DEVMODEs, Night Light, then
`Journal.Clear()` and `Session.Idle`. The journal deliberately stays active for the whole
presentation: if Beam crashes or exits mid-presentation, the next launch restores the
original state before monitoring starts.

All sequences run through a `SemaphoreSlim`. A disconnect that arrives while a fix is
running waits for it and then restores; the UI thread never blocks on a display API.

## Invariants worth protecting

1. **Journal before mutation.** `BeginApply` must complete before the first Night Light or
   mode write.
2. **One transition log point.** Only `SessionManager.TransitionTo` logs session changes.
   The tray updates UI from `StateChanged`; it does not log the transition again.
3. **`ApplyNow` is a manual override.** It does not consult the automatic trigger; the
   automatic path (`OnDisplayChanged`, `CheckCurrentState`) does.
4. **Portable layers stay portable.** No `net10.0-windows` target, no UI types, no
   P/Invoke in Core or App.
5. **Best-effort steps still restore.** A rejected mode change does not abort the session;
   the session still owns a restore path.

## Adding a feature

1. Decide which layer owns the decision. If it is pure logic, it belongs in Core.
2. If it needs the OS, add (or extend) a port in Core and implement it in Windows.
3. Sequence it in `Beam.App`, with a fake in `tests/Beam.App.Tests/Fakes.cs`.
4. Add the port to the composition roots only (`Beam.Tray.Program`, `Beam.Cli.Program`).
5. Cover it with a unit test; add a hardware check to
   [test-strategy.md](../05-delivery/test-strategy.md) if it touches a real display.

## Why this shape

[ADR-0014](../04-adr/ADR-0014-application-layer-and-ports.md) records the decision to
introduce `Beam.App` and the port boundary. [ADR-0003](../04-adr/ADR-0003-stack-dotnet-winui3.md)
records the original stack choice and the WinForms-tray addendum.
