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

## Answered (2026-11-14, Phase D)

| # | Question | Answer |
|---|---|---|
| O1 | Which hardware is available? | **This laptop (16:10 panel @ 200%) + a DELL U2718Q 16:9 monitor as projector stand-in.** S3/S4 PASS on it (2026-10-30) and the S6 500-cycle soak PASSed physically on 2026-11-09, so the MVP is verified end-to-end. A real projector, USB-C dock, KVM, AVR and a second-GPU PC are still pending — the per-release runs are tracked row by row in [hardware-matrix.md](hardware-matrix.md) (H1–H18); until the real projector arrives, the DELL stands in and those rows stay open. |
| O6 | Signing and distribution | **No code signing, ever.** There is no cert budget and no Azure tenant, so Beam ships unsigned; every build (snapshot) and every `v*` tag (release) uploads installer + portable zip + `SHA256SUMS.txt` to GitHub Releases from the GH workflows. SmartScreen's "unknown publisher" warning is documented (README, SECURITY.md); integrity = HTTPS + SHA256SUMS. See [ADR-0016](../04-adr/ADR-0016-distribution.md). |

## Still open

_(O1 and O6 moved to answered above on 2026-11-14.)_

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

**Answered 2026-11-14 (B3):** all three shipped, default on, individually switchable —
keep awake, Do Not Disturb, audio to HDMI (ADR-0017). Failures never fail the session.

### O6 — Signing and distribution

**Answered 2026-11-14 (revised same day):** see ADR-0016 and the answered table above —
ship unsigned permanently; GitHub Releases carry installer + portable zip + SHA256SUMS
on every build and every tag.
