# Test strategy

## 1. Principles

- The domain is pure and tested without Windows.
- Every native adapter has a fake with fault injection.
- Anything that can blank a screen is tested by simulation first, hardware second.
- Golden files for binary formats (Night Light blobs) and mode lists.
- No test requires a projector in CI.

## 2. Test levels

### 2.1 Unit (fast, CI, no OS)

| Area | Cases |
|---|---|
| `ModePlanner` | 16:9 predicate, tolerance, policy x mode-list matrix, refresh preference, no-candidate |
| `ScalePlanner` | sane-range rule, trap detection, standard scale snapping |
| `Classifier` | score table, thresholds, overrides, missing EDID, AVR, virtual |
| `NightLightCodec` | parse/serialize round-trip, 38/39-byte, per-device, manual/schedule, unknown bytes preserved, malformed input rejected |
| `RestorePlanner` | reverse order, "still ours", skip on user change, missing device |
| `SessionManager` | state machine transitions, failure paths, safe mode |
| `JournalStore` | atomic write, corruption, phase resume |
| `ProfileStore` | schema version, migration, merge, validation |
| CLI | args, exit codes, JSON schema |

### 2.2 Simulation (CI)

`SimulationHost` replays an event script against fakes:

```
t=0    device-added EPSON (HDMI, 1920x1080)
t=1500 inventory-changed
t=1520 classify -> projector
t=1530 modes -> [...]
...
t=9000 device-removed
```

Scenarios: happy path, no 16:9 mode, clone, dock bounce, KVM, AVR, flapping, crash at
each phase, Night Light failure, DPI unavailable, user change mid-session, two
projectors, RDP.

### 2.3 Integration (CI, read-only)

- Real `QueryDisplayConfig` on the runner (enumerate only, no mutation).
- Real EDID registry read for the runner's display.
- Real Night Light **read** and parse (no write).
- Named pipe round-trip.
- Config file atomic write.

### 2.4 Hardware-in-the-loop (manual/scripted)

| ID | Setup | Checks |
|---|---|---|
| H1 | Intel iGPU laptop + 1080p projector (HDMI) | extend, clone, second-only |
| H2 | Same + 720p projector | policy fallbacks |
| H3 | NVIDIA desktop + 1080p display | driver differences |
| H4 | USB-C dock + HDMI | enumeration timing, single apply |
| H5 | HDMI KVM | bounce handling |
| H6 | AVR passthrough | classification, ask flow |
| H7 | Two projectors | independent plans |
| H8 | 4K projector / TV | scaling, 16:9 at 4K |
| H9 | Sleep/wake with projector attached | re-apply, no churn |
| H10 | HDR on/off | Night Light behaviour |

### 2.5 Fault injection

Wrap native adapters to:
- fail the Nth call,
- return a success without changing state (silent failure),
- delay 6 s (timeout path),
- return `DISP_CHANGE_BADMODE`, `DISP_CHANGE_FAILED`, `DISP_CHANGE_BADPARAM`,
- corrupt the Night Light blob,
- make the journal path read-only / full.

Assertions: no unsupported mode applied; journal never lost; UI responsive; safe mode
after 2 failures; end state always enumerated.

### 2.6 Property-based

- For random mode lists and random failures, the final mode is always a member of the
  original list.
- Night Light codec: parse -> serialize -> parse is semantically idempotent.
- Restore: applying then restoring any plan returns the original snapshot.

## 3. Test data

| Data | Source |
|---|---|
| Night Light blobs | captured from real machines (on/off, manual/schedule, per-device) |
| EDID corpus | collected from projectors/TVs/monitors/AVRs, anonymized |
| Mode lists | captured per display model |
| Event traces | instrumented plug/unplug sessions |

Store in `tests/data/` with a README and no PII.

## 4. CI pipeline

1. Build (warnings as errors).
2. Unit tests.
3. Simulation tests.
4. Architecture tests (no `Beam.Windows` reference from `Beam.Core`; no P/Invoke in core).
5. Read-only integration tests on a Windows runner.
6. Analyzer/StyleCop.
7. Package (on tags).

No hardware tests in CI. Hardware results are recorded in `tests/hardware-results.md`.

## 5. Manual release checklist

- [ ] Fresh install on a clean VM (no .NET preinstalled assumptions).
- [ ] Plug/unplug 20x with a real projector.
- [ ] Kill the process during apply; restart; verify restore.
- [ ] Disable feature while projecting; verify restore.
- [ ] Uninstall; verify startup task removed and no leftovers.
- [ ] Accessibility Insights pass on settings and diagnostics.
- [ ] High contrast + 200% DPI + RTL layout.
- [ ] Battery: 1 h idle CPU/memory measurement.
- [ ] No network calls (Fiddler/Resource Monitor).
- [ ] Event log clean (no repeated errors).

## 6. Coverage targets

| Area | Target |
|---|---|
| `Beam.Core` | >= 90% line, >= 85% branch |
| `Beam.Windows` adapters | >= 70% with fakes |
| UI | smoke + key flows |
| Overall | >= 80% |

Coverage is a guide, not a goal; the property and fault-injection tests matter more.
