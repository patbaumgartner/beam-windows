# Vision

## 1. Problem

Windows treats a projector as "just another monitor". That assumption breaks in
predictable ways every single time someone presents:

1. **Black bars when mirroring.** A modern laptop panel is 16:10 (here: 3840x2400) or
   3:2. A TV or projector is 16:9 (usually 1920x1080 or 1280x720). In duplicate mode
   Windows drives both displays from one source, so the 16:10 desktop is pillarboxed on
   the 16:9 screen: **black bars on the left and right**. This is the core problem.
2. **Wrong scale.** Going from 3840x2400 @ 200% to 1920x1080 keeps the 200% scale,
   producing a 960x540 logical desktop. Text becomes unusable on the projected image.
   (DPI belongs to the source; Beam sets it with the same API Windows Settings uses.)
3. **Wrong colour.** Night Light is on (it is evening, that is when presentations
   happen). The projected image is orange. Presenters either live with it or dive
   into Settings mid-meeting.
4. **Wrong cleanup.** Whatever was changed is rarely changed back. The laptop is left
   at 1920x1080 @ 100% the next morning.
5. **No confidence.** Users do not know whether the projector is mirroring, extending,
   or which display is primary — and the projector is across the room.

The manual workaround is: `Win+P`, Settings > Display > resolution, Settings > Display >
scale, Settings > Display > Night Light, present, then reverse all of it. Five minutes
of fumbling in front of an audience, twice per meeting.

## 2. Personas

| Persona | Context | What they need |
|---|---|---|
| **The consultant** | 3–5 client meetings a day, different rooms, HDMI or USB-C | Zero-touch. Plug in, present, unplug, laptop unchanged |
| **The teacher / trainer** | Fixed room, fixed projector, often 4:3 or 16:9 | Predictable, repeatable, no surprises |
| **The engineer** | Shares a 4K screen in reviews, sometimes a projector | Correct aspect and readable text, not just "some resolution" |
| **The home-cinema user** | HDMI to AVR/projector, evening | Night Light off on the projector, warm on the laptop |
| **The TV user** | Mirrors to a hotel or living-room TV | Same black-bar fix; does not care whether the device is a "projector" |
| **The IT admin** | Deploys to a fleet | No admin rights, no cloud, auditable config, silent install |

## 3. Jobs to be done

- "When I mirror to a TV or projector, make the picture fill the screen without black
  bars, without me touching Settings."
- "When the meeting ends, put my laptop back exactly as it was."
- "If it guesses wrong, let me undo it in one click and never guess that way again."
- "Never give me a black screen."

## 4. Goals

| # | Goal | Measure |
|---|---|---|
| G1 | Zero-touch correct projection | p95 from display-settled to applied <= 3 s |
| G2 | Exact restoration | 100% of touched settings restored; verified by test harness |
| G3 | Never break a display | 0 black-screen incidents in 500 automated apply/restore cycles |
| G4 | Invisible cost | idle CPU < 0.1%, working set < 100 MB, no polling faster than 30 s |
| G5 | Trustworthy | every automatic action has a visible, undoable trace |
| G6 | Upstreamable | engine has zero UI dependencies; MIT licensed; PowerToys coding conventions |
| G7 | Private | offline by default; telemetry opt-in and content-free |

## 5. Non-goals

- Replacing DisplayFusion/DisplayMagician for arbitrary multi-monitor layouts.
- Colour calibration, ICC profile management, HDR tone mapping.
- Audio device switching (candidate for a later phase, not now).
- Remote control of the projector (DDC power/input is PowerDisplay's job).
- Wireless casting (Miracast) as a first-class trigger — supported only as an opt-in.
- Being a Windows service, a driver, or an elevated helper.
- A general-purpose scripting engine.

## 6. Product principles

1. **Do the obvious thing, then get out of the way.** No wizard on first plug-in.
2. **Reversible by construction.** Every mutation has a recorded inverse.
3. **Explain itself.** A "why did/didn't it trigger?" panel, not a log file.
4. **Fail visible, fail safe.** If we cannot do it right, do nothing and say so.
5. **Small surface, sharp edges documented.** One Night Light write path, one mode path.
6. **Learn, don't nag.** Ask once per device, remember forever, allow override.

## 7. Success metrics

**Leading (engineering):**
- detection-to-applied latency p50/p95
- apply success rate by display model
- restore success rate
- Night Light write verification rate
- false-positive projector classifications (user-corrected)

**Lagging (product):**
- % of sessions with zero manual display changes afterwards
- undo rate (should be low; a high rate means bad defaults)
- "disabled after first week" rate (should be near zero)

## 8. Elevator pitch

> Beam turns any TV or projector into a one-plug experience: a full-frame 16:9 picture
> with no black bars, readable scaling, Night Light off — and your laptop exactly as it
> was when you unplug. No settings, no admin, no orange slides.
