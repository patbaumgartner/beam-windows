# arc42 — 7. Deployment view

## 7.1 Runtime topology

```
Single user session
+-----------------------------------------------------------+
| Beam.App.exe  (tray, WinUI 3, single instance)            |
|   +-- Beam.Core.dll                                       |
|   +-- Beam.Windows.dll                                    |
|   +-- named pipe server  \\.\pipe\Beam-<sidHash>          |
+-----------------------------------------------------------+
        |  file I/O                         |  Win32/WinRT
        v                                   v
%LOCALAPPDATA%\Beam\              display + Night Light APIs
  settings.json
  profiles.json
  state\session.json
  logs\beam-*.log
  crash\...
```

No service. No driver. No machine-wide state. No HKLM writes.

## 7.2 Artefacts

| Artefact | Purpose |
|---|---|
| `Beam.App.exe` | tray host + settings + diagnostics |
| `Beam.Cli.exe` | scripting front-end |
| `Beam.Core.dll` | domain |
| `Beam.Windows.dll` | adapters |
| `Beam.ProjectorMode.msix` (optional) | packaged install, startup task, toasts |
| `Beam.ProjectorMode.Install.exe` (optional) | per-user installer |

MVP can ship unpackaged (zip + shortcut) to move fast; toasts and startup tasks are
easier packaged. Decision: **packaged MSIX for v1**, unpackaged dev builds for spikes.
See ADR-0003.

## 7.3 Install / first run

1. Install per user (MSIX or per-user installer); no admin.
2. Register startup task (enabled by default, user-toggleable).
3. Create `%LOCALAPPDATA%\Beam` with default config.
4. Start tray app; show one-time toast.
5. No firewall rules, no services, no scheduled tasks beyond the startup task.

## 7.4 Update

- MSIX: store/app-installer style update, or self-update later.
- Config and profiles are forward-compatible: unknown keys preserved, schema version
  checked.
- On upgrade, if `session.json` exists from an older version -> attempt restore with the
  old schema reader; if impossible, quarantine and warn.

## 7.5 Uninstall

1. Stop app; restore active session if any (best effort).
2. Remove startup task.
3. Ask whether to keep `%LOCALAPPDATA%\Beam` (settings/profiles).
4. Never touch display or Night Light settings on uninstall beyond restoring our session.

## 7.6 PowerToys variant (upstream)

- Module DLL + process manager, settings page inside PowerToys Settings, CLI under
  `PowerToys.PowerDisplay.Cli`.
- No separate installer, no separate update path, no separate tray icon (uses PowerDisplay's).
- Localization via PowerToys `.resw`.
- Telemetry via PowerToys events (opt-in rules apply).

## 7.7 Environments

| Environment | Purpose |
|---|---|
| Dev (unpackaged, debug) | spikes, fast loop |
| CI (Windows runner) | unit + simulation tests, no hardware |
| Hardware lab | S3/S5/S6 matrices |
| Beta (packaged) | real users, telemetry off by default |
| PowerToys main | upstream integration |
