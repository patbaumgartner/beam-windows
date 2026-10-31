# ADR-0007 — Event-driven detection with debounce and a 30 s reconcile

**Status:** Accepted
**Date:** 2026-10-29

## Context

Display connections produce a burst of events (device arrival, mode negotiation, EDID
read, driver rebind). Polling every second is wasteful on battery and still slower than
events. Missing an event leaves the user without automation.

## Decision

Primary sources:
- `DeviceWatcher` over `Windows.Devices.Display.DisplayMonitor` (Added/Updated/Removed),
- `GUID_CONSOLE_DISPLAY_STATE` power-setting notification (display on/off/dim, wake),
- `WM_DISPLAYCHANGE` as a cheap extra signal.

Rules:
- Coalesce with a **1.5 s debounce** (restart on each event).
- Wait for **stability**: no new device for 1.5 s, max 6 s total.
- **Self-suppression:** ignore events for 3 s after our own mutation.
- **Reconcile timer:** every 30 s, compare the active device set with the last snapshot;
  act only on a real difference (safety net for missed events).
- No faster polling, ever.
- Flapping detection: >= 3 changes in 2 minutes -> 10-minute cooldown.

## Rationale

- Events give the best latency; debounce handles the burst.
- The reconcile timer covers rare missed events without a polling tax.
- Comparing stable device ids (not array indices) avoids false diffs.

## Consequences

**Positive**
- p95 detection <= 1.5 s with near-zero idle cost.
- Robust to event storms, KVM bounces, dock re-enumeration.

**Negative**
- Two mechanisms to test (events + reconcile).
- Self-suppression can hide a genuine user change within 3 s (acceptable; reconcile
  catches it).

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Poll every 1 s | Battery/CPU, worse latency, still needs debounce |
| Poll every 10 s | Up to 10 s delay, unacceptable |
| Events only | One missed event = feature silently dead |
| WMI event watchers | Slow, heavy, unreliable for displays |

## Validation

S5: 30 plug/unplug cycles across HDMI, USB-C DP-alt, dock, sleep/wake, KVM; p95 <= 1.5 s,
0 missed, <= 1 apply per attach.
