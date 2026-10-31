# arc42 — 11. Risks and technical debt

## 11.1 Risk register

| ID | Risk | Prob. | Impact | Mitigation | Owner |
|---|---|---|---|---|---|
| R1 | Night Light registry format changes or write breaks the engine | Med | High | Strict parser, snapshot, verify, retry, gamma fallback, kill switch, golden-blob tests (S1) | dev |
| R2 | DPI device-info API behaves differently or is reset by the driver | Low | Med | Verify by re-read, apply after the mode change, revert on mismatch, advice fallback (S4) | dev |
| R3 | Aspect trigger fires on a 16:9 monitor where nothing is wrong | Med | Low | "Never for this display", optional `onlyProjectors` mode, clear toast + undo | dev |
| R4 | Clone mode has no common 16:9 mode | Med | Med | Intersection, no-op + clear message, never force | dev |
| R5 | Display driver ignores mode change but reports success | Low | High | Verify by re-read; revert; S6 | dev |
| R6 | Hung driver call freezes the app | Low | High | Worker + timeout, UI never blocked | dev |
| R7 | PowerToys rejects the upstream proposal | Med | Med | Standalone app remains; keep engine upstream-shaped (S8) | dev |
| R8 | PowerDisplay refactor conflicts (PR #51072 and friends) | High | Low | Sequence, rebase, coordinate on issue before PR | dev |
| R9 | Windows ships a native presentation profile | Low | High | Upstream-friendly posture; pivot to contribution | product |
| R10 | Enterprise policy blocks HKCU CloudStore writes | Low | Med | Detect, degrade to gamma/none, explain | dev |
| R11 | Antivirus flags unsigned binary | Med | Low | Sign in beta; document; submit to SmartScreen | release |
| R12 | Toast suppressed during presentations (DND) | Med | Low | Tray state + menu fallback | dev |
| R13 | User changes settings mid-session and we clobber | Med | High | "Still ours" predicate (UC-07) | dev |
| R14 | Journal lost (disk full, AV lock) | Low | High | Refuse to mutate if journal cannot be written | dev |
| R15 | Multi-GPU / hybrid graphics mode list differs per adapter | Med | Med | Enumerate per GDI device, never cache across sessions | dev |
| R16 | HDR makes Night Light meaningless | Med | Low | Skip when HDR active (S7) | dev |
| R17 | Virtual / wireless / capture displays treated as presentation targets | Low | Med | Excluded by default by output technology; opt-in per device | dev |
| R18 | Accessibility/localization regressions | Med | Low | Resource-first, audit in DoD | dev |

## 11.2 Accepted technical debt (deliberate)

| Debt | Why accepted | Payback trigger |
|---|---|---|
| DPI registry fallback is advice-only | The device-info API is the same one Settings uses; registry writes usually need sign-out | `-4` stops working on a future build |
| Topology is read-only in MVP | Duplicate/extend handling covers the reported problem; topology editing is a different product | UC-04 gaps, user demand |
| Night Light is undocumented | No alternative that changes the real setting | Microsoft publishes an API |
| English-only first | Localization owned by MS upstream | Upstream acceptance |
| No code signing at MVP | Cost | First public beta |
| No audio switching in MVP | Risk/benefit, `IPolicyConfig` is undocumented | UC-17 demand |
| Single projector transaction at a time | Simplicity; multi-device transactions are rare | Multi-projector reports |
| Manual hardware matrix | No lab | Community testers / PowerToys lab |

## 11.3 Open technical questions

1. Does the `-3`/`-4` DPI device-info API work and persist on 26H2? (S4)
2. Can per-device Night Light keys be mapped to a display device path? (S1)
3. Does `ChangeDisplaySettingsEx` on a clone source change all targets atomically? (S3)
4. What is the actual p95 for USB-C DP-alt enumeration? (S5)
5. Does writing the state blob while the schedule is active cause the schedule to
   re-apply immediately? (S1)
6. Is `DISP_CHANGE_BADMODE` returned for a mode that `EnumDisplaySettingsEx` listed
   (driver/mode mismatch)? (S6)
