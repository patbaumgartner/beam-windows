# Security Policy

## Supported versions

| Version | Supported |
|---|---|
| 0.3.x (latest release / `snapshot` build) | ✅ |
| 0.2.x | ❌ — upgrade to 0.3.x (latest: 0.3.2) |
| 0.1.x | ❌ |
| older | ❌ |

## What Beam is and what it can touch

Beam is a per-user tray app with **no admin rights** and **no network access** (verified
in CI by inspection of the dependency graph — no networking assembly is referenced — and
manually with Resource Monitor; see
[test-strategy.md](docs/05-delivery/test-strategy.md#6-release-checklist)). Its attack
surface is limited to the per-user state it writes:

- display mode / DPI via `ChangeDisplaySettingsEx` and `DisplayConfigSetDeviceInfo` (CCD),
- Night Light and Focus Assist (DND) via per-user registry / CloudStore,
- audio default endpoint via `IPolicyConfig`,
- the HKCU `Run` entry for "Launch at sign-in",
- files under `%LOCALAPPDATA%\Beam`: `settings.json`, `journal.json`, `status.json`,
  `beam.log`, `beam-detailed.jsonl`, and diagnostics bundle zips on the Desktop.

All of these are per-user, reversible, and snapshotted before mutation (ADR-0008); a
crash mid-apply is recovered from the journal on next start (UC-11).

## Privacy (A4)

- **No telemetry, ever** (ADR-0010): no network calls, no analytics SDK.
- **Logs carry no PII**: log lines record file *names* only, never machine or user names
  or profile paths (A4 rule, enforced in `Beam.Core.Diagnostics.LogSanitizer` and tested).
- **Diagnostics export bundle** (Diagnostics window → Export bundle…) is sanitized before
  it leaves the machine: machine name, user name and profile paths are replaced with
  `[redacted]` (case-insensitive, ≥ 3-char secrets only). The exporter sanitizes every
  text entry *again* as defense in depth, and tests assert a planted secret never
  appears in the zip (`DiagnosticsTests.Exporter_WritesZipWithSanitizedEntries`).
- **Status file** `status.json` contains display identities and settings only.

## Distribution and integrity (O6 → ADR-0016)

- Beam is **not code-signed, by permanent decision** ([ADR-0016](docs/04-adr/ADR-0016-distribution.md)):
  no cert budget, no Azure tenant, no signing secrets anywhere. Windows SmartScreen will
  show an "unknown publisher" warning on first run — expected; users can click through.
- Every build (snapshot on `main`) and every `v*` tag (release) is produced entirely by
  GitHub Actions workflows (`.github/workflows/ci.yml`, `.github/workflows/release.yml`)
  and uploaded to GitHub Releases over HTTPS: `BeamSetup-<version>.exe`,
  `beam-<version>-portable.zip`, `SHA256SUMS.txt`.
- Integrity check for users: `Get-FileHash BeamSetup-0.3.2.exe -Algorithm SHA256` and
  compare with `SHA256SUMS.txt` on the release page.

## Reporting a vulnerability

Please report privately via
[GitHub Security Advisories](https://github.com/patbaumgartner/beam-windows/security/advisories/new)
— do **not** open a public issue. You can expect an acknowledgement within 72 hours.

Include, if you have it: Beam version (`beam help --json`), Windows build, and the
relevant tail of `%LOCALAPPDATA%\Beam\beam-detailed.jsonl` (safe to share — it contains
no machine/user names; or use the Diagnostics window's **Copy summary** / **Export
bundle**, which are sanitized automatically).
