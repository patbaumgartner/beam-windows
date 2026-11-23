# Releasing

Beam versions itself from the `Version` property in
[`Directory.Build.props`](../../Directory.Build.props) (currently `0.2.0`). The version is
stamped into the assemblies at build time and into the installer at packaging time.

## Version scheme

- `0.2.0` — **Presenter Edition** (2026-11-14): settings + diagnostics windows,
  presentation extras, unsigned releases. A `0.3.0` bump means user-visible features; a
  `0.2.x` bump means bug fixes on this line.
- `0.1.1` — the patch line shipping the Nov-1 fixes (sticky cursor, decoupling).
- The **snapshot** label is always built from the latest `main` and is *not* a version — it
  is a rolling preview so the newest fixes are one click away.

## Local build

You need the .NET SDK pinned in `global.json` and Inno Setup 6. The per-user Inno Setup
install is found automatically (no admin needed).

```powershell
# Quick build + full test suite:
dotnet build Beam.sln -c Release
dotnet test  Beam.sln -c Release

# Gates you must also pass before a release:
pwsh eng/coverage.ps1 -NoTest -Enforce   # Core ≥85 / App ≥80 / CLI ≥75
dotnet format Beam.sln --verify-no-changes
pwsh eng/check-links.ps1

# Build the installer:
.\installer\build.ps1                    # -> artifacts\BeamSetup-0.2.0.exe
.\installer\build.ps1 -Version 0.2.0     # version override
```

`installer\build.ps1` reads the default version from `Directory.Build.props`, publishes both
apps as self-contained `win-x64` binaries into freshly cleaned output directories, runs
`Beam.iss` through `ISCC.exe`, and reports the produced `BeamSetup-<version>.exe`. End users do
not need to install a .NET runtime. A `-Version` override is passed to both projects, so artifact
names, file/product versions, diagnostics, and CLI output stay consistent.

## Repackaging an installed copy after a fix

The fastest ship loop when you only changed code (no installer/registry logic):

1. Stop the running instance: `Stop-Process -Name Beam -Force`.
2. Rebuild: `dotnet publish src\Beam.Tray -c Release` (tray) and, if the CLI changed,
   `dotnet publish src\Beam.Cli -c Release`.
3. Copy the fresh `Beam.exe` (and any changed DLLs) over the installed copy; the CLI is
   `Beam.Cli.exe`.
4. Restart; verify `beam status --json` in a fresh shell (or `Beam.Cli.exe status --json`
   directly) and the tray behaviour.
5. When you're confident, rebuild the real installer and install it to catch packaging issues.

> Installed copy lives at `%LOCALAPPDATA%\Programs\Beam\`. Several old/new builds share the
> same `appVersion` metadata, so to distinguish them compare the file **hash or LastWriteTime**,
> not the version string.
>
> **Names matter more than they look:** `Beam.exe` (tray) and `Beam.Cli.exe` (CLI) are
> deliberately distinct. A lower-case reusable `beam.exe` would overwrite `Beam.exe` on a
> case-insensitive filesystem when both are installed to one directory — keep new binary
> names case-distinct from existing ones.

## CI release flow

**Snapshots — every build.** `.github/workflows/ci.yml` builds and tests every push; on
`main` it additionally builds a snapshot installer, packages the portable zip +
`SHA256SUMS.txt` (`eng/package-snapshot.ps1`), and republishes all of it to the rolling
`snapshot` GitHub release (a prerelease, tagged `snapshot`).

`.github/workflows/release.yml` triggers on a `v*` tag (or `workflow_dispatch` with a tag).
Beam is **not code-signed, by permanent decision** ([ADR-0016](../04-adr/ADR-0016-distribution.md)) —
there is no signing step, no secrets, nothing to configure. The workflow:

1. Resolve the version from the tag (`v0.2.0` → `0.2.0`).
2. Publish tray + CLI with that version.
3. Build the installer with Inno Setup (downloaded in the runner).
4. Package `beam-<version>-portable.zip` + `SHA256SUMS.txt` (`eng/package-snapshot.ps1`).
5. Create/update the GitHub Release on the tag with installer + portable zip +
   checksums attached, generating release notes.

So releasing is just:

```powershell
git tag v0.2.0 # after the gates above pass locally and CI is green
git push origin v0.2.0
```

SmartScreen will show an "unknown publisher" warning on the unsigned download —
documented in README.md and SECURITY.md; users verify against `SHA256SUMS.txt`.

## Release checklist

See the [test strategy](../05-delivery/test-strategy.md#6-release-checklist) for the full
manual checklist. The short version: gates green locally, hardware matrix pass recorded, H1
and H10 at minimum, and the tray manual checks done.

## Keeping the docs honest

Every release touches two things that tend to rot:

- **Coverage numbers** in [`testing.md`](testing.md) and
  [`test-strategy.md`](../05-delivery/test-strategy.md) — update them from
  `pwsh eng/coverage.ps1 -NoTest` if they moved.
- **Test counts** in the `README.md` badge text — update from `dotnet test`.
