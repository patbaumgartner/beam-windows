# ADR-0008 — Snapshot / journal / verify / revert transaction

**Status:** Accepted
**Date:** 2026-10-29

## Context

A projector session changes several independent settings (mode, refresh, scale, Night
Light). Windows offers no cross-setting transaction. A crash, a driver failure, or a
user unplug can leave a partially applied state. The worst outcome is a laptop stuck at
an unusable resolution.

## Decision

Every session is a **write-ahead transaction**:

1. **Plan** — compute the full set of changes (pure function).
2. **Validate** — every mode is enumerated and passes `CDS_TEST`.
3. **Snapshot** — capture the exact prior value of every item (DEVMODE fields, scale,
   Night Light raw bytes).
4. **Journal** — atomically persist the plan + snapshot + phase *before* the first
   mutation. If the journal cannot be written, **do not mutate**.
5. **Apply** — serialized by a mutation gate, item by item.
6. **Verify** — re-read after each item; mismatch triggers revert of that item.
7. **Commit** — mark the session active; keep the journal for restore.
8. **Restore** — reverse order, only if the current value is still ours.
9. **Complete** — delete the journal only after every item is verified restored.

Additional rules:
- Max 3 mode candidates, 1 retry per restore item.
- 5 s timeout per native call; mutations never run on the UI thread.
- Safe mode after 2 consecutive failures.
- Self-induced display events suppressed for 3 s.

## Rationale

- The journal is the only way to survive `kill -9` and reboot.
- "Still ours" prevents clobbering user changes made during the session.
- Verify-by-re-read catches drivers that report success but do nothing.
- Reverse order is the safe unwind (Night Light is independent; mode is the riskiest).

## Consequences

**Positive**
- Crash-safe; no stuck displays.
- Auditable: the journal explains exactly what happened.
- Restore is idempotent and safe to retry.

**Negative**
- Extra disk I/O (one small fsync per session — negligible).
- More states to test (mitigated by a state-machine test harness).
- Requires a strict "still ours" comparison, including for binary blobs.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Apply and hope | Unacceptable failure mode |
| Windows System Restore point | Too heavy, too slow, not per-setting |
| Rely on Windows "Keep settings?" dialog | Only for topology/driver changes, not mode changes |
| Re-apply a saved profile on disconnect | Cannot know what the user changed; clobbers |

## Validation

S6: 500 apply/restore cycles, kill at every phase, inject API failures, assert the end
state is always an enumerated mode.
