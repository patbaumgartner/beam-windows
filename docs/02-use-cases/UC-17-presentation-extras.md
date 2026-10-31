# UC-17 — Presentation extras (awake, notifications, audio)

**Phase:** 3 · **Priority:** P2 · **Depends on:** Awake (PowerToys) or equivalent

## Goal
After the picture is right, remove the other three ways a presentation fails: the screen
sleeps, a notification pops up on the wall, and the sound comes out of the laptop.

## Actors
- Primary: user.
- Secondary: power management, notification system, audio endpoint APIs.

## Optional behaviours (all off by default except "keep awake")

| Extra | Default | Implementation notes |
|---|---|---|
| **Keep display awake while projecting** | on | `SetThreadExecutionState(ES_DISPLAY_REQUIRED | ES_CONTINUOUS)` while a session is active; release on restore. Or reuse PowerToys Awake's engine when upstreamed. |
| **Prevent sleep** | off | `ES_SYSTEM_REQUIRED`; separate toggle because some users want the machine to sleep. |
| **Suppress notifications** | off | Do Not Disturb / Focus Assist via the documented user-notification state; do not use undocumented registry writes if avoidable. Must be clearly reversible and time-boxed. |
| **Switch default audio to HDMI/DP** | off | `IMMDeviceEnumerator` + `IPolicyConfig` (undocumented) or the documented `AudioPlaybackConnection`? Prefer `IPolicyConfig` behind an interface with fallback; restore previous endpoint. High risk, hence opt-in. |
| **Mute laptop speakers** | off | Same audio stack. |
| **Hide desktop icons / taskbar** | off | Rejected for MVP: intrusive, hard to undo if we crash. |

## Main flow
1. After a successful apply, start the enabled extras.
2. Record each extra's prior state in the journal.
3. On restore, stop extras and restore prior state (default audio endpoint, DND).
4. If an extra fails, do not fail the whole session; report it in the toast as a minor
   issue.

## Alternative flows
- **A1 Another app already holds ES_DISPLAY_REQUIRED:** fine; release only our own.
- **A2 User changes the default audio device during the session:** do not restore over it.
- **A3 DND was already on:** leave it on after restore.
- **A4 Crash:** power requests are released by the OS on process exit; audio/DND restore
  is recovered by UC-11 journal.

## Postconditions
- No extra outlives the session.
- Every extra is independently toggleable and independently undoable.

## Acceptance criteria
- AC1: display never sleeps while projecting with the default settings.
- AC2: releasing extras returns the exact prior state (audio endpoint, DND).
- AC3: an extra failure never blocks mode/Night Light.
- AC4: no extra runs when the feature is paused or in safe mode.
- AC5: CPU impact of extras is unmeasurable (< 0.01%).

## Edge cases
- Laptop on battery: "keep awake" may conflict with power policy; respect the user's
  explicit choice and document battery impact.
- HDMI audio device disappears with the projector -> restore to the previous endpoint
  only if it still exists; else pick the system default.
- Multiple audio endpoints with the same name.

## Data recorded
Which extras ran, results, prior endpoints (local only).

## Tests
- Unit: extras lifecycle, journal entries.
- Integration: audio endpoint switch/restore on real hardware.
- Manual: DND, sleep behaviour on battery and AC.
