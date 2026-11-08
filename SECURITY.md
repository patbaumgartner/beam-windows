# Security Policy

## Supported versions

| Version | Supported |
|---|---|
| 0.1.x (latest `main` / `snapshot` build) | ✅ |
| older | ❌ |

Beam is a per-user tray app with **no admin rights** and **no network access**. Its attack
surface is limited to: the display/Night Light settings it writes (per-user registry +
CCD API) and the journal/log files under `%LOCALAPPDATA%\Beam`.

## Reporting a vulnerability

Please report privately via
[GitHub Security Advisories](https://github.com/patbaumgartner/beam-windows/security/advisories/new)
— do **not** open a public issue. You can expect an acknowledgement within 72 hours.

Include, if you have it: Beam version (`beam help --json`), Windows build, and the relevant
tail of `%LOCALAPPDATA%\Beam\beam-detailed.jsonl`.
