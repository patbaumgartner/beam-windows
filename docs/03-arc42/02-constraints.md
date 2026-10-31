# arc42 — 2. Constraints

## 2.1 Technical constraints

| ID | Constraint | Consequence |
|---|---|---|
| TC1 | Must run in the interactive user session | No Windows service; display APIs are session-bound |
| TC2 | No elevation | No driver, no HKLM writes, no UAC prompts |
| TC3 | Windows 11 only (validated 26H2 / build 26300) | No Windows 10 compatibility burden; can use modern APIs |
| TC4 | Night Light has no public API | Undocumented CloudStore write, isolated + fallback (ADR-0006) |
| TC5 | Per-monitor DPI has no *documented* immediate API | Undocumented source device-info `-3`/`-4` behind one adapter, verified, with an advice fallback (ADR-0013) |
| TC6 | Display APIs are Win32 (`user32`, `gdi32`) | P/Invoke via CsWin32; structs must match ABI exactly |
| TC7 | `ChangeDisplaySettingsEx` is not transactional across displays | Snapshot + journal + verify + revert (ADR-0008) |
| TC8 | A hung display driver can block a thread | Mutations on a worker with timeouts; never on the UI thread |
| TC9 | "Projector" has no ground-truth API | Trigger on aspect mismatch; classification optional (ADR-0005) |
| TC10 | Clone mode requires a mode common to all displays | Intersection algorithm (UC-04) |
| TC11 | PowerToys is C#/.NET + C++/WinRT, MIT, MS code style | Engine in C#, no UI deps, MIT (ADR-0003, ADR-0012) |
| TC12 | Localization is owned by Microsoft upstream | All strings in resources from day one |

## 2.2 Organizational constraints

| ID | Constraint | Consequence |
|---|---|---|
| OC1 | PowerToys requires an issue + agreement + spec before large PRs | Standalone first; upstream proposal in parallel (ADR-0001) |
| OC2 | PowerDisplay is under active development (PR #51072) | Sequence upstream work; avoid touching the same files early |
| OC3 | No budget for code signing certificates at MVP | Unsigned dev builds; document SmartScreen behaviour |
| OC4 | One developer, part-time | Phased roadmap; spikes gate scope |
| OC5 | Hardware access is limited (one laptop, one 1080p display) | Simulation-first testing; community hardware matrix |

## 2.3 Conventions

- C# 13 / .NET 9 (or .NET 10 if PowerToys has moved), nullable enabled, warnings as errors.
- `StyleCop`/PowerToys `.editorconfig` rules.
- One public type per file; `internal` by default; `sealed` by default.
- Async all the way; no `.Result`/`.Wait()`.
- All native calls behind an interface; no P/Invoke in view models.
- Tests: xUnit + FluentAssertions; no mocking framework for domain logic, hand-written
  fakes for adapters.
- Commit style: conventional commits; PRs reference a UC id.

## 2.4 Regulatory / privacy constraints

- GDPR: no personal data leaves the machine. EDID serial is hashed. Telemetry opt-in.
- Accessibility: WCAG-ish for the settings window; screen-reader labels; keyboard only.
- No telemetry of window titles, screen content, file paths or user names.
