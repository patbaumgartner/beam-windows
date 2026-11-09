# Testing

Beam's test suite is split along the same lines as the production code. The rule is:
**test behaviour at the layer that owns it, and fake the platform.**

## Projects

| Project | Targets | What it covers |
|---|---|---|
| `tests/Beam.Core.Tests` | `net10.0` | Aspect classification, trigger, topology model, DPI ladder/planner, Night Light codec, startup command, percentiles |
| `tests/Beam.App.Tests` | `net10.0` | Session state machine, presentation sequencing, journal, file log sink — all through fake ports |
| `tests/Beam.Windows.Tests` | `net10.0-windows` | 16:9 mode ranking, LUID/rational helpers, DEVMODE construction, display-monitor polling contract + stress |
| `tests/Beam.Cli.Tests` | `net10.0-windows` | Argument parsing, every command, exit codes, JSON shape |
| `tests/Beam.Tray.Tests` | `net10.0-windows` | Tray text mapping, pause/resume labels, startup command, session/journal contracts, concurrency stress |
| `tests/Beam.Architecture.Tests` | `net10.0` | Project-reference and portability rules from [architecture.md](architecture.md), composition-root binary-name collision guard |

Current totals: **550 tests**. Run them with:

```powershell
dotnet test Beam.sln -c Release
```

No test needs a projector, elevated rights, or a real Night Light setting. Tests that
would touch the display run only against fakes; the Windows interop tests exercise pure
struct helpers.

## Stress, UI, and interaction coverage

- **`Beam.App.Tests/SoakTests.cs`** — the long-running acceptance scenarios: the
  500-cycle apply/restore soak (0 black screens, 500/500 valid end states), the
  UC-12 random-fault property test (end state is always a member of the original
  mode list), unplug-restore 20/20, at most one apply per attach, and kill -9 at
  20 points with the next start always valid.
- **`Beam.App.Tests/PresentationErrorInjectionTests.cs`** — concurrent `ApplyNow` /
  `RestoreNow` raced through the gate, repeated apply→restore cycles,
  journal-clear races, and session-state-machine stress.
- **`Beam.Windows.Tests/DisplayMonitorStressTests.cs`** — the real polling loop against
  scripted "hardware": burst changes, rapid connect/disconnect (no event storms), p95
  latency bound, transient capture failures, and repeated start/stop cycles.
- **`Beam.Tray.Tests/SessionManagerTests.cs`** — concurrent `TryBeginFix` (exactly one
  winner), pause/resume races, and per-transition event accounting.
- **`Beam.Tray.Tests/TrayInteractionTests.cs`** — the tray's user-visible behaviour that
  is testable without a message pump: status/balloon text for every session state, the
  "Pause/Resume Beam" label, the startup Run-key value round-trip, and the cursor-guard
  lifecycle the sticky-cursor fix relies on.

## Deep-testing push (edge, property, fuzz, error injection)

Beyond scenario tests, four suites attack the seams where real-world input is
untrusted (driver-reported integers, registry bytes, user args) and where faults land:

- **`Beam.Core.Tests/DpiScaleEdgeTests.cs`** — driver-supplied `-3` steps at int
  extremes. Found and fixed two real crash bugs: `Math.Abs(int.MinValue)` threw
  `OverflowException`, and `minStep + max` could overflow to a negative ladder index
  (`IndexOutOfRangeException`). The parser now rejects impossible inputs and returns
  null instead of throwing, for any `int` triple.
- **`Beam.Windows.Tests/DisplayMonitorResilienceTests.cs`** — found and fixed a third:
  `DisplayMonitor.Start()` captured the initial snapshot unguarded, so a transient
  Win32 failure at boot (dock enumerating, config in flux) crashed the tray app at
  startup. `Start()` now starts with no baseline and lets the loop establish one.
- **`Beam.Core.Tests/NightLightCodecFuzzTests.cs`** — exhaustive single-byte corruption
  of the real 38-byte registry fixture (38 × 255 mutants) plus 500 seeded multi-byte
  mutants: every mutant either parses or throws `NightLightFormatException` — never any
  other exception, never a wrong silent parse. Roundtrip + clock-regression epoch
  monotonicity properties.
- **`Beam.Core.Tests/ScalePlannerPropertyTests.cs`** — for any pixel width (0 …
  `uint.MaxValue`) × any ladder range, the chosen scale stays on the ladder and inside
  the reported range; score overflow and division edge cases locked.
- **`Beam.Core.Tests/TopologyPropertyTests.cs`** — topology invariants for degenerate
  inputs: empty path lists, garbage topology ids, inactive paths, zero-size desktops,
  null modes, and the id-0 fallback contract.
- **`Beam.Cli.Tests/CommandLineParserEdgeTests.cs`** — near-miss flags (`--Json`,
  `--night-light`, `---json`…), missing `--device` values, repeated flags; each is
  rejected or lands in positionals, never silently mis-parsed.
- **`Beam.Cli.Tests/NoMonitorConnectedTests.cs`** — every command against a zero-display
  or internal-only topology: no crash, calm messages, correct exit codes (`apply` →
  `NoProjector`), `restore` still succeeds, Night Light commands work display-less.
- **`Beam.App.Tests/PresentationErrorInjectionTests.cs`** — each dependency faults at
  each step: the session never wedges in Applying/Restoring, a failed restore leaves the
  journal active so the next launch retries recovery, null-constructor guards, dispose
  stops event reaction.
- **`Beam.App.Tests/SessionManagerStateMatrixTests.cs`** — from every state, every
  transition method fires: no wedged transient states, pause/resume always lands in a
  valid state, event stream carries from/to/reason.
- **`Beam.App.Tests/JournalCrashSafetyTests.cs`** — corrupt/empty/truncated/`null`
  journals read as null (never throw), `Clear` removes both files, 8-thread concurrent
  writes leave a parseable file, rehydrate skips unparseable entries but restores the
  rest.
- **`Beam.Core.Tests/PercentileEdgeTests.cs`**, **`StartupCommandEdgeTests.cs`** —
  NaN/Infinity samples, boundary p, negative latencies; Run-key near-misses, quoting,
  case-insensitivity, blank/null values.

### What automation still cannot catch

Honest boundary: real Win32 mouse input, actual mode changes on physical monitors, and
shell tray behaviour need hardware. Those paths are covered by the manual hardware
matrix below and the `spikes/S7.BugRepro` A/B proof — automation finds logic and
input-handling bugs; it cannot prove a projector shows pixels.

The WinForms surface itself is deliberately thin: cursor pinning and the shell-facing
balloon logic are kept in static/pure helpers so the interaction layer stays covered.
Real Win32 mouse input is still exercised manually via `spikes/S7.BugRepro` (see below).

## The fake ports

`tests/Beam.App.Tests/Fakes.cs` contains a complete in-memory implementation of every
port plus `Topologies` builders for the two cases that matter: mirrored-with-mismatch and
extended. `PresentationHarness` wires a `PresentationManager` to those fakes and exposes
every call count, so a test can assert the exact sequence:

```csharp
using var h = new PresentationHarness(Topologies.MirroredWithMismatch(), ScaleAt200);
await h.Manager.ApplyNowAsync();

Assert.Equal(SessionState.Active, h.Session.State);
Assert.Equal(1, h.NightLight.SetActiveCalls);
Assert.Equal(1, h.Journal.BeginCalls);
```

`tests/Beam.Cli.Tests/CliFakes.cs` does the same for the CLI, with `StringWriter` capturing
stdout/stderr so JSON can be parsed in assertions.

## What is deliberately not unit tested

- **`Beam.Windows` display mutation.** `SetDisplayConfig`, `ChangeDisplaySettingsEx`,
  Night Light registry writes, and DPI device-info calls need a real graphics stack.
  They are exercised manually against the hardware matrix below. The adapters are kept
  thin so the untested surface is small.
- **`Beam.Tray` WinForms controls.** Creating a `NotifyIcon` needs a message pump and a
  shell. The logic that can be isolated (state text mapping, cursor pinning) is factored
  out or verified manually.

`eng/coverage.ps1` reports those two assemblies but does not gate them. Pretending they
are unit-covered would be worse than saying so.

## Coverage

```powershell
pwsh eng/coverage.ps1              # run tests + report
pwsh eng/coverage.ps1 -NoTest      # reuse existing TestResults
pwsh eng/coverage.ps1 -Enforce     # fail below the gate (used by CI)
```

The script unions the Cobertura reports from every test project, so a line counts as
covered if any project exercised it. Current numbers (Release, 2026-11-08):

| Assembly | Lines | Gate |
|---|---|---|
| `Beam.Core` | 381 / 400 = 95.2% | ≥ 85% |
| `Beam.App` | 462 / 474 = 97.5% | ≥ 80% |
| `Beam.Cli` | 247 / 305 = 81.0% | ≥ 75% |
| `Beam.Windows` | 108 / 611 = 17.7% | reported only |
| `Beam.Tray` | 12 / 458 = 2.6% | reported only |

Coverage is a regression alarm, not a target. The sequencing and failure-path tests in
`Beam.App.Tests` matter more than the percentage.

## Adding a test

1. Put it in the project that owns the behaviour, not in a catch-all project.
2. Prefer a fake port over a real OS call. If you need a new platform capability, add a
   port and a fake first.
3. Assert observable effects: state, call counts, files written, process output.
4. Keep tests deterministic. Do not sleep when a `TaskCompletionSource` or an awaitable
   `PendingTask` can signal completion; the display monitor tests are the exception
   because polling is the behaviour under test.

## Mutation testing

Beam includes a Roslyn-based mutation tester at `eng/MutationTester/`. It applies small
semantic mutations (operator flips, literal changes, boolean negation) to source files,
builds, runs the test suite, and reports whether each mutant was **killed** (tests caught
the change) or **survived** (tests passed despite the change).

A **surviving mutant** indicates one of two things:

1. **Weak test ("evergreen test")** — the mutated code is exercised by tests, but the
   assertions don't catch the change. Add or strengthen assertions.
2. **Dead code** — the mutated code is never reached by any test. Remove the code or add
   coverage.

### Running

```powershell
# Run up to 50 mutants against Beam.Core
pwsh eng/mutation-test.ps1 -SourceProject src/Beam.Core/Beam.Core.csproj -TestProject tests/Beam.Core.Tests/Beam.Core.Tests.csproj

# Run with a specific report output
pwsh eng/mutation-test.ps1 -Output mutation-report.json -SourceProject src/Beam.Core/Beam.Core.csproj -TestProject tests/Beam.Core.Tests/Beam.Core.Tests.csproj -SourceProject src/Beam.App/Beam.App.csproj -TestProject tests/Beam.App.Tests/Beam.App.Tests.csproj
```

Exit code is non-zero when any mutants survive — CI should fail on this.

## Hardware matrix (manual)

Run before a release; record results in the PR or release checklist.

| ID | Setup | Checks |
|---|---|---|
| H1 | Laptop + 1080p projector (HDMI) | Detect, apply, restore on unplug |
| H2 | Laptop + 720p projector | Mode fallback |
| H3 | USB-C dock + HDMI | Enumeration timing, single apply |
| H4 | HDMI KVM | Bounce/flap handling |
| H5 | AVR passthrough | Does not misclassify as internal |
| H6 | 4K projector/TV | 16:9 at 4K, DPI choice |
| H7 | Sleep/wake with projector attached | Re-apply without churn |
| H8 | Kill Beam mid-apply | Next launch restores |
| H9 | Pause while presenting, then unplug | Paused session ignores changes; resume restores |
| H10 | 200% DPI + tray menu | Menu responsive, cursor never sticks |

The sticky-cursor regression has a dedicated proof in
`spikes/S7.BugRepro` (real Win32 mouse input, deterministic A/B against the old and fixed
builds). See [debugging.md](debugging.md) for how to run it.

## CI

`.github/workflows/ci.yml` builds and tests Debug and Release, verifies formatting with
`dotnet format`, runs the coverage gate, checks Markdown links, and uploads TRX +
Cobertura artifacts. See [releasing.md](releasing.md).
