# arc42 — 1. Introduction and goals

## 1.1 Purpose

Beam detects that a projector has become an active display and switches the machine
into a correct presentation state — 16:9 mode, usable scaling, Night Light off — then
restores the previous state exactly when the projector goes away.

This chapter states the requirements and quality goals that drive the architecture.
Product vision and personas are in `../../00-vision/vision.md`.

## 1.2 Essential functional requirements

| ID | Requirement | Use case |
|---|---|---|
| F1 | Detect display activation/deactivation reliably and exactly once | UC-01 |
| F2 | Recognize a presentation display (external + aspect mismatch) | UC-02 |
| F3 | Select and apply a supported 16:9 mode | UC-03 |
| F4 | Be correct in clone/duplicate and projector-only topologies | UC-04 |
| F5 | Keep the projected desktop readable (scaling) | UC-05 |
| F6 | Turn Night Light off while projecting | UC-06 |
| F7 | Restore all changed settings exactly on disconnect | UC-07 |
| F8 | Manual apply/restore/pause from tray and hotkey | UC-08 |
| F9 | Visible, undoable result | UC-09 |
| F10 | Per-projector learned profiles | UC-10 |
| F11 | Survive crash, kill, reboot, sleep/wake | UC-11 |
| F12 | Never leave a display in an invalid state | UC-12 |
| F13 | Work with docks, KVMs, AVRs, multiple projectors | UC-13 |
| F14 | Explain decisions | UC-14 |
| F15 | Zero-config defaults + small settings surface | UC-15 |
| F16 | Scriptable CLI | UC-16 |
| F17 | Optional presentation extras | UC-17 |

## 1.3 Quality goals (top 5)

| Priority | Goal | Scenario | Measure |
|---|---|---|---|
| 1 | **Safety** | Apply a mode to a display that does not support it | Never applied; end state is always an enumerated mode; 0 black screens in 500 cycles |
| 2 | **Correctness of restore** | Unplug after a session | Exact pre-session mode, refresh, scale and Night Light bytes; 100/100 |
| 3 | **Latency** | Plug in HDMI | p95 <= 3 s from display usable to applied |
| 4 | **Trust** | Automatic change happens | 100% produce a toast or tray state; undo within one click |
| 5 | **Footprint** | Idle on battery | < 0.1% CPU, < 100 MB working set, no polling < 30 s |

## 1.4 Stakeholders

| Stakeholder | Concern |
|---|---|
| End user (presenter) | It just works; nothing breaks; one-click undo |
| Power user | Predictable, scriptable, configurable |
| IT admin | No admin rights, no cloud, auditable, silent install |
| PowerToys maintainer | Fits module architecture, tests, localization, no duplicate ownership |
| Beam developer | Testable without hardware; clear interfaces; low coupling |
| Privacy reviewer | No content, no identifiers beyond EDID hash, opt-in telemetry |

## 1.5 What success looks like

A user plugs an HDMI cable into a 1080p projector at 19:30 with Night Light on, 20 seconds
later the wall shows a clean 1920x1080 desktop at 100%, Night Light is off, a toast says
what happened, and after unplugging the laptop is back at 3840x2400 @ 200% with Night
Light exactly as the schedule left it.
