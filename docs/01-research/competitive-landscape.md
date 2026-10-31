# Competitive landscape

Goal: find out whether "the best thing available for Windows" already exists.

## 1. Direct and adjacent tools

| Tool | What it does | Projector-aware | Night Light | Exact rollback | Price |
|---|---|---|---|---|---|
| **Windows 11 Settings** | resolution, scale, Night Light, `Win+P` | no | manual | no | included |
| **PowerToys PowerDisplay 0.102** | DDC brightness/contrast/volume/colour temp/input/power, rotation, profiles, CLI | no | no | partial (profiles) | free |
| **DisplayFusion** | monitor profiles, triggers (process, window, time), hotkeys, wallpapers | no (profiles can be manual) | no | yes (profiles) | paid |
| **DisplayMagician** | game/display profiles, app triggers, CLI, NVIDIA surround | no | no | yes | free |
| **AutoActions** | runs actions on app start/stop (HDR, resolution, audio) | no | no | partial | free |
| **MultiMonitorTool (NirSoft)** | CLI/script monitor enable/disable/resolution | no | no | manual scripts | free |
| **Display Changer II (12noon)** | resolution/refresh/colour depth CLI, scripts | no | no | manual scripts | free/paid |
| **LightBulb** | Night Light replacement with schedule, per-app rules | no | yes (own filter) | n/a | free |
| **f.lux** | colour temperature by time/location | no | yes (own filter) | n/a | free |
| **Windows Auto Night Light** (store apps) | schedule Night Light | no | yes | n/a | free |
| **Monitorian / Twinkle Tray** | DDC brightness | no | no | n/a | free |
| **Projector-specific vendor tools** | projector control over LAN/RS-232 | vendor only | no | no | vendor |

## 2. Gap analysis

Nobody does the *combination*:

1. **Detect that the new display is a projector** (not just "a monitor appeared").
2. **Change mode to a correct 16:9 target** with validation and rollback.
3. **Fix scaling** so the projected desktop is readable.
4. **Turn Night Light off only while projecting.**
5. **Restore exactly** what was changed, including after a crash.
6. **Explain and undo** every automatic action.

Existing tools solve fragments:
- DisplayFusion/DisplayMagician: profiles + triggers, but the user must build the
  profile and bind it to a hotkey/app; they do not recognise projectors, do not touch
  Night Light, and their rollback is "apply another profile".
- LightBulb/f.lux: own colour filter, not Windows Night Light; no display awareness.
- PowerDisplay: closest in spirit, but no resolution, no scaling, no Night Light,
  no projector scenario.

## 3. What "best in class" means here

| Capability | Beam target |
|---|---|
| Setup | zero-config for the common case; one question at most |
| Latency | <= 3 s p95 after the display settles |
| Safety | cannot select an unsupported mode; auto-revert on verify failure |
| Reversibility | byte-exact Night Light restore; exact mode/scale restore |
| Transparency | toast with the three changes + Undo; "why?" panel |
| Robustness | docks, KVMs, AVRs, sleep/wake, crash, reboot |
| Footprint | no service, no admin, < 100 MB, < 0.1% idle CPU |
| Extensibility | CLI + JSON, Command Palette later, profile import/export |
| Privacy | offline, opt-in telemetry, no screen content, no identifiers beyond EDID hash |

## 4. Differentiation summary

- **The only tool that knows what a projector is.**
- **The only tool that treats Night Light as part of the presentation state.**
- **The only tool that guarantees the laptop is unchanged afterwards.**
- **The only one that can plausibly become a Windows/PowerToys default.**

## 5. Threats

- Microsoft ships a native "presentation profile" in a future Windows build.
  Mitigation: be upstream-friendly; a PowerToys contribution survives this better
  than a standalone product.
- DisplayFusion adds a "projector" trigger. Mitigation: depth (Night Light, DPI,
  rollback, diagnostics) and free/open-source distribution.
- Undocumented Night Light writes break. Mitigation: fallback + kill switch + tests.
