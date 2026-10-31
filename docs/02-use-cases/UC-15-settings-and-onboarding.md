# UC-15 — Settings and first-run onboarding

**Phase:** 2 · **Priority:** P1

## Goal
Get out of the way. Defaults must be right for 90% of users; the remaining 10% need a
clear, small settings surface.

## Actors
- Primary: user.
- Secondary: none.

## First run
1. Tray icon appears; no window, no wizard.
2. A single toast: "Beam is running. Plug in a projector — I'll handle the rest."
   (dismissable, shown once).
3. On the first projector connect, if classification is ambiguous, ask one question
   (UC-02). Otherwise no UI at all.
4. Settings window is reachable from the tray.

## Settings sections

**General**
- Enable Beam (on/off)
- Start with Windows (on/off; per-user startup task)
- Pause (until time)
- Language (system default)

**Projector mode**
- Apply mode on connect (on) / policy: MatchHeight · Native16x9 · Fixed · Highest16x9
- Fixed mode picker (only modes common to detected projectors, or a manual w/h)
- Refresh preference: Auto (60) · Highest · Native
- Manage scaling (on) / target logical width (1280–1920)
- Turn Night Light off (on) / scope: device if available · global
- Also treat TVs as projectors (off)
- Include wireless displays (off)
- Restore on disconnect (on, always recommended; can be disabled with a warning)

**Devices**
- Table: name, id, classification, "auto-apply", "forget"
- "Ask again" / "Treat as projector" / "Never"

**Advanced**
- Diagnostics…
- Export/import settings
- Reset all settings
- Open log folder
- "Safe mode" indicator + reset

## Main flow
1. Open from tray; window is a single WinUI 3 page with a navigation view.
2. Changes apply immediately (no OK/Apply), persisted debounced.
3. "Reset to defaults" requires confirmation.

## Alternative flows
- **A1 Settings file corrupt:** load defaults, keep the corrupt file as `.bad`, toast once.
- **A2 Settings from a newer version:** read known keys, ignore unknown, never rewrite
  the file until the user changes something.
- **A3 User disables restore:** show a warning that the laptop may stay at projector
  settings.
- **A4 Policy managed by enterprise:** show a lock icon and the effective value
  (future; no GPO in MVP).

## Postconditions
- Settings are valid, versioned JSON.
- No setting requires a restart except "start with Windows" (handled by the task).

## Acceptance criteria
- AC1: a fresh install needs zero settings changes to work.
- AC2: every setting has a default that matches the vision.
- AC3: settings survive upgrade; unknown keys are preserved.
- AC4: window opens in < 500 ms; no admin prompt.
- AC5: all strings localized; keyboard navigable; high-contrast safe.

## Edge cases
- Multiple monitors with different DPIs (WinUI 3 handles).
- Settings changed while a session is active: re-plan only on next connect, except
  "disable" which restores immediately.
- Roaming: never sync (per-machine display state).

## Data recorded
Settings changes (local only), first-run completion.

## Tests
- Unit: serialization/migration/defaults.
- UI: keyboard, high contrast, screen reader.
- Manual: fresh install, upgrade, corrupt file.
