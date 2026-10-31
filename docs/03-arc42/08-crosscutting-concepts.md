# arc42 — 8. Crosscutting concepts

## 8.1 Configuration

- Location: `%LOCALAPPDATA%\Beam\settings.json`, `profiles.json`.
- Atomic writes: temp file + `File.Replace`; keep one `.bak`.
- Schema version integer; unknown keys preserved on read/write.
- Defaults are code-defined and documented; a missing file means "defaults".
- Corrupt file -> quarantine as `.bad-<timestamp>`, load defaults, notify once.
- No registry for config. Night Light is the only registry write, and it is not config.

## 8.2 Logging and observability

- Rolling file log: 3 x 5 MB, `%LOCALAPPDATA%\Beam\logs`.
- Levels: Error, Warn, Info, Debug (default Info; Debug via settings/CLI).
- Structured fields: `event`, `deviceIdHash`, `mode`, `durationMs`, `result`.
- Never log: window titles, file paths, user name, raw EDID serial, screen content.
- In-memory ring buffer (200 events) feeds diagnostics.
- Optional ETW provider for PowerToys integration.

## 8.3 Error handling

| Layer | Rule |
|---|---|
| Adapter | Return `Result<T>` with a typed error code; never throw for expected failures |
| Domain | Decide fallback/abort; never catch-and-ignore |
| App | Convert to user-facing message + diagnostics entry; no stack traces in UI |
| Native | Wrap `GetLastError`, include API name and code in the log |

Error codes are stable strings (`display.test_failed`, `nightlight.parse_mismatch`,
`journal.write_failed`, ...). The UI localizes; the log does not.

## 8.4 Internationalization

- All user-facing strings in `.resw` from day one.
- No string concatenation for sentences; use placeholders.
- No locale-dependent logic (no UI automation, no culture parsing of registry).
- Date/time only in diagnostics, formatted with the current culture.

## 8.5 Security and privacy

- Per-user only; named pipe ACL restricted to the current user SID.
- Pipe messages validated (length, schema); no arbitrary commands.
- No elevation; no driver; no HKLM.
- No network access in MVP. Telemetry (later) opt-in, content-free, documented.
- EDID serial stored as SHA-256, not raw.
- Diagnostics export is user-initiated and previewed.

## 8.6 Threading model

- UI thread: WinUI only.
- Event queue: single consumer, serialized mutations.
- Native calls: worker with timeout (`Task.Run` + cancellation; cannot abort a hung
  driver call, so the process must remain responsive — the call is fire-and-forget with
  a result channel and a hard timeout).
- No locks held across `await`; one async semaphore for mutations.

## 8.7 State and idempotency

- Every operation has a "already in desired state" check.
- `DisplaysActivated` may fire twice; the planner is idempotent.
- Restore is idempotent; running it twice is safe.
- Self-induced display events suppressed for 3 s after a mutation.

## 8.8 Time

- `IClock` abstraction for tests.
- Debounce 1.5 s; stability window 1.5 s (max 6 s); self-suppression 3 s;
  reconcile 30 s; Night Light correction rate limit 10 min; flapping cooldown 10 min.

## 8.9 Testing seams

- `IDisplayInventory`, `IDisplayModeController`, `INightLightController`,
  `IDpiController`, `IClock`, `INotifier` are injectable.
- A `SimulationHost` replays event scripts against fakes.
- `--dry-run` uses the real readers but a no-op writer.
- Fault injection wrappers for native adapters (fail nth call, delay, corrupt result).

## 8.10 Accessibility

- Settings and diagnostics: keyboard navigable, labelled controls, high-contrast safe,
  screen-reader names.
- Toasts: text alternative; tray icon has an accessible name that reflects state.

## 8.11 Resource and lifecycle

- Single instance via mutex; second instance activates the first.
- Tray icon re-registers on `TaskbarCreated`.
- Dispose order: stop watcher -> restore if active -> flush logs -> release mutex.
- Exit is best-effort restore; a crash is handled by the journal on next start.

## 8.12 Compliance with PowerToys conventions (upstream readiness)

- `Result`-style error handling, `Logger` abstraction, resource strings, telemetry events,
  module interface, settings page binding, CLI JSON envelope.
- No third-party dependencies that PowerToys does not already ship.
