# Decision log — product owner answers (2026-10-29)

These are the answers given in review. They override anything in the earlier draft docs.
Each entry: the question, the answer, and what changed as a result.

## D1 — Name

**Asked:** Beam, "Projector Mode", or something else?
**Answer:** "lets go with the name beam".
**Effect:** Product name **Beam**; feature label "Projector mode" (ADR-0011, `00-vision/naming.md`).

## D2 — Upstream

**Asked:** standalone first + upstream in parallel, straight to a PR, or standalone only?
**Answer:** "i like the idea of an upstream contribution for powertoys" and
"standalone first and upstream in parallel".
**Effect:** ADR-0001 accepted. Standalone app is the reference implementation; upstream
target is **PowerDisplay**, not a new module (see `upstream-plan.md`).

## D3 — Location

**Asked:** where should the docs/repo live?
**Answer:** "Create Desktop/Beam".
**Effect:** `C:\Users\patbaumgartner\OneDrive\Desktop\Beam` is the canonical location for
the documentation set and the repository.

> Plan mode only permits writes under `C:\Users\patbaumgartner\.opencode\plan`, so the
> docs currently live in `.opencode\plan\beam`. They are mirrored to `Desktop\Beam` as
> soon as the agent is switched to build mode.

## D4 — Night Light semantics

**Asked:** what exactly should "Night Light off" mean?
**Answer:** "Night light is a windows settings toggle".
**Effect:** Beam must flip the **real Windows feature** (the state shown in
Settings > Display > Night Light), not apply a custom colour filter. CloudStore write is
the primary mechanism; gamma ramp is only a visual fallback (ADR-0006, UC-06).

## D5 — DPI

**Asked:** how should scaling be handled?
**Answer:** "we need to find out how to deal with that. I need the best possible solution".
**Effect:** Research found the mechanism Windows Settings itself uses:
`DisplayConfigGetDeviceInfo(-3)` / `DisplayConfigSetDeviceInfo(-4)` on the **source**,
relative steps over the ladder `100…500`. It is immediate, verifiable and reversible.
ADR-0013 rewritten; UC-05 promoted to MVP; S4 now validates rather than decides.

## D6 — What counts as a projector

**Asked:** should the trigger be "projector" or something else?
**Answer:** "Projector is like an external screen we check for available resolutions.
the main issue is the screen of this notebook has 16:10 on a TV or on a projector you
would have the black bars on the side. this is what i do not want while mirroring.
therefore we need to adjust the ratio on the main screen."
**Effect:** Trigger = **new active external display with an aspect mismatch** (covers TV
and projector). Projector classification becomes an optional Phase-2 refinement
(ADR-0005 rewritten, UC-02 rewritten). The mode policy must consider the internal
panel's aspect, not just the external display's.

## D7 — Duplicate mode

**Asked:** fix duplicate mode, or only Night Light there?
**Answer:** "Duplicate mode - fix it".
**Effect:** Duplicate is the **primary** scenario and is in the MVP. The shared source
must change mode (16:9) and scale; both are restored on unplug (UC-04 rewritten,
promoted to P0/Phase 1).

## D8 — Default 16:9 target

**Asked:** match height, always 1080p, native, or highest?
**Answer:** "Default 16:9 target - match height and fallback 1080p".
**Effect:** `MatchHeight` is the default policy: a 16:9 mode whose height equals the
internal panel's current height, else **1920x1080**, else the highest 16:9 <= that height
(UC-03, UC-04).

## D9 — Process

**Asked:** spikes first, or build the skeleton and discover issues while building?
**Answer:** "spike first, We go with the recommendation".
**Effect:** Phase 0 gates the MVP. Order: **S1 → S3 → S4 → S5 → S6**, then S7, optional
S2, and S8 in parallel. No production UI before the gates pass.

---

## Still open

| # | Question | Why it matters |
|---|---|---|
| O1 | Which hardware is available (projector model/resolution, dock, AVR, second PC)? | Decides how much of S3/S4/S6 can be validated locally |
| O2 | Confirm `Desktop\Beam` is also the git repository root (vs docs-only) | Affects solution layout |
| O3 | Trademark / store-name check for "Beam" before public release | Low risk, must be done before beta |
| O4 | Is telemetry ever acceptable (opt-in), or never? | Default is "never"; ADR-0010 |
| O5 | Presentation extras: keep awake (recommended), suppress notifications, audio switch? | Phase 3 scope |

## Assumptions still standing

1. Windows 11 laptop with a 16:10 4K panel (confirmed on this machine: 3840x2400 @ 200%).
2. The goal is a correct *projected* image, not a permanent change to the laptop.
3. Night Light is on in the evening and should be off only while mirroring/extending.
4. Zero-config is preferred over a profile editor.
5. Unsigned dev builds are acceptable during development.
