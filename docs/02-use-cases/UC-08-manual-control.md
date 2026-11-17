# UC-08 — Manual control: tray, hotkey, pause

**Phase:** 1 (MVP) · **Priority:** P0

## Goal
Give the user a fast, obvious way to force, pause, or undo projector mode without
waiting for a plug event or opening a settings app.

## Actors
- Primary: user.
- Secondary: none.

## Trigger
User action.

## Surfaces
1. **Tray icon** (left click: status flyout; right click: menu).
2. **Global hotkey** (default: `Win+Alt+P`, user-configurable; `Win+P` in any spelling is
   rejected — it is Windows' own projector flyout and Beam never registers it).
3. **Toast actions** (Undo, Settings).
4. **CLI** (UC-16).

## Tray menu
- Status line: `Display: Epson EB-2250U · 1920x1080 · 100% · Night Light off`
- `Apply projector mode now` (enabled while idle; with **no external monitor connected** it
  simulates presentation mode instead of failing — Night Light off, DPI applied, extras on,
  session stays Active until Undo)
- `Restore laptop settings` / *Undo projector mode*
- `Pause for 1 hour` / `Resume`
- `Ignore this display` (per current device; excludes it from the trigger)
- `Settings...`
- `Diagnostics...`
- `Exit`

## Main flow (manual apply)
1. User chooses "Apply projector mode now".
2. Resolve target: the active classified projector; if several, ask which (or all).
3. Run UC-03 + UC-05 + UC-06 as one transaction.
4. Toast the result (UC-09).
5. Mark the session as `userInitiated` so automatic restore still works.

## Main flow (pause)
1. User chooses "Pause for 1 hour".
2. Persist `pausedUntil = now + 1h`.
3. If currently applied, restore (UC-07) or keep? **Decision: keep applied, but do not
   auto-restore or auto-apply while paused**; show tray state "Paused (keeps current
   settings)". A second click "Restore and pause" is available.
4. On expiry, re-evaluate current topology once.

## Main flow (hotkey)
1. Toggle: if applied -> restore; else -> apply.
2. No UI unless the action fails.

## Alternative flows
- **A1 No projector active:** manual apply (tray/hotkey) **simulates presentation mode** —
  the display-mode step is skipped (there is nothing to switch), Night Light / DPI / extras
  still apply, the session stays Active, and Undo restores everything. Automatic triggers
  (display-change, startup check) are unchanged and still require a real external display.
  The CLI keeps its script-friendly contract: `beam apply` with no projector exits `3`
  (UC-16) — simulation is a tray/hotkey convenience only.
- **A2 Apply fails:** toast with reason and a "Diagnostics" link.
- **A3 Hotkey conflict:** registration fails -> show a warning in settings, keep running.
- **A4 Pause while a projector is connected:** no auto-apply on reconnect until expiry.
- **A5 Multiple projectors:** menu lists each; "Apply to all" is available.

## Postconditions
- State is explicit and visible (tray icon variant: idle / active / paused / error).
- User action is recorded in the journal and diagnostics timeline.

## Acceptance criteria
- AC1: every automatic action can be reproduced manually.
- AC2: every manual action is undoable.
- AC3: tray state always matches reality within 1 s.
- AC4: pause survives restart.
- AC5: hotkey is optional and never blocks startup if unavailable.
- AC6: no menu item does something destructive without a confirm (restore is safe).

## Edge cases
- Explorer restart: tray icon must re-register (`TaskbarCreated` message).
- High-DPI tray: icon assets at 16/20/24/32 px.
- Keyboard-only users: full menu keyboard navigation.
- Screen reader: status text exposed via UI Automation.

## Data recorded
Action, source (tray/hotkey/CLI), target device, result.

## Tests
- UI automation: menu actions, state transitions.
- Unit: pause expiry logic, target resolution.
- Manual: Explorer restart, hotkey conflict, multiple projectors.
