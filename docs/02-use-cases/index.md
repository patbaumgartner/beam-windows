# Use cases — index

Format: each use case is one file with trigger, flows, acceptance criteria and test
notes. Priorities: **P0** = MVP, **P1** = solid release, **P2** = best-in-class.

| ID | Use case | Phase | Priority | Depends on |
|---|---|---|---|---|
| UC-01 | Detect that an external display became an active display | 1 | P0 | S5 |
| UC-02 | Recognize a presentation display (external + aspect mismatch) | 1 | P0 | S3 |
| UC-03 | Apply a 16:9 display mode on connect (extend) | 1 | P0 | S3, S6 |
| UC-04 | Duplicate (mirror) mode: remove the black bars | 1 | P0 | S3, S4, S6 |
| UC-05 | Keep the desktop readable (DPI scaling) | 1 | P0 | S4 |
| UC-06 | Turn Night Light off while projecting | 1 | P0 | S1, S7 |
| UC-07 | Restore every changed setting on disconnect | 1 | P0 | S6 |
| UC-08 | Manual control: tray, hotkey, pause | 1 | P0 | UC-01 |
| UC-09 | Undo toast and one-click revert | 1 | P0 | UC-07 |
| UC-10 | Per-display profiles (auto-learned, editable) | 2 | P1 | UC-02 |
| UC-11 | Survive crash, kill, reboot, sleep/wake | 1 | P0 | S6 |
| UC-12 | Safety guardrails: never leave a broken display | 1 | P0 | S6 |
| UC-13 | Docks, KVMs, AVRs, multiple displays | 2 | P1 | UC-02 |
| UC-14 | Diagnostics: "why did/didn't it trigger?" | 2 | P1 | all |
| UC-15 | Settings and first-run onboarding | 2 | P1 | UC-08 |
| UC-16 | CLI and Command Palette integration | 3 | P2 | UC-08 |
| UC-17 | Presentation extras (awake, notifications, audio) | 3 | P2 | Awake |

## Cross-cutting scenarios (not separate files)

These are acceptance scenarios that apply to several use cases and are tested globally:

- **CS-1 Idempotency:** connecting the same projector twice produces one apply, not two.
- **CS-2 No-op:** if the display is already at the target mode and Night Light is off,
  do nothing and do not toast.
- **CS-3 User override:** if the user manually changes something while projecting,
  Beam does not fight it and does not restore over it.
- **CS-4 Kill switch:** disabling the feature restores immediately.
- **CS-5 Locale:** all user-facing strings localizable; no locale-dependent automation.
- **CS-6 Accessibility:** toasts readable by screen readers; settings keyboard-navigable.
- **CS-7 Multi-session:** two signed-in users do not interfere.

## Traceability

| Goal | Use cases |
|---|---|
| G1 zero-touch | UC-01, 02, 03, 04, 05, 06 |
| G2 exact restore | UC-07, 09, 11 |
| G3 never break | UC-12, 04, 13 |
| G4 invisible cost | UC-01 (event-driven), UC-11 |
| G5 trustworthy | UC-08, 09, 14 |
| G6 upstreamable | ADR-0001, UC-16 |
| G7 private | UC-15, ADR-0010 |
