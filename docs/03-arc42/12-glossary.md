# arc42 — 12. Glossary

| Term | Meaning |
|---|---|
| **Active display** | A display that is part of the current desktop (an active path in `QueryDisplayConfig`), not merely attached |
| **Attached display** | A display Windows knows about but which may be disabled ("PC screen only") |
| **Beam** | This product |
| **CDS_TEST** | `ChangeDisplaySettingsEx` flag that validates a mode without applying it |
| **Clone / duplicate** | Several targets driven by the same source at the same mode |
| **DEVMODE** | Win32 structure describing a display mode (width, height, refresh, orientation, ...) |
| **Device path** | Windows PnP path of a monitor, e.g. `\\?\DISPLAY#DELP1A8#5&...&UID...`; Beam's stable id (GUID suffix stripped) |
| **DPI / scale** | Per-monitor scaling percentage; logical size = physical * 100 / scale |
| **EDID** | Monitor-provided description: manufacturer, product, serial, physical size, timings |
| **Extend** | Multiple displays, each with its own desktop area |
| **GDI device name** | `\\.\DISPLAY1`, the name used by `EnumDisplaySettings` / `ChangeDisplaySettingsEx` |
| **Journal** | Persisted record of what Beam changed, used to restore and to recover from crashes |
| **Mutation gate** | Async semaphore ensuring only one display/Night Light change at a time |
| **Night Light** | Windows' blue-light reduction feature; warm tint in the evening |
| **Output technology** | How a target is connected (HDMI, DisplayPort, internal, Miracast, ...) |
| **Presentation state** | The bundle Beam applies: mode + refresh + scale + Night Light (+ extras) |
| **Projector** | A display classified as a projector by Beam's heuristic or by the user |
| **Safe mode** | State after repeated failures: no automatic mutations until reset |
| **Session** | The period between projector activation and deactivation, with one journal |
| **Stable id** | Device path without the class GUID; unique per device + port, stable across reboots |
| **Still-ours predicate** | Rule that Beam restores a value only if the current value equals what Beam set |
| **Topology** | The set of active paths and how they map sources to targets |
| **16:9** | Aspect ratio 1.777...; test `width * 9 == height * 16` (tolerance 0.5%) |
