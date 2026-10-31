# UC-06 — Turn Night Light off while projecting

**Phase:** 1 (MVP) · **Priority:** P0 · **Spikes:** S1, S7

## Goal
While a projector is active, the projected image must not be tinted orange by Night
Light. When the projector goes away, Night Light returns to exactly the state it had —
including "off", schedule-driven state, and strength.

## Actors
- Primary: user (implicitly; it is evening).
- Secondary: Windows Night Light engine, CloudStore registry.

## Trigger
`ProjectorActivated` after mode/scale steps, or `ModeApplied` if Night Light is a
post-step. Also on any Night Light state change while projecting (drift correction).

## Preconditions
- `nightLight.enabled` (feature setting) is true.
- Night Light is currently **active** (otherwise no-op).
- Not HDR (or the S7 rule says it is meaningful).

## Main flow
1. Read the state blob (default + per-device if available) and the settings blob.
2. Parse: `active`, `transitionCause`, `modifiedFiletime`, strength/schedule (read-only).
3. If not active -> no-op, record "already off".
4. Snapshot the exact raw bytes of every value we may touch (default and per-device).
5. Choose scope:
   - If per-device keys can be mapped to the projector's device path -> turn off only
     for the projector.
   - Else -> global off.
6. Build the inactive blob:
   - Keep the prologue, timestamps, and every unknown byte.
   - Remove only the `10 00` active marker; set transition cause to manual
     (`D0 0A 02`) so the engine does not think a schedule turned it off.
   - Recompute the prologue length field.
   - Bump the prologue epoch (now, at least previous + 2).
7. Write `Data` with `RegistryKey.SetValue(..., RegistryValueKind.Binary)`.
8. Re-read, parse, assert inactive. If not -> retry once after 250 ms; if still not ->
   restore snapshot, disable Night Light control for the session, surface a warning.
9. Record `NightLightOff(scope, priorState)` in the journal.
10. Start drift watch: if Night Light becomes active again while projecting (schedule
    boundary, user toggle, resume), re-apply once per 10 minutes maximum.

## Alternative flows
- **A1 Write fails / access denied:** fall back to gamma ramp (see below), or do
  nothing and warn.
- **A2 HDR active:** skip (S7) and note "Night Light has no effect in HDR".
- **A3 Per-device mapping unknown:** global off; restore global on disconnect.
- **A4 User turns Night Light back on while projecting:** respect it; stop correcting
  for the session (CS-3). Record the override.
- **A5 Night Light was already off:** no write at all.
- **A6 Schedule turns it on at sunset during a long presentation:** drift watch applies
  once; if the user turns it on again, stop.
- **A7 Machine sleeps and resumes with projector attached:** re-evaluate; if Night Light
  came back on, re-apply (subject to rate limit).

## Gamma-ramp fallback
- `CreateDC("DISPLAY", deviceName, ...)`, `GetDeviceGammaRamp` (save 3x256 words),
  `SetDeviceGammaRamp` with the identity ramp (neutral).
- Restore the saved ramp on disconnect.
- Limitations: ignored by some drivers, no effect in HDR, does not change the Night
  Light setting shown in Settings. Used only as a visual fallback, clearly labelled.

## Postconditions
- Projected image is neutral, or the user was told why it is not.
- Exact prior bytes are in the journal.
- Night Light setting is restored on disconnect (UC-07).

## Acceptance criteria
- AC1: 50/50 verified off/on cycles on 26H2 (S1) — **passed 2026-10-29**.
- AC2: semantically exact restore of both state values (active + transition cause + all
  unknown suffix bytes identical; the prologue epoch advances because Windows reverts a
  stale write).
- AC3: no write when already off.
- AC4: at most one corrective write per 10 minutes while projecting.
- AC5: a failed write never leaves the engine in a state we cannot restore.
- AC6: works with schedule on, schedule off, and per-device keys populated.
- AC7: 0 UI automation calls (no `Win+A`, no synthesized input).

## Edge cases
- Night Light strength 0 (effectively off) -> treat as off.
- Night Light schedule "sunset to sunrise" with sunset during the meeting.
- Two users signed in: Night Light is per user; only touch the current user's keys.
- Policy/registry redirection (enterprise): read may fail; degrade to gamma/none.
- Windows update changes the blob layout -> parser is strict; on mismatch, do not write;
  fall back to gamma and report "unsupported Night Light format" (never guess).

## Data recorded
Scope, prior active state, prior transition cause, verification result, fallback used.
Never the full settings blob in telemetry; only in a local diagnostic export.

## Tests
- Unit: parser/serializer round-trip against a corpus of real blobs (on/off, manual/
  schedule, 38/39 bytes, per-device variants).
- Simulation: registry adapter fake with failure injection.
- Hardware: S1 + S7.
