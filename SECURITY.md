# Security Policy

## Reporting a vulnerability

Beam is a small, per-user tray app. It holds no credentials and makes no network calls,
so the attack surface is narrow — but if you find a problem, please report it privately.

**Please do not open a public issue for security bugs.**

Report vulnerabilities to the maintainer via a private message/email before disclosure.

## Supported versions

| Version | Supported |
|---|---|
| latest `main` | ✅ |
| tagged releases | ✅ |
| older releases | ⚠️ best effort |

## What Beam does (and does not) touch

- Reads/writes the **current user's** display configuration via the public
  `QueryDisplayConfig`/`SetDisplayConfig`/`ChangeDisplaySettingsEx` APIs.
- Reads/writes the **current user's** Night Light toggle in the per-user CloudStore.
- Runs with user privileges (`PrivilegesRequired=lowest` installer); **never** elevates.
- Makes **no network calls**.

If any of that stops being true, this policy is out of date.
