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

Current totals: **330 tests**. Run them with:

```powershell
dotnet test Beam.sln -c Release
```

No test needs a projector, elevated rights, or a real Night Light setting. Tests that
would touch the display run only against fakes; the Windows interop tests exercise pure
struct helpers.

## Stress, UI, and interaction coverage

- **`Beam.App.Tests/StressTests.cs`** — burst display changes, concurrent `ApplyNow` /
  `RestoreNow` raced through the gate, repeated apply→restore cycles, journal-clear
  races, and session-state-machine stress (`Parallel.For`, barriers, rapid pause/resume).
- **`Beam.Windows.Tests/DisplayMonitorStressTests.cs`** — the real polling loop against
  scripted "hardware": burst changes, rapid connect/disconnect (no event storms), p95
  latency bound, transient capture failures, and repeated start/stop cycles.
- **`Beam.Tray.Tests/SessionManagerTests.cs`** — concurrent `TryBeginFix` (exactly one
  winner), pause/resume races, and per-transition event accounting.
- **`Beam.Tray.Tests/TrayInteractionTests.cs`** — the tray's user-visible behaviour that
  is testable without a message pump: status/balloon text for every session state, the
  "Pause/Resume Beam" label, the startup Run-key value round-trip, and the cursor-guard
  lifecycle the sticky-cursor fix relies on.

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
covered if any project exercised it. Current numbers (Release, 2026-11-07):

| Assembly | Lines | Gate |
|---|---|---|
| `Beam.Core` | 364 / 400 = 91.0% | ≥ 85% |
| `Beam.App` | 385 / 423 = 91.0% | ≥ 80% |
| `Beam.Cli` | 238 / 305 = 78.0% | ≥ 75% |
| `Beam.Windows` | 95 / 594 = 16.0% | reported only |
| `Beam.Tray` | 10 / 394 = 2.5% | reported only |

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
