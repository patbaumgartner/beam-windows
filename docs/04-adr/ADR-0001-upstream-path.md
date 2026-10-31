# ADR-0001 — Upstream into PowerDisplay, build a standalone app first

**Status:** Accepted — confirmed by the product owner: "I like the idea of an upstream contribution for PowerToys" and "standalone first and upstream in parallel".
**Date:** 2026-10-29
**Deciders:** product owner (you), engineering

## Context

The feature does not exist in PowerToys 0.102. PowerDisplay (new in 0.102) already owns
display enumeration, stable monitor identity, hot-plug detection, profiles, tray, CLI
and settings. It does not support resolution, scaling, Night Light or automatic triggers.
Open issues (#48286, #47433, #47336, #49659, #48470, #51137, #49374) show the community
and maintainers moving PowerDisplay toward profiles + resolution + triggers. PR #51072
(open) adds refresh-rate control and touches the same files we would.

PowerToys contribution rules require an issue, discussion, agreement, and a spec before
a large PR. There is no third-party plugin surface for native display control; Command
Palette extensions cannot own a background trigger or a rollback journal.

## Decision

1. Build **Beam** as a standalone, per-user app first (MVP in weeks, not months).
2. Keep `Beam.Core` UI-free and `Beam.Windows` adapters shaped exactly like
   PowerDisplay's services.
3. Pursue upstreaming as an **extension of PowerDisplay** ("projector mode" scenario:
   modes + scaling + Night Light + auto-trigger), not as a new module and not as a plugin.
4. File the upstream issue/spec early, in parallel with the standalone build, and treat
   the standalone app as the reference implementation and evidence.

## Consequences

**Positive**
- Value delivered immediately; no dependency on maintainer availability.
- Undocumented-API risk is contained outside PowerToys.
- A working prototype makes the spec credible.
- Engine can be lifted into PowerDisplay with minimal rewrites.

**Negative**
- Possible duplicate maintenance until upstream lands.
- Two UX surfaces (standalone settings vs PowerToys settings) during transition.
- Upstream may want a different scope (e.g. generic display profiles, no Night Light).

**Mitigations**
- Strict dependency rule: `Beam.Core` has no UI/OS dependencies.
- Adapter interfaces mirror PowerDisplay services 1:1.
- MIT license allows code donation.
- If upstream lands first, retire the standalone app or reduce it to a thin shim.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| New PowerToys module ("Beam") | Duplicates PowerDisplay ownership; ~20 files of module boilerplate; maintainers are consolidating display work |
| PowerToys plugin / CmdPal extension | No background trigger, no native display access, no rollback |
| Standalone only, never upstream | Leaves the best distribution channel unused; feature should be a Windows default |
| Wait for PowerToys to build it | No committed work; user need is now |

## Follow-up

- Write `../05-delivery/upstream-plan.md` issue text and spec outline.
- Comment on #48286 and the contribution thread #28769.
- Re-evaluate after S8.
