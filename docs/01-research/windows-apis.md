# Windows APIs and sharp edges

Everything below was verified against Windows 11 26H2 (build 26300) or read from the
PowerToys 0.102 source. Items marked **SPIKE** need a hands-on experiment.

## 1. Display topology and identity

### `QueryDisplayConfig` (user32)
- Flags: `QDC_ONLY_ACTIVE_PATHS` for "what is really on", `QDC_ALL_PATHS` for
  "what is attached", `QDC_VIRTUAL_MODE_AWARE` for virtual modes.
- Returns paths (`source -> target`) and mode info.
- Clone mode: several targets share the same source id / GDI device name.
- Target info via `DisplayConfigGetDeviceInfo(DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME)`:
  `monitorFriendlyDeviceName`, `monitorDevicePath`, `outputTechnology`.
- `outputTechnology` values of interest: `HDMI` (5), `DISPLAYPORT_EXTERNAL` (10),
  `DISPLAYPORT_EMBEDDED` (11), `UDI_EXTERNAL`, `MIRACAST` (15?), `INTERNAL` (0x80000000).
- **Key distinction:** "attached" is not "active". A projector can be plugged in while
  Windows is in "PC screen only". Beam must react to *active* paths only, otherwise it
  changes the laptop for a projector nobody can see.

### Stable monitor identity
- PowerDisplay's convention: device path minus the trailing `#{guid}`, e.g.
  `\\?\DISPLAY#DELP1A8#5&2b1c&0&UID4353`.
- This is unique per (physical device, port) and stable across reboot/sleep/reorder.
- Use it as the primary key for profiles, classification and journal entries.
- For "same model" matching (e.g. a fixed classroom projector) also keep the EDID
  PnP id (`DELP1A8`) and EDID serial.

### `SetDisplayConfig` vs `ChangeDisplaySettingsEx`
- `ChangeDisplaySettingsEx(deviceName, DEVMODE, ..., CDS_TEST)` then apply: per-display
  mode change, keeps topology, no admin. **This is what PowerDisplay rotation uses.**
- `SetDisplayConfig` with `SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_APPLY`: topology
  changes (enable/disable/mirror/position/primary). Needed only for UC-04/UC-10 later.
- Recommendation: modes via `ChangeDisplaySettingsEx`; topology untouched in MVP.

## 2. Enumerating modes

```
EnumDisplaySettingsEx(deviceName, ENUM_CURRENT_SETTINGS, &dm)   // current
EnumDisplaySettingsEx(deviceName, i, &dm)                       // i = 0..n
```
- Use `dm.dmPelsWidth`, `dm.dmPelsHeight`, `dm.dmDisplayFrequency`, `dm.dmBitsPerPel`.
- Deduplicate by (w, h, refresh).
- A mode is "16:9" if `w * 9 == h * 16`. For near-16:9 panels allow a tolerance
  (e.g. 1366x768 = 1.779 vs 1.778, within 0.5%).
- **SPIKE S3:** does a 16:10 laptop panel expose any 16:9 mode at the desired height?
  If not, clone mode can only use modes the projector supports, which is fine, but
  extend mode must not try to force the laptop panel.

## 3. DPI / scaling (solved — undocumented but well understood)

**DPI is a property of the *source*, not the target.** In clone mode the two targets
share one source, so they share one scale. That is exactly what mirroring needs.

Windows Settings does not use a documented API either. It calls
`DisplayConfigGetDeviceInfo` / `DisplayConfigSetDeviceInfo` with **negative device-info
types** on the source:

| Type | Meaning | Struct |
|---|---|---|
| `-3` | get DPI scale info | `DISPLAYCONFIG_SOURCE_DPI_SCALE_GET` (32 bytes) |
| `-4` | set DPI scale | `DISPLAYCONFIG_SOURCE_DPI_SCALE_SET` (24 bytes) |

```c
struct DISPLAYCONFIG_SOURCE_DPI_SCALE_GET {
    DISPLAYCONFIG_DEVICE_INFO_HEADER header; // type=-3, size=32, adapterId, id=sourceId
    int32_t minScaleRel;   // steps below recommended (100% is always the floor)
    int32_t curScaleRel;   // current, relative to recommended
    int32_t maxScaleRel;   // steps above recommended
};

struct DISPLAYCONFIG_SOURCE_DPI_SCALE_SET {
    DISPLAYCONFIG_DEVICE_INFO_HEADER header; // type=-4, size=24, adapterId, id=sourceId
    int32_t scaleRel;      // desired, relative to recommended
};
```

The OS reports **relative steps**, not percentages. Map with the observed ladder:

```
100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500   (%)
```

`recommended = ladder[abs(minScaleRel)]`, `current = ladder[abs(minScaleRel) + curScaleRel]`.
`SetDPIScaling(percent)` converts the target percent to `scaleRel = index(target) -
index(recommended)` and calls `DisplayConfigSetDeviceInfo`. This is exactly what the
Settings app does and it applies **immediately** (no sign-out, no Explorer restart).

Reference implementation (Unlicense): `lihas/windows-DPI-scaling-sample`, `DpiHelper`.
The same repo documents `DisplayConfigGetDeviceInfo(-7)` which returns the monitor
unique name used in `HKCU\Control Panel\Desktop\PerMonitorSettings\<id>` — useful only
for the registry fallback.

### Robustness rules
- Validate `sizeof` (32 / 24) and `ERROR_SUCCESS`; if either changes, treat as
  unsupported (do not guess).
- Clamp the requested percent to `[minimum, maximum]` from `-3` before setting.
- **Verify by re-reading `-3`**; if `curScaleRel` does not match, revert.
- Re-read after a mode change: some drivers reset the source scale when the mode changes.
  Apply mode first, settle, then DPI.
- In clone mode, set the scale on the **shared source**; it affects both displays.
- In extend mode, set the scale on the projector's own source only; never touch the
  internal panel's source.
- Some sources may not expose a given percentage; snap to the nearest available step.
- Registry fallback (`PerMonitorSettings\DpiValue`) requires sign-out in most cases, so
  it is **not** used automatically. It is only offered as "apply at next sign-in".

### Why this is the best available solution
- It is the same mechanism the OS's own Settings app uses, so it behaves identically
  (animations, app re-layout, per-monitor awareness).
- It is immediate, reversible and verifiable.
- It needs no admin, no service, no driver, no reboot.
- The only downside is that the type constants are undocumented; that is contained
  behind one adapter with struct-size and read-back validation.

**Design consequence:** DPI moves into the MVP because mirroring without it is the
960x540 bug. Fallback if the API is unavailable: detect the trap, advise 100%, never
write the registry silently.

## 4. Night Light

### What it is
Two binary values in the per-user CloudStore, plus (26H2) per-device variants.

```
HKCU\Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\
  default$windows.data.bluelightreduction.bluelightreductionstate\
    windows.data.bluelightreduction.bluelightreductionstate     value: Data (REG_BINARY)
  default$windows.data.bluelightreduction.settings\
    windows.data.bluelightreduction.settings                    value: Data (REG_BINARY)
  {c8829d7b-...}$windows.data.bluelightreduction.bluelightreductionstateperdevice\
    windows.data.bluelightreduction.bluelightreductionstateperdevice
  {c8829d7b-...}$windows.data.bluelightreduction.settingsperdevice\
    windows.data.bluelightreduction.settingsperdevice
```

Observed on this machine (state blob, 38 bytes):
```
43 42 01 00 0A 02 01 00 2A 06 86 E6 8B D7 06 2A 2B 0E 10
43 42 01 00 C6 14 AA 8E D0 E6 D8 EE D9 EE 01 00 00 00 00
```
Per-device state blob (39 bytes) is the same shape with an extra trailing byte.

### Documented layout (community reverse engineering, `Enyium/sem-reg-rs`)
```
prologue: 43 42 01 00 0A 02 01 00 2A 06 <epoch u32 LE> 06 2A 2B 0E <bodyLen>
body:     00
          [10 00]            if active
          [D0 0A 02]         if transition was manual (vs schedule)
          C6 14 <VLQ filetime>
          00 00 00 00
```
The settings blob holds strength and schedule (`CA 14 0E ..`, `CA 1E 0E ..`,
`CF 28 ..`, `CA 32 ..`, `CA 3C ..`). We only need to *read* it, not write it.

### Why this is risky
- Undocumented. Microsoft can change it without notice.
- The community documentation explicitly warns that writing can put the engine into a
  broken state that persists until sign-out or reboot, and in bad cases requires
  deleting the values.
- Per-device keys are new in 26H2; mapping a key to a physical display is unknown.

### Mitigations (ADR-0006)
- Snapshot the exact bytes before writing; restore on any verification failure.
- Preserve every byte we do not understand; mutate only the active marker.
- Verify by re-reading and parsing; never assume the write took.
- Rate-limit to one write per 2 s; never write in a loop.
- Fallback: per-display gamma ramp (`GetDeviceGammaRamp` / `SetDeviceGammaRamp`),
  which neutralises the warm tint without touching Night Light state. Drivers may
  ignore it, and it is meaningless in HDR.
- Kill switch: if two consecutive verifications fail, disable the feature and tell
  the user, rather than fighting the OS.

### What we will not do
- UI automation of Quick Settings (`Win+A` + click). Locale-dependent, focus-stealing,
  breaks on layout changes, and can click the wrong thing during a presentation.
- Writing the settings blob to change strength.

## 5. Presentation-display detection

### The trigger that actually matters: aspect mismatch, not device class

The user problem is not "a projector is attached"; it is **"the mirrored image has black
bars because the internal panel is 16:10 and the external display is 16:9"**. That is
observable without knowing what the device is:

1. a **new active external** display appears (not internal, not virtual/RDP), and
2. its current/native aspect differs from the internal panel's aspect (e.g. 16:9 vs
   16:10), or the topology is **clone** and a 16:9 mode is common to both.

That is the default trigger (MVP). It covers projectors **and** TVs, which is what the
user asked for, and it has no classification risk.

Projector *classification* then becomes an optional refinement used for:
- profiles (a classroom projector vs a hotel TV),
- policies ("only auto-apply for projectors", off by default),
- nicer naming in the UI.

### Classification (optional, Phase 2)

No API says "this is a projector". `outputTechnology` says *how* it is connected, not
*what* it is. If classification is enabled, use a scored heuristic plus a one-time user
confirmation.

Signals, strongest first:

| Signal | Source | Weight | Notes |
|---|---|---|---|
| User said "this is a projector" | config, keyed by device path | absolute | wins over everything |
| EDID physical size = 0x00 / 0x00 | EDID bytes 0x15–0x16 | +25 | projectors cannot report a fixed image size; many TVs/monitors do report it |
| Friendly name contains `PJ`, `PROJECTOR`, `BEAM`, `DLP`, `LCD PROJ` | target name / EDID descriptor | +25 | cheap and effective |
| Known projector PnP id (curated list: Epson, BenQ, Optoma, Acer PJ, ViewSonic, Sony, Panasonic, JVC, Christie, LG PJ, Hisense) | EDID bytes 8–11 | +20 | list is data, updatable without code |
| Native timing is exactly 16:9 while internal panel is 16:10/3:2 | EDID preferred timing | +10 | weak on its own |
| Connection is HDMI / DP / USB-C DP-alt | `outputTechnology` | +5 | TVs also qualify |
| No MCCS brightness support | DDC/CI capabilities | +5 | many projectors lack it; many monitors too |
| EDID serial is absent/zero | EDID bytes 12–15 | +5 | common on projectors |

Decision: `>= 50` auto-classify; `30–49` ask once ("Is <name> a projector?"); `< 50`
ignore. Every classification stores its reasons so the diagnostics panel can explain it.

**SPIKE S2:** validate the score against a corpus of real EDIDs (projectors, TVs,
monitors, AVRs). Tune weights. If precision < 95% at the auto threshold, raise the
threshold and ask more often.

## 6. Other sharp edges

- **AVR passthrough:** the EDID Windows sees may be the receiver's, not the
  projector's. Name often contains `AVR`, `RX-V`, `DENON`, `MARANTZ`. Treat as
  "unknown, ask", and remember the answer for that port.
- **Docks / KVMs:** display arrives late (USB-C alt-mode negotiation), or disappears
  and reappears. Debounce >= 1.5 s, and make apply/restore idempotent.
- **Projector powered off, cable attached:** target may still be "active" with a valid
  mode. Beam will apply; that is harmless. Do not use DDC power state as a trigger.
- **HDR:** Night Light is not meaningful/effective in HDR on some configurations.
  Detect advanced colour and skip the Night Light step rather than write uselessly.
- **Windows "Keep these display settings?" dialog:** only appears for topology/driver
  changes. Mode changes via `ChangeDisplaySettingsEx` do not show it, which is why the
  CDS_TEST + verify + auto-revert design matters.
- **Remote sessions:** RDP/virtual displays must never be classified as projectors.
- **Multi-user / fast user switching:** config is per user; Night Light is per user;
  display state is per session. No service, no cross-session work.
- **Secure desktop / UAC:** we never need elevation, so no interaction.
- **Colour profiles / ICC:** changing mode can invalidate an ICC association. Do not
  touch colour management in MVP; note it as a known limitation.
