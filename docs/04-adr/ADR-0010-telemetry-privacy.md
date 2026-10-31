# ADR-0010 — Local-first, opt-in, content-free telemetry

**Status:** Accepted
**Date:** 2026-10-29

## Context

To improve classification and reliability we need field data (which displays fail, which
modes are chosen). But Beam observes displays during presentations, which is sensitive
context. PowerToys has its own telemetry rules and an opt-in model.

## Decision

- **MVP: no telemetry at all.** Local counters and logs only.
- Later, if added: **opt-in**, off by default, documented, and content-free.
- Never collect: window titles, app names, file paths, user name, screen content, raw
  EDID serial, IP/MAC, location.
- Collect at most: hashed device model id, output technology, chosen mode, result code,
  duration bucket, Windows build, app version.
- Provide a "view exactly what will be sent" screen before enabling.
- Upstream: use PowerToys' telemetry infrastructure and its consent rules.

## Rationale

- Trust is a core product goal (G5, G7).
- A projector tool runs in front of audiences; surveillance-adjacent data collection
  would be indefensible.
- Local logs + a user-initiated diagnostics export solve support without telemetry.

## Consequences

**Positive**
- No privacy review burden at MVP.
- Simpler compliance (GDPR: no processing).
- Diagnostics export gives better data than aggregate telemetry anyway.

**Negative**
- Slower to learn about rare hardware failures.
- Classification corpus must be gathered manually/community.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Opt-out telemetry | Violates trust goal |
| Anonymous aggregate by default | Still requires consent in many jurisdictions |
| Crash reporting service | Third-party dependency, privacy surface |

## Implementation notes

- `ITelemetry` interface with a `NullTelemetry` default; no accidental calls.
- A build-time flag can disable the interface entirely for the privacy-sensitive build.
