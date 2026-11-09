# ADR-0015 — Detection polling and bounded-time deviation (amends ADR-0007)

**Status:** Accepted
**Date:** 2026-11-08
**Amends:** ADR-0007 (event-driven detection with debounce and a 30 s reconcile)
**Related:** UC-01 (detection), UC-12 rule 6 (bounded time), S5 (detection soak), S6 (500-cycle safety soak)

## Context

ADR-0007 specified event-driven detection: `DeviceWatcher` over
`Windows.Devices.Display.DisplayMonitor`, `GUID_CONSOLE_DISPLAY_STATE` power
notifications, and `WM_DISPLAYCHANGE`, with a 1.5 s debounce, a stability wait
(no new device for 1.5 s, max 6 s), 3 s self-suppression, a 30 s reconcile
timer, no faster polling, and a flap cooldown (≥ 3 changes in 2 minutes →
10-minute cooldown).

UC-12 rule 6 additionally requires every native call to be wrapped with a 5 s
timeout so a hung driver cannot hang the app.

The shipped implementation deviates from both, deliberately:

1. **Detection is polling, not event-driven.**
   `Beam.Tray.Program` polls `DisplayMonitor` every **250 ms** and
   `PresentationManager` applies a **3 s self-change window** (default
   `DefaultSelfChangeWindow`) after its own mutations. There is no
   `DeviceWatcher`, no power-setting notification, no 30 s reconcile timer,
   and no flap cooldown.

2. **No 5 s native-call timeout.**
   P/Invoke display calls are not cancellable, so the 5 s wrapper from UC-12
   rule 6 is not implemented. Mutations instead run on a worker thread behind
   the single mutation gate (UC-12 rule 7), so a hung call blocks only the
   worker, never the UI tray thread; the gate serialises apply/restore/DPI/Night
   Light and the 3 s self-change window suppresses re-trigger.

## Decision

Accept the deviation as implemented:

- **Detection:** 250 ms `DisplayMonitor` polling in the tray composition root
  (`Beam.Tray.Program`), plus the 3 s self-change window in
  `PresentationManager`. This is deterministic, easy to fake in unit tests
  (no projector, no UI thread, no event pump), and measures well within UC-01
  AC2's p95 ≤ 1.5 s detection budget (S5 validates this on hardware).
- **Bounded time:** not implemented as a 5 s native-call wrapper. Mutations
  run on a worker via the mutation gate (UC-12 rule 7), which is the
  achievably-testable equivalent: no UI-thread hang, serialised mutations, and
  safe-mode entry after 2 consecutive failures (UC-12 rule 11).

ADR-0007 remains the *intended* design for a future event-driven revision; this
ADR records why the shipped code differs and that the difference is accepted.
ADR-0007 is **not** changed to match — the event-driven path is deferred, not
removed.

## Rationale

- Simplicity: one capture cadence, no event-burst coalescing, no reconcile
  timer to test.
- Testability: the whole engine (`Beam.App`) is `net10.0`, portable, and
  unit-tested with fakes; an event-driven detection layer would require a UI
  thread / real device events.
- Latency: 250 ms polling detects a connect in at most ~250 ms, comfortably
  inside the 1.5 s p95 budget; S5 confirms this across 30 plug/unplug cycles.
- Correctness: a missed event cannot silently dead the feature because every
  attach is re-evaluated on the next poll; the 3 s self-change window prevents
  fix loops without a reconcile timer.

## Consequences

**Positive**
- p95 detection ≤ 1.5 s over 30 cycles (S5), 0 missed, ≤ 1 apply per attach.
- 0 black screens and 500/500 valid end states across the 500-cycle safety
  soak (S6 / UC-12 AC1-AC2).
- Deterministic, fully unit-tested detection path.

**Negative**
- Detection latency is bounded by the poll cadence (≤ 250 ms) rather than
  event-driven (near-instant); acceptable within the UC-01 budget.
- No 30 s safety-net reconcile: a permanently broken capture would go unnoticed
  until the next poll succeeds; mitigated by the capture failure path (Display
  monitor keeps polling on transient failure).
- UC-12 rule 6's literal 5 s native-call timeout is not implemented; rule 7's
  mutation gate + worker execution is the substitute.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Implement ADR-0007 as specified (events + reconcile + flap cooldown) | Requires a UI thread / real device events; not unit-testable in `Beam.App`; added complexity for a latency budget already met by polling |
| Implement the 5 s native-call timeout | P/Invoke display calls are not cancellable; a `CancellationTokenSource` cannot interrupt a blocking native call, so the timeout would be a lie |
| Events only | One missed event = feature silently dead; no safety net |

## Validation

- S5: 30 plug/unplug cycles (HDMI, USB-C DP-alt, dock, sleep/wake, KVM); p95
  detection ≤ 1.5 s, 0 missed, ≤ 1 apply per attach.
- S6 / UC-12 AC1-AC2: 500 automated apply/restore cycles, 0 black screens,
  500/500 valid end states; property test — end state is always a member of the
  original mode list.
- Roadmap Phase-1 exit: unplug restore 20/20; kill -9 at 20 points → next
  start always valid; 500-cycle S6 clean.

## Superseded-by

None (deviation accepted; ADR-0007 deferred for a future revision).
