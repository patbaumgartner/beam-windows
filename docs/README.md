# Beam — Projector Mode for Windows

> Plug in. Look right. Every time.

**Status:** Plan accepted by the product owner. Tray MVP + CLI shipped (0.1.0); the
presentation engine is decoupled into Core/App/Windows; installer + CI active.
**Author:** planning session, 2026-10-29 (refactor documented 2026-11-01)
**Target OS:** Windows 11 (validated against 26H2, build 26300)
**Target PowerToys:** 0.102.3001.0 (released 2026-10-27)

---

## 1. The one-paragraph summary

Beam is a small, per-user Windows utility that notices when a projector becomes an
active display and atomically switches the machine into a "presentation state":
a correct 16:9 mode (instead of the laptop's 16:10 panel mode), a usable scaling
factor, and Night Light temporarily off — then restores every one of those settings
exactly when the projector goes away. It is event-driven, needs no admin rights,
never leaves a display in an unsupported mode, and offers one-click undo.

## 2. What already exists (checked, not assumed)

| Thing | State today | Implication |
|---|---|---|
| PowerToys 0.102 **PowerDisplay** | DDC/CI monitor control: brightness, contrast, volume, colour-temperature preset, input source, power state, **rotation**, profiles, CLI, hot-plug watcher | Best upstream home; already owns display identity + events |
| PowerDisplay **resolution** support | **Does not exist.** Modes are only enumerated for rotation; PR #51072 (open) adds *refresh rate* | Gap we can fill |
| PowerDisplay **Night Light** | **Does not exist.** Issue #49659 asks for a slider | Gap we can fill |
| PowerDisplay **auto triggers** | Only "on LightSwitch theme change". Issues #48286, #48470, #51137, #49374 ask for profiles/triggers | Direction of travel matches Beam |
| PowerToys **LightSwitch** | Dark/light theme switching; schedule can follow Night Light | Adjacent, not overlapping |
| PowerToys **Awake** | Keeps machine awake | Reuse for presentation extras, do not duplicate |
| Any built-in Windows feature | No "projector mode", no display profiles, no per-display Night Light automation | Real gap |
| Third-party | DisplayFusion, DisplayMagician, AutoActions, MultiMonitorTool, LightBulb, f.lux | None combines projector *detection* + mode + Night Light + exact rollback |

**Conclusion:** the feature does not exist in PowerToys and no mainstream Windows
tool does the whole job. Beam is worth building.

## 3. Recommended strategy (see ADR-0001)

1. **Build Beam as a standalone app first** — it ships value now, and doubles as the
   reference implementation and evidence for an upstream proposal.
2. **Keep the engine UI-free** (`Beam.Core` + `Beam.Windows`) so it can be lifted
   into PowerToys.
3. **Upstream into PowerDisplay**, not as a new module: add display modes, DPI,
   Night Light and *auto-triggers* to the existing module. A new module would duplicate
   PowerDisplay's ownership, identity model and hot-plug watcher.
4. **A PowerToys "plugin" is not viable** — PowerToys has no third-party plugin surface
   for display configuration. Command Palette extensions cannot change display modes.

## 4. Document map

```
beam/
  README.md                     <- you are here
  00-vision/
    vision.md                   problem, personas, goals, non-goals, metrics
    naming.md                   name candidates and the decision
  01-research/
    powertoys-landscape.md      what 0.102 ships, source-level findings
    windows-apis.md             the APIs, the Night Light blob, the sharp edges
    competitive-landscape.md    what else exists and why it falls short
    experiments.md              the spikes that must pass before we commit
  02-use-cases/
    index.md                    all use cases, priority, phase, traceability
    UC-01 .. UC-17              one file per use case
  03-arc42/
    01 .. 12                    arc42 chapters
  04-adr/
    ADR-0001 .. ADR-0015        architecture decisions (status varies: see each ADR)
  05-delivery/
    roadmap.md                  phases, exit criteria, definition of done
    upstream-plan.md            issue text, spec outline, PR sequencing
    test-strategy.md            unit, simulation, hardware matrix
    decisions.md                the product owner's confirmed decisions
    open-questions.md           what is still open
  06-development/
    architecture.md             the real dependency rules & runtime sequence
    testing.md                  how to run tests, fakes, coverage, hardware matrix
    debugging.md                logs, symptoms, sticky-cursor repro
    releasing.md                versioning, installer, CI release flow
```

## 5. Suggested review order

1. `00-vision/vision.md` — is this the product you want?
2. `02-use-cases/index.md` — are the right use cases in the right phases?
3. `05-delivery/open-questions.md` — the decisions I cannot make for you.
4. `04-adr/ADR-0001` and `ADR-0006` — the two riskiest choices.
5. Everything else.

## 6. Scope at a glance

| Phase | Theme | Use cases | Outcome |
|---|---|---|---|
| 0 | Spikes | S1, S3, S4, S5, S6 (S2/S7/S8 as needed) | Prove Night Light write, DPI, modes, detection, safety |
| 1 | MVP | UC-01, 02, 03, 04, 05, 06, 07, 08, 09, 11, 12 | Duplicate **and** extend: 16:9 + readable scale + Night Light off + exact restore |
| 2 | Solid | UC-10, 13, 14, 15 | Profiles, docks/AVRs, diagnostics, onboarding, optional projector classifier |
| 3 | Best-in-class | UC-16, 17 | CLI + Command Palette, presentation extras |
| 4 | Upstream | — | PowerDisplay PR(s) + spec |

## 7. Non-negotiable engineering rules

1. **Never apply a mode that was not enumerated** for that display.
2. **CDS_TEST before every apply.**
3. **Snapshot before mutate; verify after mutate; journal before apply.**
4. **Restore only what we changed, and only if it is still our value.**
5. **No admin, no service, no telemetry without opt-in.**
6. **Undocumented APIs are isolated behind one interface with a fallback.**

## 8. Known unknowns (honest list)

- Whether the Night Light registry write is stable across 26H2 per-device keys. (S1)
- How reliably EDID alone distinguishes a projector from a TV/monitor — **no longer on
  the critical path**: the trigger is aspect mismatch, classification is optional.
- Whether PowerToys maintainers want auto-triggers inside PowerDisplay. (S8)
- Whether a 16:9 mode of the *right height* exists on a 16:10 laptop panel in clone mode. (S3)

**Resolved during planning:**
- DPI: Windows Settings uses `DisplayConfigGetDeviceInfo(-3)` /
  `DisplayConfigSetDeviceInfo(-4)` on the **source**, with relative steps. Immediate,
  verifiable, restorable. See ADR-0013. S4 only confirms it on this hardware.
- Duplicate mode: the shared source must change (mode + scale); this is the primary
  scenario, not an edge case. See UC-04.

These remaining items are turned into spikes in `01-research/experiments.md`, each with a
go/no-go gate.

## 9. Confirmed decisions (2026-10-29)

Recorded in `05-delivery/decisions.md`.

| Topic | Decision |
|---|---|
| Name | **Beam**; feature label "Projector mode" |
| Upstream | Standalone first, upstream into PowerDisplay in parallel |
| Delivery location | `Desktop\Beam` for docs and the repo |
| Night Light | The **real Windows Settings toggle**, not a custom filter |
| DPI | `-3`/`-4` source device-info API (ADR-0013); best available solution |
| Trigger | New active external display with an **aspect mismatch** (covers TV + projector) |
| Duplicate mode | **Fix it** — shared 16:9 mode + source scale + restore |
| Default 16:9 target | **Match height**, fall back to 1920x1080 |
| Process | **Spikes first**, then MVP |
