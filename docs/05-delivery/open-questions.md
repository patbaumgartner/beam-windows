# Open questions

Everything that blocked the plan has been answered; the answers are recorded in
`decisions.md`. What remains is either operational or Phase-2/3 scope.

## Answered (2026-10-29)

| # | Question | Answer |
|---|---|---|
| Q1 | Duplicate mode behaviour | **Fix it** — shared 16:9 mode + source scale + restore |
| Q2 | May Beam change the laptop's own resolution? | **Yes, when Windows forces it** (duplicate). In extend, only the external display |
| Q3 | Default 16:9 target | **Match height, fall back to 1920x1080** |
| Q4 | Night Light scope | **The real Windows Settings toggle.** Per-device scope if S1 proves it is mappable; otherwise global |
| Q5 | Name | **Beam** ("Projector mode" as the feature label) |
| Q6 | Upstream ambition | **Standalone first + upstream into PowerDisplay in parallel** |
| Q7 | Presentation extras | Deferred to Phase 3; keep-awake is the default-on candidate |
| Q8 | Where the docs/repo live | **`Desktop\Beam`** |
| Q9 | Spikes first? | **Yes** — S1, S3, S4, S5, S6 gate the MVP |
| Q10 | What counts as a "projector" | **Aspect mismatch on a new active external display** (covers TV + projector); classification optional |

## Still open

### O1 — Hardware for the spikes (blocking S3/S4/S6)

Which of these do you have access to?

- a projector — model and native resolution?
- an external monitor (16:9?)
- a USB-C dock
- an AVR / HDMI switcher
- a second PC with a different GPU (Intel vs NVIDIA)

Without at least one 16:9 external display, S3 and S4 cannot be validated and the MVP
cannot be verified end-to-end.

### O2 — Repository layout

Confirm that `Desktop\Beam` should contain **both** the docs and the solution, e.g.

```
Desktop\Beam\
  docs\        (this documentation set)
  src\         (Beam.sln, Beam.Core, Beam.Windows, Beam.App, Beam.Cli)
  tests\
```

or whether you want the docs folder as-is and the code elsewhere.

### O3 — Trademark and store name

"Beam" needs a trademark and Microsoft Store name check before a public beta. Low risk,
but it must happen before release. Fallback name: **Cue**.

### O4 — Telemetry

Default in the plan: **none, ever, in the MVP**; later opt-in and content-free
(ADR-0010). Confirm this is acceptable for the long term, or say "never" and the
interface is removed entirely.

### O5 — Presentation extras (Phase 3)

Which do you actually want?

- keep the display awake while projecting (recommended)
- suppress notifications (Do Not Disturb)
- switch audio to HDMI
- none of the above

### O6 — Signing and distribution

Unsigned dev builds are fine for Phase 0/1. For beta: code signing certificate, or
ship unsigned with a SmartScreen warning? (winget is possible either way.)
