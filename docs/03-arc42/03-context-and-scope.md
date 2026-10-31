# arc42 — 3. Context and scope

## 3.1 Business context

```
            plugs cable / presses Win+P
  User  -------------------------------------->  Beam
    ^                                             |
    |  toast, tray state, undo                    |  changes mode, scale, Night Light
    +---------------------------------------------+
                                                  |
                        +-------------------------+-------------------------+
                        |                         |                         |
                  Windows display          Windows Night Light        User config
                  subsystem (Win32)        (CloudStore registry)      (%LOCALAPPDATA%)
```

| Neighbour | Relationship |
|---|---|
| User | Plug/unplug, answers one classification question, uses tray/hotkey/CLI |
| Windows display subsystem | Source of topology/modes; target of mode changes |
| Windows Night Light | State read/written to remove the warm tint |
| Windows notifications | Delivery of the result toast |
| Windows power management | Sleep/display-off notifications; optional keep-awake |
| PowerToys (optional host) | Settings page, module lifecycle, CLI conventions, updates |
| Command Palette (optional) | Front-end for CLI commands |

## 3.2 Technical context

| External interface | Direction | Protocol/API |
|---|---|---|
| Display topology | in | `QueryDisplayConfig`, `DisplayConfigGetDeviceInfo` |
| Display modes | in/out | `EnumDisplaySettingsEx`, `ChangeDisplaySettingsEx`, `DEVMODE` |
| Topology changes (later) | out | `SetDisplayConfig` (not MVP) |
| Device arrival | in | `DeviceWatcher` over `Windows.Devices.Display.DisplayMonitor` |
| Display on/off | in | `GUID_CONSOLE_DISPLAY_STATE` power setting notification |
| Monitor identity | in | device path, EDID from `HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\...` |
| Night Light | in/out | `HKCU\...\CloudStore\...` binary `Data` values |
| Gamma fallback | out | `GetDeviceGammaRamp` / `SetDeviceGammaRamp` (gdi32) |
| Notifications | out | Windows App SDK app notifications |
| CLI/IPC | in | named pipe `\\.\pipe\Beam-<sid>` |
| Config | in/out | `%LOCALAPPDATA%\Beam\*.json` |
| Logs | out | `%LOCALAPPDATA%\Beam\logs\` |

## 3.3 Scope

**In scope**
- Active-display detection and projector classification.
- 16:9 mode selection and application in extend and clone topologies.
- Optional scaling management.
- Night Light off/restore, per device when possible.
- Exact rollback, crash recovery, safety guardrails.
- Tray, hotkey, toast, settings, diagnostics, CLI.
- Optional presentation extras (Phase 3).

**Out of scope**
- Topology editing (enable/disable/arrange/primary) in MVP.
- Colour calibration, ICC, HDR tone mapping.
- Projector control over LAN/RS-232/DDC power.
- Audio routing in MVP (Phase 3, opt-in).
- Remote/cloud configuration, fleet management.
- Windows 10 support.
- Replacing general-purpose display-profile tools.

## 3.4 Interfaces to PowerToys (if upstreamed)

| Beam | PowerDisplay |
|---|---|
| `IDisplayInventory` | `DisplayConfigInventory` |
| `IDisplayModeController` | new `DisplayModeService` (pattern: `DisplayRotationService`) |
| `IProjectorClassifier` | new, lives in `PowerDisplay.Lib` |
| `INightLightController` | new service |
| `IStateJournal` | `MonitorStateManager` + `CrashRecovery` |
| `IProfileStore` | `ProfileStore` (extend `ProfileMonitorSetting`) |
| Trigger engine | `DisplayChangeWatcher` |
| Settings | `PowerDisplayPage.xaml`, `PowerDisplayProperties` |
| CLI | `PowerDisplay.Cli` + `PowerDisplay.Ipc` |
