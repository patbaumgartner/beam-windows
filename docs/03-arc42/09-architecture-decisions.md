# arc42 — 9. Architecture decisions

Full records live in `../04-adr/`.

| ADR | Title | Status | Impact |
|---|---|---|---|
| ADR-0001 | Upstream into PowerDisplay, standalone app first | Accepted | High |
| ADR-0002 | No Windows service; per-user tray app | Accepted | High |
| ADR-0003 | .NET + WinUI 3 + CsWin32 | Accepted | Medium |
| ADR-0004 | `ChangeDisplaySettingsEx` for modes, `QueryDisplayConfig` for topology | Accepted | High |
| ADR-0005 | Trigger on aspect mismatch; projector classifier optional | Accepted | High |
| ADR-0006 | Night Light via CloudStore write with gamma fallback | Accepted (gated by S1) | High (risk) |
| ADR-0007 | Event-driven detection with debounce + 30 s reconcile | Accepted | Medium |
| ADR-0008 | Snapshot/journal/verify/revert transaction | Accepted | High |
| ADR-0009 | Versioned JSON config and profiles in `%LOCALAPPDATA%` | Accepted | Low |
| ADR-0010 | Local-first, opt-in, content-free telemetry | Accepted | Medium |
| ADR-0011 | Name: Beam | Accepted | Low |
| ADR-0012 | MIT license | Accepted | Low |
| ADR-0013 | DPI via source device-info `-3`/`-4` | Accepted (validated by S4) | High |
| ADR-0014 | UI-free application layer and port boundary | Accepted | High |
| ADR-0015 | Detection polling and bounded-time deviation (amends ADR-0007) | Accepted | Medium |

## Decision dependencies

```
ADR-0001 (upstream)
  +-- ADR-0002 (no service)
  +-- ADR-0003 (stack)
  +-- ADR-0004 (mode API)
  |     +-- ADR-0008 (transaction)
  |           +-- ADR-0009 (journal/config)
  +-- ADR-0005 (classification)
  +-- ADR-0006 (night light)
  |     +-- ADR-0010 (privacy)
  +-- ADR-0007 (detection)
    +-- ADR-0015 (detection polling deviation, amends ADR-0007)
  +-- ADR-0013 (DPI)
```

## Superseded / rejected alternatives

| Alternative | Rejected because | See |
|---|---|---|
| New PowerToys module | duplicates PowerDisplay ownership; huge boilerplate | ADR-0001 |
| PowerToys plugin / CmdPal extension as product | no background trigger, no native display access | ADR-0001 |
| Windows service | cannot touch session display state or per-user Night Light | ADR-0002 |
| UI automation of Night Light | locale/focus fragility, unacceptable during a presentation | ADR-0006 |
| Gamma ramp only | does not change the actual setting; HDR-incompatible | ADR-0006 |
| ML projector classifier | no training data, unexplainable, overkill | ADR-0005 |
| Classify before acting | adds a question and a failure mode to the main scenario; misses TVs | ADR-0005 |
| Polling every second | battery/CPU cost, worse latency than events | ADR-0007 |
| `SetDisplayConfig` for mode changes | more complex, topology side effects, needs full path set | ADR-0004 |
| Registry `PerMonitorSettings` as primary DPI path | may require sign-out | ADR-0013 |
