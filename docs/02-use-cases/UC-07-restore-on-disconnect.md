# UC-07 — Restore every changed setting on disconnect

**Phase:** 1 (MVP) · **Priority:** P0 · **Spike:** S6

## Goal
When the projector goes away (or the user asks), put the machine back exactly as it
was — mode, refresh, orientation, scale, Night Light — without clobbering changes the
user made in the meantime.

## Actors
- Primary: user unplugging.
- Secondary: display stack, Night Light engine.

## Trigger
- `DisplaysDeactivated` containing the projector.
- `ProjectorDeactivated`.
- Explicit `Restore` (tray, hotkey, CLI, toast Undo).
- Feature disable / app exit (best effort).
- Recovery on next start if a journal is found (UC-11).

## Preconditions
- An active journal exists for this projector session.

## Main flow
1. Load the journal (in-memory, mirrored to disk).
2. Verify the projector is really gone (or the user asked to restore anyway).
3. For each recorded change, in reverse order:
   - **Night Light:** read current state. If it is still the state we set (off), write
     back the snapshot bytes. If the user changed it, skip and record `skipped:user`.
   - **Scale:** read current. If still ours, restore snapshot. Else skip.
   - **Mode/refresh/orientation:** read current. If still ours, restore snapshot mode
     with `CDS_TEST` + apply + verify. Else skip.
4. Verify each restoration; retry once on transient failure.
5. If a display is missing (projector unplugged before restore of its mode): skip that
   item; it will be re-created by Windows on next connect.
6. Delete the journal only after all restorable items are verified.
7. Emit `Restored(items, skipped)`; toast only if something was skipped or failed.
8. Suppress self-induced display events for 3 s.

## Alternative flows
- **A1 User changed resolution manually while projecting:** skip mode restore (CS-3).
- **A2 Projector unplugged while apply was in progress:** abort apply, restore whatever
  was already applied, then finish.
- **A3 Restore fails:** keep the journal, retry on next start, show a non-blocking
  warning with a "Restore now" action.
- **A4 Two projectors:** one journal per device; restore each independently.
- **A5 Feature disabled mid-session:** restore immediately.
- **A6 App crash:** journal remains; UC-11 recovers on next start.

## Postconditions
- Every setting Beam changed is back to its pre-session value, or explicitly skipped
  because the user changed it.
- Journal is deleted only when nothing is left to restore.

## Acceptance criteria
- AC1: 100/100 restore cycles return the exact pre-session mode, refresh, scale and
  Night Light bytes (S6).
- AC2: never overwrites a user change made during the session.
- AC3: restore completes within 2 s of the projector being gone.
- AC4: a failed restore is retried at next start and never silently dropped.
- AC5: no restore action when Beam did not change anything.

## Edge cases
- Laptop lid closed while projecting -> internal display inactive; restore must not
  re-enable it (we never changed topology in MVP).
- Docking station removal takes the projector and the network with it -> restore is
  local, no network needed.
- Windows already restored the mode itself (driver behaviour) -> detect and no-op.
- Night Light was turned on by the schedule while we were projecting -> policy: restore
  the snapshot only if current state equals what we set; otherwise leave it (the user's
  schedule is now correct).

## Data recorded
Items restored, items skipped and why, durations, failures.

## Tests
- Unit: reverse-order planner, "still ours" predicate, skip logic.
- Simulation: change-during-session matrix.
- Hardware: S6 loop (500 cycles).
