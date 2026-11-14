# Presenter Edition plan (Beam 0.2)

Created 2026-11-13 after a full user + security review of the 0.1.1 codebase
(`src/Beam.Tray`, `Beam.App`, `Beam.Windows`, `Beam.Cli`, installer, `SECURITY.md`)
and validation with the product owner. Answers that drove this plan:

| # | Decision | Answer |
|---|---|---|
| V1 | Primary presenting setup | **Duplicate/mirror** — optimize the fix for it (no extend "presenter view" in 0.2) |
| V2 | Global hotkey (toggle apply/undo) | Nice to have — ship in Phase B, low priority |
| V3 | Glanceable "presenting" signal | **Tray icon state variant + rich tooltip** (no always-on-top panel in 0.2) |
| V4 | Presentation extras (UC-17) | **Keep-awake (default on), DND, audio-to-HDMI** — all three, each toggleable + restored |
| V5 | Dev second screen | **Persisted settings**, `beam status` shows the live tray session, **per-display "Ignore this display"** |
| V6 | UI scope | Polished tray menu **plus a real settings window** |
| V7 | Security scope | **Quick wins first, and start code signing now** |
| V8 | 16:9 mode choice | **Prefer the projector's EDID-native mode**, 60 Hz preferred, 30 Hz fallback |

Goal: **"Plug in. Look right. Every time."** — plus: glance at the laptop corner and
*know* you're presenting; never have the wall screen sleep, pop a notification, or
lose the sound mid-talk; and never re-configure anything after a restart.

---

## Phase A — Security & robustness quick wins (ship first, ~1 week part-time)

Motivation: these remove real races/corruption/PII issues found in review and are
prerequisites for trusting the presenter features on stage.

| # | Task | Files | Notes |
|---|---|---|---|
| A1 | **CLI takes the same single-instance lock.** Mutating commands (`apply`, `restore`, `dpi set`, `nightlight`) acquire `Local\Beam.SingleInstance` with a short timeout before touching display/Night Light/journal; read-only commands (`status`, `devices`, `dpi read`, `help`) stay lock-free. | `src/Beam.Cli/Program.cs`, `CliApp.cs` | Without this, `beam apply` races the tray's fix sequence: two processes calling `SetDisplayConfig`/writing `journal.json` concurrently. |
| A2 | **Atomic journal writes + schema hardening.** Replace `Delete`+`Move` with `File.Move(tmp, path, overwrite: true)` (atomic on NTFS). Add `Version` to `JournalRecord`; `Read()` caps input size (e.g. 256 KB) and ignores unknown/newer versions gracefully. | `src/Beam.App/Journal/JournalStore.cs`, `IJournalStore.cs`, tests | Crash between delete+move currently loses crash-recovery. |
| A3 | **Log hardening.** Rotate `beam.log` / `beam-detailed.jsonl` at 5 MB × 3; escape `\r`/`\n` in the plain-text log (currently only spaces are quoted → log forging via exception text / EDID names); batch appends under the existing lock. | `src/Beam.App/Diagnostics/FileLogSink.cs`, tests | `beam-detailed.jsonl` is already safe (JSON-serialized). |
| A4 | **Less PII in logs.** Drop `machineName`/`userName` from the startup log line (or gate behind a `--verbose` flag). Logs are what users attach to bug reports (SECURITY.md). | `src/Beam.Tray/Program.cs` | Keep OS version + app version. |
| A5 | **Diagnosable single instance.** Write `%LOCALAPPDATA%\Beam\instance.lock` with PID + timestamp; on mutex-squat, log which PID holds it. | `src/Beam.Tray/Program.cs` | Any same-user process can currently pre-create the mutex and silently block Beam. |
| A6 | **No display query on the UI thread.** `TrayService.ShowStatus()` currently constructs `DisplayInventory` and runs `QueryDisplayConfig` on the UI thread — use the monitor's cached snapshot instead. | `src/Beam.Tray/TrayService.cs` | Same class of stall the cursor-guard comments warn about. |
| A7 | **Explicit shell launch.** `ProcessStartInfo { UseShellExecute = true }` for the "Open log folder" explorer launch. | `src/Beam.Tray/TrayService.cs` | Constant path today, but make the intent explicit. |
| A8 | **Start code signing.** Azure Trusted Signing (or a cert) in the release workflow; sign `Beam.exe`, `Beam.Cli.exe`, and the Inno installer; add a CI step that verifies signatures; document SmartScreen expectations; answer O6. | `.github/workflows/release.yml`, `installer/build.ps1`, `docs/05-delivery/open-questions.md` | Unsigned downloads at a venue = SmartScreen friction exactly when a speaker installs. |

**Phase A exit:** all A-items merged, gates green (build 0 warnings, format, tests incl.
new journal/log/mutex tests, architecture, coverage, mutation, links), signing working
on a `v0.2.0` test tag.

---

## Phase B — Presenter core (~2–3 weeks part-time)

| # | Task | Files | UC/ADR |
|---|---|---|---|
| B1 | **State-aware tray icon + rich status.** New `TrayIconRenderer` draws state variants from the brand mark (idle / presenting / paused / safe-mode, e.g. colored badge ring; light- and dark-taskbar readable). Tooltip becomes the full status line: `Presenting — Epson EB-2250U · 1920×1080@60 · 100% · Night Light off`. Balloons only for Active / errors / safe mode, 5 s, warning icon on errors. | `src/Beam.Tray/TrayIconRenderer.cs` (new), `TrayService.cs`, `TrayPresentation.cs` | UC-08 postcondition "tray icon variant"; V3 |
| B2 | **Prefer EDID-native 16:9 mode.** Extend `DisplayTarget` with `NativeMode` (from `QueryDisplayConfig` target preferred mode / EDID detailed timing); change `DisplayModeController.Rank16x9` to rank *native 16:9 first*, then highest pixels, 60 Hz over 30 Hz; keep `MaxCandidates` bound + verify-after-apply. | `DisplayTopology.cs` (`DisplayTarget.NativeMode`), `DisplayInventory.cs`, `DisplayModeController.cs`, property tests | V8; new ADR for the ranking change |
| B3 | **Presentation extras (all journaled, all restored, failures never fail the session):** keep-awake via `SetThreadExecutionState(ES_DISPLAY_REQUIRED\|ES_CONTINUOUS)` while Active (default **on**); DND via snapshot/restore of the per-user notification setting (default on, best-effort); audio → HDMI default endpoint via `IMMDeviceEnumerator`, snapshot previous default, restore on disconnect only if it still exists (default on). Each behind a Core port (`IKeepAwake`, `IDndController`, `IAudioController`), adapters in `Beam.Windows`, orchestration + journal entries in `PresentationManager`. | Core `Ports/ISystemPorts.cs`, new `Beam.Windows/*` adapters, `PresentationManager.cs`, `JournalStore.cs` | UC-17 (V4); ADR-0017-extras |
| B4 | **Persisted settings.** New versioned `settings.json` at `%LOCALAPPDATA%\Beam` (atomic write, unknown keys preserved, corrupt → `.bad` + defaults + one toast). Moves `IsEnabled/UseMode/UseDpi/UseNightLight` plus extras toggles, ignored-display list, hotkey into it. `PresentationManager` loads/saves through a Core port. | `src/Beam.App/Settings/SettingsStore.cs` (new), `Core/Ports`, `Program.cs` (tray) | UC-15; ADR-0009; V5 |
| B5 | **"Ignore this display" (per-device).** Tray item enabled while an external display is present; stores `DisplayTarget.Identity` in settings; trigger evaluation skips ignored identities. | `TrayService.cs`, `TriggerCondition.cs`/`PresentationManager.cs`, settings | UC-08, UC-10 subset; V5 |
| B6 | **`beam status` reflects the live tray.** Tray writes an atomic `status.json` (state, applied mode/DPI/Night Light, timestamp, PID) on every transition; CLI `status` merges it in (`session` block) when present. | new `Beam.App` `StatusWriter`, `TrayService`/`Program.cs`, `CliApp.Status` | UC-16; V5 |
| B7 | **Global hotkey (nice-to-have).** Default `Win+Alt+P`, configurable, `RegisterHotKey`; toggle apply/undo; registration failure → warning in settings, never blocks startup. | `TrayService.cs` + Core port | UC-08 AC5; V2 |

**Phase B exit:** plug in a projector → icon visibly flips to "presenting", tooltip shows
what was applied, wall screen never sleeps / no popups / sound on projector, unplug →
exact restore; settings survive restart; dev can ignore their office monitor; `beam status`
tells the truth about the running tray. Verified on real hardware per the matrix in
`test-strategy.md`.

---

## Phase C — UI: settings window + polish (~3 weeks part-time)

| # | Task | Notes |
|---|---|---|
| C1 | **Settings window** (WPF inside the tray exe — keeps single-exe per-user install and the Inno installer simple; WinUI 3 would need MSIX packaging): General (enable, start with Windows, pause 15 min/1 h/indefinite), Projector mode (mode policy incl. "prefer native 16:9", DPI target, Night Light, restore on disconnect), Extras (awake/DND/audio), Devices (table: name, identity, auto-apply / ignore / forget), Advanced (diagnostics, export settings, reset to defaults, open log folder, safe-mode indicator + reset). Changes apply immediately, debounced persist. | UC-15; V6 |
| C2 | **Tray menu polish:** owner-drawn items with icons, shortcut hints, section headers; rich status header (from B1); "Ignore this display"; "Pause for 1 h" next to indefinite pause. | UC-08 |
| C3 | **First-run toast once** (UC-15): "Beam is running. Plug in a projector — I'll handle the rest." | UC-15 |
| C4 | **Diagnostics window (minimal UC-14):** live timeline ring buffer (200 events), "why didn't it trigger?" reasons, copy-summary, export bundle (zip to Desktop, **no machine/user names**), open log folder, re-evaluate now, reset safe mode. | UC-14 + A4 privacy |
| C5 | **Accessibility & localization pass:** keyboard navigation, UIA names on the tray icon/menu, high-contrast-safe icon variants, all strings externalized (start `en`, structure for loc). Explorer-restart re-registration (`TaskbarCreated`). | UC-08/15 ACs; roadmap 2.7 |

---

## Phase D — Release hardening (~1 week part-time)

- Sign and publish `v0.2.0` (installer + binaries), verify in CI; winget manifest as a follow-up.
- Hardware matrix runs: user's projector(s) incl. 30 Hz-only rooms, USB-C dock / KVM flapping (UC-13), 4K vs 1080p beamers (B2).
- Docs: README features/usage, `SECURITY.md` (PII change in A4, signing, new attack-surface notes),
  roadmap status, answer O1/O6 in `open-questions.md`, CHANGELOG 0.2.0, new ADRs (native-mode
  ranking, extras, settings store, signing).

---

## Standing rules for all phases

- **Layering unchanged:** Core = pure domain + ports; App = orchestration/journal/settings,
  no P/Invoke; Windows = thin adapters; Tray/CLI = composition roots. Architecture tests
  enforce it; every new capability goes through a Core port so the App layer stays
  fake-testable.
- **Safety invariants unchanged:** snapshot-before-mutate, verify-after-write, bounded
  candidates, self-change suppression, safe mode after 2 failures, crash recovery via
  journal — extras (B3) must obey "failure never fails the session" (UC-17 AC3).
- **Gates per PR:** build (0 warnings), `dotnet format --verify-no-changes`, tests
  (574+ growing), architecture tests, coverage (Core ≥85 %/App ≥80 %/CLI ≥75 %),
  mutation (0 survivors), link check. Manual hardware verification for anything touching
  displays/audio.
- **Privacy:** no network, no admin, no telemetry (ADR-0010); logs and export bundles
  carry no machine/user names (A4, C4).

## Estimate

Part-time (~15 h/week): A ≈ 1 wk, B ≈ 2–3 wks, C ≈ 3 wks, D ≈ 1 wk → **0.2 "Presenter
Edition" in ~7–8 weeks**, shippable incrementally behind the signed-installer pipeline.
