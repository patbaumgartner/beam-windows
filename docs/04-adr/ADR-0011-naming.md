# ADR-0011 — Name: Beam

**Status:** Accepted — confirmed by the product owner: "lets go with the name beam".
**Date:** 2026-10-29

## Context

The product needs a name that works as an app, a CLI verb, a PowerToys module label and
a namespace. PowerToys favours short, single-word names (Awake, Peek, Run, LightSwitch).

## Decision

**Beam** is the product name; **"Projector mode"** is the feature label and the
PowerToys-facing description.

Tagline: *Plug in. Look right. Every time.*

## Rationale

- Short, memorable, verb-able (`beam apply`, "beam it").
- Evokes a projector beam without being literal.
- Fits the PowerToys naming family.
- Low collision risk in the Windows utility space (Mixer's "Beam" is defunct).

## Consequences

- Namespaces, assemblies, folder names, pipe names derive from `Beam`.
- Upstream, the label may become "Projector mode" inside PowerDisplay; the internal
  codename can remain Beam.

## Open item

Run a trademark and store-name check before the first public release. If blocked, the
fallback is **Cue** (reserved).

See `../../00-vision/naming.md` for the full candidate table.
