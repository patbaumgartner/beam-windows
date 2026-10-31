# ADR-0002 — No Windows service; per-user tray app

**Status:** Accepted
**Date:** 2026-10-29

## Context

A "Windows app/service" was requested. A service is the intuitive answer for
"always running, starts at boot". However:

- Display configuration APIs (`QueryDisplayConfig`, `ChangeDisplaySettingsEx`) operate on
  the calling session's desktop. A service in session 0 cannot change the user's display.
- Night Light state lives in `HKCU`, per user, in the user's CloudStore.
- A service would need an IPC bridge into the session anyway, plus elevation, plus a
  second process to maintain.
- PowerToys itself uses per-user processes for this class of feature.

## Decision

Ship a **per-user tray application** with:
- a single-instance mutex,
- a per-user startup task (or MSIX startup task),
- a named-pipe server for CLI/IPC restricted to the current user,
- no elevation, no service, no driver.

## Consequences

**Positive**
- Direct access to the correct session and user hives.
- No admin prompt, no service installer, no security surface.
- Simple lifecycle; crash recovery via journal.

**Negative**
- Does not run before login (acceptable: projectors are used after login).
- Does not cover "apply before any user logs in" (not a real scenario).
- Per-user install rather than machine-wide (acceptable; PowerToys is per-user too).

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Windows service + session helper | Two processes, IPC, elevation, no benefit |
| Scheduled task running as SYSTEM | Same session-0 problem |
| Driver / filter driver | Massive cost, signing, risk |
| MSIX background task | Cannot hold the event loop/state we need; still session-bound |

## Notes

If a future requirement appears (e.g. apply a projector profile at the lock screen),
revisit with a session-aware helper, not a service.
