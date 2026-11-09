# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Current version is **0.1.1** (per `installer/build.ps1` default).

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
