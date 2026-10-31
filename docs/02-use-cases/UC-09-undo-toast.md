# UC-09 — Undo toast and one-click revert

**Phase:** 1 (MVP) · **Priority:** P0

## Goal
Make every automatic change visible and reversible in one click, so the user never has
to trust a black box — and so a wrong classification costs one click, not a meeting.

## Actors
- Primary: user.
- Secondary: Windows toast system.

## Trigger
Successful automatic apply (UC-03 + UC-05 + UC-06), or a partial apply.

## Main flow
1. Build a compact summary:
   - Title: `Projector mode on`
   - Body: `<name> · 1920x1080 · Night Light off` (only items actually changed)
   - Actions: `Undo`, `Settings`
2. Show a toast (respect Do Not Disturb; if suppressed, update tray state silently and
   keep the Undo available in the tray menu).
3. If the user clicks **Undo**:
   - Run UC-07 restore.
   - Mark the device as `doNotAutoApply` for this session (until next physical connect).
   - Toast `Laptop settings restored`.
4. If the user clicks **Settings**: open the settings page, section "Projector mode".
5. Toast auto-dismisses after 10 s; the action remains available in the tray.

## Alternative flows
- **A1 Partial apply** (mode ok, Night Light failed): title `Projector mode on (1 issue)`,
  body lists the failure, actions `Details`, `Undo`.
- **A2 Restore itself fails:** toast `Could not restore all settings`, action `Retry`.
- **A3 No toast permission / notifications disabled:** tray icon shows a badge for 30 s;
  menu has `Undo projector mode`.
- **A4 User clicks Undo after unplugging:** no-op (already restored), toast
  `Nothing to undo`.
- **A5 Repeated connects:** do not spam toasts; if applied within the last 5 minutes,
  update the existing toast instead of stacking.

## Postconditions
- The user has seen what changed (or the tray reflects it).
- Undo is always reachable while a session is active.

## Acceptance criteria
- AC1: 100% of automatic applies produce either a toast or a tray state change.
- AC2: Undo restores within 2 s and is idempotent.
- AC3: Undo never appears when nothing was changed.
- AC4: toast text is localized and contains no technical jargon or error codes.
- AC5: toast is screen-reader accessible.
- AC6: no toast during full-screen exclusive apps (games) — queue it instead.

## Edge cases
- Toast suppressed by Focus Assist / DND.
- Multiple projectors -> one toast per device, grouped.
- Locale right-to-left layouts.
- Windows 11 notification settings per app disabled.
- Undo clicked twice quickly -> single restore.

## Data recorded
Toast shown/clicked, undo result. No content.

## Tests
- Unit: summary text builder, change detection.
- UI: toast action callbacks (mock notifier), DND suppression path.
- Manual: accessibility insights pass.
