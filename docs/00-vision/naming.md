# Naming

## Decision (proposed)

**Beam** — product name.
**"Projector mode"** — the human-readable feature label used in settings, toasts and
the PowerToys module description.

Tagline: **"Plug in. Look right. Every time."**

## Why Beam

| Criterion | Assessment |
|---|---|
| Fits PowerToys naming | One word, like Awake, Peek, Run, LightSwitch, PowerRename |
| Says what it does | A projector *beams*; "beam" is the verb for throwing an image on a wall |
| Short and typeable | 4 letters, no ambiguity in CLI (`beam apply`, `beam status`) |
| Verb-able | "Beam it", "beam mode on" |
| Not descriptive-bureaucratic | Better than "Display Configuration Automation Utility" |
| Neutral | No vendor, no geography, no culture-specific idiom |
| Trademark risk | Low but **not checked** — Mixer's "Beam" brand is dead; must run a formal check before publishing |

## Candidates considered

| Name | Pro | Con | Verdict |
|---|---|---|---|
| **Beam** | short, evocative, verb-able | slightly abstract alone | **chosen** |
| Projector Mode | instantly clear, searchable | not a name, awkward in a module list next to "Awake" | use as subtitle/label |
| Cue | stage metaphor, very short | ambiguous (queue? cue card?) | reserve |
| Stagehand | charming, memorable | 9 letters, implies stage automation | reject |
| Aspect | precise (16:9) | only covers one of three jobs | reject |
| Matinee | cinema feel | obscure, implies movies not meetings | reject |
| CastReady | descriptive | branded-sounding, "cast" collides with Chromecast | reject |
| Lumen | light-related | collides with brightness units and several products | reject |
| SlideSync | describes slides | excludes video/desktop sharing | reject |

## Naming rules for the codebase

- Namespace root: `Beam`
- Assemblies: `Beam.Core`, `Beam.Windows`, `Beam.App`, `Beam.Cli`
- Settings folder: `%LOCALAPPDATA%\Beam`
- Named pipe: `\\.\pipe\Beam-<userSidHash>`
- Mutex: `Global\Beam.SingleInstance` (or `Local\` — see ADR-0002)
- Registry: none (config is a file; Night Light is the only registry write)
- Toast app ID: `Beam.ProjectorMode`
- Upstream: PowerDisplay settings page section titled **"Projector mode (Beam)"** until
  maintainers choose otherwise.

## Open naming question

If the feature lands inside PowerDisplay, "Beam" may be dropped in favour of
"Projector mode". That is acceptable: the brand matters for the standalone app,
the function matters upstream. Do not spend political capital on the name.
