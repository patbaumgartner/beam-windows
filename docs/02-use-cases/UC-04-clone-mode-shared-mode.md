# UC-04 — Duplicate (mirror) mode: remove the black bars

**Phase:** 1 (MVP) · **Priority:** P0 · **Spikes:** S3, S4, S6

## Goal
This is the **primary scenario**. In duplicate mode Windows drives both displays from
one source. A 16:10 laptop panel therefore sends a 16:10 image to a 16:9 projector, and
the projector shows black bars on the left and right. Beam must switch the shared source
to a 16:9 mode and to a scale that keeps the desktop readable, then restore both.

## Why the laptop must change
In clone mode there is exactly one source mode. The projector cannot show 3840x2400;
Windows picks a shared mode. If the laptop stays at 16:10, the projector pillarboxes.
There is no way to keep the laptop at 16:10 and give the projector a 16:9 image in
duplicate. Therefore **the laptop's source must change**, and with it the DPI (DPI is a
property of the source). This is expected and is what the user asked for.

Visual result after Beam:
- projector: full-frame 16:9, **no black bars**;
- laptop panel: 1920x1080 scaled to the panel, small bars top/bottom (unavoidable on a
  16:10 panel), same image as the projector.

## Actors
- Primary: user pressing `Win+P` -> Duplicate, or plugging in while duplicate is the
  default.
- Secondary: display stack.

## Trigger
`PresentationActivated` with topology = clone/duplicate.

## Preconditions
- >= 2 active targets share one source (same `sourceId` / same GDI device name).
- At least one is the external display.

## Main flow
1. Identify the clone group: targets sharing the same source id and GDI device name.
2. Enumerate modes for the shared GDI device; enumerate target modes for each member.
3. Compute the **intersection** of 16:9 modes supported by all members.
4. Choose with the UC-03 policy:
   - `MatchHeight` -> 16:9 at the internal height if common; else **1920x1080**; else
     highest common 16:9 <= internal height.
   - `Native16x9` -> projector native if common.
   - `Fixed` -> pinned mode if common.
   - `Highest16x9` -> highest common.
5. If the intersection is empty -> do nothing, toast "No common 16:9 mode", record reason.
6. Snapshot: shared DEVMODE, source DPI (`-3`), Night Light bytes; journal.
7. Apply the mode to the shared GDI device (`CDS_TEST` -> apply -> verify).
8. Settle 300 ms, then set the **source** DPI so that logical width is in
   `[1280, 2560]` (for 1920x1080 -> **100%**), via `-4`; verify via `-3` (UC-05).
9. Turn Night Light off (UC-06).
10. Verify both targets report the chosen mode; if not, revert mode + DPI.
11. Toast: `Duplicate · 1920x1080 · 100% · Night Light off`.

## Alternative flows
- **A1 Projector-only (`Win+P` -> Second screen only):** no clone; UC-03 on the
  projector; laptop panel is inactive, nothing to restore there.
- **A2 Extend:** UC-03; DPI only on the external source.
- **A3 User switches Extend <-> Duplicate while projecting:** re-plan; keep one journal;
  re-apply only the delta.
- **A4 Clone includes a 4:3 projector:** intersection may be 4:3; do not force 16:9;
  leave Windows' choice and report.
- **A5 No common 16:9 (e.g. 16:10 laptop + 1280x720 projector is fine, but a 4:3 panel
  is not):** no-op + clear message.
- **A6 DPI API unavailable:** set the mode anyway, detect the 960x540 trap, advise 100%
  (UC-05 L3), and keep the toast explicit.
- **A7 Driver resets DPI after the mode change:** apply DPI after the mode, re-verify,
  retry once.

## Postconditions
- Shared source is 16:9, both displays show the same image with no bars on the projector.
- Logical desktop width is sane.
- Journal contains the shared mode + source DPI + Night Light inverse.

## Acceptance criteria
- AC1: 3840x2400 @ 200% + 1920x1080 duplicate -> 1920x1080 @ 100%, no projector bars.
- AC2: never chooses a mode absent from the intersection.
- AC3: unplug restores 3840x2400 @ 200% exactly.
- AC4: 10 duplicate/extend toggles produce one coherent session.
- AC5: empty intersection -> no mutation.
- AC6: both targets verified after apply.
- AC7: the laptop panel is only ever set to a mode it advertises.

## Edge cases
- Three-way clone (laptop + projector + capture card): intersection of all three.
- Projector native 1280x720: 720p common, 1080p not; choose 720p, scale 100% -> 1280
  logical width (below 1280? exactly 1280, allowed).
- 4K projector: 3840x2160 common? laptop panel 3840x2400 may not support 3840x2160;
  intersection likely 1920x1080.
- Clone + HDR: DPI independent; Night Light may be skipped.
- Windows may itself pick 1920x1080 on plug; Beam detects "already correct" and only
  fixes DPI + Night Light.

## Data recorded
Clone group, intersection size, chosen mode, DPI before/after, verification, bars
before/after (diagnostic screenshot optional, user-initiated only).

## Tests
- Unit: intersection, policy, DPI target calculation.
- Simulation: topology transitions, empty intersection, DPI failure.
- Hardware: S3 (duplicate first), S4, S6.
