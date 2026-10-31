# UC-02 — Recognize a presentation display (external + aspect mismatch)

**Phase:** 1 (MVP) · **Priority:** P0 · **Spike:** S3

## Goal
Decide that a newly active display should trigger presentation mode — without needing to
know whether it is technically a projector. The trigger is the **problem**, not the
device class: an external display whose aspect ratio differs from the laptop panel, so
mirroring would produce black bars.

## Actors
- Primary: user plugging HDMI/DP/USB-C, or pressing `Win+P`.
- Secondary: Windows display stack.

## Trigger
`DisplaysActivated` from UC-01.

## Preconditions
- The new display is an active path.
- Beam is enabled and not paused.

## Decision rule (MVP)

```
if display is internal            -> ignore (never a presentation target)
if display is virtual/RDP/Miracast (default) -> ignore
if topology == Clone and a 16:9 mode is common to all members -> PRESENTATION
if display.aspect != internal.aspect (tolerance 2%)            -> PRESENTATION
else                                                            -> ignore
```

Examples:
- 16:10 laptop + 16:9 projector/TV -> **presentation** (the black-bar case).
- 16:9 laptop + 16:9 projector -> same aspect: no mode change needed; Night Light only
  (if enabled) — or nothing.
- 16:10 laptop + 16:10 monitor -> ignore.
- 4:3 projector + 16:9 laptop -> presentation, but 16:9 may not be common; UC-04 decides.
- RDP session -> ignore.

## Main flow
1. Classify the display as internal / external / virtual.
2. Compute the aspect of the new display's current and native mode, and of the internal
   panel.
3. Apply the decision rule.
4. If presentation -> `PresentationActivated(device, reason)` with the reason recorded
   (`aspect-mismatch`, `clone-common-16x9`).
5. Persist nothing yet; the profile is created after the first successful apply (UC-10).

## Optional refinement (Phase 2, off by default)
If `onlyProjectors = true`, run the scored EDID classifier (ADR-0005) and require a
projector verdict. This is for users who do not want Beam to act on hotel TVs. It is
**not** required for the main scenario and must never block the MVP path.

## Alternative flows
- **A1 User does not want automation for this device:** "Never for this display" stored
  per device id; no further action.
- **A2 TV vs projector distinction needed:** only for naming/profiles; not for the core
  behaviour.
- **A3 AVR in the chain:** the active target may be the receiver; the aspect rule still
  applies (AVRs usually present 16:9). Apply to the receiver's source; the projector
  follows. Record the device id of the actual target.
- **A4 Wireless display:** excluded by default; opt-in per device.
- **A5 Same aspect but user still wants Night Light off:** separate toggle
  (`nightLightOnly`), does not require a mode change.

## Postconditions
- A clear, explainable decision per new display.
- No user question in the default path.
- No display mutation yet.

## Acceptance criteria
- AC1: 16:10 laptop + 16:9 projector triggers 100% of the time.
- AC2: 16:10 laptop + 16:10 monitor triggers 0% of the time.
- AC3: RDP/virtual displays never trigger.
- AC4: the reason is visible in diagnostics.
- AC5: "Never for this display" is honoured across reboots and port changes.
- AC6: no classification question is asked in the default configuration.

## Edge cases
- Projector reports 16:9 native but is currently at 4:3 (user setting) -> use native
  aspect for the decision, current mode for the plan.
- Laptop panel is 3:2 (Surface) -> same logic.
- Ultra-wide external (21:9) -> presentation; 16:9 target may letterbox the external,
  which is acceptable and explainable; policy can pin a different mode.
- Two externals, one 16:9 and one 16:10 -> only the 16:9 one is a presentation target.

## Data recorded
Device id, aspects, topology, decision, reason. No content.

## Tests
- Unit: decision table across aspects/topologies/device kinds.
- Simulation: plug sequences.
- Hardware: S3 matrix.
