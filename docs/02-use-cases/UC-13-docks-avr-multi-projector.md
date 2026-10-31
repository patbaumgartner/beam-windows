# UC-13 — Docks, KVMs, AVRs, multiple projectors

**Phase:** 2 · **Priority:** P1 · **Depends on:** UC-01, UC-02

## Goal
Work in the messy real world: USB-C docks, KVM switches, AV receivers, conference-room
switchers, and more than one projector.

## Scenarios and rules

| Scenario | Behaviour |
|---|---|
| **USB-C dock** | Display appears seconds after dock attach; debounce handles it. If the dock re-enumerates twice, coalesce. Never apply while the display set is still growing (wait for 1.5 s of quiet). |
| **KVM switch** | Display disappears then reappears with the same device id. Treat as deactivate + activate only if the gap exceeds 3 s; otherwise ignore (avoid restore/apply churn). |
| **AVR passthrough** | The active target may be the receiver. Classify as "unknown/ask" unless the user said projector. Remember per port. Never assume the AVR's native mode is the projector's. |
| **Conference switcher** | Same as AVR; often presents a fixed 1080p EDID. Treat as a display; ask once. |
| **Two projectors** | Classify independently; apply per device; one journal entry each; toast grouped. |
| **Projector + TV** | Only projectors trigger by default; TV can be opted in per device ("treat like projector"). |
| **Wireless display (Miracast)** | Excluded by default (`includeWireless = false`). If enabled, apply only mode/Night Light, never scaling. |
| **Capture card / virtual display** | Excluded by default; can be opted in. |
| **Dock with both HDMI and DP to the same projector** | Two device ids for one physical panel; apply to the active one only; profile per id. |

## Main flow
1. Build the device set after debounce.
2. Wait for stability: no new device for 1.5 s (max 6 s total).
3. Classify each new device (UC-02).
4. For each classified projector, plan and apply (serialized).
5. If the device set changes during apply -> abort, wait for stability, re-evaluate.

## Alternative flows
- **A1 AVR only, no projector EDID:** do not apply; show "Unknown display — is this a
  projector?" once.
- **A2 Projector behind a switcher that reports a different EDID each time:** classify by
  friendly name; if unstable, ask and pin by port.
- **A3 Dock firmware re-enumerates every 30 s:** detect flapping (>= 3 changes in 2 min)
  and stop acting for 10 min; log it.
- **A4 Two projectors with different native modes:** each gets its own plan; no shared
  mode assumptions (unless clone).

## Postconditions
- One plan per physical projector.
- No action during unstable enumeration.
- Flapping is detected and damped.

## Acceptance criteria
- AC1: dock attach applies once (not 2–3 times).
- AC2: KVM bounce under 3 s causes no restore/apply churn.
- AC3: AVR is never silently treated as a projector.
- AC4: flapping enters a 10-minute cooldown and is visible in diagnostics.
- AC5: two projectors both get correct, independent modes.

## Edge cases
- Display arrives with EDID not yet readable -> retry EDID read up to 3 s.
- Dock removal takes the projector and the only network adapter -> no network needed.
- Projector connected to a Thunderbolt dock's downstream port.
- Hot-plug during a Windows update reboot -> out of scope.

## Data recorded
Device set timeline, stability waits, flapping detection, per-device plans.

## Tests
- Simulation: event sequences for dock, KVM, flapping, AVR.
- Hardware: USB-C dock, HDMI KVM, AVR, two projectors.
