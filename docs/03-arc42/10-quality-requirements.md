# arc42 — 10. Quality requirements

## 10.1 Quality tree

```
Quality
+-- Safety (must)
|   +-- no unsupported mode
|   +-- no black screen
|   +-- no un-restorable change
+-- Reliability
|   +-- crash/reboot recovery
|   +-- idempotency
|   +-- dock/KVM robustness
+-- Performance
|   +-- detection latency
|   +-- apply latency
|   +-- idle footprint
+-- Usability
|   +-- zero-config
|   +-- explainability
|   +-- one-click undo
+-- Maintainability
|   +-- pure core, testable without hardware
|   +-- upstream-shaped adapters
+-- Privacy/Security
    +-- local-only
    +-- least privilege
```

## 10.2 Quality scenarios

| ID | Scenario | Response | Measure |
|---|---|---|---|
| QS1 | Projector does not support the chosen mode | try next candidate, then no-op | end state always enumerated |
| QS2 | Process killed during apply | next start restores/verifies | valid display within 5 s |
| QS3 | Plug HDMI at 19:30 with Night Light on | apply 1080p + Night Light off | p95 <= 3 s |
| QS4 | Unplug after session | exact restore | 100/100 mode+scale+bytes |
| QS5 | User changed scale manually while projecting | do not fight, do not restore over it | 0 clobbers |
| QS6 | Dock re-enumerates 3 times | one apply | <= 1 apply per attach |
| QS7 | Night Light write rejected | restore snapshot, warn, gamma fallback | no broken engine |
| QS8 | Two projectors attached | independent plans | both correct |
| QS9 | Idle on battery for 1 h | no measurable impact | < 0.1% CPU |
| QS10 | User asks "why didn't it trigger?" | explicit reason in diagnostics | 100% decisions explained |
| QS11 | Feature disabled while active | immediate restore | <= 2 s |
| QS12 | Screen reader user opens settings | all controls labelled | audit pass |

## 10.3 Metrics and targets

| Metric | Target | How measured |
|---|---|---|
| Apply success rate | >= 99% on supported hardware | local counters (opt-in) + S6 |
| Restore success rate | 100% | S6 |
| Detection p95 | <= 1.5 s | instrumented runs |
| End-to-end p95 | <= 3 s | instrumented runs |
| Black screens | 0 | S6 |
| Unsupported modes applied | 0 | S6 + assertion in code |
| False-positive classification | < 5% | S2 corpus + user corrections |
| Idle CPU | < 0.1% | Performance Monitor, 1 h |
| Working set | < 100 MB | Task Manager |
| Startup time | < 500 ms to tray | stopwatch |

## 10.4 Test strategy summary

See `../05-delivery/test-strategy.md`.

- Unit: planner/classifier/parser/restore (pure, fast, no OS).
- Simulation: event scripts, fake adapters, fault injection.
- Integration: real registry read, real display enumeration (no mutation) on CI.
- Hardware-in-the-loop: S3/S5/S6, manual + scripted.
- Manual exploratory: docks, KVMs, AVRs, projectors.
- Regression: golden blobs for Night Light, golden mode lists for planners.

## 10.5 Definition of "done" for a use case

1. Acceptance criteria met and demonstrated.
2. Unit + simulation tests merged.
3. Diagnostics reason emitted.
4. Strings localized.
5. Docs updated (UC + ADR if a decision changed).
6. No new warnings; analyzer clean.
7. Manual verification on at least one real projector (for UC-03/06/07).
