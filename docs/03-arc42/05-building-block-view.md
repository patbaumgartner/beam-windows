# arc42 — 5. Building block view

## 5.1 Level 1 — overall

```
+---------------------------------------------------------------+
| Beam.App  (WinUI 3, tray, settings, toasts, single instance)   |
+-----------------------------+---------------------------------+
                              |
+-----------------------------v---------------------------------+
| Beam.Core  (pure domain)                                      |
|  TriggerEngine · Classifier · ModePlanner · ScalePlanner      |
|  NightLightPlanner · SessionManager (state machine)           |
|  Journal model · Profile model · Diagnostics timeline         |
+-----------------------------+---------------------------------+
                              | interfaces
+-----------------------------v---------------------------------+
| Beam.Windows  (adapters)                                      |
|  DisplayInventory · DisplayModeController · TopologyReader    |
|  EdidReader · NightLightController · GammaRampController      |
|  DpiController · NotificationService · ConfigStore            |
|  JournalStore · PipeServer · StartupTask · Logging            |
+---------------------------------------------------------------+
```

## 5.2 Beam.Core

| Block | Responsibility | Key interfaces |
|---|---|---|
| `TriggerEngine` | Debounce events, build topology snapshots, emit activation/deactivation | `IDisplayInventory`, `IClock` |
| `Classifier` | Score a display, apply overrides, decide auto/ask/ignore | `IEdidReader`, `IClassificationStore` |
| `ModePlanner` | Choose a 16:9 mode from an enumerated list per policy and topology | pure |
| `ScalePlanner` | Compute target scale / detect the 960x540 trap | pure |
| `NightLightPlanner` | Decide whether/where to turn Night Light off | pure |
| `SessionManager` | State machine, transaction ordering, journal lifecycle | all of the above |
| `RestorePlanner` | Reverse-order restore with "still ours" predicate | pure |
| `ProfileStore` (model) | Learned per-device settings, migration, validation | `IProfileStore` |
| `DiagnosticsLog` | Bounded ring buffer of decisions with reasons | `IClock` |

**Rule:** `Beam.Core` must not reference `Beam.Windows`, WinUI, `Microsoft.Win32.*`, or
`Windows.Win32.*`. It is testable on any OS.

## 5.3 Beam.Windows

| Adapter | Responsibility | Notes |
|---|---|---|
| `DisplayInventory` | `QueryDisplayConfig` -> `TopologySnapshot` | active paths only |
| `DisplayModeController` | enumerate/apply modes with `CDS_TEST` + verify | worker thread, timeout |
| `TopologyReader` | clone groups, primary, positions, output technology | read-only in MVP |
| `EdidReader` | device path -> EDID bytes -> `EdidInfo` | cached per session |
| `NightLightController` | read/parse/write CloudStore state, snapshot bytes | strict parser, no guessing |
| `GammaRampController` | save/set/restore gamma ramp per display | fallback only |
| `DpiController` | read/set the **source** DPI scale via device-info `-3`/`-4`, verify, snapshot | relative steps; struct sizes 32/24; ADR-0013 |
| `NotificationService` | toasts with actions | respects DND |
| `ConfigStore` | atomic JSON read/write, schema version, backup | `%LOCALAPPDATA%\Beam` |
| `JournalStore` | atomic session journal | `state\session.json` |
| `PipeServer` | CLI IPC, same-user only, ACL | `\\.\pipe\Beam-<sid>` |
| `StartupTask` | per-user run-at-login | no admin |
| `Logging` | rolling file log + ETW | no PII |
| `ProjectorClassifier` (optional) | scored EDID heuristic for `onlyProjectors` mode | Phase 2; off by default |

## 5.4 Beam.App

| Component | Responsibility |
|---|---|
| `TrayIcon` | state icon, menu, Explorer-restart resilience |
| `SettingsWindow` | WinUI 3 page, navigation, immediate apply |
| `DiagnosticsWindow` | live timeline, "why not", export |
| `ToastController` | build summary, actions, dedupe |
| `HotkeyService` | optional global hotkey, conflict reporting |
| `SingleInstance` | mutex + activate existing window |
| `AppHost` | composition root, DI, lifecycle, crash handling |

## 5.5 Beam.Cli

| Component | Responsibility |
|---|---|
| `Program` | parse args, map to pipe requests |
| `PipeClient` | connect/launch with timeout |
| `Output` | human text or JSON envelope |
| `ExitCodes` | stable codes (0/1/2/3/4/5) |

## 5.6 Interfaces (contract sketch)

```csharp
interface IDisplayInventory {
    TopologySnapshot Read(TopologyQuery query);          // active/all
}

interface IDisplayModeController {
    IReadOnlyList<DisplayMode> Enumerate(string gdiDeviceName);
    DisplayMode? GetCurrent(string gdiDeviceName);
    ApplyResult Test(string gdiDeviceName, DisplayMode mode);
    ApplyResult Apply(string gdiDeviceName, DisplayMode mode);   // no registry persistence
    ApplyResult Verify(string gdiDeviceName, DisplayMode mode);
}

interface IEdidReader {
    EdidInfo? Read(string devicePath);                   // null = unreadable
}

interface INightLightController {
    NightLightState Read();
    NightLightSnapshot Snapshot();
    WriteResult Disable(NightLightScope scope);
    WriteResult Restore(NightLightSnapshot snapshot);
    bool IsSupported { get; }
}

interface IDpiController {
    DpiScaleInfo? Read(uint adapterId, uint sourceId);      // -3: min/cur/max relative
    DpiResult Set(uint adapterId, uint sourceId, int percent); // -4, then verify
    bool IsSupported { get; }
}

interface IStateJournal {
    void Begin(SessionRecord record);
    void MarkApplied(string itemId);
    void MarkRestored(string itemId);
    void Complete();
    SessionRecord? Load();
}
```

## 5.7 Data ownership

| Data | Owner | Lifetime |
|---|---|---|
| Topology snapshot | `TriggerEngine` | in-memory, per event |
| Classification | `ConfigStore` | persistent, per device id |
| Profile | `ConfigStore` | persistent, per device id |
| Session journal | `JournalStore` | until verified restore |
| Night Light bytes | `JournalStore` | until verified restore |
| Timeline | `DiagnosticsLog` | ring buffer + rolling log |
| Settings | `ConfigStore` | persistent |

## 5.8 DPI detail (why it is per source)

```
DISPLAYCONFIG_PATH_INFO
  sourceInfo: adapterId, id  <-- DPI belongs here
  targetInfo: adapterId, id  <-- monitor identity, EDID, connector

duplicate: 1 source -> N targets   => 1 scale for all
extend:    N sources -> N targets  => N scales
```

`DisplayConfigGetDeviceInfo` with type `-3` (size 32) and
`DisplayConfigSetDeviceInfo` with type `-4` (size 24) operate on the **source**
(`header.id = sourceInfo.id`, `header.adapterId = sourceInfo.adapterId`).

Ladder: `100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500` (%).
`recommended = ladder[abs(minScaleRel)]`; `current = ladder[abs(min) + cur]`.

## 5.9 Dependency rules

1. `Beam.Core` -> nothing but BCL.
2. `Beam.Windows` -> `Beam.Core` + Win32/WinRT.
3. `Beam.App` -> `Beam.Core` + `Beam.Windows` + WinUI.
4. `Beam.Cli` -> `Beam.Core` (DTOs) only; never `Beam.Windows`.
5. No cycles. Enforced by project references and an architecture test.
