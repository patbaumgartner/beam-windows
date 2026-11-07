# Contributing to Beam

Thanks for wanting to contribute! Beam is a small tray app that takes the pain out of
presenting on a 16:9 projector from a laptop that wants to render in its own shape.

## Ground rules

- **Be kind.** Follow the [Code of Conduct](CODE_OF_CONDUCT.md).
- **One change per PR.** Small, reviewable diffs land faster.
- **Keep the layers honest.** Portable logic belongs in `Beam.Core`, sequencing in
  `Beam.App`, P/Invoke in `Beam.Windows`, and wiring in `Beam.Tray` / `Beam.Cli`.
  `Beam.Architecture.Tests` will fail a PR that puts WinForms, `[DllImport]`, or a
  Windows-only reference in Core or App — don't fight the rule, design to it.
- **Write a test** for the logic you change. The sequencing is testable without hardware;
  new platform work gets a port + a fake first.
- **Windows only.** Beam talks directly to `QueryDisplayConfig`/`SetDisplayConfig` and the
  CloudStore, so it cannot run on macOS/Linux.

## Getting started

1. Install the [.NET SDK](https://dot.net) (see `global.json` for the pinned version).
2. `dotnet build Beam.sln`
3. `dotnet test Beam.sln`
4. `pwsh eng/coverage.ps1` — per-assembly coverage report (gates in CI).
5. Open `Beam.sln` in Visual Studio or your editor of choice.

The repository uses [arc42](docs/03-arc42) documentation and records decisions as
[ADRs](docs/04-adr). Practical how-tos live in [docs/06-development](docs/06-development).

## Project layout

| Path | Purpose |
|---|---|
| `src/Beam.Core` | Pure domain + ports + logging abstraction (net10.0, no Windows) |
| `src/Beam.App`  | UI-free application layer: session, presentation sequence, journal, file logging (net10.0, no Windows) |
| `src/Beam.Windows` | Thin Win32/WinRT adapters that implement the Core ports (net10.0-windows) |
| `src/Beam.Tray` | WinForms tray composition root (`Beam.exe`) |
| `src/Beam.Cli`  | CLI composition root (`Beam.Cli.exe`, invoked as `beam`) |
| `tests/` | Six xUnit projects: Core, App, Windows, Tray, CLI, Architecture (330 tests) |
| `eng/` | `coverage.ps1` (report + gate) and `check-links.ps1` (Markdown link check) |
| `spikes/` | Numbered proof-of-concept spikes (S1–S7), incl. `S7.BugRepro` cursor bug proof |
| `docs/` | Vision, research, use cases, arc42, ADRs, delivery and development notes |

## The trigger condition

Beam fires when the current topology is duplicate (`DisplayTopologyId.Clone`) **and** an
external display's signal differs in aspect from the shared desktop. It then removes the
bars by switching the shared source to the best supported 16:9 mode, applies a readable
DPI, and turns Night Light off — restoring everything on disconnect.

Do not weaken the trigger to "any clone with a mismatch heuristic": on Windows 11 the
topology id is the canonical signal. `CurrentTopologyId == 0` falls back to a heuristic
only so a failed read never silently disables the fix.

## Testing without a projector

Use the DELL U2718Q (3840×2160) as a stand-in. `beam status --json` shows every active
path; `beam apply --dry-run` prints the plan without touching the display. The full
presentation cycle (apply → active → disconnect → restore) is covered by fake ports in
`tests/Beam.App.Tests` — no hardware needed, runs in CI.

## The upstream PR

See [docs/05-delivery/upstream-plan.md](docs/05-delivery/upstream-plan.md) for the
PowerToys/PowerDisplay donation path and [ADR-0001](docs/04-adr/ADR-0001-upstream-path.md)
and [ADR-0014](docs/04-adr/ADR-0014-application-layer-and-ports.md) for the reasoning.

## Commit messages

Short, imperative: `Fix DPI ladder lookup on 4K sources`. Reference issues when relevant:
`Apply fix #12`.

## Before you open a PR

- [ ] `dotnet build Beam.sln -c Release` — clean, zero warnings
- [ ] `dotnet test Beam.sln -c Release` — all tests pass
- [ ] `pwsh eng/coverage.ps1 -NoTest -Enforce` — gates pass (Core ≥ 85, App ≥ 80, CLI ≥ 75)
- [ ] `dotnet format Beam.sln --verify-no-changes` — clean
- [ ] `pwsh eng/check-links.ps1` — all relative docs links resolve
- [ ] You followed the C# conventions in [.editorconfig](.editorconfig)
- [ ] Docs updated for user-visible behaviour changes
