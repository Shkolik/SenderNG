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

## Other known deferred items

- Foam CNC mode: functional foam-specific behavior (mode is persisted but only
  changes jog layout today).
- P0-D.2 connect dialog: port list + refresh + baud selector UI (D.1 baud
  plumbing is done). See `docs/p0-controller-plumbing-plan.md`.

> The full ioSender gap map lives in `docs/iosender-gaps.md`. The cycle-bar
> realtime bytes and Home/Unlock/Reset bindings are done, and the status read
> loop / `$I` handshake / accessory parsing landed (P0-A/B/C); older notes
> claiming otherwise are stale.
