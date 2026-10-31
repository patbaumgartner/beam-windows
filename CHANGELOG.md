# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Current version is **0.1.0** (per `installer/build.ps1` default).

## [Unreleased]

### Fixed
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

### Changed
- **New laser-beam brand icon** (tray + exe + installer): a stylised laser cone in
  Pantone 638C (`#00AED8`) with a white hot core and a deep-blue edge, drawn as a filled
  polygon so it survives downscaling. Reads on both light and dark taskbars. Applies to
  the runtime tray icon (`TrayService.DrawProjectorIcon`) and the multi-size
  `app.ico` (`installer/generate-icon.ps1`).

### Added
- `StartupCommand` pure-logic helper + 9 unit tests (in `Beam.Tray.Tests`).

## [0.1.0] — 2026-10-31

Phase 1 MVP: tray app + CLI + installer + CI. Initial release.
