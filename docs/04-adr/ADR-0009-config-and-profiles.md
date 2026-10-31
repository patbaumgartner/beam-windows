# ADR-0009 — Versioned JSON config and profiles in %LOCALAPPDATA%

**Status:** Accepted
**Date:** 2026-10-29

## Context

Beam needs settings, per-device classifications and per-projector profiles. Options:
registry, JSON files, SQLite, PowerToys settings store.

## Decision

- `%LOCALAPPDATA%\Beam\settings.json` — user settings.
- `%LOCALAPPDATA%\Beam\profiles.json` — classifications and learned profiles.
- `%LOCALAPPDATA%\Beam\state\session.json` — active transaction journal.
- JSON with `schemaVersion`, atomic writes (temp + `File.Replace`), one `.bak`.
- Unknown keys preserved; unknown future schema -> refuse to load that file, keep it,
  warn once.
- No registry for config. No roaming. No cloud.

## Rationale

- Human-readable and diffable; easy to support and to attach to a bug report.
- No COM/registry complexity; no admin.
- Atomic replace prevents corruption on power loss.
- Per-user location matches the per-user product model.

## Consequences

**Positive**
- Trivial backup/export/import.
- Testable with plain file fakes.
- No machine-wide state; uninstall is clean.

**Negative**
- Users can hand-edit and break it (mitigated by validation + quarantine).
- No built-in multi-device sync (intentional).

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Registry | Opaque, harder to support, no atomic multi-key write |
| SQLite | Overkill for < 100 KB of data |
| PowerToys settings store | Only available when upstreamed; keep a clean seam |
| Roaming AppData | Display state is machine-specific |

## Schema sketch

```json
{
  "schemaVersion": 1,
  "general": { "enabled": true, "startWithWindows": true, "pausedUntil": null },
  "projector": {
    "applyModeOnConnect": true,
    "modePolicy": "MatchHeight",
    "fixedMode": { "width": 1920, "height": 1080 },
    "refreshPreference": "Auto60",
    "manageScaling": true,
    "targetLogicalWidth": 1600,
    "nightLight": { "turnOff": true, "scope": "Device" },
    "includeWireless": false,
    "treatTvsAsProjectors": false
  },
  "devices": {
    "\\\\?\\DISPLAY#EPSON123#5&...": {
      "classification": "Projector",
      "score": 85,
      "reasons": ["name", "physicalSizeZero", "pnpAllowlist"],
      "userOverride": null
    }
  }
}
```
