# ADR-0016 — Distribution: no code signing; Releases carry checksums (answers O6)

**Status:** Accepted (revised 2026-11-14: signing removed permanently)
**Date:** 2026-11-14
**Related:** O6 (open-questions.md), A8 (presenter-edition plan Phase A), UC-12 rule 8 (no tampering), `installer/build.ps1`, `eng/package-snapshot.ps1`, `.github/workflows/ci.yml`, `.github/workflows/release.yml`, SECURITY.md

## Context

Beam ships as a per-user installer (`BeamSetup-<version>.exe`, Inno Setup) plus two
executables (`Beam.exe` tray, `Beam.Cli.exe`). Unsigned binaries trigger SmartScreen's
"unknown publisher" warning on machines that have not seen the file before. Open
question O6 asked: code-signing certificate, or ship unsigned with a SmartScreen
warning?

There is no certificate budget and no Azure tenant available (and none expected), so
any signing pipeline would be dead code. The owner decided on 2026-11-14: **Beam is
never code-signed.**

## Decision

**Ship unsigned. No signing script, no signing secrets, no signing workflow steps —
now or later.** Distribution is entirely GitHub Actions workflows uploading artifacts
to GitHub Releases:

1. **Every push to `main`** (`.github/workflows/ci.yml`) builds the snapshot
   installer with Inno Setup, packages `beam-<version>-portable.zip` +
   `SHA256SUMS.txt` (`eng/package-snapshot.ps1`), and republishes all three to the
   rolling `snapshot` release.
2. **Every `v*` tag** (`.github/workflows/release.yml`) publishes tray + CLI with the
   tag's version, builds the installer, packages the portable zip + `SHA256SUMS.txt`,
   and creates/updates the tag's GitHub Release with all three files attached.
3. **Integrity instead of signatures:** `SHA256SUMS.txt` (SHA-256 of the installer and
   the portable zip) ships with every release; downloads come over HTTPS from GitHub.
   Users who care verify with `Get-FileHash`.
4. **SmartScreen expectation is documented** in README.md and SECURITY.md: the
   "unknown publisher" warning on first run is expected, not a sign of tampering.

## Rationale

- A signing pipeline without credentials is untestable dead weight; the workflow would
  silently skip the one step that matters.
- GitHub Releases + HTTPS + published SHA-256 checksums give users a real integrity
  check without a certificate.
- winget submission does not require signing for the manifest itself and can be
  revisited independently if the project grows.

## Consequences

**Positive**
- Release flow is simple and fully reproducible from the tag alone: build → package →
  upload, all in CI.
- No secrets to manage, rotate, or leak.

**Negative**
- SmartScreen shows "Windows protected your PC / unknown publisher" on first run.
  Mitigated by documenting it and by `SHA256SUMS.txt`.
- Some antivirus products flag fresh unsigned binaries more aggressively; the
  checksums and the public CI build log are the answer.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Azure Trusted Signing in CI | Needs an Azure tenant + 3 Actions secrets; owner has none and will not get them |
| Commercial code-signing cert | No budget; cert file is a high-value secret needing manual renewal |
| Self-signed cert committed to the repo | Worse than unsigned — SmartScreen flags self-signed as actively suspicious; cert would leak and be revoked |

## Validation

- `SHA256SUMS.txt` is attached to the `snapshot` release and to every `v*` release;
  CI logs list the packaged files.
- `pwsh eng/check-links.ps1` passes; no doc references a signing script.

## Superseded-by

None.
