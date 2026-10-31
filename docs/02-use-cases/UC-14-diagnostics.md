# UC-14 — Diagnostics: "why did/didn't it trigger?"

**Phase:** 2 · **Priority:** P1 · **Depends on:** all

## Goal
Replace "it doesn't work" with an answer the user can read and the maintainer can act on.

## Actors
- Primary: user, support engineer.
- Secondary: logs, journal, config.

## Trigger
Tray > Diagnostics, or a failed action's "Details" link.

## Content
1. **Now**
   - Active displays: name, device id (short), output technology, current mode, scale,
     clone group, classification + score + reasons.
   - Feature state: enabled, paused, safe mode, Night Light capability, DPI capability.
   - Journal: active session, items, phase.
2. **Timeline** (last 200 events, in-memory + rotated log)
   - `12:04:03.112 display-added EPSON EB-2250U`
   - `12:04:04.640 classified projector score=85 (name,size,16:9)`
   - `12:04:04.701 plan mode 1920x1080@60 policy=MatchHeight`
   - `12:04:04.712 apply ok 41ms`
   - `12:04:04.980 night-light off verified`
   - `12:04:05.010 toast shown`
3. **Why not?** decision trace with explicit reasons:
   - "not a projector (score 12)"
   - "paused until 13:20"
   - "already at target mode"
   - "no 16:9 mode advertised"
   - "Night Light already off"
   - "HDR active: Night Light skipped"
   - "safe mode after 2 failures"
4. **Actions**
   - Copy summary to clipboard.
   - Export bundle (zip): settings (secrets none), profiles, journal, last 3 log files,
     EDID hex of active displays, OS/PowerToys version. **User-initiated only.**
   - Open log folder.
   - "Re-evaluate now".
   - "Reset safe mode".

## Main flow
1. Open window, populate from live state + ring buffer.
2. Timeline updates live.
3. Export writes a zip to Desktop with a timestamped name.

## Alternative flows
- **A1 No events yet:** show "No projector activity yet" plus what Beam is watching.
- **A2 Log file locked:** copy to temp before zipping.
- **A3 Very long session:** ring buffer bounded at 200 events; full log rotated at 5 MB x 3.

## Postconditions
- The user can explain the behaviour without reading source code.
- The export contains enough to reproduce (no screen content, no window titles).

## Acceptance criteria
- AC1: every automatic decision has a human-readable reason.
- AC2: timeline timestamps are monotonic and include durations.
- AC3: export works offline and contains no PII beyond device names/EDID.
- AC4: diagnostics never mutates display state (read-only except explicit actions).
- AC5: window is keyboard navigable and screen-reader friendly.

## Edge cases
- Localized device names with non-ASCII characters.
- A device that vanished between snapshot and render.
- Export while a journal is being written (snapshot copy).
- Logs from a previous version (schema mismatch) -> show, don't crash.

## Data recorded
Nothing new; this is a read surface over existing data.

## Tests
- Snapshot tests for timeline formatting.
- Manual: reproduce each "why not" reason.
- Privacy review of the export bundle.
