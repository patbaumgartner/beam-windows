# Releasing

Beam versions itself from the `Version` property in
[`Directory.Build.props`](../../Directory.Build.props) (currently `0.1.1`). The version is
stamped into the assemblies at build time and into the installer at packaging time.

## Version scheme

- `0.1.1` — the current patch line shipping the Nov-1 fixes (sticky cursor, decoupling).
  A bump to `0.2.0` means user-visible features; a `0.1.2` bump means bug fixes on this
  line.
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
.\installer\build.ps1                    # -> artifacts\BeamSetup-0.1.1.exe
.\installer\build.ps1 -Version 0.2.0     # version override
```

`installer\build.ps1` publishes `Beam.Tray` and `Beam.Cli`, runs `Beam.iss` through
`ISCC.exe`, and reports the produced `BeamSetup-<version>.exe`.

## Repackaging an installed copy after a fix

The fastest ship loop when you only changed code (no installer/registry logic):

1. Stop the running instance: `Stop-Process -Name Beam -Force`.
2. Rebuild: `dotnet publish src\Beam.Tray -c Release` (tray) and, if the CLI changed,
   `dotnet publish src\Beam.Cli -c Release`.
3. Copy the fresh `Beam.exe` (and any changed DLLs) over the installed copy; the CLI is
   `Beam.Cli.exe`.
4. Restart; verify `Beam.Cli.exe status --json` and the tray behaviour.
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

`.github/workflows/ci.yml` builds and tests every push, then on `main` builds a snapshot
installer and republishes it to the `snapshot` GitHub release (`make_latest: true`).

`.github/workflows/release.yml` triggers on a `v*` tag (or `workflow_dispatch` with a tag):

1. Resolve the version from the tag (`v0.2.0` → `0.2.0`).
2. Publish tray + CLI with that version.
3. Build the installer with Inno Setup (downloaded in the runner).
4. Create a GitHub release with the installer attached, generating release notes.

So releasing is just:

```powershell
git tag v0.2.0 # after the gates above pass locally and CI is green
git push origin v0.2.0
```

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
