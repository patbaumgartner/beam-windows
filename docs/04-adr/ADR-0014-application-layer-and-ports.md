# ADR-0014 — UI-free application layer and port boundary

**Status:** Accepted
**Date:** 2026-11-01
**Replaces/refines:** ADR-0003 (stack), ADR-0004 (resolution API), ADR-0007 (detection events)

## Context

Phase 1 shipped a WinForms tray MVP whose `PresentationManager` and `SessionManager` lived
inside `Beam.Tray`. That worked, but two problems were becoming structural:

1. **The tray was the only host.** The CLI, tests and the future WinUI 3 settings app each
   had to reach through a WinForms assembly to get at the core sequencing, so "test the
   presentation" meant "construct a tray".
2. **The sequencing touched Windows directly.** The apply/restore code in the tray called
   display, DPI and Night Light adapters directly. Because those only exist on
   `net10.0-windows`, the *whole* sequencing layer was pinned to Windows — even the parts
   that were pure decisions.

We want the presentation engine to be (a) host-independent, (b) unit-tested without a
projector or a UI thread, and (c) liftable into PowerToys/PowerDisplay as-is (ADR-0001).

## Decision

Introduce **`Beam.App`**, a plain `net10.0` project (no `-windows` suffix, no WinForms, no
P/Invoke, no `Beam.Windows` reference) that owns:

- `PresentationManager` — the apply → active → disconnect → restore sequences,
- `SessionManager` — the `Idle/Applying/Active/Restoring/Paused` state machine,
- `JournalStore` — the crash journal (portable file I/O),
- `FileLogSink` — the human + JSONL logger.

`Beam.App` depends only on **ports** declared in `Beam.Core.Ports`:

| Port | Windows implementation |
|---|---|
| `ITopologySource` | `Beam.Windows.DisplayInventory` |
| `IDisplayChangeSource` | `Beam.Windows.DisplayMonitor` |
| `IDisplayModeController` | `Beam.Windows.DisplayModeController` |
| `IDpiController` | `Beam.Windows.WindowsDpiController` |
| `INightLightController` | `Beam.Windows.NightLightController` |

The composition roots (`Beam.Tray.Program`, `Beam.Cli.Program`) are the **only** places that
know both the ports and the concrete adapters. `Beam.Cli` also references `Beam.Windows`
directly to build its one-shot diagnostic commands with the real adapters; that is allowed
because the CLI is a root, and its *logic* still runs against ports held by `CliApp`.

`Beam.Architecture.Tests` enforces the boundary: it reads the `.csproj` files and scans the
portable layers' source for `System.Windows.Forms`, `[DllImport]`, `[LibraryImport]` and
`Microsoft.Win32`, failing CI with a pointer to the rule.

## Consequences

**Positive**

- The entire presentation cycle is unit-tested without a projector or a UI thread
  (330 tests at Phase 1, ~91% line coverage in Core and App).
- PowerToys can lift Core + App without carrying any UI or Win32 code.
- The tray becomes a thin shell; the CLI shares the same engine.
- `net10.0` (non-`-windows`) is a compile-time guarantee the portable layers cannot touch a
  Windows-only API.

**Negative**

- One more project and one more layer of interfaces to navigate when reading the code.
- The port surface is fixed by what the Phase 1 adapters needed; adding a feature may mean
  extending a port (and its fake) before writing the adapter.
- A rare temptation to bypass a port with a direct reference; the architecture test is the
  backstop, and it is non-negotiable.

**Deferred**

- A WinUI 3 settings surface (ADR-0003) can later reference `Beam.App` directly — nothing in
  `Beam.App` changes shape when that host arrives.
- Night Light, DPI and mode restoration stay in `Beam.Windows`; they are thin and verified on
  hardware (see [test-strategy](../05-delivery/test-strategy.md#4-manual-verification)).

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Keep sequencing in `Beam.Tray` | Locks the engine to WinForms; CLI and tests must fake WinForms; no upstream path |
| Put sequencing in `Beam.Core` | Core would need the ports anyway but would also own workflow; Core stays small and pure by not owning it |
| Share a mutable facade + mutex between tray and CLI | Works but serialises on a process the UI thread must answer; the CLI is deliberately one-shot and side-effect-light |
| A real IPC layer (`PipeServer`) now | The CLI does not need the tray's loop; add IPC only when a Windows shell needs it (original plan §5.4) |
