# ADR-0018 — Native 16:9 mode ranking ("prefer native", B2/C1)

**Status:** Accepted
**Date:** 2026-11-14
**Related:** B2 (presenter-edition plan Phase B), C1 settings window, UC-03 (apply 16:9), ADR-0004 (resolution API), `DisplayModeController.Rank16x9`, `DisplayModeControllerPropertyTests`

## Context

In duplicate (clone) mode Windows forces one shared source resolution on every display.
Beam picks a 16:9 mode so a 16:9 projector shows no bars. Q3 fixed the policy: match
height, fall back to 1920×1080. But "which 16:9 mode" has a subtle best answer: the
projector's **EDID-native** mode is the one the beam was designed for — usually the
sharpest and least-scaled. Some users, however, prefer the highest pixel count even
when it means the projector scales (e.g. a 1080p-native projector shown a 4K signal
downscaled by the GPU can look softer than native 1080p).

## Decision

`DisplayModeController.Rank16x9(modes, nativeMode, preferNative)` ranks 16:9 candidate
modes by, in order:

1. **Native first** — when `preferNative` is true (the default), a mode equal to the
   external display's EDID-native mode sorts ahead of everything (`IsNativeMode`).
2. **Pixel count descending** — more pixels first (subject to the bounded-candidate cap).
3. **60 Hz over 30 Hz** — higher refresh wins on ties (30 Hz rooms still work; see the
   hardware matrix).

The preference is a **user setting**: `BeamSettings.PreferNativeMode` (default `true`),
exposed in the settings window's Projector mode section. `ApplyCloneWith16x9(bool
preferNative = true)` threads it through, and `PresentationManager` passes the setting
on every apply. When `preferNative` is false the native boost is skipped entirely and
ranking is purely pixels → refresh.

## Rationale

- Native-first is the right default for projectors (manufacturer-tuned mode, no
  scaler), which is why it is on by default.
- Making it a setting costs one bool and covers the minority who prefer max pixels.
- Keeping the ranking a pure static function in `Beam.Windows` (property-tested:
  native-first when on, skipped when off, always 16:9, always bounded) means the
  policy is exhaustively tested without hardware.

## Consequences

**Positive**
- 4K vs 1080p beamers both get their native mode by default; 30 Hz-only rooms fall
  back to 1920×1080@30 cleanly (H2/H6 matrix rows).
- The choice is visible and reversible in Settings — no registry hacking.

**Negative**
- One more persisted setting and one more UI row.
- "Native" is detected from the EDID of the *currently connected* external; on a KVM
  flap to a different projector the next apply re-ranks against the new native mode
  (desired, but worth knowing).

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Always highest pixels | Softer image on native-1080p projectors fed 4K; the common presenter case is native projectors |
| Always native, no setting | Removes user agency; some rooms genuinely look better maxed out |
| Match-height-then-max-width hard rule (Q3 only) | Q3 is the fallback when no native 16:9 exists; it says nothing about ranking among several 16:9 modes |

## Validation

- Property tests (`DisplayModeControllerPropertyTests`): with `preferNative: true` the
  native mode is ranked first among 16:9 modes; with `false` it is not boosted; the
  result is always 16:9 and within the bounded-candidate cap.
- App tests: `PreferNativeMode` round-trips through the settings store and reaches
  `ApplyCloneWith16x9` as `LastPreferNative`.
- Hardware matrix H1/H6 (1080p and 4K projectors) confirm the native mode is chosen
  and verified after write.

## Superseded-by

None.
