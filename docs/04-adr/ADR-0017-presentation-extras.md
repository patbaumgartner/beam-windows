# ADR-0017 — Presentation extras: keep-awake, Do-Not-Disturb, audio (B3 / UC-17)

**Status:** Accepted
**Date:** 2026-11-14
**Amends:** answers O5 (open-questions.md)
**Related:** UC-17 (presentation extras), UC-12 AC3 (failure never fails the session), B3 (presenter-edition plan Phase B), ADR-0008 (transactional apply), ADR-0014 (ports)

## Context

UC-17 asks which presentation extras Beam should toggle while presenting: keep the
display awake, suppress notifications (Do Not Disturb), switch audio to the HDMI/projector
output. O5 deferred the choice to Phase B. All three are useful on stage, and all three
are **reversible user state**, so they join the same snapshot → apply → verify → restore
journal cycle as mode, DPI, and Night Light.

The hard requirement is UC-12 AC3: **a failing extra must never fail the session**.
Audio COM APIs in particular can fail on machines with unusual audio stacks; that must
degrade to a log line, not a safe-mode trip.

## Decision

Ship all three extras, **default-on, individually switchable** in settings
(`KeepAwake`, `DoNotDisturb`, `AudioToHdmi` in `BeamSettings`), implemented as Core ports
with Windows adapters in `src/Beam.Windows/Extras/`:

1. **Keep awake** — `KeepAwakeAdapter` holds
   `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)`
   on a dedicated thread while a fix is active and releases it (`ES_CONTINUOUS`) on
   restore. No registry writes; the OS releases the request automatically if Beam dies.
2. **Do Not Disturb** — `DndController` writes the Focus Assist "toasts off" DWORD
   under the per-user `HKCU` key it reads, snapshots the previous value, and restores
   it byte-for-byte. Per-user only — no admin.
3. **Audio to HDMI** — `AudioController` enumerates the HDMI/projector render endpoint
   and calls `SetDefaultEndpoint` on the **`IPolicyConfig`** interface (IID
   `F8679F50-850A-41CE-8C69-0AAE27B02D41`) obtained by casting the
   `CLSID_MMDeviceEnumerator` ComImport coclass (the cast performs the QI — no
   `CoCreateInstance` of the interface CLSID needed). The previous default render
   endpoint is snapshotted and restored on disconnect.

All three are wrapped so that **any exception is caught, logged, and treated as
"extra skipped"** — the mode/DPI/Night Light sequence is unaffected and the failure
counter (safe mode) is not incremented by extras alone. Each extra's before/after state
is persisted in the crash journal (`JournalExtras`) so a kill mid-presentation still
restores on next launch.

## Rationale

- All three answers to O5 were wanted; they are cheap, per-user, and reversible.
- Going through Core ports (`IKeepAwake`, `IDndController`/`ISystemPorts`,
  `IAudioController`) keeps `Beam.App` portable and fake-testable — the whole extras
  cycle is unit-tested without audio hardware.
- "Failure never fails the session" is enforced structurally (extras run after the
  core fix and never throw into it), not just by convention.
- `SetThreadExecutionState` is self-cleaning on process death, unlike a persisted
  sleep setting — the safest possible keep-awake mechanism.

## Consequences

**Positive**
- Presentations no longer die to screen sleep, popping toasts, or laptop speakers.
- Extras are unit-tested end-to-end with fakes (snapshot → apply → restore → verify).
- Crash-mid-presentation restores extras too (journal).

**Negative**
- Three more user-visible toggles to maintain and localize.
- DND via registry is the documented-but-unofficial Focus Assist path; if Microsoft
  changes it, the extra degrades to a logged skip (by design), not a failure.
- Audio switching can pick a different HDMI device than the user expects on
  multi-monitor GPU setups; the restore puts back exactly what was there.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Keep-awake via persisted "never sleep" power setting | Survives a Beam crash as a forgotten system change; `SetThreadExecutionState` cannot |
| DND via WNS/Notification platform APIs | Requires app registration and is heavier; the Focus Assist DWORD is what Settings itself writes |
| Audio via `CoCreateInstance(IPolicyConfig CLSID)` | The policy-config CLSID is not registered for direct cocreation on all builds; casting the enumerator coclass is the reliable QI path |
| Extras fail the session like core changes | Violates UC-17 AC3; audio stacks are too heterogeneous to risk safe mode |
| Extras off by default | O5 recommended keep-awake default-on; users who dislike DND/audio can switch them off in one click |

## Validation

- Unit/simulation: extras snapshot/apply/restore/verify with fakes; failure-injection
  tests prove a throwing extra logs and leaves the session state untouched
  (`Beam.App.Tests`, UC-17 AC3).
- Journal: kill-at-N-points tests include active extras; next launch restores them.
- Hardware: H-matrix rows H3/H4 (dock, KVM) verify audio follows the projector and is
  restored on unplug.

## Superseded-by

None.
