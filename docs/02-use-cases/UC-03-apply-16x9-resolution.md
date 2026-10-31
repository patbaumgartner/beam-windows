# UC-03 — Apply a 16:9 mode on connect (extend mode)

**Phase:** 1 (MVP) · **Priority:** P0 · **Spikes:** S3, S6

## Goal
In extend mode, put the external display into a correct 16:9 mode chosen from modes it
actually supports, without touching the laptop panel.

## Actors
- Primary: user.
- Secondary: display driver, Windows display stack.

## Trigger
`PresentationActivated` (UC-02) with topology = extend.

## Preconditions
- External display is an active path.
- Topology is extend (duplicate is UC-04).
- Feature enabled; not paused; not in safe mode.

## Policy (default: `MatchHeight`)
| Policy | Selection rule |
|---|---|
| `MatchHeight` (default) | 16:9 mode whose height equals the internal panel's current height; if none, **fall back to 1920x1080**; if absent, the highest 16:9 mode <= that height |
| `Native16x9` | the display's preferred/native timing if exactly 16:9 |
| `Fixed` | user-pinned mode (default 1920x1080) |
| `Highest16x9` | highest 16:9 mode the display supports |

Refresh: prefer 60 Hz; else highest <= 60; else native. Never interlaced. Never < 50 Hz
unless it is the only 16:9 mode.

## Main flow
1. Read current mode of the external display (`ENUM_CURRENT_SETTINGS`).
2. Enumerate all modes for its GDI device.
3. Filter to exact or near 16:9 (`w*9 == h*16`, tolerance 0.5%), progressive.
4. Order by policy, then refresh preference, then area.
5. If current == best -> no-op (CS-2).
6. Snapshot + journal (UC-11) **before** mutating.
7. `DEVMODE` with `DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY`; `CDS_TEST`.
8. On test failure -> next candidate (max 3).
9. Apply without `CDS_UPDATEREGISTRY`; verify by re-read.
10. On verify failure -> revert to snapshot; mark failed.
11. Hand off to UC-05 (scale) and UC-06 (Night Light).

## Alternative flows
- **A1 No 16:9 mode:** do nothing; toast "No 16:9 mode on <name>"; offer "use native".
- **A2 External is primary:** apply to the external only; laptop unchanged.
- **A3 User changes mode during apply:** abort; do not fight.
- **A4 Mode change re-enumerates:** suppress self-induced events for 3 s.
- **A5 HDR:** mode change allowed; Night Light step may be skipped (S7).

## Postconditions
- External is at a 16:9 mode from its own list, or unchanged.
- Laptop panel's mode is untouched.
- Journal has the inverse.

## Acceptance criteria
- AC1: never applies a mode absent from `EnumDisplaySettingsEx`.
- AC2: `CDS_TEST` before every apply.
- AC3: 100/100 applies land on an enumerated mode.
- AC4: p95 <= 2 s after the decision.
- AC5: no-op when already correct.
- AC6: failure leaves the display exactly as before.
- AC7: 1080p projector gets 1920x1080 under MatchHeight, Native, Highest and Fixed.

## Edge cases
- 4096x2160 (DCI 4K) excluded by the ratio filter.
- 1366x768 allowed by tolerance, never preferred.
- 50 Hz PAL-only projector accepted.
- Anamorphic/ultrawide out of scope; user can pin a mode.

## Tests
- Unit: policy x mode-list matrix, ratio filter.
- Simulation: failing `CDS_TEST`, silent-success drivers.
- Hardware: S3 + S6.
