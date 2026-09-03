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

## Legacy emulator data source vs. modern WorkParameters snapshot — DONE

**Resolved (ambient provider bridge):** `Machine.WorkParametersProvider` is a static
`Func<WorkParameters?>` registered once by the host (`MainViewModel` subscribes to
`IGrblService.WorkParametersChanges` and closes over the latest immutable snapshot).
`Machine.Reset()` — run by every `GCodeEmulator`/`GCodeParser` — now seeds its
coordinate-system table (G54..G59.3 plus G28/G30/G92 rows when the controller reports
them), tool table and active system from that snapshot instead of the stubbed legacy
`GrblWorkParameters` statics; `NGCExpr` PRB expressions (`#5061-#5069`, `#5070`) read
the snapshot through `Machine.GetProbePosition`/`ProbeSuccess`. Null provider keeps the
old empty-table behavior. Consequences:

- Emulator G54..G59.3 activation, G10 L2 (P0 = active system, P1-9, P10-12 =
  G92/G28/G30 rows), G28/G30, G92/G92.1/G92.2 and G43 (seeded tool table) all
  execute their real logic against controller data. The parser's `G10 L2 P0`
  gate was also fixed: `P0` maps to the active system (`"Current"` code) instead
  of being rejected and silently dropped.
- PRB expressions return the controller's reported probe position/success instead
  of being dropped as unresolvable.

Covered by `EmulatorWorkParametersBridgeTests` (7 tests: seeded activation +
G10 P0 write, G10 L2 Pn, G28, G92 (+P10 write), PRB expressions, G43 via seeded
tool table, null-provider default). Note: the emulator applies offsets only in the
G53/G28/G30 machine-coordinate paths — regular moves are modeled in machine
coordinates, so seeded offsets do not shift plain-program limits (pre-existing
emulator semantics, unchanged by the bridge).

## GCodeJob.LoadFile parse-error policy — DONE

**Resolved (delegate policy):** the CS0665 stub is gone. `LoadFile(filename,
Func<ParseErrorInfo, bool>? onParseError)` takes the error policy as a delegate:
the handler receives the raw offending line, its line number, and the parser
message, and returns `true` to skip the line and keep loading or `false` to
abort. With **no handler the load fails fast** (clears rows, returns `false`).
`ParseErrorInfo` is a record, keeping Core UI-free — the ioSender-style
Yes/No MessageBox belongs to the future File-load feature's view layer, which
will pass it as the delegate (`Yes` → skip, `No` → abort, exactly ioSender's
`ok = MessageBox.Show(...) == Yes` semantics). Covered by `GCodeJobTests`
(4 tests: fail-fast default, skip-and-continue, abort, clean file + limits).

## Other known deferred items

- **Settings split: `global.json` + machine profiles** — replace the
  Mill/Foam mode switch with named machine profiles (a profile = machine
  type preset + connection + per-module settings), per
  `docs/settings-migration.md`. Prerequisite for Machine physical
  configuration below: jog/plotter/limits consumers read the active
  profile's machine type, and a shop with two mills of different
  geometry becomes expressible. Encompasses the settings-abstraction
  work (Global vs Profile tiers) plus the profile-manager UI in the App
  Settings Global group.
- **Keyboard shortcuts / key-map settings** — global hotkeys for the
  frequently-used buttons (cycle bar Start/Hold/Resume/Stop, Rewind, jog
  directions, MDI focus, Home/Unlock/Reset) with a settings screen to remap.
  Plumbing first: a keymap service (action → key binding, persisted like the
  other settings), command hookup in MainWindow, then the settings tab.
  Check ioSender's shortcut set for parity defaults before finalizing.
- **Machine physical configuration** — the app has no model of the machine's
  axis layout today: foam cutters are two-carriage machines (X/Y work plane
  plus an Z/A second plane), mills are XYZ cross-axis, and the app only knows
  "N axes" from `$I [AXS:]`. This item defines a machine geometry model
  (axis roles, workplane semantics, kinematics flavor) that drives jog
  layout, limits presentation and — critically — the g-code plotter, whose
  projection depends on axis semantics.
  **Studied together with the 2D/3D program view** (see
  `docs/iosender-gaps.md` P2): the coupling is real, so the axis-role
  **schema** is designed first as part of this item; the plotter then builds
  against that model and the config UI can follow later without blocking it.
  Subsumes the axis-layout half of Foam CNC mode below. Research on how
  other senders (ioSender, UGS, LinuxCNC, Mach, Centroid, Masso, foam CAM
  tools) model this lives in `docs/machine-config-research.md`; V1 lean is
  named presets (Mill / Foam) with a per-axis role map underneath. The
  foam plotter has field precedents now — GrblHotWire's two flat canvases
  (XY | ZA, one per carriage plane) and hoWiGS's isometric loft (see the
  research doc). V1 decision: isometric + flat views on a plain canvas,
  no 3D lib; true 3D orbit deferred. **Storage prerequisite:** profiles
  replace the Mill/Foam "mode" split of `settings.json` (one file per
  machine + `global.json`) — the migration plan is in
  `docs/settings-migration.md` and lands first.
- **Continuous jog** — hold-to-jog with continuous motion: velocity-based
  `$J=` without a target distance (ioSender's Continuous mode), feedrate
  selection, and stop-on-release (hold realtime byte). Axis layout comes
  from the existing jog pad, later from Machine physical configuration
  above.
- **Converters (Excellon `.drl` / HPGL `.plt` → g-code)** — land right
  **after File actions**: verified in ioSender source, converters are
  open-file types, not a separate flow — `GCode.File.Load` matches the
  extension, runs the converter (with a JobParameters dialog for
  feeds/speeds), and writes g-code into the loaded program; **Save
  persists that output**. Ports of the BSD `Excellon2GCode` /
  `HpglToGCode` parsers feeding the existing `GCodeProgram` pipeline (see
  the File actions entry in `docs/iosender-gaps.md`).
- **G-code transforms (Rotate / ArcsToLines / Compress / Wrap)** —
  token-level rewrites over the existing parser/emulator machinery, per
  the verified mechanics in `docs/iosender-gaps.md`. **Ordering
  constraint: land before finalizing probing** — the probing module's
  height-map apply and rotation will be rebuilt on the same token-level
  machinery (replacing the line-based `ProgramTransform`), so the shared
  transform infrastructure comes first and probing reuses it. The File →
  Transform menu itself can follow File actions; converters stay
  independent open-file types.
- Foam CNC mode: functional foam-specific behavior (mode is persisted but only
  changes jog layout today).
- Telnet / WebSocket transports: `TcpGrblTransport` covers plain TCP; telnet
  option negotiation and WebSocket are still not implemented. See
  `docs/p0-controller-plumbing-plan.md` D.3.
- **Camera** — deferred indefinitely: no planned in-app value. If ever
  wanted, it comes as a 3rd-party plugin; the old plan's libs
  (OpenCvSharp / DirectShow / V4L2 capture) live here in the decision tree
  only, not in the README.

> The full ioSender gap map lives in `docs/iosender-gaps.md`. The cycle-bar
> realtime bytes, Home/Unlock/Reset bindings, the status read loop / `$I` handshake /
> accessory parsing (P0-A/B/C), and the connect dialog (P0-D) are all landed; older
> notes claiming otherwise are stale. The cycle bar is now the single run-control
> surface (the Program view's transport bar was removed) and gates on the session
> state: buttons stay visible, enabling/disabling as the session starts, holds,
> resumes, or stops. The session is local orchestration only — the controller is
> still driven exclusively by the same realtime bytes, now sent once per state
> transition instead of blindly per button press.
>
> Status bar / MDI / Cutting widget updates: the status bar is ioSender's exact
> four-slot layout (Blocks | Message | RunTime | JogStep) in fixed-width slots, so
> Block/RunTime/JogStep changes never shift the other texts; the separate centered
> connection slot is gone — it duplicated Message's "Connected: <port>" text, and
> connection info flows through Message like upstream. The static signals/limits
> summary right of the MDI Send button was removed as redundant (the signals panel
> shows live state). The Cutting widget's live feed reads 0 during a feed hold
> (grblHAL reports the programmed feed while the machine is stopped) and restores
> on resume; speed is untouched. Overrides mirror the controller's `Ov:` report
> (ioSender parity): a cycle stop shows 100% because grbl resets overrides on soft
> reset, an app restart shows the controller's actual overrides — the app never
> invents values.
