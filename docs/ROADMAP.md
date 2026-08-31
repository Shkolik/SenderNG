# SenderNG Roadmap

Gaps and planned work, roughly in priority order. Items land here when a task
surfaces a known limitation that we deliberately defer.

## Controller capability detection (grbl handshake) — DONE

**Resolved (P0, WS-B):** the connect-time `$I` handshake is implemented and the
Coolant module now hides the Mist toggle when unsupported. `RealGrblService`
runs a `$I` exchange on connect; `ControllerInfoParser` reads `[VER:]`/
`[AXS:]`/`[OPT:]` (verified against a real grbl 1.2i board) and fills an
immutable `ControllerCapabilities` exposed on `IGrblService`. `CoolantViewModel`
shows `HasFlood`/`HasMist` from `_grbl.Capabilities` and the XAML binds button
visibility. Demo reports a full capability set so both toggles stay visible.

Note: the honest default is `CoolantMist = isGrblHAL` (a reliable signal for
classic grbl 1.1's `ENABLE_M7` isn't exposed via `$I`). Axis count comes from
`[AXS:]`, tool count from the 4th `[OPT:]` field.

## Controller connect (Serial / Network / Demo) — DONE

**Resolved (connect dialog v2):** the connect dialog is a three-tab dialog —
Serial (port list + refresh + baud, grayed when no ports found), Network (TCP
endpoint for grblHAL Ethernet boards, enabled only when the settings say network
connectivity is available) and Demo (always available, with a disclaimer). The
app starts **disconnected** — it is never launched in demo mode — the connect
dialog opens right after startup, and the UI shows an obvious "NOT CONNECTED"
state until the user picks Serial / Network / Demo. Network plumbing is in:
`TcpGrblTransport` (`IGrblTransport` over a TCP socket) drives the existing
`RealGrblService`; `NetworkAvailable` / `NetworkEndpoint` settings gate the tab.

## Legacy emulator data source vs. modern WorkParameters snapshot — OPEN

**Gap:** the G-code emulator (`GCodeEmulator` via `Machine`) still reads its
coordinate-system table from the legacy static `GrblWorkParameters.CoordinateSystems`,
which the port reduced to a stub (`Get() => false`, `Process(d) => false`) — nothing
populates it. In the original ioSender, `GrblWorkParameters.Get(model)` ran a
synchronous `$#` exchange with the controller and its `process` handler filled the
table (G28/G30/G54..G59.3/G92 via `AddOrUpdateCS`, plus tools, TLO, PRB);
`Machine.Reset()` then copied it into `Machine.coordinateSystems`.

Today the same data lives in the modern `WorkParameters` snapshot
(`Models/WorkParameters.cs`, parsed from `$#`/status and published via
`IGrblService.WorkParametersChanges` by Demo/RealGrblService), but the two worlds were
never reconnected. Consequences:

- Emulator G28/G30/G92/G54..G59.3/G10 handlers are effectively dead: the lookups
  always miss. They were hardened to no-op instead of NRE in the nullable-cleanup
  pass, but they never execute their real logic.
- `NGCExpr` PRB expressions read NaN — `GrblWorkParameters.ProbePosition` is never
  parsed (stub `Get()`).

**TODO:** feed `Machine.coordinateSystems` from the `WorkParameters` snapshot (or
retire the legacy static and route `NGCExpr` PRB to the snapshot), then re-enable
emulator G28/G30/G92 handling with tests.

## GCodeJob.LoadFile parse-error policy — OPEN

**Gap:** on a parse error `LoadFile` has a leftover port stub
(`GCodeJob.cs` catch block, flagged by CS0665). The original ioSender asked the
user via MessageBox whether to continue loading (`ok = MessageBox.Show(...) == Yes`);
the port hardcoded the answer to `false`, leaving `if ((ok = false))` — always-false
branch, dead `block = sr.ReadLine()`. The comment says "always continue" but the code
actually **aborts** the load (loop ends, no End marker, `LoadFile` returns false).

**TODO:** pick one policy and make code+comment agree — either genuinely skip parse
errors and keep reading (`ok = true`, continue), or abort explicitly — then replace
the always-false condition so the intent is self-documenting.

## Other known deferred items

- Foam CNC mode: functional foam-specific behavior (mode is persisted but only
  changes jog layout today).
- Telnet / WebSocket transports: `TcpGrblTransport` covers plain TCP; telnet
  option negotiation and WebSocket are still not implemented. See
  `docs/p0-controller-plumbing-plan.md` D.3.

> The full ioSender gap map lives in `docs/iosender-gaps.md`. The cycle-bar
> realtime bytes, Home/Unlock/Reset bindings, the status read loop / `$I` handshake /
> accessory parsing (P0-A/B/C), and the connect dialog (P0-D) are all landed; older
> notes claiming otherwise are stale.
