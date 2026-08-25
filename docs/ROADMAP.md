# SenderNG Roadmap

Gaps and planned work, roughly in priority order. Items land here when a task
surfaces a known limitation that we deliberately defer.

## Controller capability detection (grbl handshake)

**Gap:** the Coolant module always shows both Flood and Mist toggle buttons.
Realtime coolant override toggles (`0xA0`/`0xA1`) are identical in classic
grbl 1.1 and grblHAL, but:

- grbl 1.1: Mist exists only when the firmware was compiled with `ENABLE_M7`
  (default off) - the toggle is silently ignored otherwise.
- grblHAL: capability flags (`hal.coolant_cap.flood` / `.mist`) gate the
  toggles; a controller without a coolant output ignores them.

Until we parse capability flags from the controller, we cannot know whether
Mist (or even Flood) is actually supported.

**Plan:** implement the full grbl/grblHAL handshake (`$I` + `[OPT:...]` parsing,
grblHAL build/version detection) in `RealGrblService`, expose a capability set
on `IGrblService` (e.g. `ControllerCapabilities`), and let the Coolant module
hide unsupported toggles. Same capability data will drive other UI decisions
(axis count from `[OPT:...]` is already partially used).

## Other known deferred items

- Cycle bar (Cycle Start / Feed Hold / Stop) still sends placeholder strings
  via `SendAsync` - should use `SendRealtime` (`0x7E`, `0x21`, `0x18`).
- Real machine `MachineState` parsing: `Flood`/`Mist` flags are demo-only until
  the status-report parser reads the accessory-state field.
- Foam CNC mode: functional foam-specific behavior (mode is persisted but only
  changes jog layout today).
