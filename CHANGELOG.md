# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Current version is **0.1.0** (per `installer/build.ps1` default).

## [Unreleased]

### Fixed
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
