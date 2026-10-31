# arc42 — 4. Solution strategy

## 4.1 Strategy in one page

1. **Event-driven, not polling.** React to display device events and power notifications;
   a 30 s reconcile timer is a safety net, not the mechanism.
2. **Detect, classify, plan, validate, snapshot, apply, verify, record.** Every action
   goes through the same pipeline; no component mutates the system directly.
3. **One transaction per projector session.** The journal is the single source of truth
   for what was changed and how to undo it.
4. **Adapters at the edges, pure logic in the middle.** All Win32/registry/COM calls sit
   behind interfaces; the planner and classifier are pure and fully unit-testable.
5. **Undocumented APIs are quarantined.** Night Light (and DPI) live behind one
   interface each, with a documented fallback and a kill switch.
6. **Never guess silently.** Ambiguous displays produce one question; every automatic
   decision is explainable.
7. **Build standalone, shape for upstream.** `Beam.Core` has no UI/OS dependencies; the
   Windows adapter layer maps 1:1 onto PowerDisplay's existing services.

## 4.2 Technology decisions (summary)

| Concern | Choice | ADR |
|---|---|---|
| Runtime | C# / .NET 9, WinUI 3 (Windows App SDK) for UI | ADR-0003 |
| P/Invoke | CsWin32 source generator | ADR-0003 |
| Hosting | Per-user tray app, single instance, no service | ADR-0002 |
| Mode change | `EnumDisplaySettingsEx` + `ChangeDisplaySettingsEx` with `CDS_TEST` | ADR-0004 |
| Topology read | `QueryDisplayConfig` | ADR-0004 |
| Detection | `DeviceWatcher` + `GUID_CONSOLE_DISPLAY_STATE` + debounce | ADR-0007 |
| Trigger | External display + **aspect mismatch** (covers TV and projector) | ADR-0005 |
| Classification (optional) | Scored EDID/name/size heuristic + user confirmation | ADR-0005 |
| Night Light | CloudStore binary write + gamma fallback | ADR-0006 |
| DPI | Source DPI device-info `-3`/`-4`, relative steps, verified | ADR-0013 |
| Safety | Snapshot/journal/verify/revert + mutation gate | ADR-0008 |
| Config | Versioned JSON in `%LOCALAPPDATA%` | ADR-0009 |
| Privacy | Local-first, opt-in, content-free | ADR-0010 |
| Upstream | PowerDisplay extension, not a new module | ADR-0001 |

## 4.3 Decomposition

```
Beam.App (WinUI 3 tray + settings)
   |  uses
Beam.Core (domain: state machine, planner, classifier, journal model)
   |  depends on interfaces only
Beam.Windows (adapters: display, EDID, Night Light, DPI, notifications, registry)
   |
Windows APIs
```

`Beam.Cli` talks to `Beam.App` over a named pipe and shares `Beam.Core` DTOs.

## 4.4 How the quality goals are achieved

| Goal | Mechanism |
|---|---|
| Safety | Enumerated-only rule, `CDS_TEST`, mutation gate, verify, auto-revert, safe mode |
| Exact restore | Byte-exact Night Light snapshot; DEVMODE snapshot; "still ours" predicate |
| Latency | Event-driven + 1.5 s debounce; parallel reads; apply is a single API call |
| Trust | Toast + tray + undo + diagnostics timeline |
| Footprint | No polling, no service, lazy EDID reads, bounded ring buffer |
| Upstreamability | Pure core, PowerDisplay-shaped adapters, MIT, resource strings |

## 4.5 Deliberate simplifications (MVP)

- Topology is read, never changed.
- DPI uses only the `-3`/`-4` source API; registry fallback is advice-only.
- One presentation session at a time per device; no multi-device transactions.
- No audio switching.
- No cloud, no sync, no accounts.
- English-first strings, but all externalized.

## 4.6 What would make us change the strategy

- S1 fails: Night Light becomes gamma-only; UC-06 is downgraded.
- S4 fails on real hardware: DPI degrades to L3 advice at runtime (UC-05 stays in MVP,
  but automatic scaling is disabled).
- S3 finds no common 16:9 mode for duplicate: the duplicate path pins 1920x1080 and
  documents the limitation.
- PowerToys accepts a broader "display scenarios" design: Beam becomes a thin
  presentation-display preset on top of it.
- Windows ships a native presentation profile: pivot to upstream contribution or retire.
