# UC-10 — Per-projector profiles (auto-learned, editable)

**Phase:** 2 · **Priority:** P1 · **Depends on:** UC-02, UC-03, UC-05, UC-06

## Goal
Different rooms and projectors need different answers. Beam should learn the right
answer the first time and reuse it, without the user ever building a profile by hand.

## Actors
- Primary: user (edits only when needed).
- Secondary: profile store.

## Trigger
First successful apply for a device; user edits; import.

## Data model
```json
{
  "schemaVersion": 1,
  "deviceId": "\\\\?\\DISPLAY#EPSON123#5&2b1c&0&UID4353",
  "edid": { "pnpId": "EPSON123", "serialHash": "sha256:...", "name": "EPSON EB-2250U" },
  "topology": "extend",
  "mode": { "width": 1920, "height": 1080, "refresh": 60, "policy": "MatchHeight" },
  "scaling": { "percent": 100, "managed": true },
  "nightLight": { "turnOff": true, "scope": "device" },
  "createdAt": "...", "lastUsedAt": "...", "useCount": 7,
  "notes": "Room 3, HDMI wall plate"
}
```

## Main flow (auto-learn)
1. First successful apply for a device creates a profile from the applied values.
2. Subsequent connects use the profile first; if a value is unsupported (mode gone),
   fall back to the policy and update the profile after success.
3. `useCount` and `lastUsedAt` are updated; profiles with 0 uses for 180 days are
   pruned (configurable, off by default).

## Main flow (edit)
1. Settings lists profiles by display name, with a "last used" line.
2. Editable: mode policy, pinned mode, refresh preference, scaling on/off + target,
   Night Light on/off + scope, "never auto-apply for this device".
3. "Reset to defaults" and "Delete profile".
4. Changes apply on the next connect; "Apply now" for immediate effect.

## Alternative flows
- **A1 Same model, different unit:** profiles are per device id (per port). A
  "apply this profile to all devices of this model" action exists but is explicit.
- **A2 Device id changes** (different port/dock): fall back to EDID model match, ask
  once, then create a new profile.
- **A3 Corrupt profile file:** quarantine it, start fresh, warn once.
- **A4 Import conflict:** merge by device id, newest `lastUsedAt` wins, keep a backup.

## Postconditions
- The profile store is valid JSON, schema-versioned, human-readable.
- A profile never contains a mode that is not currently supported without validation.

## Acceptance criteria
- AC1: second connect to the same projector is zero-question and uses the profile.
- AC2: profile edits take effect without restart.
- AC3: import/export round-trips losslessly.
- AC4: a deleted device's profile does not break startup.
- AC5: profile validation rejects unsupported modes at load time (warn, don't apply).

## Edge cases
- Projector moved to another port in the same room -> model fallback.
- Two identical projectors in adjacent rooms -> per-port ids keep them apart.
- Profile from a future schema -> refuse to load, keep the file, warn.
- Roaming profiles / OneDrive: store in `%LOCALAPPDATA%`, never roam.

## Data recorded
Profile id, use count, last used, validation failures.

## Tests
- Unit: serialization, migration, merge, validation.
- Simulation: unsupported mode fallback.
- Manual: two projectors, port swap.
