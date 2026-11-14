# Security Policy

## Supported versions

| Version | Supported |
|---|---|
| 0.2.x (latest release / `snapshot` build) | ✅ |
| 0.1.x | ❌ — upgrade to 0.2.0 |
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

## Signing and distribution (O6 → ADR-0016)

- Release artifacts (`Beam.exe`, `Beam.Cli.exe`, `BeamSetup-<version>.exe`) are signed in
  CI with **Azure Trusted Signing** (RFC 3161 timestamped) via `installer/sign.ps1`; a
  `signtool verify /pa /all` gate fails the release if anything is unsigned
  ([ADR-0016](docs/04-adr/ADR-0016-signing-and-distribution.md)).
- Snapshot builds (every push to `main`) are intentionally unsigned previews.
- Even signed, a brand-new publisher reputation takes a few downloads before SmartScreen
  stops warning — expected, and the reason the release notes mention it.
- No secrets are committed: ATS credentials live in GitHub Actions secrets only.

## Reporting a vulnerability

Please report privately via
[GitHub Security Advisories](https://github.com/patbaumgartner/beam-windows/security/advisories/new)
— do **not** open a public issue. You can expect an acknowledgement within 72 hours.

Include, if you have it: Beam version (`beam help --json`), Windows build, and the
relevant tail of `%LOCALAPPDATA%\Beam\beam-detailed.jsonl` (safe to share — it contains
no machine/user names; or use the Diagnostics window's **Copy summary** / **Export
bundle**, which are sanitized automatically).
