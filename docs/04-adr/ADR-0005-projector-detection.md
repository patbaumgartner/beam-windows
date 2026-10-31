# ADR-0005 — Trigger on aspect mismatch; projector classification is optional

**Status:** Accepted (trigger) · classifier deferred to Phase 2
**Date:** 2026-10-29
**Supersedes:** the earlier "classify first, then act" version of this ADR

## Context

The original plan made projector classification the gate: score the EDID, ask the user
if ambiguous, then act. The user clarified the actual problem:

> "The main issue is the screen of this notebook has 16:10 on a TV or on a projector you
> would have the black bars on the side. This is what I do not want while mirroring."

So the trigger is not "is this a projector?" — it is **"will mirroring this display
produce black bars?"** That is directly observable: a new *external* display whose
aspect ratio differs from the internal panel's, or a clone group where a 16:9 mode is
common.

This matters because:
- It covers TVs and projectors, which is what the user wants.
- It has no classification risk and needs no user question in the default path.
- Windows has no "this is a projector" API anyway (`outputTechnology` describes the
  connector, not the device class).

## Decision

1. **Primary trigger (MVP): aspect mismatch.**
   ```
   external && active && !virtual
     && (topology == Clone with a common 16:9 mode
         || aspect(display) != aspect(internal) within 2%)
   ```
2. **Classification is an optional refinement (Phase 2, off by default).** When
   `onlyProjectors = true`, run the scored EDID heuristic from
   `../01-research/windows-apis.md` and require a projector verdict. This exists for
   users who do not want Beam to act on hotel TVs; it must never block the MVP path.
3. **User override always wins** and is stored per stable device id ("never for this
   display", "treat as presentation display").
4. Every decision records its reason (`aspect-mismatch`, `clone-common-16x9`,
   `user-override`, `not-external`, ...) for diagnostics.

## Rationale

- Solves the reported problem with zero questions and zero classification risk.
- TV and projector are treated identically, which is correct: both are 16:9 displays
  being mirrored from a 16:10 panel.
- Classification remains available for profiles and for users who need it, without
  being on the critical path.
- The aspect test is deterministic, cheap and unit-testable.

## Consequences

**Positive**
- No corpus needed for the MVP; S2 no longer gates the release.
- No first-run question for the common case.
- Fewer false negatives (a projector with a generic EDID still triggers).

**Negative**
- A 16:9 external monitor also triggers. Mitigation: "never for this display", plus the
  optional `onlyProjectors` mode.
- The user loses per-device naming by device class in the default path (profiles still
  key on device id, so naming comes from the EDID friendly name).

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Classify first, act second (original) | Adds a question and a failure mode to the main scenario; does not cover TVs |
| Act on any external display | Triggers for 16:10/16:9 monitors where nothing is wrong |
| Act only on `Win+P` = Duplicate | Misses extend mode and misses plug-and-play duplicate defaults |
| Output technology == HDMI | TVs, monitors and AVRs also use HDMI; meaningless |

## Validation

S3 must confirm the aspect rule against real duplicate/extend/second-only topologies on
a 16:10 panel. The optional classifier (S2) is validated separately when Phase 2 starts.
