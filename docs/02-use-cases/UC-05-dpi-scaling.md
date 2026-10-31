# UC-05 — Keep the desktop readable (DPI scaling)

**Phase:** 1 (MVP, because mirroring needs it) · **Priority:** P0 · **Spike:** S4

## Goal
Changing a 3840x2400 @ 200% source to 1920x1080 keeps the 200% scale and produces a
960x540 logical desktop. Beam must set the scale so the projected desktop is readable,
and restore the original scale afterwards.

## Key facts

1. **DPI is a property of the source, not the target.**
2. In **duplicate** mode both displays share one source -> one scale. Setting the source
   scale to 100% gives both displays a 1920x1080 logical desktop. This is the desired
   mirroring result.
3. In **extend** mode each display has its own source -> set only the external source.
4. Windows Settings uses an undocumented but stable device-info API:
   `DisplayConfigGetDeviceInfo(-3)` and `DisplayConfigSetDeviceInfo(-4)`.

## API contract (verified against the reverse-engineered reference)

```c
// header: type, size, adapterId, id = SOURCE id  (20 bytes, 4-byte aligned)
struct DISPLAYCONFIG_SOURCE_DPI_SCALE_GET {   // type = -3, size = 32
    DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    int32_t minScaleRel;   // steps below recommended; 100% is always the floor
    int32_t curScaleRel;   // current, relative to recommended
    int32_t maxScaleRel;   // steps above recommended
};

struct DISPLAYCONFIG_SOURCE_DPI_SCALE_SET {   // type = -4, size = 24
    DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    int32_t scaleRel;      // desired, relative to recommended
};
```

Ladder (observed from the Settings app):

```
100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500  (%)
recommended = ladder[abs(minScaleRel)]
current     = ladder[abs(minScaleRel) + curScaleRel]
set(p)      = DisplayConfigSetDeviceInfo(-4, ladderIndex(p) - ladderIndex(recommended))
```

Rules:
- Use the **source** id from `DISPLAYCONFIG_PATH_SOURCE_INFO`, never the target id.
- Validate `sizeof(GET) == 32` and `sizeof(SET) == 24`; if not, treat as unsupported.
- Clamp `p` to `[ladder[abs(min)], ladder[abs(min)+max]]`.
- Verify with a second `-3` call; never trust the set call alone.

## Target scale rule

Let `logicalW = width * 100 / scale`.

| Condition | Action |
|---|---|
| `logicalW` in `[1280, 2560]` | leave as is |
| `logicalW < 1280` | lower the scale to the nearest step that puts `logicalW` in range; prefer 100% |
| `logicalW > 2560` | raise the scale to the nearest step that puts it in range |

Examples:
- 1920x1080 @ 200% -> 960 -> set **100%** -> 1920.
- 1920x1080 @ 150% -> 1280 -> leave (edge, acceptable).
- 3840x2160 @ 200% -> 1920 -> leave.
- 1280x720 @ 200% -> 640 -> set 100% -> 1280.

Clone override: if the user chose "always 100% while mirroring", use 100% regardless.

## Main flow

1. **After** the mode change has been applied and verified (UC-03/UC-04), wait 300 ms for
   the driver to settle.
2. Resolve the source id for the affected display (or the shared source in clone).
3. `-3` -> min/cur/max; map to percentages.
4. Compute target with the rule above.
5. If target == current -> no-op (record "scale already correct").
6. Snapshot current scale (percent) in the journal.
7. `-4` with `scaleRel`; check `ERROR_SUCCESS`.
8. Re-read `-3`; require `current == target`.
   - mismatch -> retry once after 250 ms.
   - still mismatch -> revert to snapshot; mark DPI unsupported for this session.
9. Re-verify after 500 ms (some drivers apply asynchronously).
10. Record `ScaleApplied(from, to, sourceId)`.

## Fallback ladder

| Level | Mechanism | When |
|---|---|---|
| **L1** | `-4` device-info API | default; verified immediate |
| **L2** | `HKCU\Control Panel\Desktop\PerMonitorSettings\<id>\DpiValue` | **not automatic**: offer "apply at next sign-in" with a clear note |
| **L3** | Advice + deep link to Settings > Display | L1 unsupported/failed |
| **L4** | Skip silently | remote session, HDR-only edge, or custom scaling |

At L3 the 960x540 trap is still detected and reported, with a one-click deep link
(`ms-settings:display`). Never loop, never nag more than once per session.

## Clone vs extend

| Topology | Source to change | Effect |
|---|---|---|
| Duplicate | the shared source | both displays, one scale |
| Extend | the external display's source | projector only; laptop untouched |
| External only | the external source | projector only |
| Internal only | none | no action |

## Alternative flows

- **A1 Custom scaling factor** (Windows "Custom scaling", `LogPixels` not 96):
  the ladder does not represent it. Do not change it; advise the standard value once.
- **A2 User changes scale manually while projecting:** stop managing it for the session;
  do not restore over the user's value (CS-3).
- **A3 Driver resets scale on mode change:** because we apply DPI *after* the mode, this
  is covered; re-verify once.
- **A4 App does not re-layout:** out of scope; most per-monitor-v2 apps do.
- **A5 Remote session:** never touch DPI.
- **A6 `-3` returns values outside the ladder:** treat as unsupported, do not guess.
- **A7 Scale already correct:** zero writes.

## Postconditions
- Logical width in `[1280, 2560]` (or an explicit, explained exception).
- Original scale in the journal.
- Restore returns the exact original percentage (UC-07).

## Acceptance criteria
- AC1: 1920x1080 @ 200% is detected as the trap 100% of the time.
- AC2: the fix uses `-4` and is verified by `-3`.
- AC3: clone: one call fixes both displays; extend: the laptop scale is unchanged.
- AC4: restore returns the exact original percentage.
- AC5: unsupported API -> no registry write, one advice, no loop.
- AC6: no sign-out, no Explorer restart.
- AC7: no crash in the test app set (Explorer, Office, browser, Teams, VS Code).

## Edge cases
- Recommended scale is not 100% (e.g. 150% on a 1080p 15" panel): the relative math
  still works; `scaleRel = 0` means recommended, not 100%.
- Max scale below the computed target -> clamp and explain.
- Multi-GPU: source ids are per adapter; resolve from the path.
- HDR: independent; no interaction.
- Fast user switching: per-session, no cross-session calls.

## Data recorded
Source id hash, from/to percent, min/max/recommended, mechanism (L1/L2/L3), verification,
duration. No user identifiers.

## Tests
- Unit: ladder math, relative conversion, target rule, clamp.
- Simulation: `-3`/`-4` fakes with success, `ERROR_INVALID_PARAMETER`, silent no-op,
  out-of-ladder values, async apply.
- Hardware: S4 (duplicate + extend + restore + mode-change reset), S6.
- App matrix: Explorer, Office, browser, Teams, VS Code re-layout after the change.
