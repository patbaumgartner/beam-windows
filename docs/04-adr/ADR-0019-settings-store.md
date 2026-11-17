# ADR-0019 — Settings store: versioned JSON, atomic writes, forward-compatible (B4/C1)

**Status:** Accepted
**Date:** 2026-11-14
**Related:** B4 (plan Phase B), C1 settings window, ADR-0009 (config and profiles), ADR-0010 (privacy), UC-15, `BeamSettings`, `SettingsStore`, `ISettingsStore`

## Context

Beam needs one per-user settings file. Requirements that accumulated over Phases B/C:

- Survive a crash mid-write (never a half-written JSON).
- Never throw on load: missing/corrupt/newer files must degrade to defaults, with the
  corrupt file preserved for diagnosis (and one toast, per the B4 exit criterion).
- Coexist with future versions: a newer Beam may add keys; an older Beam must not
  delete them (unknown-key preservation), and must not choke on a newer schema
  version.
- Stay in `Beam.App` (portable `net10.0`, fake-testable) — so no Win32, no
  `Environment.SpecialFolder` magic beyond a plain path.
- C1 added six new optional fields (`TimedPauseMinutes`, `RestoreOnDisconnect`,
  `DpiTargetPercent`, `PreferNativeMode`, `KnownDevices`, `FirstRunToastShown`) without
  a schema bump; the post-0.2.0 `ProjectorTopology` ("Mirror"/"Extend") is another such
  optional field.

## Decision

One JSON file per user at `%LOCALAPPDATA%\Beam\settings.json`, implemented by
`SettingsStore : ISettingsStore` in `Beam.App`:

1. **Schema version in the payload**: `BeamSettings.Version`, `CurrentVersion = 1`.
   Readers ignore (without clobbering) files claiming a *newer* version.
2. **Optional trailing constructor parameters with defaults** for every post-v1 field,
   so v1 files written by 0.1.x still deserialize and new fields round-trip through v1 —
   that is why the C1 fields did **not** bump `CurrentVersion` to 2.
3. **PascalCase JSON** via System.Text.Json defaults — matching the record's property
   names; the CLI's `--json` output uses camelCase options instead, deliberately, so
   machine output stays conventional while the on-disk format stays stable.
4. **Atomic, serialized writes**: `Save` holds a lock, serializes to `settings.json.tmp`,
   then `File.Move(tmp, path, overwrite: true)`. A crash leaves either the old or the new
   file, never a partial one — and concurrent in-process saves (display-change handler +
   startup + tray) cannot collide on the tmp file. (They did before 2026-11-17: a
   projector connect-burst saved twice within 3 ms and one save was lost with
   "settings.json.tmp ... being used by another process".)
5. **Unknown-key preservation**: on save the existing file is parsed as a
   `JsonNode`; known keys are overwritten with the new values, unknown keys are kept
   verbatim (newer versions' data survives an older build's save).
6. **Corrupt file handling**: unreadable JSON is renamed to `settings.json.bad`
   (timestamped when that exists), defaults are returned, and `CorruptFileSetAside`
   fires once so the UI can show a single toast.
7. **Never throws**: `Load()` catches everything and returns `Defaults`.

## Rationale

- tmp+rename is the only atomic replace on NTFS without a transaction; it is what the
  journal (ADR-0008) uses too.
- Keeping the schema at v1 for additive, optional fields is standard forward/backward
  compatibility: old builds ignore unknown keys (STJ default), new builds default
  missing keys.
- Preserving unknown keys on write protects downgrade-then-upgrade cycles.
- A single flat file is right for ~15 scalar/list settings; a database would be
  over-engineering. `%LOCALAPPDATA%\Beam` is per-user, needs no admin, and is already
  the log/journal directory (one place to look, one place to delete on uninstall).

## Consequences

**Positive**
- Settings survive restart, crashes mid-write, and version downgrades.
- Corrupt files are never silently overwritten — they are kept as `*.bad`.
- Fully unit-tested with a fake store and a real store against a temp dir (round-trip,
  clamp, corrupt set-aside, newer-version ignore, unknown-key preservation).

**Negative**
- JSON has no cross-process file locking: two Beam instances could race a save. Mitigated
  by the single-instance mutex in the tray composition root; the CLI only writes via
  `StatusFile`-style atomic paths and does not rewrite settings. In-process saves are
  serialized by the lock in `Save` (decision 4).
- PascalCase on disk is slightly unusual for JSON config, but stability of the on-disk
  format beats convention here.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Registry (HKCU) | Harder to export/back up, no atomic multi-value writes, harder to fake in tests; the diagnostics "export settings" button wants a file |
| INI / TOML | Extra parser dependency for zero benefit at this size |
| Bump schema to v2 for the C1 fields | Breaks 0.1.x reading 0.2.0 files and vice versa for no gain — optional params with defaults are exactly the compatible case |
| `File.Replace` instead of tmp+rename | Fails across volumes and needs the destination to exist; Move(overwrite) is simpler |
| Delete-and-rewrite | Loses data on crash mid-write — the one failure mode we must never have |

## Validation

- `Beam.App.Tests`: settings round-trip through a real `SettingsStore` in a temp dir;
  corrupt file → `*.bad` + event + defaults; newer `Version` → defaults, file
  untouched; unknown keys preserved across a save; C1 fields round-trip with defaults
  on a v1 file.
- Tray/App Phase C tests: every settings-window change persists immediately (debounced
  through `PresentationManager.PersistSettings`) and is visible after a simulated
  restart.

## Superseded-by

None.
