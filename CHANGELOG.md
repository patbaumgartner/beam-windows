# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Current version is **0.3.1** (per `installer/build.ps1` default).

## [Unreleased]

### Changed
- **Presentation ownership is explicit.** While the tray is running, direct `beam apply` and
  `beam restore` are refused with a clear instruction to use tray Apply/Undo; the two processes
  can no longer independently own and restore the same presentation. Standalone CLI mutations
  now verify the settled topology, roll back partial failures, and persist original mode data.
- **Manual 16:9 override verification matches its purpose.** Apply verifies that the owned
  source reached 16:9; only automatic apply additionally requires the external mismatch trigger
  to clear. Internal-panel target signals no longer invalidate a correct projector mode.

### Fixed
- **Night Light works on current Windows 11 values.** The codec accepts the captured 27-byte
  default off/schedule layout with no FILETIME field, while retaining strict validation and
  semantic restore. Restoration also preserves a newer user/schedule change.
- **Disconnect and crash recovery are reliable.** Disconnected DEVMODE targets are skipped,
  applying-state disconnects cannot finish Active, and owned-target identity—not display count—
  drives restoration. Journals now persist original source modes and whether mode mutation began,
  so pre-mode crashes do not change topology and standalone restores do not lose source modes.
- **Restore does not undo unrelated user choices.** No-journal/inactive-journal restore leaves
  topology alone. Inactive DPI sources are skipped, external-only displays are not mistaken for
  simulation, Auto-apply exclusions are honored during verification, and mode/DPI/Night Light/
  notification/audio restoration preserves newer user changes. Audio is never switched without
  a captured inverse.
- **Topology and status edge cases.** Shared-desktop duplicate detection works when the topology
  ID is unavailable, and stale `status.json` records are ignored unless the recorded Beam process
  is actually live.
- **Raw production logs no longer include the user-profile path** from startup or Open Log Folder.
- **Production verification added:** repeatable hardware, CLI/Night Light, and real-UI suites.
  Current suite: **1017 tests, 0 failed**. Real DELL U2718Q Mirror apply/Undo, automatic trigger,
  and disconnect recovery pass; real Settings/Diagnostics controls and sanitized exports pass.

## [0.3.1] — 2026-11-25

### Changed
- **Maintenance release of the 0.3.0 code line** — refreshed installer, portable ZIP and
  `SHA256SUMS.txt` under a new tag so the published artifacts match the current sources.
  No product changes since 0.3.0; see §0.3.0 for the feature and fix list.

### Fixed
- **Stabilized `Capture_AlwaysThrows_NeverRaisesChanged_KeepsPolling` under CI load** — the test
  asserted more than three 10 ms poll ticks inside a fixed 150 ms window; Windows' ~15.6 ms
  timer resolution and a busy runner delivered only two ticks on the `v0.3.0` Debug leg.
  It now waits for the polling condition with a bounded deadline and reads the counters
  with `Interlocked`/`Volatile`, so a genuinely dead loop still fails.

## [0.3.0] — 2026-11-23

### Added
- **Extend-to-projector option** — a new persistent setting (`ProjectorTopology` in `%LOCALAPPDATA%\Beam\settings.json`, shown as "Projector screen" in Settings) chooses between **Mirror** (default) and **Extend** when Beam applies presentation mode; unknown or missing values fall back to Mirror. Extend is applied via `SetDisplayConfig` with `SDC_TOPOLOGY_EXTEND`, putting the best 16:9 mode on the first connected external display.
- **Simulated presentation mode for manual apply** — clicking "Apply projector mode now" in the tray (or pressing the hotkey) with no external monitor connected no longer fails and restores immediately; Beam now simulates presentation mode (stays Active: Night Light off, DPI applied, presentation extras on), and undo restores the previous state. Automatic triggers on display changes are unchanged and still require a real projector.
- **Self-contained distributions** — the installer and portable ZIP now include the complete `win-x64` runtime, so Beam works on clean machines without installing .NET. Packages include the MIT license and third-party attribution; publish outputs are freshly staged and checked for expected executables, runtime files, and matching versions.
- **A real `beam` command** — both distributions include `beam.cmd`, forwarding to `Beam.Cli.exe` with its exact exit code. The installer adds/removes only Beam's per-user PATH entry when the CLI component is selected, and portable users can run `.\beam.cmd` from the extracted folder.

### Changed
- **Win+P stays with Windows** — the projector flyout is Windows' own shortcut, so Beam never registers it: `Win+P` (any casing/modifier spelling of just the Windows key + P) is rejected as a configurable Beam hotkey and falls back to the default **Win+Alt+P**.
- **Release provenance and versioning are enforced** — manual release dispatch checks out the requested tag and verifies its commit before building; stable releases run restore, formatting, build, tests, coverage, mutation, and link gates. Version overrides now flow into assembly/file/product metadata. The SDK stays on its pinned feature band while allowing security patch updates.
- Version bumped to **0.3.0** in `Directory.Build.props`; `installer/Beam.iss`, the release workflow default tag and the bug-report template placeholder all point at 0.3.0.

### Fixed
- **Verify-after-mode no longer reads a stale topology** ([issue #1](https://github.com/patbaumgartner/beam-windows/issues/1)).
  `ChangeDisplaySettingsEx` returns before Windows has re-enumerated the display paths, so the
  single immediate post-apply `QueryTopology()` could false-fail the verify
  ("verify-after-mode failed") even though the apply succeeded — observed live on 2026-11-14
  (CDS result=0 at 22:37:56.140, stale verify failure at 22:37:56.389). `PresentationManager`
  now settle-polls the topology (100 ms interval, up to 20 attempts ≈ 2 s) until the trigger
  condition clears, and still fails (and unwinds) when the mismatch is genuinely still present
  after the window. Test suite: **826 tests** (was 794 at 0.2.0; App 207, Core 312, Cli 92).
- **Settings saves no longer race** — observed live on 2026-11-16 at 16:39:15: a projector
  connect-burst made the display-change handler and the startup check both record the same
  external display 3 ms apart, and both `Save` calls wrote the same `settings.json.tmp`, so one
  save was lost with "file in use by another process". `SettingsStore.Save` is now serialized
  (lock around tmp-write + rename), and the known-devices check-then-act in
  `PresentationManager` (`RecordKnownDevices` / `SetDeviceAutoApply` / `ForgetDevice`) is guarded
  by a lock, so a device is recorded exactly once per connect burst.
- **All Settings values now survive a real restart.** The on-disk store previously omitted
  the pause preset, restore-on-disconnect, fixed DPI target, prefer-native mode, known-device
  profiles, and first-run toast flag from both serialization and deserialization. In-memory
  tests hid the gap; a full `SettingsStore` round-trip now covers every one of these fields.

- **CLI restore failures are now truthful and retryable.** `beam restore` returns exit code 1
  when display mode, DPI, or Night Light restoration fails (matching its JSON `success` field),
  and keeps the recovery journal instead of deleting the only retry state. Successful restores
  still clear it.
- **Tray restore failures remain visible and retryable.** Display-mode, DPI, and Night Light
  outcomes now gate restore completion. A failed Undo returns to Active with its original
  snapshots and journal intact instead of reporting Idle and discarding them; a later Undo can
  retry, and only a fully verified restore clears recovery state and returns to Idle.
- **Windows mode restore no longer reports rejected writes as success.** A failed CDS apply now
  tries the remaining refresh-rate fallbacks, every display is attempted, and failed snapshots
  remain available for retry. `DisplayModeController.Restore` returns an error instead of
  clearing those snapshots and claiming completion.
- **Undo now restores only what Beam actually changed.** Simulated presentation no longer
  switches topology on Undo; disabled mode/DPI/Night Light policies no longer cause restoration
  writes; and a failure before the first mutation no longer invokes a blind display restore.
- **A recovery journal is now mandatory before mutation.** Journal writes are serialized,
  flushed to disk, and surfaced on failure instead of silently swallowed. Tray and CLI apply
  abort before changing any setting when persistence fails; the CLI reports a structured JSON
  error when requested, and an existing journal is preserved for recovery.
- **Display-only changes are now detected.** The monitor compares topology, source/target
  modes, DPI, target identity, and desktop position instead of only adapter/source IDs. Mode,
  resolution, same-source target replacement, and Extend ↔ Duplicate changes now raise an
  event; identical polls remain silent. A failed startup capture also publishes its first
  successful baseline through `Current`.
- **Extend mode now changes projector DPI, not laptop DPI.** Source selection is shared by
  pre-apply capture, DPI application, live status, journalling, and restore. The external source
  is used in Extend even when the internal panel is enumerated first; Mirror and single-display
  simulated presentation retain their previous source selection.
- **Native display identification is corrected.** USB-C DisplayPort tunnel targets are external,
  LVDS/embedded DisplayPort/embedded UDI panels are internal, EDID manufacturer words are
  byte-swapped and validity-gated, rotation names match Windows, and current DPI percentages
  are resolved into monitor snapshots. Multi-GPU paths now group by `(AdapterId, SourceId)` so
  identical source numbers on unrelated adapters cannot form a false clone or aspect mismatch.
- **HDMI audio switching now uses the correct COM ABI.** The adapter uses `CPolicyConfigClient`
  and the real `IPolicyConfig` IID/full vtable, proper 20-byte `PROPERTYKEY` values, and
  `PropVariantClear`. Switch/restore verify the default endpoint by reading it back, and stale
  inactive endpoints are not restored. Reflection tests pin the ABI without changing audio on
  the test machine.
- **Presentation operations no longer block the tray UI.** Public apply/restore/re-evaluate/
  enable/Night Light operations enter through the default task scheduler before any native call.
  Display events are chained in order (and `PendingTask` covers the whole chain), disconnects
  mark Restoring before dispatch, and safe-mode icon/menu updates marshal entirely to the UI
  context instead of updating WinForms state from a worker.
- **Pause/resume now reconciles reality.** A real presentation disconnected while paused is
  restored on manual or timed resume without a second event; a projector connected while paused
  or disabled is evaluated on resume/re-enable. Simulated no-projector sessions stay active,
  disabling while paused-active still restores, indefinite pause cancels stale timers, and pause
  is deferred during Applying/Restoring. The tray's primary Pause action honors the configured
  15-minute/1-hour/indefinite preset. Public operations now run in FIFO invocation order, so an
  immediate Apply → Restore sequence cannot let Restore evaluate the pre-Apply Idle state.
- **CLI commands work while the tray is running.** The tray's lifetime singleton is now separate
  from a short-lived named operation semaphore shared by both processes. Mutations still
  serialize across tray/CLI (with async-safe cross-thread release), but the tray no longer makes
  every mutating CLI command fail. `apply --dry-run` takes no lock. A real integration check with
  the singleton held reaches the expected no-projector result instead of a lock error.
- **Settings and device controls now work end to end.** Changing the hotkey immediately
  unregisters/re-registers it; Devices Auto-apply clicks update the manager; ignored devices stay
  visible and checked so they can be unignored. Live device/safe-mode changes update the window,
  Automatic DPI can be unchecked, reset restores Auto-apply and combo state, and closed windows
  unsubscribe instead of leaking.
- **Exit now shuts down safely.** Tray exit stops display input/timers, drains queued and running
  operations, and best-effort restores active or paused-active presentation before disposing
  adapters. Repeated shutdown is idempotent; failed restoration leaves the journal armed for
  next-start recovery instead of disappearing into a half-disposed pipeline.
- **CLI validation and JSON failures are consistent.** Command-specific flags/arity and Windows
  DPI ladder values are checked before locks or hardware access. Option-looking `--device` values
  are rejected, unsupported DPI reads return exit code 4, and parser/usage/device/unexpected
  failures emit one structured `success:false` JSON object whenever `--json` was requested.
- **Quality gates now fail closed.** All projects treat warnings as errors; missing gated
  coverage fails; broken mutation baselines and stale/missing/empty reports fail instead of
  producing a false pass. Mutation child output drains concurrently, and CI actually runs 50
  mutants each for Core and App (with snapshots and stable releases gated on the result).
- **Core parsing edges are safe.** Lowercase F-key hotkeys now work like their uppercase forms,
  and a Night Light epoch too close to `uint.MaxValue` throws instead of wrapping into a stale
  value Windows would reject.
- **Settings saves preserve concurrent changes.** Snapshot creation and store writes are now
  serialized in the same order, so a delayed older save cannot overwrite a newer setting.
- **Failed DPI verification rolls back.** The adapter uses freshly read scale metadata and, when
  the target cannot be verified, writes and verifies the original relative step instead of
  leaving an unverified scale behind. No-op targets do not write or sleep.
- **Mutation-identified test gaps are closed.** Timeline capacity-one/reuse, fractional refresh
  formatting, leading log control characters, tiny thread IDs, and repeated rotation now have
  explicit behavioral coverage. Capacity-only `StringBuilder` arithmetic was removed.

Test suite: **983 tests, 0 failed** (Architecture 11, Core 351, Cli 109, Windows 95, App 256,
Tray 161) — was 826 at the issue-#1 fix.

## [0.2.0] — 2026-11-14

**Presenter Edition** (plan: [docs/05-delivery/presenter-edition-plan.md](docs/05-delivery/presenter-edition-plan.md), Phases B–D).

### Added
- **Live status file** `%LOCALAPPDATA%\Beam\status.json` (atomic tmp+rename, 64 KiB read cap, never throws) plus a rich, length-capped tray status header; `beam status --json` merges a camelCase `session` block read from the running tray (B1/B6).
- **"Prefer native 16:9" mode policy** — the EDID-native 16:9 mode is ranked first by default and the choice is switchable in Settings; ranking is pixels → 60 Hz over 30 Hz, always bounded (B2/C1, [ADR-0018](docs/04-adr/ADR-0018-native-mode-ranking.md)).
- **Presentation extras**, default on and individually switchable: keep the display awake (`SetThreadExecutionState`), silence notification toasts (HKCU Focus Assist DWORD, snapshotted and restored), switch default audio to the HDMI/projector endpoint via `IPolicyConfig.SetDefaultEndpoint` obtained by casting the `MMDeviceEnumerator` coclass (B3, [ADR-0017](docs/04-adr/ADR-0017-presentation-extras.md)). Extras never fail the session (UC-17 AC3) and their state is crash-journaled.
- **Persisted settings** at `%LOCALAPPDATA%\Beam\settings.json` — schema v1, atomic tmp+rename writes, unknown-key preservation, corrupt files set aside as `*.bad` with a single toast (B4, [ADR-0019](docs/04-adr/ADR-0019-settings-store.md)).
- **Ignore-display** exclusions and a known-devices table (auto-apply / ignore / forget) gating the trigger (B5/C1).
- **Global hotkey** (default `Win+Alt+P`) to apply/restore projector mode from anywhere (B7).
- **Settings window** (WPF inside the single `Beam.exe` — no MSIX packaging needed): General (enable, hotkey, pause preset 15 min / 1 h / indefinite), Projector mode (mode policy incl. prefer-native, fixed DPI target, Night Light, restore-on-disconnect), Extras, Devices, Advanced (export settings, open log folder, safe-mode indicator + reset). Changes apply immediately with debounced persist (C1).
- **Diagnostics window**: live 200-event timeline ring buffer, copy-summary, **export bundle** (zip written to the Desktop, sanitized — machine/user names redacted, A4), re-evaluate now, reset safe mode (C4).
- **Tray menu polish**: timed-pause items ("Pause for 15 min" / "Pause for 1 h" next to the indefinite pause), shortcut hint on the Apply item, richer status header (C2); **first-run toast** shown exactly once (C3).
- **Accessibility & localization pass**: all tray/window strings externalized (`TrayStrings`, structure ready for loc), UIA names on icon and menu, `TaskbarCreated` re-registration after Explorer restarts (C5).
- **Distribution without signing** (A8/Phase D, [ADR-0016](docs/04-adr/ADR-0016-distribution.md)): no code signing, permanently. Every push to `main` uploads installer + `beam-<version>-portable.zip` + `SHA256SUMS.txt` to the rolling `snapshot` GitHub Release; every `v*` tag publishes the same three files to its release — all built by the GH workflows. SmartScreen "unknown publisher" is documented; verify downloads against `SHA256SUMS.txt`.
- **794 tests** (was 574 at 0.1.x): Architecture 11, Core 306, App 199, Windows 59, Tray 145, Cli 74 — including the Phase B/C property, settings round-trip, diagnostics-sanitization and tray view-model suites.

### Changed
- Version bumped to **0.2.0** in `Directory.Build.props`; `installer/build.ps1` default, `installer/Beam.iss`, the release workflow default tag and the bug-report template placeholder all point at 0.2.0.

## [0.1.1] — 2026-11-07

### Fixed
- **The installer no longer overwrites the tray with the CLI.** The CLI published a
  lowercase `beam.exe` and the tray `Beam.exe`; on Windows' case-insensitive filesystem,
  installing both into one directory silently replaced the tray app with the CLI, so
  `Beam.exe` was really the command-line tool and never started the tray. The CLI now
  publishes as `Beam.Cli.exe` (invoked as `beam`), and an architecture test guards the
  two composition-root outputs against case-folding collisions.

### Changed
- Version bumped to **0.1.1** so the fixed installer is distinguishable from the 0.1.0
  build that shipped the sticky-cursor bug. `Beam.exe` now reports `appVersion=0.1.1.0`.

## [Unreleased]

### Fixed
- **The tray icon now actually shows the new cyan high-beam mark.** The embedded
  `high-beam.png` was a black silhouette that `TrayService` recoloured at draw time with
  a GDI+ `ImageAttributes` colour matrix — but `System.Drawing.Common` silently ignores
  colour matrices on `DrawImage`, so the tray (and the exe icon) rendered the old black
  logo. All rasters (`app.ico`, `high-beam.png`, `assets/beam-256.png`) are now generated
  pre-coloured in Pantone 638C from `high-beam.svg` by `installer/generate-icon.ps1`, and
  `DrawBeamIcon` draws the mark as rendered. New `TrayIconTests` pin the cyan fill and
  halo in the embedded assets and in the drawn icon at 48/64/96 px, so a black icon can
  never ship again.
- **DPI step parser crashed on driver-supplied extremes.** `DpiScale.From` called
  `Math.Abs` on `minRel`, which throws `OverflowException` for `int.MinValue`, and
  computed `minStep + max` in `int`, which can overflow to a negative ladder index and
  throw `IndexOutOfRangeException`. Both now reject the impossible input and return
  null — the fix sequence degrades gracefully instead of crashing.
- **The tray app could crash at boot when the display config was in flux.**
  `DisplayMonitor.Start()` captured the initial snapshot without guarding against
  transient Win32 failures (dock enumerating, driver hiccup). It now starts with no
  baseline and lets the poll loop establish one, so a failed first capture no longer
  kills startup.
- **The context menu no longer looks hung.** Opening the tray menu a second time could leave it
  wearing the shell's busy cursor: WinForms registers its window classes with a null class
  cursor, so once a wait cursor latched (transient UI-thread stall, shell contention) nothing
  cleared it while the pointer was over the menu — the app looked frozen even though its message
  loop was answering in ~1 ms. The drop-down now pins the arrow cursor when it opens and
  re-asserts it for as long as it is displayed.
- **Opening the menu no longer reads the registry on the UI thread.** `RefreshMenuState` called
  `IsStartupEnabled()` inline from `ContextMenuStrip.Opening`, so a slow hive read (profile load,
  AV filter, concurrent writer) stalled the menu while it was being shown. The menu now uses a
  cached value that is refreshed on the thread pool and published back to the UI thread; an
  in-flight read is discarded if the user toggles the setting meanwhile.
- **State-change balloons no longer fire while the menu is open.** Tooltip and balloon updates
  driven by background `StateChanged` events are deferred until the menu closes, so the tray no
  longer re-enters `Shell_NotifyIcon` from inside the drop-down's modal message loop.
- **Launch at sign-in now round-trips with the installer.** `IsStartupEnabled` compared the
  HKCU `Run` value to the bare `"…\Beam.exe"` form, but the installer writes
  `"…\Beam.exe" --hidden` — so after an installer install the tray checkbox showed
  unchecked and toggling it created a conflicting entry. Matching now accepts both forms
  (new `StartupCommand` helper, unit tested).
- **Launch at sign-in gives feedback.** The toggle now shows a confirmation balloon on
  success and a warning (with checkbox revert) on failure, and the check state is
  re-read from the registry every time the menu opens.
- **Pause/Resume label always matches state.** The menu item now derives its text from the
  session state (in `ApplyStateToUi` and on menu open), instead of only being updated in
  the click handler — no more stale "Pause Beam" when already paused.
- **Single-instance and startup initiation**: `ToggleStartup` no longer resolves a null
  executable path silently; it is reported and aborted with feedback.
- **The tray project builds again.** Five stale pre-refactor source files
  (`SessionManager`, `PresentationManager`, `JournalStore`, `StartupCommand`,
  `DisplayModeController`) were left in `Beam.Tray` alongside the real modules in
  `Beam.App`/`Beam.Core`/`Beam.Windows`; the SDK compiles every `.cs`, so the duplicates
  broke the build and shadowed the shared `SessionManager` with a second, unrelated
  instance. They are removed and `Program.cs` now wires the `Beam.Windows` adapters
  directly.

### Changed
- **New high-beam brand icon, now a scalable SVG** (tray + exe + installer): the Noun
  Project "high beam" headlight in Pantone 638C (`#00AED8`) over a deep `#0080B4` halo,
  redrawn as vector shapes with thicker strokes so it stays readable at 16 px. The SVG
  (`src/Beam.Tray/Assets/high-beam.svg`) is the single source of truth —
  `installer/generate-icon.ps1` (through `installer/render-icon.fsx`, Svg.NET)
  rasterises it pre-coloured into the multi-size `app.ico`, the 512 px
  `high-beam.png` the tray draws at the shell's DPI size, and `assets/beam-256.png`.
  Reads on both light and dark taskbars.
- **Decoupled into five projects.** The presentation engine moved from inside the tray
  into a UI-free `Beam.App` layer (net10.0) that talks only to ports declared in
  `Beam.Core.Ports`. `Beam.Windows` is now the single home for Win32/WinRT adapters;
  `Beam.Tray` and `Beam.Cli` are thin composition roots. The whole apply → active →
  disconnect → restore cycle is unit-tested with fakes — no projector, no screen
  blanking (see [ADR-0014](docs/04-adr/ADR-0014-application-layer-and-ports.md)).
- **Documentation rewritten for what actually ships.** New
  [docs/06-development](docs/06-development) (architecture, testing, debugging,
  releasing), the arc42 building-block view now matches the five projects, and the
  test strategy reports the real coverage instead of an aspirational matrix.
- **CI rewritten and gated.** `.github/workflows/ci.yml` now builds and tests Debug and
  Release, verifies formatting with `dotnet format`, enforces coverage gates on
  Core/App/CLI, and checks that every relative docs link resolves. Snapshot installers
  are still built on every `main` push; release installers on `v*` tags.

### Added
- `StartupCommand` pure-logic helper + unit tests (in `Beam.Tray.Tests`).
- `Beam.App` project with `PresentationManager`, `SessionManager`, `JournalStore` and
  `FileLogSink`, all testable against fake ports.
- `Beam.Architecture.Tests` — project-reference rules and no-UI/no-P/Invoke guards for
  the portable layers.
- `eng/coverage.ps1` (union Cobertura report + Core ≥ 85 / App ≥ 80 / CLI ≥ 75 gate) and
  `eng/check-links.ps1`.
- Coverage for the adapter contract: `Beam.Windows.Tests` (display-monitor polling,
  LUID/rational/DEVMODE helpers, mode ranking) and domain model coverage for the topology
  model, topology ids and trigger condition (`Beam.Core.Tests/DisplayModelTests.cs`).
- **330 tests** across six test projects (was 100-odd across Core + tray), including a
  guard against composition-root binary name collisions.
- **550 tests** after the deep-testing push: property-style suites (scale planner,
  topology invariants), an exhaustive single-byte fuzz of the Night Light codec plus
  500 seeded mutants, exhaustive state-machine matrix tests, error-injection tests for
  every presentation dependency, journal crash-safety (corrupt/truncated/concurrent),
  CLI parser adversarial cases, and a no-monitor-connected contract suite covering
  every command with zero displays attached.
- `DisplayMonitor` lifecycle is now explicit: `Stop()`/`Dispose()` are idempotent and
  terminal, `Start()` after dispose throws.
- Stress and interaction suites: `tests/Beam.App.Tests/SoakTests.cs` (500-cycle
  apply/restore soak with 0 black screens and 500/500 valid end states; UC-12
  random-fault property; unplug restore 20/20; kill -9 at 20 points → next start
  always valid), `tests/Beam.Windows.Tests/DisplayModeControllerPropertyTests.cs`
  (UC-12 property: end state is always a member of the original mode list), and the
  extended `tests/Beam.Windows.Tests/DisplayMonitorStressTests.cs` (real polling loop
  vs. scripted hardware: UC-01 AC2/AC3, S5 p95 detection ≤ 1.5 s, KVM-bounce
  coalescing), plus `Beam.Tray.Tests/TrayInteractionTests.cs` and concurrency tests in
  `SessionManagerTests.cs` (tray text/labels, startup Run-key round-trip, cursor-guard
  lifecycle, exactly-one-winner `TryBeginFix`).
- Tray context menu: **Open log folder** item (opens `%LOCALAPPDATA%\Beam` so
  `beam-detailed.jsonl` can be attached to a bug report), and the **Apply / Restore**
  actions are now contextually enabled — `Apply projector mode now` only when idle,
  `Undo projector mode` only while a fix is active — so a click is never a no-op.
- File logging writes `beam.log` (human) and `beam-detailed.jsonl` (structured) under
  `%LOCALAPPDATA%\Beam`; a locked log file never takes the app down.
- **Documentation diagrams** (`docs/media/`): architecture layering, session state
  machine, the tray menu with contextual enable/disable, and a before/after of the
  black-bar problem — embedded in the README.

### Removed
- Dead code sweep: `src/Beam.Tray/AppLog.cs` (a duplicate of `Beam.App`'s `FileLogSink`;
  all logging already went through `Beam.Core.Diagnostics.Log` — the log folder path now
  comes from `FileLogSink.DefaultDirectory`), the unreferenced `docs/media/*.png`
  screenshots, the unused `Svg` central package version, the duplicate `.idea/` entry in
  `.gitignore`, and the redundant copy-to-output of the tray assets in `Beam.Tray.csproj`
  (both are embedded resources and are only ever loaded as such) — plus the never-called
  `FileLogSink.LogDirectory` instance property, `NightLightEntry.IsValid`, and an unused
  `using` in `IJournalStore.cs`.


## [0.1.0] — 2026-10-31

Phase 1 MVP: tray app + CLI + installer + CI. Initial release.
