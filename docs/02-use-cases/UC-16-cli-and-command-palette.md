# UC-16 — CLI and Command Palette integration

**Phase:** 3 · **Priority:** P2

## Goal
Make Beam scriptable and reachable from where power users already are — and align with
PowerToys' CLI conventions so the upstream port is trivial.

## Actors
- Primary: power user, IT admin, scripts.
- Secondary: PowerToys Command Palette.

## Commands
```
beam status [--json]
beam apply [--device <id|name>] [--profile <name>] [--dry-run]
beam restore [--device <id>]
beam pause --for 1h | --until 18:00
beam resume
beam devices [--json]
beam profiles list|show|export|import
beam diagnostics export [--out <path>]
beam safe-mode reset
```

## Behaviour
- Exit codes: `0` success, `1` generic failure, `2` invalid arguments, `3` no projector,
  `4` unsupported operation, `5` safe mode.
- `--json` emits one object per invocation: `{ version, success, state|error }`
  (mirrors PowerToys LightSwitch CLI conventions).
- `--dry-run` prints the plan (mode, scale, Night Light) without mutating.
- All commands talk to the running app over the named pipe; if the app is not running,
  `apply` may start it (unless `--no-launch`).
- No command requires elevation.

## Command Palette extension (optional)
- Commands: "Projector mode: apply", "restore", "pause 1 hour", "status".
- Shows the current projector name and mode in the subtitle.
- Does not own state; calls the CLI/pipe.
- Ships only after the core is stable; not an MVP dependency.

## Alternative flows
- **A1 App not running:** start it, wait up to 5 s for the pipe, then execute.
- **A2 Pipe busy:** queue with a 5 s timeout, then fail with a clear message.
- **A3 Device selector ambiguous:** list matches, exit 2.
- **A4 Safe mode:** mutating commands exit 5 with a hint to `beam safe-mode reset`.

## Postconditions
- Scripts get deterministic, documented output.
- No hidden interactive prompt in non-interactive mode.

## Acceptance criteria
- AC1: every tray action has a CLI equivalent.
- AC2: `--json` is stable and versioned; no localized text inside JSON fields.
- AC3: `--dry-run` performs zero mutations (verified by test hook).
- AC4: exit codes are documented and tested.
- AC5: CLI works from a non-elevated shell and from Task Scheduler.

## Edge cases
- Unicode device names in arguments.
- Paths with spaces for `--out`.
- Concurrent CLI calls (serialized by the app).
- CLI invoked from a different user session -> refused.

## Tests
- Unit: argument parsing, JSON schema, exit codes.
- Integration: pipe round-trip, app-not-running path.
- Manual: Task Scheduler invocation.
