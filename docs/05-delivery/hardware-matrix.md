# Hardware matrix

Per-release manual verification. The base checks **H1–H10** are defined in
[test-strategy.md](test-strategy.md#41-hardware-matrix); this file extends them with the
0.2.0 presenter-edition rows (**H11–H18**) and records results. A green unit suite is
not permission to skip this — run before every release and attach the outcome to the
release notes (test-strategy.md §6 release checklist).

## Environment used so far

- Dev laptop with a 16:10 internal panel @ 200% DPI.
- **DELL U2718Q** 16:9 external monitor as projector stand-in — S3/S4 PASS (2026-10-30),
  S6 500-cycle physical soak PASS (2026-11-09).
- Real projector, USB-C dock, KVM, AVR, second-GPU PC: **pending** (O1). Until the real
  projector arrives the DELL stands in and the rows marked "real projector" stay open.

## Matrix

| ID | Setup | Checks | Status |
|---|---|---|---|
| H1 | Laptop + 1080p projector (HDMI) | detect, apply 16:9, readable DPI, restore on unplug | stand-in PASS (S3/S6); real projector pending |
| H2 | 720p projector | mode fallback, no unsupported mode applied | pending |
| H3 | USB-C dock + HDMI | enumeration timing, exactly one apply | pending |
| H4 | HDMI KVM | bounce/flap coalescing, no restore loop | sim + stress tests PASS; hardware pending |
| H5 | AVR passthrough | not misclassified as an internal panel | pending |
| H6 | 4K projector/TV | native 4K 16:9 chosen (prefer-native), sensible DPI | pending |
| H7 | Sleep/wake with projector attached | re-apply without churn | pending |
| H8 | Kill Beam mid-apply | journal restores the display on next start | automated PASS (kill-at-20 tests); hardware pending |
| H9 | Timed pause while presenting, then unplug | paused session ignores changes; auto-resume after 15 min / 1 h restores | automated PASS (`PauseFor` tests); hardware pending |
| H10 | 200% DPI + tray menu | menu responsive, cursor never sticks, icon crisp at 48/64/96 px | PASS 2026-11-09 (TrayIconTests + manual) |
| H11 | 30 Hz-only room | 30 Hz 16:9 accepted, no unsupported mode; 60 Hz preferred when available | S6 finding on stand-in: 60 Hz rejected on the shared source, 30 Hz accepted — PASS |
| H12 | Dock hot-plug flap (UC-13) | ≤ 1 apply per attach, cooldown respected, no event storm | automated PASS (`RapidConnectDisconnect_NoEventStorm`); hardware pending |
| H13 | KVM switch bounce | topology re-read after bounce, single apply, no loop | pending |
| H14 | 4K vs 1080p beamers (B2) | prefer-native picks the EDID-native 16:9; toggle off → max pixels (ADR-0018) | property tests PASS; hardware pending |
| H15 | Audio extra (B3) | default render endpoint switches to the HDMI/projector on apply, restores on unplug | fake-tested end-to-end; hardware pending |
| H16 | Explorer restart (`TaskbarCreated`) | tray icon re-registers, exactly one icon, no monitor loop | code wired (`Program.cs` HostForm); hardware pending |
| H17 | Diagnostics bundle (A4) | zip on the Desktop contains **no** machine/user names; `summary.txt` sanitized | automated PASS (`Exporter_WritesZipWithSanitizedEntries`) |
| H18 | Fresh install on a clean Windows 11 VM | per-user install works from the GitHub Release download; uninstall leaves no residue | manual VM run pending |

## How to record a run

Add the date, machine/projector model, Beam version (`beam help --json`) and result to
the row (or append a dated note below the table), and paste the summary into the release
notes of the `v*` tag.
