# ADR-0012 — MIT license

**Status:** Accepted
**Date:** 2026-10-29

## Context

The goal includes contributing the engine to PowerToys, which is MIT licensed. A
different license (GPL, Apache-2.0 with patent clauses, proprietary) would block or
complicate a code donation.

## Decision

License Beam under **MIT**, with the Microsoft PowerToys copyright header style for files
intended for donation.

## Rationale

- Direct compatibility with PowerToys.
- Maximum reuse; no friction for community contributors.
- Simple, well understood.

## Consequences

**Positive**
- Code can be moved into PowerToys without relicensing.
- Community can fork and improve.

**Negative**
- No patent grant (unlike Apache-2.0). Accepted; PowerToys itself is MIT.
- Competitors can reuse the code. Accepted; the value is the integration and polish.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Apache-2.0 | Patent grant is nice, but incompatible friction with MIT donation (one-way compatible, but headers/notices add work) |
| GPL | Incompatible with PowerToys |
| Proprietary | Contradicts the upstream goal |
