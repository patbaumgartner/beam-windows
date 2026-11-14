# ADR-0016 — Signing and distribution (answers O6)

**Status:** Accepted
**Date:** 2026-11-14
**Related:** O6 (open-questions.md), A8 (presenter-edition plan Phase A), UC-12 rule 8 (no tampering), `installer/sign.ps1`, `installer/build.ps1`, `.github/workflows/release.yml`, SECURITY.md

## Context

Beam ships as a per-user installer (`BeamSetup-<version>.exe`, Inno Setup) plus two
executables (`Beam.exe` tray, `Beam.Cli.exe`). Unsigned binaries trigger SmartScreen's
"unknown publisher" warning on every machine that has not seen the file before, which
kills trust in a beta. Open question O6 asked: code-signing certificate, or ship unsigned
with a SmartScreen warning? (winget is possible either way.)

Constraints:

- No code-signing cert file should live in the repo or on a dev machine's disk if it can
  be avoided; CI secrets must never be committed.
- Snapshot builds (every push to `main`) must stay cheap and need no signing material.
- The release flow already tags `v*` and builds the installer in
  `.github/workflows/release.yml`.

## Decision

**Sign release artifacts in CI with Azure Trusted Signing; keep snapshot builds
unsigned; verify signatures as a CI gate.** Concretely:

1. `installer/sign.ps1` implements three modes:
   - `-Mode AzureTrustedSigning` — `AzureSignTool` with tenant/client-id/client-secret
     from GitHub Actions secrets (`AZURE_TENANT_ID` / `AZURE_CLIENT_ID` /
     `AZURE_CLIENT_SECRET`), RFC 3161 timestamping (`timestamp.acs.microsoft.com`).
     No certificate file ever touches disk or the repo. This is the recommended CI path.
   - `-Mode Certificate` — `signtool.exe` with a code-signing cert from the local
     certificate store (`-Thumbprint`), for developer machines.
   - `-Mode Verify` — `signtool verify /pa /all` plus `Get-AuthenticodeSignature`;
     exits non-zero if any file is unsigned or invalid. This runs as a release gate.
2. The release workflow calls `sign.ps1 -Mode AzureTrustedSigning` on `Beam.exe`,
   `Beam.Cli.exe`, and `BeamSetup-<version>.exe` after `installer/build.ps1`, then
   `-Mode Verify` before attaching the artifacts to the GitHub release.
3. Snapshot installers (built on `main`) are **not** signed — they are rolling previews,
   and Azure Trusted Signing quota is better spent on releases.
4. winget manifest submission is a **follow-up after the first signed beta** — winget
   accepts signed binaries and the publisher reputation starts accumulating there.

## Rationale

- Azure Trusted Signing issues short-lived tokens; nothing secret is stored in a
  certificate file that could leak, and CI rotation is a secret rotation, not a cert
  re-issuance.
- Timestamping means signatures stay valid after the token expires.
- Signing in CI (not on a dev machine) makes the release reproducible from the tag
  alone.
- A Verify gate catches "forgot to sign" before anything is published.

## Consequences

**Positive**
- Signed releases remove the permanent "unknown publisher" state; SmartScreen
  reputation starts building from the first signed download.
- No secrets in the repo; dev machines can still sign ad-hoc with a store cert.

**Negative**
- Even signed, a brand-new publisher reputation takes a few downloads before
  SmartScreen stops warning — documented in SECURITY.md so users are not surprised.
- Azure Trusted Signing needs the three secrets provisioned once; until then the
  release pipeline fails closed (Verify gate), which is the desired behavior.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Ship unsigned, accept SmartScreen warning | Destroys trust for a tool that asks users to plug in projectors on stage; O6 answered "sign for beta" |
| Self-signed cert committed to the repo | Worse than unsigned — SmartScreen flags self-signed as actively suspicious; cert would leak and be revoked |
| Commercial code-signing cert file in CI secrets | Works, but the cert file is a higher-value secret than ATS credentials and needs manual renewal; ATS is the modern default |
| Sign snapshot builds too | Wastes ATS quota and slows every `main` build for artifacts nobody installs permanently |

## Validation

- `sign.ps1 -Mode Verify` run in the release workflow gates every `v*` tag (CI log shows
  `status=Valid` and the signer subject for each file).
- Manual: after the first signed release, `Get-AuthenticodeSignature BeamSetup-0.2.0.exe`
  reports `Valid` with the Azure Trusted Signing identity.

## Superseded-by

None.
