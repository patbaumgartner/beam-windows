# UC-11 — Survive crash, kill, reboot, sleep/wake

**Phase:** 1 (MVP) · **Priority:** P0 · **Spike:** S6

## Goal
Beam must never be the reason a laptop is stuck at 1024x768. If it dies mid-operation,
the next start repairs the machine.

## Actors
- Primary: none (system).
- Secondary: journal file, startup task.

## Trigger
- Process start.
- Resume from sleep.
- Session unlock.
- Unexpected exit (detected on next start).

## Journal design
- Path: `%LOCALAPPDATA%\Beam\state\session.json`
- Written **before** the first mutation, fsync'd.
- Contents: schema version, session id, device id, timestamp, ordered list of
  `{kind, before, after, appliedAt, verified}` for mode, refresh, orientation, scale,
  Night Light (raw bytes base64), plus `phase`.
- Deleted after a fully verified restore.
- Atomic write: temp file + `File.Replace`.

## Main flow (startup)
1. Acquire single-instance mutex; if another instance runs, exit.
2. Load config; validate.
3. If `session.json` exists:
   - If the device is still active and `phase == active` -> verify state; re-apply if
     drifted (rate-limited), keep journal.
   - If the device is gone, or `phase == applying/restoring` -> run UC-07 restore,
     then delete journal.
4. Start watcher (UC-01).
5. If a projector is already active -> evaluate once (idempotent).

## Main flow (sleep/wake)
1. On resume, re-read topology and Night Light state.
2. If a session is active and something drifted -> correct once (rate-limited).
3. If the projector disappeared during sleep -> restore.

## Alternative flows
- **A1 Journal corrupt:** move to `session.bad.json`, do not restore blindly, notify
  "Previous session state could not be read"; offer "reset display settings" deep link.
- **A2 Crash during apply:** `phase == applying`; restore the recorded `before` values
  for items already marked applied.
- **A3 Crash during restore:** `phase == restoring`; resume restore from the first
  unverified item.
- **A4 Reboot with projector attached:** restore is not needed (Windows re-enumerates);
  verify and continue a new session.
- **A5 Two instances:** second exits with code 0 after focusing the first.
- **A6 Startup task disabled by user:** app still works when launched manually.

## Postconditions
- No stale journal after a successful start.
- No display left in a non-enumerated mode.
- At most one apply per session.

## Acceptance criteria
- AC1: kill -9 at 20 random points during apply -> next start always yields a valid
  display state (S6).
- AC2: journal is never partially written (atomic replace).
- AC3: startup repair completes within 5 s.
- AC4: no repair loop (max 3 attempts per start, then safe mode).
- AC5: safe mode is visible in the tray and in diagnostics.

## Edge cases
- Disk full -> journal write fails -> **do not mutate**; surface an error.
- Antivirus locks the journal -> retry, then refuse to mutate.
- Fast startup / hibernate: display state may be restored by the driver; verify first.
- Multiple users: journal is per user profile.

## Data recorded
Session id, phase transitions, repair actions, crash markers.

## Tests
- Unit: journal round-trip, phase machine, corruption handling.
- Fault injection: kill at every phase; disk full; read-only folder.
- Hardware: S6.
