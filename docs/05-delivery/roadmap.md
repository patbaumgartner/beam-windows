# Roadmap

Estimates assume one developer, part-time (approx. 15 h/week), and include tests.

## Phase 0 — De-risk (2 weeks)

| # | Task | Output | Gate |
|---|---|---|---|
| 0.1 | S1 Night Light write/restore | spike note, parser prototype | **DONE 2026-10-29** — 50/50 verified, semantic restore |
| 0.2 | S5 detection events | instrumentation, timings | p95 <= 1.5 s, 0 missed |
| 0.3 | S3/S6 modes/duplicate | mode matrix on real hardware, bar check | **DONE 2026-10-30** — 16:9 shared source applied + persisted on DELL U2718Q |
| 0.4 | S4 DPI `-3`/`-4` | capability report on this laptop | **DONE 2026-10-30** — read verified; set pending (gated `--yes`) |
| 0.5 | S6 safety loop | harness skeleton | Phase 1, task 1.14 |
| 0.6 | S7 Clone detector + topology id | correct clone/extend/mirror detection | **DONE 2026-10-30** — QDC_DATABASE_CURRENT topology id, fixed TOPOLOGY_* constants |
| 0.7 | S8 HDR interaction | rule for Night Light under HDR | documented behaviour |
| 0.8 | S9 upstream soundings | issue comments, feedback | direction acceptable or not |
| 0.9 | S10 classifier (optional) | EDID corpus, scorer | only needed if `onlyProjectors` ships |

**Exit:** go/no-go per capability; ADRs updated; no production UI yet.

## Phase 1 — MVP (4–5 weeks)

Scope: duplicate **and** extend, one external display, mode + DPI + Night Light +
restore + safety + tray.

| # | Task | UC | Status |
|---|---|---|---|
| 1.1 | Solution skeleton, DI, logging, single instance | — | |
| 1.2 | `DisplayInventory` + stable ids + snapshot diff | UC-01 | **code-complete** (S3/S5/S7) |
| 1.3 | Presentation trigger (external + aspect mismatch) | UC-02 | **code-complete** `PresentationManager.IsTriggerConditionMet` + monitor |
| 1.4 | `ModePlanner` + `DisplayModeController` (test/apply/verify) | UC-03 | **code-complete** (S6 + DisplayModeController) |
| 1.5 | Clone planner + shared-source apply + verify all targets | UC-04 | **code-complete** (S6) — real-hardware PASS |
| 1.6 | `DpiController` (`-3`/`-4`) + `ScalePlanner` + verify | UC-05 | **DONE** — read+planner PASS (S4); set path implemented (`DpiController.SetPercentageAndVerify`, wired into `PresentationManager.ApplyDpi` + CLI `dpi set`) |
| 1.7 | `NightLightController` (parse/write/verify/snapshot) | UC-06 | **PASS** (S1) + wired into Beam.Tray |
| 1.8 | Journal + `SessionManager` state machine | UC-11 | **code-complete** `SessionManager` in Beam.Tray |
| 1.9 | Restore planner + "still ours" (mode, DPI, bytes) | UC-07 | **DONE** — mode+NightLight restore code-complete; **DPI restore implemented** (capture original DPI before apply, restore on unplug/recovery, persisted in journal for crash recovery) |
| 1.10 | Mutation gate + guardrails + safe mode | UC-12 | **DONE** — SemaphoreSlim gate, CDS_TEST, snapshot-before-mutate, verify-after-mutate, bounded attempts, self-change suppression, safe mode after 2 failures; **safe mode surfaced in tray** with warning balloon (UC-12 AC5) | |
| 1.11 | Tray icon, menu, hotkey, pause | UC-08 | **DONE 2026-10-31** — WinForms MVP: responsive menu, Pause/Resume, Apply/Restore, Launch at sign-in. **2026-11-01:** laser-beam tray/exe icon in Pantone 638C + white (light/dark readable), startup toggle now matches the installer's `--hidden` value and gives balloon feedback, pause/resume label stays in sync with session state, sign-in checkbox re-read on menu open |
| 1.12 | Toast + undo | UC-09 | **DONE (undo only)** — "Undo projector mode" tray menu item reverses an active fix (restores mode + DPI + Night Light), enabled whenever a fix is active; wired to `RestoreNowAsync`. Toast notifications skipped per product decision. | |
| 1.13 | Minimal settings (enable, policy, night light, startup) | UC-15 (partial) | **DONE** — "Enable Beam" master switch, "Night Light" manual on/off toggle, "Policy" submenu (control display mode / DPI / Night Light), plus existing "Launch at sign-in". `PresentationManager.IsEnabled`, `UseMode`, `UseDpi`, `UseNightLight` + `SetNightLight(bool)` gating the fix sequence per policy. | |
| 1.14 | Simulation test suite + S6 run | — | **DONE 2026-11-08** — `tests/Beam.App.Tests/SoakTests.cs` (500-cycle soak, 0 black screens, 500/500 valid end states; UC-12 random-fault property; unplug 20/20; ≤1 apply per attach; kill-at-20-points) + `tests/Beam.Windows.Tests/DisplayModeControllerPropertyTests.cs` (UC-12 property) + extended `DisplayMonitorStressTests.cs` (UC-01 AC2/AC3, S5 p95) | |

**Exit criteria**
- Duplicate a 1080p projector from the 3840x2400 @ 200% laptop: 1920x1080 @ 100%, **no
  black bars on the projector**, Night Light off, toast, undo.
- Extend to the same projector: projector at 1920x1080, laptop untouched.
- Unplug: exact restore of mode + scale + DPI + Night Light bytes, verified 20/20.
- Kill -9 at 20 points: next start always valid.
- 500-cycle S6 clean.
- Idle CPU < 0.1%.

## Phase 2 — Solid (4–5 weeks)

| # | Task | UC |
|---|---|---|
| 2.1 | Profiles: auto-learn, edit, import/export | UC-10 |
| 2.2 | Optional projector classifier (EDID scoring, `onlyProjectors`) | UC-02 refinement |
| 2.3 | Docks, KVM, AVR, multi-display, flapping | UC-13 |
| 2.4 | Diagnostics window + export bundle | UC-14 |
| 2.5 | Full settings + onboarding | UC-15 |
| 2.6 | Packaging (MSIX), startup task, uninstall | — |
| 2.7 | Accessibility + localization pass | — |
| 2.8 | Hardware matrix (community testers) | — |

**Exit:** beta-ready; no known data-loss or display-breaking bug; docs complete.

## Phase 3 — Best-in-class (3–4 weeks)

| # | Task | UC |
|---|---|---|
| 3.1 | CLI + JSON + exit codes | UC-16 | **DONE 2026-10-31** — `Beam.Cli`: status/devices/apply/restore/dpi/nightlight + `--json` + exit codes, installer + CI packaged |
| 3.2 | Command Palette extension | UC-16 | |
| 3.3 | Presentation extras: keep awake, DND, audio (opt-in) | UC-17 |
| 3.4 | Performance/robustness hardening | — |
| 3.5 | Signing + SmartScreen + release pipeline | **DONE 2026-10-31** — Inno Setup installer + GitHub Actions CI snapshot + release on tag |

## Phase 4 — Upstream (parallel, ongoing)

| # | Task |
|---|---|
| 4.1 | Issue + spec in PowerToys (see `upstream-plan.md`) |
| 4.2 | Rebase on PR #51072 (refresh rate) |
| 4.3 | PR 1: display mode support in PowerDisplay (service + profile fields + CLI) |
| 4.4 | PR 2: Night Light service + setting + CLI |
| 4.5 | PR 3: projector classification + trigger |
| 4.6 | Docs + localization handoff |

## Milestones

| Milestone | Date (relative) | Definition |
|---|---|---|
| M0 Spikes done | +2 weeks | go/no-go recorded |
| M1 Private alpha | +8 weeks | MVP on the author's laptop (duplicate + extend) |
| M2 Public beta | +13 weeks | Phase 2, packaged, 10 testers |
| M3 1.0 | +17 weeks | Phase 3 core, signed |
| M4 Upstream PRs | +10 weeks onward | first PR merged or in review |

## Definition of done (global)

- Acceptance criteria of the UC met.
- Unit + simulation tests; no analyzer warnings.
- Diagnostics reason emitted.
- Strings localized.
- Docs (UC/ADR/arc42) updated.
- Manual verification on real hardware for anything that touches displays.

## Explicitly deferred

- Topology editing (enable/disable/arrange/primary).
- HDR-aware tone mapping.
- Audio routing as a default (only opt-in extra).
- Cloud sync, fleet management, GPO.
- Windows 10.
- macOS/Linux.
