# Hardware matrix

Per-release manual verification. The base checks **H1–H10** are defined in
[test-strategy.md](test-strategy.md#41-hardware-matrix); this file extends them with the
0.2.0 presenter-edition rows (**H11–H18**) and records results. A green unit suite is
not permission to skip this — run before every release and attach the outcome to the
release notes (test-strategy.md §6 release checklist).

## Environment used so far

- Dev laptop with a 16:10 internal panel @ 200% DPI.
- **DELL U2718Q** 16:9 external monitor as projector stand-in — S3/S4 PASS (2026-10-30),
  S6 500-cycle physical soak PASS (2026-11-09).
- Real projector, USB-C dock, KVM, AVR, second-GPU PC: **pending** (O1). Until the real
  projector arrives the DELL stands in and the rows marked "real projector" stay open.

## Matrix

| ID | Setup | Checks | Status |
|---|---|---|---|
| H1 | Laptop + 1080p projector (HDMI) | detect, apply 16:9, readable DPI, restore on unplug | stand-in PASS (S3/S6); real projector pending |
| H2 | 720p projector | mode fallback, no unsupported mode applied | pending |
| H3 | USB-C dock + HDMI | enumeration timing, exactly one apply | pending |
| H4 | HDMI KVM | bounce/flap coalescing, no restore loop | sim + stress tests PASS; hardware pending |
| H5 | AVR passthrough | not misclassified as an internal panel | pending |
| H6 | 4K projector/TV | native 4K 16:9 chosen (prefer-native), sensible DPI | pending |
| H7 | Sleep/wake with projector attached | re-apply without churn | pending |
| H8 | Kill Beam mid-apply | journal restores the display on next start | automated PASS (kill-at-20 tests); hardware pending |
| H9 | Timed pause while presenting, then unplug | paused session ignores changes; auto-resume after 15 min / 1 h restores | automated PASS (`PauseFor` tests); hardware pending |
| H10 | 200% DPI + tray menu | menu responsive, cursor never sticks, icon crisp at 48/64/96 px | PASS 2026-11-09 (TrayIconTests + manual) |
| H11 | 30 Hz-only room | 30 Hz 16:9 accepted, no unsupported mode; 60 Hz preferred when available | S6 finding on stand-in: 60 Hz rejected on the shared source, 30 Hz accepted — PASS |
| H12 | Dock hot-plug flap (UC-13) | ≤ 1 apply per attach, cooldown respected, no event storm | automated PASS (`RapidConnectDisconnect_NoEventStorm`); hardware PASS 2026-11-14 + 2026-11-15 (U2718Q rig): real and scripted disconnect/reconnect each produced exactly 1 trigger eval, `triggerMet=False` in extend, no storm, no unrequested apply; mirrored replug still pending (run log) |
| H13 | KVM switch bounce | topology re-read after bounce, single apply, no loop | pending |
| H14 | 4K vs 1080p beamers (B2) | prefer-native picks the EDID-native 16:9; toggle off → max pixels (ADR-0018) | property tests PASS; hardware pending |
| H15 | Audio extra (B3) | default render endpoint switches to the HDMI/projector on apply, restores on unplug | fake-tested end-to-end; 2026-11-14 hardware partial — no HDMI audio endpoint on this rig (DP only): skip + restore paths logged; real switch pending a display with audio (projector); re-confirmed 2026-11-15 |
| H16 | Explorer restart (`TaskbarCreated`) | tray icon re-registers, exactly one icon, no monitor loop | PASS 2026-11-14 (U2718Q rig): explorer killed → `Taskbar recreated - re-registering tray icon` in beam.log, exactly 1 Beam process (PID 12316); re-PASS 2026-11-15 (log line 09:09:34.562, 1 process PID 12316) |
| H17 | Diagnostics bundle (A4) | zip on the Desktop contains **no** machine/user names; `summary.txt` sanitized | automated PASS (`Exporter_WritesZipWithSanitizedEntries`); PASS 2026-11-14 hardware run — in-process export, 4 entries, sanitize scan: no matches; re-PASS 2026-11-15 (`beam-diagnostics-20261115-090936.zip`, scan: no matches) |
| H18 | Fresh install on a clean Windows 11 VM | per-user install works from the GitHub Release download; uninstall leaves no residue | manual VM run pending |

## How to record a run

Add the date, machine/projector model, Beam version (`beam help --json`) and result to
the row (or append a dated note below the table), and paste the summary into the release
notes of the `v*` tag.

## Run log

### 2026-11-27 — production regression cycle, DELL U2718Q stand-in rig, Beam 0.3.2 (commit 17fd5b7)

- `eng/production-test.ps1 -Suite Hardware`: **PASS** — manual Mirror Apply/Undo, automatic
  duplicate detection, and disconnect recovery; final baseline compares topology, source/signal
  modes, refresh rates, and DPI.
- `eng/production-ui-test.ps1`: **PASS** — Settings persistence, sanitized Diagnostics export,
  pause/resume, startup toggle, and single-instance behavior on the real desktop.
- Night Light, CLI, and FinalState suites: **PASS**. Deliberate settings and fingerprint
  mismatches were rejected by negative controls, then restored.
- Quality: **1017 tests**, 8/8 acceptance gates, 100/100 mutants, Core 91.0% / App 95.8% /
  CLI 96.8% coverage.
- Residual: the SONY PJ disconnected before its final rerun; no tested display exposed an HDMI
  audio endpoint, so real projector audio switching (H15) remains pending with the other
  real-projector/dock/KVM/AVR rows.

### 2026-11-14 — programmatic runbook, DELL U2718Q stand-in rig, Beam 0.2.0 (commit e60ba4f)

Run driven via `SetDisplayConfig` (topology flags + `DISPLAYCONFIG_PATH_ACTIVE` toggles),
which fires the same `WM_DISPLAYCHANGE` events as Win+P / a cable pull. Recorded
2026-11-15. Evidence excerpts (timestamps from `%LOCALAPPDATA%\Beam\beam.log`):

- **H16 PASS.** `taskkill /f /im explorer.exe`, restart, 10 s wait:
  - `beam.log`: `[23:58:55.262] [tid=  2] [INFO] [Tray] Taskbar recreated - re-registering tray icon`
  - `Get-Process`: exactly one `Beam` process, `Id=12316` (the tray).
- **H17 PASS.** In-process export via `DiagnosticsBundleExporter` →
  `C:\Users\patbaumgartner\OneDrive\Desktop\beam-diagnostics-20261114-235900.zip`:
  - entries: `summary.txt` 381 B, `beam.log` 86736 B, `beam-detailed.jsonl` 132562 B,
    `status.json` 225 B
  - sanitize scan of every extracted entry for `patbaumgartner` and `MYXPS13PLUS`:
    `(no matches)`
  - `summary.txt` head: `Beam 0.2.0 - diagnostics summary` / `Session state: Idle` /
    `Settings: enabled=True mode=True dpi=True nightLight=True keepAwake=True dnd=True audioToHdmi=True ...`
- **H12 partial.** Real disconnect of the U2718Q at 22:39:20 was handled cleanly:
  - `[22:39:20.893] [Monitor] Display change detected sessionState=Idle previousDisplays=2 currentDisplays=1 disconnect=True`
  - `[22:39:21.109] [Monitor] Trigger evaluation triggerMet=False mirrored=False hasMismatch=False externalCount=0 totalPaths=1 evaluationMs=215`
  - no further trigger events afterwards (no storm); session stayed `Idle`.
  - Scripted unplug/replug ×3 could not run: the monitor had dropped out of the CCD
    (`SetDisplayConfig` topology flags rc=31; `target 4165 not found among available
    display paths`; PnP `Status=Unknown`, `DISPLAY\DELA0E9\4&255E004D&7&UID4165`).
    Rerun after a physical power-cycle / re-plug of the U2718Q.
- **H15 partial.** No HDMI/projector audio endpoint exists on this rig (DP only):
  - `[21:58:24.657] [Fix] Extra skipped: no HDMI/projector audio endpoint found step=extras extra=audio endpoint=none previous={0.0.0.00000000}.{b9e68ab1-3509-4c25-97e1-064e36b3bd0b}`
  - `[22:38:02.780] [Restore] Extra restored: default audio endpoint restored step=extras extra=audio endpoint={0.0.0.00000000}.{b9e68ab1-3509-4c25-97e1-064e36b3bd0b} stillExists=False`
  - The actual endpoint switch stays open until a display with an audio endpoint
    (real projector) is attached.
- **Finding (2026-11-14, 22:37 duplicate run).** The fix applied clone + CDS
  `1920x1080@60` to the shared source successfully, then verify-after-mode failed
  250 ms later:
  - `[22:37:56.140] [DisplayMode] CDS apply gdiName=\\.\DISPLAY1 mode=1920x1080@60Hz result=0` /
    `[22:37:56.140] 16:9 mode applied successfully ... appliedMode=1920x1080@60Hz`
  - `[22:37:56.389] [Fix] Topology after mode change topologyId=2 isMirrored=True activePaths=2 ... error="verify-after-mode failed: mirror aspect mismatch still present after apply"`
  - `PresentationManager.cs` re-queries the topology immediately after
    `ApplyCloneWith16x9` returns — no settle delay (the clone apply itself waits
    `settleMs=1500`). Likely a stale-read race. The unwind was clean regardless:
    DEVMODEs `3840x2400@60` / `1920x1080@60` restored, DPI 225% verified, Night Light
    restored, audio endpoint restored, session back to `Idle` (`restore complete`).
  - Also noted: over DP the U2718Q reports a `1920x1080@60` preferred mode; the 16:9
    candidates were `1920x1080@60, 2048x1152@60, 1920x1080@30` (no `3840x2160`), so the
    4K rows (H6/H14) still need a real 4K display.
- **H7** (sleep/wake) still pending a physical wake with the monitor attached.

### 2026-11-27 — repaired production build, DELL U2718Q + SONY PJ

Real Windows production tests on the 16:10 XPS 9320. The original failures were reproduced
before repair: Mirror verification stayed red after a valid source change, the 27-byte default
Night Light value was rejected, and disconnect restore failed while writing the disconnected
external DEVMODE. A SONY PJ also demonstrated that manual 16:9 override cannot require its
original automatic trigger to clear when the projector signal remains 1920x1200.

- **PASS — Mirror Apply/Undo on DELL U2718Q.** Shared source reached 16:9, session reached
  Active, Night Light and DPI were applied, and Undo restored the captured Extend topology,
  source modes, DPI, Night Light, and extras.
- **PASS — automatic duplicate detection.** A forced aspect mismatch produced one automatic
  apply and a valid 16:9 Active session; Undo restored the baseline.
- **PASS — disconnect recovery.** Disabling the external path restored the internal panel and
  cleared the journal. The disconnected target's DEVMODE is now skipped instead of leaving the
  session permanently Active with recovery armed.
- **PASS — Night Light.** Both the default and per-device values turned off and restored
  semantically, including the timestamp-less 27-byte default layout.
- **PASS — real UI and CLI.** Settings controls/persistence, diagnostics export sanitization,
  startup toggle, pause/resume, single-instance behavior, documented CLI commands, JSON,
  reachable exit codes, and final Idle/no-journal state all passed.
- **SONY evidence.** The SONY PJ was disconnected before the final rerun, so this remains
  evidence of the reproduced defect and its regression coverage, not a final SONY hardware pass.
  No HDMI/projector audio endpoint was exposed on either tested rig; actual audio switching is
  still pending suitable hardware (H15).

### 2026-11-15 — rerun after U2718Q re-attach, DELL U2718Q stand-in rig, Beam 0.2.0 (commit 37ec3db)

U2718Q physically re-attached ~09:04 (PnP `Status=OK`, targetId 4165 active again,
`topologyId=4` EXTEND, 2 active paths). Runbook rerun 09:07–09:09 plus a focused flap
test 09:36–09:38, driven via `SetDisplayConfig` (topology flags + `PATH_ACTIVE`
toggles with raw mode indices + `SDC_USE_SUPPLIED_DISPLAY_CONFIG`). Evidence excerpts
(timestamps from `%LOCALAPPDATA%\Beam\beam.log`):

- **H12 PASS (scripted flap cycles).** 3 unplug/replug cycles of target 4165 in EXTEND:
  - `SetDisplayConfig(unplug target=4165, paths=1) rc=0` and
    `SetDisplayConfig(plug target=4165, paths=2) rc=0` (cycle 1; in cycles 2/3 the
    still-connected monitor re-attached by itself after the scripted unplug).
  - Every disconnect and every reconnect produced exactly ONE event pair, e.g.
    - `[09:36:18.231] [tid=  5] [INFO] [Monitor] Display change detected sessionState=Idle previousDisplays=2 currentDisplays=1 disconnect=True`
    - `[09:36:18.369] [tid=  5] [INFO] [Monitor] Trigger evaluation triggerMet=False mirrored=False hasMismatch=False externalCount=0 totalPaths=1 evaluationMs=137`
    - `[09:36:31.371] ... previousDisplays=1 currentDisplays=2 disconnect=False` +
      `[09:36:31.497] ... Trigger evaluation triggerMet=False ... externalCount=1 totalPaths=2 evaluationMs=126`
    - (same pattern for 09:36:40/09:36:52, 09:37:01/09:37:13, and the earlier 09:04:40
      physical re-attach and 09:18:02/09:18:06 flap)
  - No event storm, no fix attempts, session stayed `Idle`, CLI `needsFix=false`
    (`triggerMet=False` is correct in extend). One real duplicate trigger on
    2026-11-14 (18:39, 22:37) produced exactly one fix run per trigger.
- **H16 re-PASS.** `taskkill /f /im explorer.exe` at 09:09:17, restart, 10 s wait:
  `[09:09:34.562] [tid=  2] [INFO] [Tray] Taskbar recreated - re-registering tray icon`;
  `Get-Process Beam` → exactly one process, `Id=12316`.
- **H17 re-PASS.** In-process export →
  `C:\Users\patbaumgartner\OneDrive\Desktop\beam-diagnostics-20261115-090936.zip`:
  entries `summary.txt` 381 B, `beam.log` 87195 B, `beam-detailed.jsonl` 133258 B,
  `status.json` 225 B; sanitize scan for `patbaumgartner` / `MYXPS13PLUS`:
  `(no matches)`.
- **H15 re-confirmed partial.** DP-only rig still has no HDMI/projector audio
  endpoint: `Extra skipped: no HDMI/projector audio endpoint found` (fix) and
  `Extra restored: default audio endpoint restored ... stillExists=False` (restore)
  logged again.
- **Tooling finding.** A pure programmatic topology switch
  (`SetDisplayConfig(SDC_TOPOLOGY_CLONE)`, 09:08:15 and 09:29:33, rc=0) keeps the same
  (adapter, source) IDs, so Beam's `DisplayMonitor` — which fires only when the
  display-ID set changes — logs nothing, while the CLI live view correctly shows
  `mirrored:true, aspectMismatch:true, needsFix:true`. A real Win+P duplicate
  re-enumerates a loopback source and does fire (2026-11-14 18:39/22:37:
  `triggerMet=True mirrored=True ... topologyId=2`). In clone mode the U2718Q is
  driven through a virtual target (50335813), so a scripted "unplug while mirrored"
  via target 4165 is not possible — the mirrored-replug check stays pending a real
  dock/KVM flap while presenting.
- **Verify-after-mode race** from the 2026-11-14 22:37 run above was classified as a
  stale-read race (no settle delay after CDS apply; the clone apply waits
  `settleMs=1500`, the mode apply does not) and filed as
  [issue #1](https://github.com/patbaumgartner/beam-windows/issues/1) on 2026-11-15 —
  fixed the same day (commit `bc6baa3`): the verify-after-mode now settle-polls
  `QueryTopology()` (100 ms × up to 20 attempts) instead of reading once, so a slow OS
  re-enumeration no longer false-fails the verify.
