<!-- Thanks for contributing to Beam! -->

## What changed?

<!-- One or two sentences. Conventional commit style is used on `main` (feat:/fix:/docs:/test:/chore:). -->

## How was it tested?

- [ ] `dotnet test Beam.sln` — all green
- [ ] `dotnet format Beam.sln --verify-no-changes` — clean
- [ ] `pwsh eng/check-links.ps1` — links resolve
- [ ] Manual check on real hardware if the change touches displays, DPI, Night Light, or the tray UI (note what you ran)

## Scope guardrails

- [ ] The change never leaves a display in an unsupported/broken mode and always restores exactly (mode, DPI, Night Light bytes)
- [ ] No admin rights required; per-user only
- [ ] Docs updated if behaviour changes (`README.md`, `docs/`, `CHANGELOG.md`)
