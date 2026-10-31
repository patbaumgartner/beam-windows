# UC-12 — Safety guardrails: never leave a broken display

**Phase:** 1 (MVP) · **Priority:** P0 · **Spike:** S6

## Goal
Make "Beam broke my screen" impossible by construction, not by testing harder.

## Actors
- Primary: none (policy).
- Secondary: all mutating components.

## Rules (normative)

1. **Enumerated only.** A mode may only be applied if it was returned by
   `EnumDisplaySettingsEx` for that exact GDI device in the current session.
2. **Test first.** `ChangeDisplaySettingsEx(..., CDS_TEST)` must succeed before apply.
3. **Snapshot first.** No mutation without a persisted inverse (UC-11).
4. **Verify after.** Re-read state after every mutation; mismatch => revert.
5. **Bounded attempts.** Max 3 mode candidates, max 1 retry per restore item.
6. **Bounded time.** Every native call is wrapped with a 5 s timeout; a hung driver
   must not hang the app (run mutations on a worker, not the UI thread).
7. **One writer.** A single mutation gate (semaphore) so apply/restore/DPI/Night Light
   never interleave.
8. **Self-change suppression.** Ignore display events for 3 s after our own mutation.
9. **No topology changes in MVP.** Never enable/disable/disable displays, never change
   primary, never change orientation (unless restoring).
10. **Protected displays.** The internal panel is never mode-changed in extend mode.
    In clone mode it is changed only because Windows requires a shared mode, and only
    to a mode it advertises.
11. **Safe mode.** After 2 consecutive failures, enter safe mode: no automatic
    mutations, tray shows an error, diagnostics offers a manual retry.
12. **Kill switch.** Disabling the feature restores immediately, synchronously.
13. **No elevation.** If an API returns access denied, report; never prompt for UAC.
14. **No blind restore.** Restore only values we set and that are still ours.

## Main flow (apply guard)
1. Acquire mutation gate.
2. Validate plan against rules 1, 2, 10.
3. Persist journal (rule 3).
4. Apply; verify; on failure revert.
5. Release gate; emit result.

## Failure taxonomy
| Class | Example | Action |
|---|---|---|
| Recoverable | `DISP_CHANGE_BADMODE` | try next candidate |
| Transient | device busy, `DISP_CHANGE_FAILED` | retry once after 300 ms |
| Fatal for session | Night Light verification twice failed | disable that capability, safe mode for it |
| Fatal for app | journal cannot be written | do not mutate, show error |

## Acceptance criteria
- AC1: 0 unsupported modes applied in 500-cycle S6.
- AC2: 0 black screens in S6.
- AC3: every mutation has a persisted inverse before it happens.
- AC4: a hung driver cannot hang the tray or the UI (timeout test).
- AC5: safe mode triggers after 2 failures and is visible.
- AC6: disabling the feature always restores.

## Edge cases
- Display unplugged between validation and apply -> apply fails; no revert needed.
- Driver returns success but does not change mode -> verification catches it.
- Two projectors apply concurrently -> serialized by the gate.
- User unplugs during restore -> skip missing items, finish the rest.

## Tests
- Fault injection at every step (S6).
- Property test: for random mode lists and random failures, the end state is always a
  member of the original mode list.
