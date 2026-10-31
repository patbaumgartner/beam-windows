# arc42 — 6. Runtime view

## 6.1 State machine (SessionManager)

```
Idle
 | display-activated(projector)
 v
Evaluating --(already correct)--> Idle
 | needs change
 v
Planning -> Validating --(invalid)--> Idle (notify)
 | valid
 v
Snapshotting --(journal write fails)--> Idle (error, no mutation)
 |
 v
Applying -> Verifying --(fail)--> Reverting -> Idle (notify)
 | ok
 v
Active --(display-deactivated | user restore | disable)--> Restoring -> Idle
 | failure x2
 v
SafeMode (no automatic mutations)
```

## 6.2 Scenario: plug in a projector (happy path)

```
User        Beam.App      SessionManager   Inventory   Classifier   ModeCtrl   NightLight
 |  plug       |               |              |            |           |           |
 |------------>| device-added  |              |            |           |           |
 |             |--debounce 1.5s->|            |            |           |           |
 |             |               |--read------->|            |           |           |
 |             |               |<--snapshot---|            |           |           |
 |             |               |--classify---------------->|           |           |
 |             |               |<--projector(score 85)-----|           |           |
 |             |               |--enumerate---------------------------->|           |
 |             |               |<--modes------------------------------|           |
 |             |               |--plan (1920x1080@60)                  |           |
 |             |               |--snapshot night light----------------------------->|
 |             |               |<--bytes-------------------------------------------|
 |             |               |--journal write (fsync)                |           |
 |             |               |--CDS_TEST---------------------------->|           |
 |             |               |<--ok---------------------------------|           |
 |             |               |--apply------------------------------->|           |
 |             |               |<--ok---------------------------------|           |
 |             |               |--verify----------------------------->|           |
 |             |               |--disable night light----------------------------->|
 |             |               |<--verified----------------------------------------|
 |             |<--applied----|              |            |           |           |
 |<--toast-----|               |              |            |           |           |
```

Target: <= 3 s p95 from "display usable" to toast.

## 6.2.1 Scenario: duplicate mode (the primary case)

```
User      Beam.App    SessionManager  Inventory  Planner   ModeCtrl  DpiCtrl  NightLight
 | Win+P dup |             |             |          |         |        |         |
 |---------->| topology change           |          |         |        |         |
 |           |--debounce 1.5s->|         |          |         |        |         |
 |           |             |--read------>|          |         |        |         |
 |           |             |<--snapshot--|          |         |        |         |
 |           |             |--clone group, common 16:9?      |        |         |
 |           |             |--intersect(internal, external)  |        |         |
 |           |             |--plan 1920x1080@60, scale 100%  |        |         |
 |           |             |--snapshot devmode+dpi+NL bytes  |        |         |
 |           |             |--journal write (fsync)          |        |         |
 |           |             |--CDS_TEST (shared GDI)--------->|        |         |
 |           |             |--apply mode-------------------->|        |         |
 |           |             |--verify mode (both targets)-----|        |         |
 |           |             |--settle 300 ms                 |        |         |
 |           |             |--set source DPI 100% (-4)---------------->|         |
 |           |             |--verify DPI (-3)----------------------------|         |
 |           |             |--Night Light off (device if mappable)--------------->|
 |           |             |<--verified----------------------------------------------|
 |<--toast---|             |             |          |         |        |         |
```

Notes:
- If Windows already chose 1920x1080 on plug, the mode step is a no-op and only DPI +
  Night Light change (still one toast).
- The laptop panel necessarily changes too; that is the price of removing the bars in
  duplicate mode, and it is restored on unplug.
- DPI is set **after** the mode change because some drivers reset the source scale when
  the mode changes.

## 6.3 Scenario: unplug (restore)

```
User        Beam.App      SessionManager   ModeCtrl   NightLight   Journal
 |  unplug     |               |              |           |           |
 |------------>| device-removed|              |           |           |
 |             |--debounce---->|              |           |           |
 |             |               |--load journal|           |           |
 |             |               |--read current state------>|           |
 |             |               |--still ours? (yes)       |           |
 |             |               |--restore mode----------->|           |
 |             |               |<--verified---------------|           |
 |             |               |--restore bytes---------------------->|
 |             |               |<--verified--------------------------|
 |             |               |--complete/delete journal------------>|
 |             |<--restored----|              |           |           |
```

Reverse order: Night Light, scale, mode, refresh, orientation (each independent).

## 6.4 Scenario: Night Light fallback

```
NightLightController.Disable()
  -> Read() -> parse
  -> if !active: NoOp
  -> Snapshot()
  -> Write(inactive)
  -> Read() -> parse
     -> active still true? retry once (250ms)
        -> still true? Restore(snapshot); IsSupported=false; emit Warning
     -> false? ok
  -> on any parse mismatch: do not write; IsSupported=false; use GammaRampController
```

## 6.5 Scenario: crash during apply

```
Phase persisted: applying, items: [mode(applied,verified), nightLight(pending)]
Process killed.
Next start:
  journal.Load() -> phase=applying
  if projector still active:
      verify mode -> ok
      nightLight pending -> do not assume; read current; if != snapshot, restore snapshot
      complete
  else:
      restore all applied items; complete
```

## 6.6 Scenario: clone mode

```
topology = Clone(group=[internal, projector])
modesInternal = enumerate(internal)
modesProjector = enumerate(projector)
common = intersect16x9(modesInternal, modesProjector)
if common empty -> no-op + notify
else:
  apply best(common) to the shared GDI device (CDS_TEST -> apply -> verify)
  settle 300 ms
  set source DPI so logicalW in [1280, 2560]  (1920x1080 -> 100%)
  verify all targets in group report the same mode
  verify DPI by re-reading -3
```

## 6.7 Scenario: ambiguous classification

```
score = 42 -> AskUser(display)
  Yes -> store override(projector) -> continue apply
  No  -> store override(not-projector) -> Idle
  Dismiss -> Idle; ask again next connect (max 3 times, then treat as No)
```

## 6.8 Scenario: user disables the feature while active

```
Settings toggle off
  -> SessionManager.Disable()
  -> if journal active: Restore() synchronously (<= 2 s)
  -> stop watcher
  -> tray icon idle
```

## 6.9 Timing budget

| Step | Budget |
|---|---|
| debounce | 1.5 s |
| inventory + EDID | 50 ms |
| classify | < 1 ms |
| enumerate modes | 20 ms |
| journal write (fsync) | 20 ms |
| CDS_TEST + apply | 200–800 ms |
| settle + DPI set + verify | 100–400 ms |
| verify mode | 20 ms |
| Night Light write + verify | 50 ms |
| **total p95** | **<= 3 s** |

## 6.10 Concurrency

- All mutations serialized by one async gate.
- Inventory reads may run concurrently with UI.
- Event handling on a background queue; UI updates marshalled to the dispatcher.
- Native calls wrapped with timeouts on a dedicated worker.
