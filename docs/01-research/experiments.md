# Experiments (spikes) — go/no-go before committing

Each spike is time-boxed, has a written pass criterion, and produces a note in
this folder plus a decision update in the relevant ADR. No production code before
the Phase-0 gates pass.

## S1 — Night Light write/restore on 26H2 (highest risk)

**Question:** can we toggle the **real Windows Settings Night Light toggle** off/on by
writing only the state blob, verify it by re-reading, and restore the exact original
bytes, 50 times in a row without breaking the engine?

**Method:**
1. Snapshot both state values (default + per-device).
2. Parse with the documented layout; assert active flag.
3. Write inactive variant, preserving unknown bytes and prologue timestamps.
4. Re-read, parse, assert inactive. Toggle back. Repeat 50x.
5. Kill the process mid-write, restart, verify recovery.
6. Check behaviour with HDR on, with schedule on, with per-device keys populated.

**Pass:** 50/50 verified writes, 0 engine breakages, exact byte restore, no sign-out.
**Fail:** fall back to gamma-ramp-only mode and re-scope UC-06.

**Also answer:** do the per-device keys let us disable Night Light for *one* display?
Map key -> device path if possible; otherwise global toggle only.

## S2 — Projector classification precision (optional, Phase 2)

**Question:** can EDID + name + physical size classify projectors with >= 95%
precision at the auto threshold?

Only needed if the optional `onlyProjectors` mode ships. The MVP trigger is aspect
mismatch and does not use this spike.

**Method:** collect >= 40 EDIDs (projectors, TVs, monitors, AVRs, virtual displays);
implement the scorer from `windows-apis.md`; run offline; tune weights; measure
precision/recall.

**Pass:** precision >= 95%, recall >= 80% at auto threshold; false positives are
correctable and remembered.
**Fail:** raise threshold, ask more, never auto-apply without confirmation.

## S3 — 16:9 mode availability and clone constraints (mirroring first)

**Question:** when mirroring a 3840x2400 16:10 laptop to a 1920x1080 16:9 projector,
which shared modes exist, and what does Windows do to the panel and the scale?

**Method:** enumerate all modes on the internal panel and on an external 1080p display;
test duplicate/extend/second-only; for each, record the source mode, target modes, GDI
mode, logical size at the current scale, and where black bars appear (screenshot). Then
apply 1920x1080 + DPI 100% on the shared source and verify both displays.

**Pass:** a deterministic rule that yields a **bar-free 16:9 image on the projector**
and a readable logical desktop, in both duplicate and extend.
**Fail:** restrict MVP to duplicate-with-fixed-1080p; document extend limitations.

## S4 — Per-monitor DPI set immediately (now a concrete implementation spike)

**Question:** does the undocumented `-3` / `-4` device-info API work on 26H2, apply
immediately, survive a mode change, and restore exactly?

**Method:**
1. `QueryDisplayConfig` -> adapterId + **sourceId** for the internal panel and the
   external display.
2. `DisplayConfigGetDeviceInfo(type=-3)` -> min/cur/max relative steps; map with the
   ladder `100,125,150,175,200,225,250,300,350,400,450,500`.
3. `DisplayConfigSetDeviceInfo(type=-4, scaleRel)` -> 100%; re-read `-3` to verify.
4. Repeat after a mode change (1920x1080) to see whether the driver resets it.
5. Test in duplicate (shared source) and extend (separate sources).
6. Measure app re-layout, Explorer behaviour, and whether any app crashes.
7. Restore the original scale and verify.

**Pass:** scale changes within 1 s, verified by re-read, survives a mode change when
applied after it, restores exactly, no sign-out, no Explorer restart.
**Fail:** fall back to "advice + deep link" (L3); do not write `PerMonitorSettings`
automatically.

## S5 — Detection latency and event coverage

**Question:** which event fires first and most reliably for HDMI, USB-C DP-alt and
dock connections?

**Method:** instrument `DeviceWatcher`, `WM_DISPLAYCHANGE`, `GUID_CONSOLE_DISPLAY_STATE`,
and a 30 s reconcile poll; log timestamps over >= 30 plug/unplug cycles including
sleep/wake and KVM switching.

**Pass:** p95 detection <= 1.5 s after the display is usable; no missed connections
over 30 cycles; no duplicate applies.
**Fail:** add adaptive polling (2 s while "settling", 30 s idle).

## S6 — Safe apply/restore loop

**Question:** can we apply and restore 500 times without a single black screen or
stuck mode?

**Method:** automated harness on 2 machines (Intel iGPU laptop, NVIDIA desktop) with a
1080p and a 720p display; randomise policies; inject failures (invalid mode, API error,
process kill); assert display always ends in a valid enumerated mode.

**Pass:** 500/500 valid end state, 0 black screens, 0 unsupported modes applied.
**Fail:** no release.

## S7 — Night Light vs HDR interaction

**Question:** does Night Light apply at all in HDR, and does writing the blob while HDR
is on cause artefacts?

**Method:** toggle HDR, read advanced-colour state, apply/restore Night Light, capture
screenshots and state.

**Pass:** documented behaviour and a rule ("skip Night Light when HDR is active" or
"gamma fallback").
**Fail:** disable Night Light step under HDR.

## S8 — Upstream feasibility

**Question:** will PowerToys accept display modes + Night Light + auto-trigger in
PowerDisplay?

**Method:** read issues #48286/#47433/#47336/#49659/#48470, PR #51072; post a short
proposal comment on #48286 and the contribution thread #28769; ask for a spec review.

**Pass:** maintainer indicates the direction is acceptable -> write spec, then PR.
**Fail:** ship standalone only; keep engine upstream-shaped for a later attempt.

## Spike order and budget

| Order | Spike | Effort | Blocks |
|---|---|---|---|
| 1 | S1 Night Light | 2–3 days | UC-06, ADR-0006 |
| 2 | S3 modes / duplicate / bars | 1–2 days | UC-02/03/04, ADR-0004 |
| 3 | S4 DPI `-3`/`-4` | 1–2 days | UC-05, ADR-0013 |
| 4 | S5 detection | 1–2 days | UC-01, ADR-0007 |
| 5 | S6 safety loop | 2–3 days | release gate |
| 6 | S7 HDR | 0.5 day | UC-06 edge |
| 7 | S2 classification | 2 days (data-dependent) | Phase 2 only (`onlyProjectors`) |
| 8 | S8 upstream | ongoing | ADR-0001 |

Total Phase 0: ~2 weeks part-time. Nothing else starts until S1, S3, S4, S5 and S6 pass.
