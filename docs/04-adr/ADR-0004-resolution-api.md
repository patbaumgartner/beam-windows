# ADR-0004 — Modes via ChangeDisplaySettingsEx, topology via QueryDisplayConfig

**Status:** Accepted
**Date:** 2026-10-29

## Context

Two Win32 families can change display configuration:

1. `EnumDisplaySettingsEx` / `ChangeDisplaySettingsEx` with `DEVMODE` — per-display mode,
   refresh, orientation; `CDS_TEST` validates first.
2. `QueryDisplayConfig` / `SetDisplayConfig` — full path/mode topology; can change
   everything at once, including enable/disable/position/primary.

PowerDisplay already uses (1) for rotation, with `CDS_TEST` then apply without
`CDS_UPDATEREGISTRY`, and uses `QueryDisplayConfig` for inventory.

## Decision

- **Read topology and identity** with `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` +
  `DisplayConfigGetDeviceInfo`.
- **Enumerate and change modes** with `EnumDisplaySettingsEx` and
  `ChangeDisplaySettingsEx` (`CDS_TEST` -> apply without registry persistence -> verify).
- **Do not change topology** in MVP. `SetDisplayConfig` is reserved for a later
  "projector profile includes topology" feature, behind its own ADR.

## Rationale

- Mode change is the actual need; topology is already what the user chose with `Win+P`.
- `ChangeDisplaySettingsEx` does not show the "Keep these display settings?" dialog for
  mode changes (unlike some topology changes), which keeps the flow zero-touch.
- `CDS_TEST` gives a cheap pre-flight validation.
- Same pattern as existing PowerDisplay code -> upstream port is a copy of a known-good
  approach.
- `SetDisplayConfig` requires constructing a complete, consistent path+mode array; one
  mistake blanks a display. Too risky for MVP.

## Consequences

**Positive**
- Small, well-understood API surface.
- Works on hybrid graphics; per-GDI-device enumeration avoids adapter confusion.
- No admin, no registry persistence, easy revert.

**Negative**
- Clone mode must be handled by applying to the shared source (UC-04).
- Cannot enable a disabled projector; the user must use `Win+P` (documented).
- Orientation changes require swapping width/height (already solved in PowerDisplay).

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| `SetDisplayConfig` for everything | Complex, risky, topology side effects, needs full path set |
| `ChangeDisplaySettings` (global) | Changes all displays; wrong |
| WMI `Win32_DesktopMonitor` / `WmiSetDisplay` | Slow, limited, unreliable |
| Driver/vendor SDKs | Not general |

## Validation

S3 and S6 must confirm: mode lists are complete, clone behaviour is as expected, and
verification catches silent failures.
