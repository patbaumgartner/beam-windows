# Spike results

One section per spike. A spike is done when the gate in `roadmap.md` is met and the
result is recorded here.

## S3 — Display topology, clone modes, mode indices

**Date:** 2026-10-29
**Machine:** Windows 11 26H2, build 26300, .NET 8.0.427
**Status:** in progress (enumeration done; change path pending hardware)
**Code:** `spikes/S3.DisplayInventory`, `src/Beam.Windows/Display`,
`src/Beam.Core/Display`, `tests/Beam.Core.Tests/DisplayTopologyTests.cs`

### Question

Can Beam enumerate the real topology (sources, targets, modes, clone groups) with enough
fidelity to (a) detect a new external display and (b) plan a bar-free duplicate mode?

### Devices seen

| Path | Source | Target | Native | Current desktop | Aspect |
|---|---|---|---|---|---|
| `\\.\DISPLAY1` | src=0 | SHP `DBM:1551` | 3840x2400 | 1920x1080 | 16:10 native |
| `\\.\DISPLAY2` | src=1 | HP Z27u G3 `CQB:36D0` | 2560x1440 | 2560x1440 | 16:9 |

The HP Z27u is 16:9; the internal panel is 16:10. This is exactly the mismatch Beam
targets.

### Key finding: mode indices are NOT direct array indices

With `QDC_ALL_PATHS | QDC_VIRTUAL_MODE_AWARE`, `DISPLAYCONFIG_PATH_SOURCE_INFO.modeInfoIdx`
and `DISPLAYCONFIG_PATH_TARGET_INFO.modeInfoIdx` are a **packed encoding**. For example:

```
PATH 0 (laptop):  src modeIdx=0x0001FFFF  tgt modeIdx=0x00000002
PATH 1 (HP):      src modeIdx=0x0004FFFF  tgt modeIdx=0x00030005
```

The classic interpretation — modeInfoIdx is a direct index into the mode array, or equals
`DISPLAYCONFIG_PATH_MODE_IDX_INVALID (0xFFFFFFFF)` — fails here: the high word `0xFFFF`
marks a *virtual* path, and the direct index the docs describe is only the active path.

The robust method is to **search the mode array** for the entry whose `infoType`
(source=1 / target=2) and `id` (source id / target id) match the path:

```csharp
static DISPLAYCONFIG_MODE_INFO? FindMode(infoType, id, adapterId, modes, modeCount)
```

This returns the correct pair from the array:
- laptop source mode: `3840x2400 pos(0,0)`; target mode `3840x2400@59.99`
- HP source mode: `2560x1440 pos(3840,0)`; target mode `2560x1440@59`

### Other observations

- Every inactive path reports a huge virtual source×target cross product (112 paths from
  just 4 sources). Active-only enumeration is the sensible default for the app.
- The laptop's DPI ladder is `min=-2, cur=-2, max=+1` **relative to the recommended
  scale**. The HP reports `cur=0` (at recommended). This confirms `DisplayConfigGetDeviceInfo(-3)`
  works and validates the relative-step model in ADR-0013.
- `DisplayConfigGetDeviceInfo(-3)` returns 32-byte payloads (header + 3 INT32) as expected.
- Internal panel detection: `DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL (18)` and
  `INTERNAL_VIRTUAL (0x80000000)` identify the panel reliably by EDID path
  (`SHP1551`).

### Backlog

- [x] Test duplicate mode on the dock monitor: set both targets to a 16:9 shared source
      and confirm no bars. → **S6 PASS** (DELL U2718Q, 2026-10-30).
- [ ] Test on the projector this afternoon.
- [x] Measure DPI rel-step mapping against the 100..500 ladder (ADR-0013 numbers). → S4.
- [x] **Clone detector:** stop grouping by `SourceId`; use `QueryDisplayConfig`'s
      `currentTopologyId` to report a true clone. → **S7** (2026-10-30).


## S5 — Display connect/disconnect detection

**Date:** 2026-10-30
**Status:** implemented, ready to run
**Code:** `src/Beam.Windows/Display/DisplayMonitor.cs`,
`src/Beam.Core/Display/ConnectionSnapshot.cs`,
`src/Beam.Core/Stats/Percentiles.cs`,
`spikes/S5.DisplayEvents`

### Architecture

```
DisplayMonitor
  poll interval: 250 ms  (hard-coded; far faster than the 1.5s bar)
  captures:      ConnectionSnapshot (list of DisplayId + WxH + scale + name)
  diffs by:       DisplayConfig source identity (AdapterId + SourceId)
  callbacks:      Changed(previous, current)   LatencyUpdated(summary)

Latency tracking:
  timestamp at poll start — timestamp of change detection
  samples: double ms, stored in a List<double>
  p95 via nearest-rank Percentile.Compute
```

### Key decisions

- **Poll-based** (250 ms) rather than a WMI subscription: simpler, deterministic,
  sub-250 ms latency to the poll that catches the change. A real WMI event fires once
  the system has committed the change; our poll catches it one interval later at worst.
- **Callbacks invoked outside the lock** so a handler that reads `Latency` or `Current`
  or that captures the monitor again cannot self-deadlock.
- **Event capture to locals** inside the lock (`onChanged = Changed`) prevents a
  `Changed -=` during enumeration.

### Acceptance criteria

| # | Criterion | Target |
|---|-----------|--------|
| 1 | Detection latency: p95 across ≥ 5 events | ≤ 1500 ms |
| 2 | No events dropped under normal load | 0 missed |
| 3 | False-positive rate (duplicate events) | 0 |
| 4 | Snapshots are order-independent | stable |

### Backlog

- [ ] Run `S5.DisplayEvents watch` on the dock, plug/unplug, measure real p95.
- [ ] Validate the monitor works with the 16:9 external/16:10 internal clone group.
- [ ] Confirm timing when the dock reconnects after sleep (S0ix warm-reconnect path).
- [x] Hook into the Beam tray app: when external display with aspect mismatch connects,
        trigger the full fix sequence (NightLight → duplicate mode → DPI). → **Beam.Tray**
        `PresentationManager`, 2026-10-30; hardware validation pending.

## S7 — Clone detector (currentTopologyId)

**Date:** 2026-10-30
**Status:** **PASS** — fixed; added to spike-results.md
**Code:** `src/Beam.Core/Display/DisplayTopology.cs`,
`src/Beam.Windows/Display/DisplayInventory.cs`,
`tests/Beam.Core.Tests/DisplayTopologyTests.cs`

### Bug identified

`DisplayTopology.ActiveCloneGroups` grouped paths by `SourceId` to detect clones.
This fails on this laptop (Windows 11 26H2 build 26300), where "Duplicate" is represented
as **two sources (src=0 internal, src=1 DELL) rendering the same DWM content**. The
grouping-by-SourceId reports `clone=0` even in genuine duplicate.

### Fix

1. **`DisplayTopology.CurrentTopologyId`** (new): the canonical topology id read via a
   separate `QueryDisplayConfig(QDC_DATABASE_CURRENT | QDC_VIRTUAL_MODE_AWARE, &topologyId)`
   call. Only `QDC_DATABASE_CURRENT` accepts a non-NULL `currentTopologyId`; all other
   flag combinations return `ERROR_INVALID_PARAMETER` (87) if passed. The enum values:
   `DISPLAYCONFIG_TOPOLOGY_INTERNAL=1`, `CLONE=2`, `EXTEND=4`, `EXTERNAL=8`.

2. **`DisplayTopology.IsMirrored`** (new): `currentTopologyId == CLONE`; falls back to
   grouping-by-source when topology id is 0 (read failed), so a failed topology read
   never silently disables the presentation trigger.

3. **`DisplayTopology.HasMirrorAspectMismatch`** (rewritten): first fast-check
   `if (!IsMirrored) return false`. When mirrored, groups by `(width, height, x, y)`
   of the source mode (not SourceId) to handle the two-source duplicate case. In true
   single-source clone, all paths share one source id and one desktop position; the
   new grouping also handles it.

### Bug found: `SetDisplayConfig` topology constant values

The spike's `restore` command used `SdcTopologyExtend = 0x00000001`, which is actually
`TOPOLOGY_INTERNAL` (1). The correct `TOPOLOGY_EXTEND` is 0x00000004 (4). This is why
the DELL vanished after the first `restore` — it was switching to internal-only mode.
Fixed: `DisplayNative.SdcTopologyExtend = 0x00000004`; `SdcTopologyInternal = 0x00000001`.

### New tests (93 total, +4)

- `IsMirrored_True_WhenTopologyIdIsClone` / `False_WhenNotClone`
- `HasMirrorAspectMismatch_DetectsTwoSourceDuplicate` (the S6 bug case)
- `HasMirrorAspectMismatch_False_TwoSourceDuplicateSameAspect` (clean case)

### Backlog

- [ ] Verify on real hardware: with DELL active in Duplicate, `currentTopologyId` reads
      as `CLONE (2)`. When DELL reconnected, `S6 snapshot` should show
      `topology=clone mirrored=YES` and correct aspect mismatch.

## S6 — Duplicate mode + 16:9 shared source (the black-bar fix)

**Date:** 2026-10-30
**Machine:** Windows 11 26H2, build 26300, .NET 8.0.427
**Status:** **PASS** — gate met, end-to-end on real hardware (DELL U2718Q standing in for
the projector). Core mechanism validated; clone detector fixed separately (S7).
**Code:** `spikes/S6.DuplicateApply`, `src/Beam.Windows/Display/DisplayNative.cs`

### Question

Can Beam enter duplicate mode, force the shared source to 16:9 so the 16:9 external
fills edge-to-edge (no side bars), keep the change long enough to be useful, and then
restore the previous state?

### Sequence (all verified on real hardware)

| Step | API | Result |
|---|---|---|
| Enter duplicate | `SetDisplayConfig(0,.., SDC_TOPOLOGY_CLONE \| SDC_APPLY)` | OK |
| Shared source → 3840x2160@60 | `ChangeDisplaySettingsEx(CDS_TEST)` | **bad mode** |
| Fallback → 3840x2160@30 | CDS_TEST | OK |
| Apply + persist | CDS with `CDS_UPDATEREGISTRY` | **OK** |
| Persistence | wait ~30s | **did NOT revert** |
| Visual (DELL U2718Q) | full screen | no side bars ✅ |
| Visual (laptop panel) | 3840x2400 shows 2160 | top/bottom bars (expected, harmless) |
| Restore | `SetDisplayConfig(SDC_TOPOLOGY_EXTEND \| SDC_APPLY)` | OK, laptop back to 3840x2400 |

After apply, the shared source reported `desktop 3840x2160 signal 3840x2400` for the
internal panel and `desktop 3840x2160 signal 3840x2160` for the DELL: a single 16:9
source driving both targets — DELL full, laptop 16:9.

### Key finding: persistence requires `CDS_UPDATEREGISTRY`

The first attempt applied without persistence (`flags=0`) and showed an immediate OK,
then the OS **reverted** to the saved config within a couple of seconds. Re-running with
`CDS_UPDATEREGISTRY` (0x0001) on the final apply call kept the change through 30s+ of
observation. This is the crucial difference and matches the S6 design note in the plan:
"persistence of topology/mode changes requires CdsUpdateRegistry, else the OS reverts."

### Findings that changed the design

1. **Win+P Duplicate is not always one source.** On this laptop (single LUID
   `00000000:0001594D`, two paths), Windows 11 represents "Duplicate" as **two sources**
   rendering the same DWM content — `src=0` internal, `src=1` DELL. Both must be set to the
   16:9 resolution; clone-by-SourceId is therefore the wrong detector for the *trigger*
   (S3-backlog → S7: use `QueryDisplayConfig`'s `currentTopologyId`).
2. **60 Hz is rejected on the shared source; 30 Hz is accepted.** The internal panel's
   native signal stays 3840x2400@60; CDS to 3840x2160@60 fails, @30 succeeds. The fix
   must try a ladder of refresh rates, not a single one.
3. **`ChangeDisplaySettingsEx` for a plain resolution change needs the full DEVMODE**
   (`dmFields` = width|height alone is insufficient → `DispChangeFailed`). Including
   `dmDisplayFrequency` fixed it: `dmFields |= DmDisplayFrequency`.

### Backlog

- [ ] Actual projector once one is connected (DELL validated as stand-in).
- [ ] Beam's restore on disconnect must restore **both** the original topology and the
        shared-source mode (snapshot the DEVMODE via `EnumDisplaySettings` before changing
        and apply it on restore).

## S4 — DPI get/set/restore via -3 / -4

**Date:** 2026-10-30
**Machine:** Windows 11 26H2, build 26300, .NET 8.0.427
**Status:** read path verified on real hardware; set path implemented, pending mutating test
**Code:** `spikes/S4.Dpi`, `src/Beam.Windows/Display/DpiController.cs`,
`src/Beam.Core/Display/{DpiScale,DpiScaleLadder,ScalePlanner}.cs`

### Live read result

```
\.\DISPLAY1  src=0
    current=225% recommended=300% range=[100..400] rel=(-7,-2,2)
    planner -> 200%  setRel=-3
```

- `DisplayConfigGetDeviceInfo(-3)` returns 32-byte payloads and parses against the ladder.
- The laptop panel exposes `-7..+2` relative steps => recommended 300%, min 100%, max 400%.
- `ScalePlanner` selected 200% (logical 1920 for a 4K source), step `-3` to write.
- The undocumented relative-step model in ADR-0013 and the working reference algorithm
  (`lihas/windows-DPI-scaling-sample`) are confirmed against this machine.

### Implementation notes

- `DpiScaleValue.RelativeFor(target)` computes the step to write:
  `index(target) - index(recommended)`.
- `DpiController` gates on struct size (GET 32, SET 24) and never guesses.
- SET (`-4`) + verify-by-reread + one retry is implemented but not yet executed on this
  box (it resizes windows, so it is gated behind `--yes` and the projector test).

### Bug found by the test suite (fixed)

`ScalePlanner.Score` used `int.MaxValue - 1000 + logicalWidth` as the penalty for a
logical width below 1280. For widths over 1000 this **overflows int** and wraps negative,
so a `1097px` logical desktop scored as the *minimum* and won. Changed to
`long.MaxValue - logicalWidth`. This was caught only because a unit test asserted the
chosen percentage.

### Backlog

- [ ] Run `set <gdi> <percent> --yes` on the external display or projector, verify,
      restore to the exact original.
- [ ] Confirm DPI survives a mode change (applied after it) per ADR-0013 rule 4.

## S1 — Night Light write/restore

**Date:** 2026-10-29
**Machine:** Windows 11 26H2, build 26300, .NET 8.0.427
**Status:** **PASS** — gate met (50/50 verified, semantic restore)
**Code:** `spikes/S1.NightLight`, `src/Beam.Core/NightLight`, `src/Beam.Windows/NightLight`,
`tests/Beam.Core.Tests`

### Question

Can Beam flip the **real** Windows Night Light toggle (the one in Settings), verify it,
and restore the exact prior state — without breaking the Night Light engine?

### Live values observed

Two values exist on this build:

| Value | Length (off) | Suffix | Epoch |
|---|---|---|---|
| default `bluelightreductionstate` | 38 bytes | `00 00 00 00` | schedule-driven |
| per-device `{c8829d7b-...}...stateperdevice` | 39 bytes | `01 00 00 00 00` | schedule-driven |

Both parsed strictly on the first attempt. The 38/39-byte split is exactly the extra
suffix byte documented by the community reverse engineering.

### What was proven

| Check | Result |
|---|---|
| Strict parse of both live values | pass |
| `on` verified by re-read | pass — 38 → 43 bytes (+`10 00`, +`D0 0A 02`) |
| `off` verified by re-read | pass — 43 → 41 bytes |
| `restore` of an ON snapshot after `off` | pass — both values `active=True` |
| 50 off/on cycles | 50/50, zero failures |
| Restore original OFF/Schedule state | pass — 38/39 bytes, `active=False`, `cause=Schedule` |

### Findings that changed the design

1. **Restore cannot be byte-exact.** Windows reverts a write whose prologue epoch is not
   greater than the stored one. Every write must re-serialize with
   `epoch = max(now, stored + 2)`. Restore is therefore *semantically* exact: same active
   state, same transition cause, same unknown suffix bytes, fresh epoch. ADR-0006, UC-06
   AC2 and the roadmap were corrected accordingly.
2. **Two values, not one.** On 26H2 the per-device value exists alongside the default.
   Beam must snapshot and restore both, and preserve each one's own suffix length.
3. **The `manual` marker matters.** Writing `10 00` alone leaves the schedule able to
   override the state; `D0 0A 02` marks the change as manual. Beam sets manual when it
   turns Night Light off, and restores the snapshot's original cause afterwards.
4. **Malformed input must never throw past the parser.** The test suite found that a
   truncated value produced `ArgumentOutOfRangeException`, which the controller did not
   catch. Fixed: the codec now throws only `NightLightFormatException`, and `Read()` has a
   catch-all that returns an unparsable entry instead of propagating.

### Residual risk

- Undocumented format; a Windows update may change it. Mitigation: strict parse, never
  write on mismatch, golden-blob tests, re-run S1 on each new build.
- Engine breakage from a malformed write was not observed across 100+ writes, but the
  mitigation (snapshot + verify + restore) stays mandatory.
- HDR interaction is still open (S8).

### How to re-run

```powershell
dotnet build Beam.sln
dotnet run --project spikes/S1.NightLight -- dump      # read-only
dotnet run --project spikes/S1.NightLight -- status    # read-only
dotnet run --project spikes/S1.NightLight -- snapshot
dotnet run --project spikes/S1.NightLight -- on
dotnet run --project spikes/S1.NightLight -- off
dotnet run --project spikes/S1.NightLight -- restore
dotnet run --project spikes/S1.NightLight -- cycle 50 --yes
```

Snapshot file: `%LOCALAPPDATA%\Beam\spikes\s1-nightlight-snapshot.json`.
