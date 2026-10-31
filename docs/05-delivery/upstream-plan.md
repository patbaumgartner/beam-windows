# Upstream plan — PowerToys / PowerDisplay

## 1. Objective

Land "projector mode" capabilities in PowerToys, ideally inside **PowerDisplay**, without
duplicating existing ownership or fighting in-flight work.

## 2. Preconditions (from CONTRIBUTING.md)

1. An issue exists and there is agreement on problem, fit and solution.
2. Larger scope requires a spec.
3. Announce intent on the contribution thread (#28769).
4. PR description written by a human.
5. Localization handled by Microsoft.

## 3. Proposed issue (draft)

**Title:** `[PowerDisplay] Projector mode: auto-apply 16:9 + scaling + Night Light off on connect`

**Body (draft):**

> ### Description
> When a laptop with a 16:10 (or 3:2) panel is mirrored to a 16:9 TV or projector,
> Windows drives both displays from one source, so the image is pillarboxed: **black
> bars on the left and right**. If the laptop was at 200%, the switch to 1920x1080 also
> leaves a 960x540 logical desktop. And if Night Light is on, the projected image is
> orange.
>
> PowerDisplay already owns display enumeration, stable monitor identity and hot-plug
> detection. This proposal adds the missing pieces as a "projector mode" scenario:
> 1. detect a new active **external display whose aspect differs** from the internal panel
>    (this covers TVs as well as projectors — no fragile device classification required),
> 2. apply a supported 16:9 mode (policy: match height / native / fixed / highest),
> 3. set the **source** DPI scale so the logical desktop stays readable (the same
>    device-info API the Settings app uses),
> 4. temporarily disable Night Light while projecting,
> 5. restore every changed value exactly on disconnect, with a crash-safe journal.
>
> ### Scenario
> Consultant plugs HDMI into a 1080p projector at 19:30 with Night Light on. Wall shows
> 1920x1080 at 100% with neutral colours within 3 seconds. Unplug restores 3840x2400 @
> 200% and the Night Light schedule state.
>
> ### Relation to existing issues
> Builds on #48286 (display profiles), #47336/#47433 (resolutions), #49659 (Night Light),
> #48470/#51137/#49374 (triggers). Refresh-rate PR #51072 uses the same
> `ChangeDisplaySettingsEx` path; this should be sequenced after it.
>
> ### Scope
> MVP: duplicate **and** extend, one external display, mode + DPI + Night Light +
> restore. Profiles and the optional EDID classifier follow. No topology editing, no
> service, no admin.
>
> ### Risks
> Night Light has no public API; proposal is a strict, snapshot/verify/rollback registry
> write with a gamma fallback and a kill switch. Happy to discuss alternatives.

## 4. Spec outline (if maintainers ask)

1. Goals / non-goals / user stories.
2. Detection: events, debounce, stability, self-suppression, reconcile.
3. Classification: signals, weights, thresholds, overrides, UI question.
4. Mode selection: policies, 16:9 predicate, refresh rules, clone intersection.
5. Scaling: capability ladder, sane-range rule, fallback.
6. Night Light: format, strict parse, snapshot, verify, scope, fallback, kill switch.
7. Transaction: journal, phases, verify, revert, safe mode.
8. Restore: "still ours", order, partial failure.
9. UX: tray, toast, settings, diagnostics, CLI.
10. Data model changes: `ProfileMonitorSetting` fields, `MonitorStateEntry`, settings JSON.
11. Telemetry (PowerToys rules).
12. Test plan: unit, simulation, hardware matrix.
13. Rollout: feature flag, settings default off? (Recommend on.)
14. Open questions.

## 5. PR sequencing

| PR | Content | Why this order |
|---|---|---|
| PR 0 | Issue + spec + design doc in `doc/devdocs/modules/powerdisplay/` | Required before code |
| PR 1 | `DisplayModeService` + profile fields (width/height/refresh) + CLI `set --resolution` | Aligns with #47336/#47433; rebase after #51072 |
| PR 2 | `NightLightService` + setting + CLI + tests | Independent; highest risk; small surface |
| PR 3 | Classifier + trigger engine + "projector mode" settings section | Depends on PR 1 + 2 |
| PR 4 | Docs, localization handoff, telemetry events | Last |

Keep each PR reviewable (< 800 changed lines where possible). Separate refactors from
features.

## 6. Integration mapping

| Beam | PowerDisplay target |
|---|---|
| `IDisplayInventory` | `DisplayConfigInventory` (reuse) |
| `IDisplayModeController` | new `DisplayModeService` (mirror `DisplayRotationService`) |
| `IEdidReader` | new `EdidReader` in `PowerDisplay.Lib/Drivers` |
| `IProjectorClassifier` | new `ProjectorClassifier` + JSON weights |
| `INightLightController` | new `NightLightService` |
| `IStateJournal` | `MonitorStateManager` + `CrashRecovery` |
| `IProfileStore` | `ProfileStore` + `ProfileMonitorSetting` |
| `TriggerEngine` | extend `DisplayChangeWatcher` consumers |
| `NotificationService` | PowerToys toast helpers |
| `ConfigStore` | PowerToys settings JSON |

## 7. Coordination risks

- **PR #51072** (refresh rate) is open and touches `DisplayChangeWatcher`,
  `MonitorManager`, `MonitorStateManager`, `MonitorViewModel`, settings. Wait for merge
  or coordinate a rebase.
- `MonitorStateManager` is being extended; adding fields there will conflict.
- Maintainers may prefer a generic "display profiles + triggers" design over a
  projector-specific feature. Be flexible: the classifier can be one trigger type among
  several ("on display classified as projector").
- Do not add a new module. Do not add a service. Do not add third-party dependencies.

## 8. Fallback if upstream is rejected

- Keep Beam standalone (MIT), publish on GitHub + winget.
- Offer the engine as a library for other tools.
- Re-approach upstream after Windows/PowerToys direction changes.
- Do not fork PowerToys.

## 9. Success criteria

- A maintainer accepts the issue and the spec direction.
- At least PR 1 (modes) is merged.
- Beam's standalone app can be reduced to a thin shim or retired.
