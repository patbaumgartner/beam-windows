# PowerToys landscape (verified on this machine)

Environment inspected:
- Windows 11 **26H2**, build **26300.0**
- PowerToys **0.102.3001.0**, installed 2026-10-27 (`v0.102.3001`)
- Laptop panel: **3840x2400 (16:10)**, 200% scale, single active display
- PowerDisplay module: **installed but disabled** in `settings.json`

## 1. Modules that touch our problem

### PowerDisplay (new in 0.102)
Source: `src/modules/powerdisplay` (sparse clone inspected).

Already implemented:
- `DisplayConfigInventory` — `QueryDisplayConfig` + `DisplayConfigGetDeviceInfo`, one
  entry per target, keyed by device path.
- `MonitorIdentity` — stable ID = device path without the `#{guid}` suffix, i.e.
  `\\?\DISPLAY#DELP1A8#5&abc&0&UID12345`. Stable across reboot, sleep, reordering.
- `DisplayRotationService` — `EnumDisplaySettings` -> mutate `DEVMODE` -> `CDS_TEST` ->
  `ChangeDisplaySettingsEx`. This is the exact pattern a resolution service needs.
- `DisplayChangeWatcher` — `DeviceWatcher` over `DisplayMonitor` + console-display-state
  power setting, 1 s debounce, `DisplayChanging` (sync) then `DisplayChanged` (debounced).
- `MonitorManager`, `MonitorStateManager`, crash recovery (`CrashDetectionScope`),
  blacklist of broken monitors.
- Profiles (`PowerDisplayProfile`, `ProfileStore`) — currently **DDC settings only**
  (brightness, contrast, volume, colour-temperature VCP).
- CLI + named-pipe IPC + typed contracts, tray icon, LightSwitch integration.

Not implemented:
- Resolution / display mode selection (only rotation swaps width/height).
- DPI / scaling.
- Night Light.
- Triggers other than LightSwitch theme change.

In flight (open PR #51072, `[PowerDisplay] Refresh Rate`):
- Adds `DisplayRefreshRateService`, per-monitor refresh enumeration, persistence in
  monitor state, battery/energy-saver downshift, settings UI.
- Touches `DisplayChangeWatcher`, `MonitorManager`, `MonitorStateManager`,
  `MonitorViewModel`, `Settings.UI`.
- **Coordination risk:** any Beam PR touching the same files must be sequenced after
  or rebased onto this.

### LightSwitch
- Dark/light theme switching, schedule modes `fixed-hours`, `sunset-to-sunrise`,
  `follow-night-light`.
- Already reads Night Light schedule state — evidence that Night Light state is
  observable, and a precedent for "follow Night Light" behaviour.
- Does not change display mode, scaling, or Night Light itself.

### Awake
- Keeps the machine awake. Reuse for presentation extras (Phase 3) instead of
  reimplementing power requests.

## 2. Open issues that define the gap

| # | Title | Relevance |
|---|---|---|
| 48286 | Power Display: Display Profiles for Windows Display Configuration Switching | Topology profiles; explicitly mentions a "TV Profile" |
| 47433 | Utility to change display resolution on the fly | Direct overlap with UC-03 |
| 47336 | Power Display - Add Monitor Resolutions to profiles, taskbar switcher | Direct overlap |
| 49659 | Night Light slider in Power Display | Confirms no Night Light support |
| 48470 | Auto-switch Power Display profiles based on active app | Precedent for triggers |
| 51137 | Profiles automatically activated at a particular time of day | Precedent for triggers |
| 49374 | Switch Profile based on custom timings | Precedent for triggers |
| 48505 | Context-Aware Shortcut State Machine (display/profile based hotkeys) | Precedent |
| 50432 | [CmdPal] Add Power Display profile switching | CLI/Command Palette precedent |

**Read:** maintainers and community are already pushing PowerDisplay toward
"profiles + resolution + triggers". Beam's projector scenario is the sharpest,
most concrete instance of that direction.

## 3. Contribution rules (from CONTRIBUTING.md)

1. File an issue **before** writing code.
2. Conversation, then agreement on problem, fit and solution.
3. Larger scope requires a **spec** before implementation.
4. Announce intent on the "Would you like to contribute?" thread (#28769).
5. PR description must be written by a human, in their own words.
6. Localization is done by the internal Microsoft team; community localization PRs
   are rejected.
7. New modules carry a large fixed cost: installer, settings page, module interface,
   telemetry, localization, docs, OOBE assets.

## 4. Integration points if we upstream

| Beam concept | PowerDisplay extension point |
|---|---|
| Display identity | `Monitor.Id` (already stable, per port) |
| Mode enumeration/apply | new `DisplayModeService`, mirroring `DisplayRotationService` |
| Hot-plug trigger | existing `DisplayChangeWatcher` |
| Persistence | `ProfileMonitorSetting` + `MonitorStateEntry` (add width/height/refresh) |
| Settings UI | `PowerDisplayPage.xaml` + `PowerDisplayProperties.cs` |
| CLI | `PowerDisplay.Cli` commands (`set --resolution`, `profile apply`) |
| Night Light | new service + settings toggle + CLI setting |
| Notifications | PowerToys toast infrastructure |

## 5. Why not a new PowerToys module

- PowerDisplay already owns display enumeration, identity, hot-plug, profiles, tray,
  CLI and settings. A second display module would duplicate all of it.
- A new module needs ~20 files of boilerplate before the first feature works.
- Maintainers are actively consolidating display work into PowerDisplay (#48286,
  #47336, PR #51072).
- "Projector mode" is a *scenario*, not a *device class*: it is a policy on top of
  display control.

## 6. Why not a PowerToys plugin / Command Palette extension

- PowerToys has no third-party plugin model for native display APIs.
- Command Palette extensions run sandboxed-ish and are UI surfaces; they can call a
  CLI, but cannot own a background trigger or a rollback journal.
- A CmdPal extension is a good *front-end* later (UC-16), not the product.
