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
