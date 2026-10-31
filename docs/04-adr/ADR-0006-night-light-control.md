# ADR-0006 — Night Light: CloudStore write with gamma fallback

**Status:** Accepted — **validated by spike S1 on 2026-10-29** (see
`05-delivery/spike-results.md`).
**Date:** 2026-10-29

## Context

Windows has **no public API** to enable/disable Night Light. The state is stored in
per-user CloudStore binary values:

```
HKCU\...\CloudStore\Store\DefaultAccount\Current\
  default$windows.data.bluelightreduction.bluelightreductionstate\
    windows.data.bluelightreduction.bluelightreductionstate   (Data, REG_BINARY)
  default$windows.data.bluelightreduction.settings\...
  {guid}$windows.data.bluelightreduction.bluelightreductionstateperdevice\...  (26H2)
  {guid}$windows.data.bluelightreduction.settingsperdevice\...
```

The layout is documented only by reverse engineering (Enyium/sem-reg-rs). Writing applies
immediately, but a malformed write can put the engine into a broken state until sign-out
or reboot.

Options:
1. Write the state blob.
2. Gamma ramp override.
3. UI automation (`Win+A`, click).
4. Do nothing; tell the user.

## Decision

Use **(1) as primary, (2) as fallback, never (3)**.

**Semantics (confirmed by the product owner):** "Night Light is a Windows settings
toggle." Beam must flip the **real Windows feature** — the same state shown in
Settings > Display > Night Light — not apply a custom colour filter. That is why the
CloudStore state blob, not a gamma-only filter, is the primary mechanism. The gamma ramp
is only a visual fallback when the real toggle cannot be driven.

- Parse strictly; if the blob does not match the known layout, do not write.
- Snapshot exact bytes of every value touched (default + per-device).
- Mutate only the active marker (`10 00`), preserve all unknown bytes, set manual
  transition cause, recompute the prologue length and epoch.
- Re-read and verify; retry once; on second failure restore the snapshot, disable Night
  Light control for the session, and surface a warning.
- Rate-limit: one corrective write per 10 minutes while projecting.
- Prefer per-device scope when a key can be mapped to the projector; otherwise global.
- Skip when HDR is active (S7) or Night Light is already off.
- Fallback to gamma ramp (neutral ramp) only if the registry path is unsupported.
- Feature-level kill switch: two verification failures -> `IsSupported = false` until
  restart, with a diagnostics entry.

## Rationale

- It changes the *actual* Night Light setting, which is what the user asked for, and it
  restores to the true prior state.
- UI automation is locale-dependent, focus-stealing, and dangerous during a
  presentation. Unacceptable.
- Gamma-only does not change the setting and is ignored by some drivers/HDR.

## Consequences

**Positive**
- Correct visible result and correct restore.
- No UI automation, no input synthesis, no focus stealing.
- Isolated behind `INightLightController`; replaceable if Microsoft ships an API.

**Negative**
- Undocumented format; can break on Windows updates.
- Potential engine breakage if a write is malformed (mitigated by strict parsing and
  snapshot/restore).
- Per-device key mapping is unknown (S1).

## Mitigations / test obligations

- Golden-blob corpus: on/off, manual/schedule, 38/39 bytes, per-device variants, HDR.
- Round-trip property: parse -> serialize -> parse yields the same semantic state.
- Never write if the parse is not strict-success.
- S1 passed 50/50 on 2026-10-29; re-run on each new Windows build before release.

## Validation (spike S1, 2026-10-29)

Run on Windows 11 26H2 build 26300 with two live values present: the default value
(38 bytes) and one per-device value (39 bytes, 5-byte suffix).

| Check | Result |
|---|---|
| Strict parse of both live values | pass (default + per-device) |
| Turn on, verified by re-read | pass (38 -> 43 bytes, +`10 00`, +`D0 0A 02`) |
| Turn off, verified by re-read | pass (43 -> 41 bytes) |
| Restore an ON snapshot after turning off | pass (both values back to `active=True`) |
| 50 toggle cycles, verified each time | 50/50, zero failures |
| Restore original OFF/Schedule state | pass (38/39 bytes, `active=False`, `cause=Schedule`) |

Restore is **semantically exact, not byte-exact**: the prologue epoch must advance or
Windows reverts the write, so the restored value is re-serialized with a fresh epoch while
the semantic state (active, transition cause) and all unknown suffix bytes are identical.
The settings value (strength, schedule) is never touched.

Two robustness bugs were found by the codec test suite and fixed before shipping:
a truncated value threw `ArgumentOutOfRangeException` instead of
`NightLightFormatException`, and `Read()` did not have a catch-all guard.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| UI automation | Fragile, locale/focus, unacceptable risk |
| Gamma ramp only | Does not change the setting; driver/HDR limitations |
| Public API | Does not exist |
| Ask the user to toggle | Defeats the purpose |

## Revisit trigger

Microsoft publishes a Night Light API, or S1 fails twice on new builds.
