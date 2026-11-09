# Test strategy

This chapter is the delivery-level view: what is automated, what is verified by hand, and
what "done" means before a release. The day-to-day mechanics (how to run a single test, how
the fakes are wired, how coverage is collected) live in
[docs/06-development/testing.md](../06-development/testing.md).

## 1. Principles

1. **Test behaviour where it is owned.** Pure decisions in `Beam.Core`, sequencing in
   `Beam.App`, adapters in `Beam.Windows`.
2. **Fake the platform, not the logic.** The application layer runs against in-memory
   ports; no test needs a projector, a registry write, or elevation.
3. **Anything that can blank a screen is exercised twice.** Simulation first, hardware
   second. A green unit suite is not permission to skip the hardware matrix.
4. **Do not claim coverage you do not have.** `Beam.Windows` and `Beam.Tray` are reported
   but not gated; pretending they are unit-covered would be worse than saying so.
5. **A flaky test is a bug.** Time-sensitive tests use polling helpers with timeouts and
   deterministic signals, never bare `Thread.Sleep` as a synchronisation primitive.

## 2. Automated levels

### 2.1 Unit — `net10.0`, fast, no OS

| Suite | Typical cases |
|---|---|
| `Beam.Core.Tests` | Aspect classification and tolerance, trigger truth table, topology model (mirror fallback when the topology id is unreadable), DPI ladder/planner traps, Night Light blob round-trip and malformed input, `StartupCommand` matching, percentile maths |
| `Beam.App.Tests` | Session transitions, apply/restore ordering, journal before mutation, recovery on launch, pause semantics, failure paths (`ApplyNow` override), `FileLogSink` formats and no-throw guarantee |
| `Beam.Architecture.Tests` | Project-reference rules; no WinForms/P/Invoke/`Microsoft.Win32` in portable layers; composition roots have no inbound references |

### 2.2 Adapter contracts — `net10.0-windows`, no mutation

`Beam.Windows.Tests` covers the parts of the adapter layer that are pure or externally
observable without changing a display:

- best 16:9 mode ranking and refresh preference,
- `LUID` formatting/parsing and the `DISPLAYCONFIG_RATIONAL` helper,
- `DEVMODE` construction,
- `DisplayMonitor` polling contract: one event per real change, no re-raise on identical
  polls, capture failure does not stop the loop, `Stop`/`Dispose` are idempotent and
  terminal.

### 2.3 CLI — `net10.0-windows`, no process

`Beam.Cli.Tests` drives `CliApp` with fake ports and `StringWriter`, so every command,
usage error, exit code and JSON shape is asserted without spawning a process or touching
the real display:

- `status`, `devices` (human + `--json`),
- `apply` (plan, `--dry-run`, rejection),
- `restore` (with/without Night Light),
- `dpi read` / `dpi set` (including out-of-range values),
- `nightlight on|off`,
- unknown command, missing arguments, help.

### 2.4 Tray text — `net10.0-windows`

`Beam.Tray.Tests` covers `TrayPresentation`, the pure mapping from session state to menu
text and icon. WinForms control creation needs a message pump and the shell, so the
`NotifyIcon` itself is verified by hand (see §4).

### 2.5 Simulation through fakes

`tests/Beam.App.Tests/Fakes.cs` provides a complete in-memory implementation of every port
plus `PresentationHarness`, which exposes call counts and ordering. Scenarios covered
include:

- happy path: plug in → apply → active → unplug → restore → idle,
- no trigger when extended, when aspects match, or when only the internal panel is present,
- user pause (automatic trigger ignored) and resume,
- `ApplyNow` as a deliberate manual override,
- journal write failure and recovery on the next launch,
- Night Light write/snapshot failure,
- DPI read/write failure,
- disconnect arriving while a fix is still running.

### 2.6 Simulation through fakes

`tests/Beam.App.Tests/Fakes.cs` provides a complete in-memory implementation of every port
plus `PresentationHarness`, which exposes call counts and ordering. Scenarios covered
include:

- happy path: plug in → apply → active → unplug → restore → idle,
- no trigger when extended, when aspects match, or when only the internal panel is present,
- user pause (automatic trigger ignored) and resume,
- `ApplyNow` as a deliberate manual override,
- journal write failure and recovery on the next launch,
- Night Light write/snapshot failure,
- DPI read/write failure,
- disconnect arriving while a fix is still running.

### 2.7 Mutation testing

Beam includes a Roslyn-based mutation tester (`eng/MutationTester/`) that applies small
semantic mutations to source files and runs the test suite against each mutant:

- **Killed mutants** — tests fail, meaning the change is caught (good).
- **Surviving mutants** — tests pass despite the change, indicating either a weak test
  ("evergreen test") or dead code that nothing exercises.

Run with:

```powershell
pwsh eng/mutation-test.ps1 -SourceProject src/Beam.Core/Beam.Core.csproj -TestProject tests/Beam.Core.Tests/Beam.Core.Tests.csproj
```

Exit code is non-zero when any mutants survive.


### 2.8 Soak, property, and fault-injection tests

`tests/Beam.App.Tests/SoakTests.cs` carries the long-running, deterministic acceptance
scenarios (no bare sleeps; fakes; seeded RNG; polling helpers):

- **S6 / UC-12 AC1-AC2 — 500-cycle apply/restore soak:** 0 black screens,
  500/500 valid end states, exactly one apply and one restore per cycle.
- **UC-12 property test:** for random mode lists and random injected apply failures,
  the end state after every cycle is always a member of the original mode list
  (`Beam.Windows.Tests/DisplayModeControllerPropertyTests.cs` at the controller level;
  safe-mode streak tracking at the manager level in `SoakTests.cs`).
- **UC-01 AC2 — plug/unplug:** 30 cycles, exactly one event per physical action.
- **UC-01 AC3 — no event storm:** ≤ 2 events per physical action (KVM bounce
  coalesces within one poll interval).
- **S5 — detection:** p95 ≤ 1.5 s over 30 plug/unplug cycles, 0 missed, ≤ 1 apply
  per attach (`DisplayMonitorStressTests.cs` measures change→event latency itself).
- **Roadmap Phase-1 exit:** unplug restore 20/20; kill -9 at 20 points → the next
  start is always valid (journal-only survival); 500-cycle S6 clean.

Fault injection is exercised at every sequencing step (Night Light, mode, DPI, journal)
via the fakes' `ApplyResult`/`RestoreResult` and `BeforeCall` hooks; a killed process
survives with only the journal, and recovery restores exactly what was left applied.

## 3. Coverage

```powershell
pwsh eng/coverage.ps1              # run tests + report
pwsh eng/coverage.ps1 -NoTest      # reuse existing TestResults
pwsh eng/coverage.ps1 -Enforce     # fail below the gate (used by CI)
```

The script unions the Cobertura reports from every test project, so a line counts as
covered if any project exercised it.

| Assembly | Current (2026-11-08) | Gate |
|---|---|---|
| `Beam.Core` | 381 / 400 = 95.2% | ≥ 85% |
| `Beam.App` | 462 / 474 = 97.5% | ≥ 80% |
| `Beam.Cli` | 247 / 305 = 81.0% | ≥ 75% |
| `Beam.Windows` | 108 / 611 = 17.7% | reported only |
| `Beam.Tray` | 12 / 458 = 2.6% | reported only |
| Overall | 1210 / 2248 = 53.8% | — |

The gate is a regression alarm, not a target. A change that lowers Core or App coverage by
more than a rounding error needs a justification in the pull request.

## 4. Manual verification

### 4.1 Hardware matrix

Run before a release and record the result in the release notes or pull request.

| ID | Setup | Checks |
|---|---|---|
| H1 | Laptop + 1080p projector (HDMI) | Detect, apply 16:9, readable DPI, restore on unplug |
| H2 | Laptop + 720p projector | Mode fallback, no unsupported mode applied |
| H3 | USB-C dock + HDMI | Enumeration timing, exactly one apply |
| H4 | HDMI KVM | Bounce/flap handling, no restore loop |
| H5 | AVR passthrough | Not misclassified as an internal panel |
| H6 | 4K projector/TV | 16:9 at 4K, sensible DPI choice |
| H7 | Sleep/wake with projector attached | Re-apply without churn |
| H8 | Kill Beam mid-apply | Next launch restores before monitoring starts |
| H9 | Pause while presenting, then unplug | Paused session ignores changes; resume restores |
| H10 | 200% DPI + tray menu | Menu responds, cursor never sticks |

### 4.2 Tray interaction checks

The tray is manual because it needs Explorer, the notification area and real mouse input:

1. Launch from a terminal: icon appears, startup balloon is correct.
2. Right-click the icon: menu opens immediately; hover every item; the cursor stays an
   arrow. Repeat ten times, including while a session change is in flight.
3. Click **Launch at sign-in** on and off: the installer's `--hidden` value is recognised,
   and a confirmation or warning balloon appears.
4. Click **Pause Beam** / **Resume Beam**: the label and tooltip match the state.
5. **Apply projector mode now** with no projector attached: the session fails gracefully
   and the CLI still reports a usable state.
6. **Restore display** while idle: no error, no display flash.
7. **Exit Beam**, relaunch: single instance, no duplicate icons or monitor loops.

### 4.3 Sticky-cursor regression proof

`spikes/S7.BugRepro` reproduces the 2026-10-31 bug with a real WinForms message loop and
the shell's context-menu path. It reports a suspected stall when the UI pulse stops while
the menu is open. Run it against the old and new builds; see
[debugging.md](../06-development/debugging.md#reproducing-the-sticky-cursor-bug) for the
exact commands and the A/B results.

## 5. CI pipeline

`.github/workflows/ci.yml`:

1. Restore, then `dotnet format --verify-no-changes` (Release leg).
2. Build Debug and Release; warnings are errors.
3. `dotnet test --collect:"XPlat Code Coverage"` for both configurations.
4. Coverage gate on the Release leg (`eng/coverage.ps1 -NoTest -Enforce`).
5. Markdown link check (`eng/check-links.ps1`).
6. Upload TRX and Cobertura artifacts.
7. On `main`: build a snapshot installer and publish it to the `snapshot` release.

No hardware is attached to CI. Display mutation, Night Light writes and tray interaction
are manual by design.

## 6. Release checklist

Use this in addition to the hardware matrix:

- [ ] `dotnet test Beam.sln -c Release` — all tests pass.
- [ ] `pwsh eng/coverage.ps1 -NoTest -Enforce` — gates pass.
- [ ] `dotnet format Beam.sln --verify-no-changes` — clean.
- [ ] `pwsh eng/check-links.ps1` — all relative links resolve.
- [ ] Fresh install on a clean Windows 11 VM; no developer SDK assumptions.
- [ ] Uninstall removes the `Run` entry and leaves no `Program Files` residue.
- [ ] Plug/unplug 20× with a real projector; no stuck state, no duplicate applies.
- [ ] Kill the process during apply; restart; verify the journal restores the display.
- [ ] Verify the tray at 100%, 150% and 200% DPI, light and dark taskbar.
- [ ] Check `%LOCALAPPDATA%\Beam\beam.log` and `beam-detailed.jsonl` for unexpected errors.
- [ ] Confirm no network calls (Resource Monitor or Fiddler).
