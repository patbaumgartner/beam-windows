# arc42 — 5. Building block view

> This chapter describes the code that exists today. The original plan called for a WinUI 3
> settings app, a named-pipe IPC layer and an EDID classifier; those are still Phase 2/3
> ideas in the [roadmap](../05-delivery/roadmap.md), not part of the shipped 0.1.0 build.
> The authoritative dependency rules live in
> [docs/06-development/architecture.md](../06-development/architecture.md).

## 5.1 Level 1 — overall

```
+--------------------------------------------------------------------------+
| Beam.Tray   (net10.0-windows, WinForms)    Beam.Cli (net10.0-windows)     |
|  Program, TrayService, TrayPresentation     Program, CliApp, CommandLine  |
|  composition root                           composition root            |
+------------------------------+---------------------------+---------------+
                               |                           |
+------------------------------v---------------------------v---------------+
| Beam.App   (net10.0, no UI, no P/Invoke)                                  |
|  PresentationManager · SessionManager · JournalStore · FileLogSink        |
|  talks only to Beam.Core ports                                            |
+------------------------------+-------------------------------------------+
                               |
+------------------------------v-------------------------------------------+
| Beam.Windows   (net10.0-windows)                                          |
|  DisplayInventory · DisplayMonitor · DisplayModeController                |
|  WindowsDpiController · NightLightController · DisplayNative              |
+------------------------------+-------------------------------------------+
                               |
+------------------------------v-------------------------------------------+
| Beam.Core   (net10.0, portable)                                           |
|  DisplayTopology · TriggerCondition · AspectRatio · DpiScaleLadder        |
|  ScalePlanner · NightLightBlobCodec · StartupCommand · Ports · Log        |
+--------------------------------------------------------------------------+
```

Arrows point at *references*. `Beam.Tray` and `Beam.Cli` both reference Core, App and
Windows; nothing references the composition roots. `Beam.App` and `Beam.Core` target plain
`net10.0`, so they cannot accidentally compile a Windows-only API.

## 5.2 Beam.Core — the portable domain

| Block | Responsibility |
|---|---|
| `DisplayTopology`, `DisplayPath`, `DisplayTarget`, `CloneGroup` | Immutable model of the CCD topology: source/target ids, modes, DPI relative values, friendly name, internal-panel flag |
| `TriggerCondition` | The single definition of "a projector needs fixing": mirrored **and** an external display **and** an aspect mismatch |
| `AspectRatio` | Classify 16:9 / 16:10 / other, with a tolerance |
| `DpiScale`, `DpiScaleLadder`, `ScalePlanner` | Snap a requested percentage to the supported ladder, detect the 960×540 trap, choose a readable scale |
| `NightLightBlobCodec`, `NightLightState` | Parse/serialise the registry blob, preserving unknown bytes |
| `StartupCommand` | Compare/normalise `Run`-key command lines (`"…\Beam.exe"` vs `"…\Beam.exe" --hidden`) |
| `Ports` (`ITopologySource`, `IDisplayChangeSource`, `IDisplayModeController`, `IDpiController`, `INightLightController`) | The only surface the application layer may use for platform work |
| `Log`, `ILogSink` | Ambient logging facade; default is silent, the composition root installs a sink |

**Rule:** no UI types, no P/Invoke, no `Microsoft.Win32`, no reference to another Beam
project. `Beam.Architecture.Tests` enforces it.

## 5.3 Beam.App — orchestration

| Block | Responsibility |
|---|---|
| `PresentationManager` | Serialises the apply and restore sequences, owns the journal handshake, recovery on launch, DPI selection, display-change handling |
| `SessionManager` | `Idle`/`Applying`/`Active`/`Restoring`/`Paused` state machine; the **only** place a transition is logged |
| `JournalStore` / `IJournalStore` | Atomic crash journal at `%LOCALAPPDATA%\Beam\journal.json`; active for the whole presentation, cleared only after a verified restore |
| `FileLogSink` | `beam.log` + `beam-detailed.jsonl` in `%LOCALAPPDATA%\Beam`; never throws into the caller |

`PresentationManager` depends exclusively on the Core ports, so the full
apply → active → disconnect → restore cycle is unit-tested with fakes.

## 5.4 Beam.Windows — adapters

| Adapter | Responsibility | Notes |
|---|---|---|
| `DisplayInventory` | `QueryDisplayConfig` → `DisplayTopology`; capture a `ConnectionSnapshot` for change detection | Read-only, also used by the CLI |
| `DisplayMonitor` | Poll the inventory every 250 ms, compare snapshots, raise `Changed` once per real change | Injectable capture; measures p95 detection latency |
| `DisplayModeController` | Clone + best 16:9 mode, `CDS_TEST` before apply, verify; restore captured DEVMODEs | Worker-thread safe; returns an error string instead of throwing |
| `WindowsDpiController` | Read/write the **source** DPI scale via device-info `-3`/`-4`, then verify | ADR-0013 |
| `NightLightController` | Snapshot every parsable CloudStore state, disable, restore, re-read to verify | ADR-0006 |
| `DisplayNative` | The single home for `DllImport` declarations, structs and constants | Kept small on purpose |

These are the only files allowed to call Windows. They are thin and integration-tested
manually against the hardware matrix in the
[test strategy](../05-delivery/test-strategy.md).

## 5.5 Beam.Tray and Beam.Cli — composition roots

| Component | Responsibility |
|---|---|
| `Beam.Tray.Program` | DPI awareness, log sink, single-instance mutex, construct adapters, recovery, start the monitor, run the WinForms message loop |
| `TrayService` | `NotifyIcon`, context menu, state text, cursor pinning, startup toggle, balloons deferred while the menu is open |
| `TrayPresentation` | Pure mapping from session state to menu text/icon (unit tested) |
| `Beam.Cli.Program` | Install the log sink, construct the real adapters |
| `CliApp` | All commands (`status`, `devices`, `apply`, `restore`, `dpi`, `nightlight`) against Core ports; testable with `StringWriter` |
| `CommandLine` | Argument parsing and `CliUsageException` |

Both roots are composition roots, but they have different jobs. The tray runs the long-lived
presentation loop (monitor + presentation manager + WinForms message loop). The CLI is a
one-shot diagnostic/control tool: `status` reports the topology, `apply --dry-run` prints
the plan without touching anything, and `nightlight` reads or writes the real toggle. They
share the adapters and the Core ports; the CLI never starts the monitor and never runs a
presentation session.

## 5.6 Ports (contract sketch)

```csharp
interface ITopologySource { DisplayTopology Query(); }

interface IDisplayChangeSource {
    event Action<ConnectionSnapshot?, ConnectionSnapshot>? Changed;
    void Start();
    void Stop();
}

interface IDisplayModeController {
    string? ApplyCloneWith16x9();   // null = success
    string? Restore();
}

interface IDpiController {
    DpiScale? Read(DisplayPath path);
    bool SetPercentageAndVerify(DisplayPath path, int percent, DpiScaleValue? current = null);
}

interface INightLightController {
    IReadOnlyList<NightLightSnapshot> SnapshotAll();
    NightLightWriteResult SetActive(bool active);
    NightLightWriteResult Restore(IReadOnlyList<NightLightSnapshot> snapshots);
}
```

`IJournalStore` (`Beam.App.Journal`) is the only non-Core port: it is portable file I/O and
therefore lives next to the code that uses it.

## 5.7 Data ownership

| Data | Owner | Lifetime |
|---|---|---|
| Topology snapshot | `DisplayMonitor` | in memory, replaced on change |
| Session state | `SessionManager` | process lifetime |
| Crash journal | `JournalStore` | from `BeginApply` until a verified restore |
| Night Light blob bytes | `JournalStore` | same as the journal |
| Human log | `FileLogSink` | append-only |
| Detailed log (JSONL) | `FileLogSink` | append-only |
| Settings | Not implemented yet (Phase 2: `ConfigStore`) | — |

## 5.8 DPI detail (why it is per source)

```
DISPLAYCONFIG_PATH_INFO
  sourceInfo: adapterId, id  <-- DPI belongs here
  targetInfo: adapterId, id  <-- monitor identity/EDID, connector

duplicate: 1 source -> N targets   => 1 scale for all
extend:    N sources -> N targets  => N scales
```

`DisplayConfigGetDeviceInfo` type `-3` (size 32) and `DisplayConfigSetDeviceInfo` type `-4`
(size 24) operate on the **source** (`header.id = sourceInfo.id`,
`header.adapterId = sourceInfo.adapterId`).

Ladder: `100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500` (%).
`recommended = ladder[abs(minScaleRel)]`; `current = ladder[abs(min) + cur]`.

## 5.9 Dependency rules

1. `Beam.Core` → BCL only.
2. `Beam.App` → `Beam.Core` only (no Windows, no UI, no P/Invoke).
3. `Beam.Windows` → `Beam.Core` + Win32/WinRT.
4. `Beam.Tray` → Core, App, Windows.
5. `Beam.Cli` → Core, App, Windows; never `Beam.Tray`.
6. No cycles. Enforced by project references and `Beam.Architecture.Tests`.
