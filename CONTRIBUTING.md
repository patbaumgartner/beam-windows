# Contributing to Beam

Thanks for wanting to contribute! Beam is a small tray app that takes the pain out of
presenting on a 16:9 projector from a laptop that wants to render in its own shape.

## Ground rules

- **Be kind.** follow the [Code of Conduct](CODE_OF_CONDUCT.md).
- **One change per PR.** Small, reviewable diffs land faster.
- **Write a test** for the logic you change. The display layer is thin on purpose; the
  decision logic lives in `Beam.Core` and is testable without hardware.
- **Windows only.** Beam talks directly to `QueryDisplayConfig`/`SetDisplayConfig` and the
  CloudStore, so it cannot run on macOS/Linux.

## Getting started

1. Install the [.NET SDK](https://dot.net) (see `global.json` for the pinned version).
2. `dotnet build Beam.sln`
3. `dotnet test Beam.sln`
4. Open `Beam.sln` in Visual Studio or your editor of choice.

The repository uses [arc42](docs/03-arc42) documentation and records decisions as
[ADRs](docs/04-adr).

## Project layout

| Path | Purpose |
|---|---|
| `src/Beam.Core` | Decision logic (aspect matching, DPI planning) — pure, testable, no Windows |
| `src/Beam.Windows` | Thin P/Invoke layer over the Windows display config APIs |
| `src/Beam.Tray` | The WinForms tray app (outputs `Beam.exe`) |
| `src/Beam.Cli` | Scriptable CLI front end (outputs `beam`) |
| `spikes/` | Throwaway proof-of-concept spikes, one per numbered capstone |
| `tests/` | xUnit test projects |
| `docs/` | Vision, research, use cases, arc42, ADRs, delivery notes |

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
path; `beam apply --dry-run` prints the plan without touching the display.

## The upstream PR

See [docs/05-delivery/upstream-plan.md](docs/05-delivery/upstream-plan.md) for the
PowerToys/PowerDisplay donation path and [ADR-0001](docs/04-adr/ADR-0001-upstream-path.md)
for the reasoning.

## Commit messages

Short, imperative: `Fix DPI ladder lookup on 4K sources`. Reference issues when relevant:
`Apply fix #12`.

## Before you open a PR

- [ ] `dotnet build Beam.sln` is clean
- [ ] `dotnet test Beam.sln` passes
- [ ] You followed the C# conventions in [.editorconfig](.editorconfig)
- [ ] Docs updated for user-visible behaviour changes
