# ADR-0013 — DPI scaling via the source DPI device-info API (-3 / -4)

**Status:** Accepted
**Date:** 2026-10-29
**Spike:** S4 (validates the mechanism on this hardware; does not decide it)

## Context

Changing a 3840x2400 @ 200% source to 1920x1080 keeps the 200% scale and produces a
960x540 logical desktop — unusable on a wall. The user asked for "the best possible
solution", not a guess.

Findings:

- There is **no documented** per-monitor DPI setter. `SystemParametersInfo(
  SPI_SETLOGICALDPIOVERRIDE)` is deprecated and primary-display only.
- Windows Settings itself uses `DisplayConfigGetDeviceInfo` / `DisplayConfigSetDeviceInfo`
  with **negative device-info types** on the **source**:
  - `-3` = get DPI scale (`minScaleRel`, `curScaleRel`, `maxScaleRel`), struct size 32
  - `-4` = set DPI scale (`scaleRel`), struct size 24
- Values are **relative steps**, not percentages. Observed ladder:
  `100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500`.
- A working reference implementation exists (`lihas/windows-DPI-scaling-sample`,
  Unlicense), which is how this was confirmed rather than guessed.
- **DPI belongs to the source.** In duplicate mode both displays share one source and
  therefore one scale — which is exactly what mirroring needs.

## Decision

Use the `-3` / `-4` device-info API as the **only automatic** DPI mechanism.

```
struct GET { header(type=-3,size=32,adapterId,id=SOURCE); int min, cur, max; }
struct SET { header(type=-4,size=24,adapterId,id=SOURCE); int scaleRel; }

recommended = ladder[abs(minScaleRel)]
current     = ladder[abs(minScaleRel) + curScaleRel]
set(p)      = DisplayConfigSetDeviceInfo(-4, ladderIndex(p) - ladderIndex(recommended))
```

Rules:

1. Resolve the **source** id from `DISPLAYCONFIG_PATH_SOURCE_INFO`; never use the target.
2. Validate `sizeof(GET) == 32`, `sizeof(SET) == 24`, and `ERROR_SUCCESS`. If either
   differs, treat the capability as unsupported — never guess.
3. Clamp the target to `[min, max]` reported by `-3`.
4. **Apply DPI after the mode change** and after a 300 ms settle; some drivers reset the
   source scale when the mode changes.
5. **Verify by re-reading `-3`.** Retry once after 250 ms; on a second failure revert to
   the snapshot and mark DPI unsupported for the session.
6. Clone: set the **shared source** (affects both displays). Extend: set the **external
   source** only; never touch the internal panel's source.
7. Snapshot and restore the exact original percentage (UC-07).
8. Registry `PerMonitorSettings\DpiValue` is **never written automatically** (it usually
   needs a sign-out). It is only offered as "apply at next sign-in" (L2).
9. No loop, no nagging: one attempt per session per display.

Target rule: `logicalW = width * 100 / scale`; keep `logicalW` in `[1280, 2560]`,
preferring 100% when below the range.

## Consequences

**Positive**
- Same mechanism as the OS Settings app, so behaviour (app re-layout, per-monitor
  awareness, animations) is identical.
- Immediate, reversible, verifiable, no admin, no sign-out, no Explorer restart.
- Solves the 960x540 trap in duplicate mode, which is the primary scenario.

**Negative**
- The type constants are undocumented. Contained behind `IDpiController` with struct-size
  and read-back validation, so a Windows change degrades to L3 advice instead of
  corrupting anything.
- Custom scaling factors (non-ladder) are not representable; Beam advises instead.
- A few legacy apps do not re-layout until restarted. Documented, not fixed.

## Fallback ladder

| Level | Mechanism | Behaviour |
|---|---|---|
| L1 | `-4` device-info | automatic, verified |
| L2 | `PerMonitorSettings` | offered only as "apply at next sign-in" |
| L3 | advice + `ms-settings:display` deep link | no writes, one message |
| L4 | skip | remote session, custom scaling, unsupported struct size |

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Registry `PerMonitorSettings` as primary | Usually requires sign-out; unacceptable mid-meeting |
| `SPI_SETLOGICALDPIOVERRIDE` | Deprecated, primary only, global |
| Ignore DPI | Ships the 960x540 bug the user explicitly wants fixed |
| Force 100% always | Wrong for 4K projectors and large panels |
| Choose a resolution that is "sane" at 200% | Requires a mode the projector does not have |

## Validation

S4 must demonstrate on real hardware: immediate change, verified by re-read, survival of
a mode change (applied after it), exact restore, and no app crashes. If S4 fails on this
machine, the ADR stands but the capability downgrades to L3 at runtime.
