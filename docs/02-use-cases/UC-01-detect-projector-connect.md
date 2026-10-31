# UC-01 — Detect that an external display became active

**Phase:** 1 (MVP) · **Priority:** P0 · **Spike:** S5

## Goal
Notice, quickly and exactly once, that a display became an *active* part of the
desktop, and hand a stable snapshot to the rest of the system.

## Actors
- Primary: the user plugging in a cable (HDMI, DP, USB-C DP-alt) or docking.
- Secondary: Windows display stack.

## Trigger
Any of:
- `DeviceWatcher` (over `DisplayMonitor`) `Added` / `Updated` / `Removed`
- `WM_DISPLAYCHANGE`
- `GUID_CONSOLE_DISPLAY_STATE` power notification (display on/off/dim)
- application start, session unlock, resume from sleep
- 30 s reconcile timer (safety net)

## Preconditions
- Beam is running and enabled.
- Not in "paused" state.
- Not in safe mode.

## Main flow
1. Event arrives; start/restart a 1.5 s debounce timer.
2. On debounce expiry, call `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)`.
3. Build a topology snapshot: for each active path, device path, GDI name, friendly
   name, output technology, source mode (w/h/refresh), clone group, position, primary.
4. Compare with the previous snapshot using stable device ids, not array order.
5. If the set of active device ids is unchanged and modes are unchanged -> stop
   (no action, no log spam).
6. If one or more *new* active device ids appeared -> emit `DisplaysActivated(added)`.
7. If one or more disappeared -> emit `DisplaysDeactivated(removed)`.
8. Pass the snapshot to the classifier (UC-02) and the state machine.

## Alternative flows
- **A1 Display attached but not active** (PC screen only): not "added" as active;
  no action. Log at debug level.
- **A2 Rapid replug / KVM bounce:** debounce coalesces; ids may reappear; if the
  resulting snapshot equals the pre-event snapshot, no action.
- **A3 Dock attach:** multiple devices appear at once; process as one batch.
- **A4 Resume from sleep with projector already attached:** treat as a fresh
  evaluation, but apply only if not already in the target state (idempotent).
- **A5 App start with projector attached:** evaluate once, but respect
  `applyOnStartup` (default true only if the projector was not already handled in
  this session).

## Postconditions
- A topology snapshot exists with stable ids.
- Exactly one `DisplaysActivated` / `DisplaysDeactivated` per real change.
- No display mutation has happened yet (detection is read-only).

## Acceptance criteria
- AC1: p95 from "display usable by Windows" to event <= 1.5 s (S5).
- AC2: 30/30 plug/unplug cycles produce exactly one event each.
- AC3: no event storm: <= 2 events per physical action.
- AC4: no polling more often than 30 s when idle.
- AC5: RDP/virtual displays never produce `DisplaysActivated` for classification.
- AC6: "PC screen only" with a cable attached produces no activation event.

## Edge cases
- Projector connected through an AVR: the active target is the AVR; still an activation
  (UC-13 decides classification).
- Monitor reported twice (same EDID, two ports): ids differ, so two entries; profiles
  keyed per port.
- Display asleep: `GUID_CONSOLE_DISPLAY_STATE` off is not a disconnect; do not restore
  on screen-off.

## Data recorded
Topology snapshot (ids, names, output tech, modes), event source, timestamps.
No content, no window titles, no user identifiers.

## Tests
- Unit: debounce/coalescing, snapshot diffing, idempotent event emission.
- Simulation: fake inventory sequences (plug, unplug, bounce, dock, RDP).
- Hardware: S5 matrix.
