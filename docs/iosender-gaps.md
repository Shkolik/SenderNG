# SenderNG ↔ ioSender — Gap Map & Checkable Plan

> This is the high-level, checkable plan for closing the gap to the original
> ioSender (`terjeio/ioSender` `master` @ `3a817cf`, plus the `foamcut` branch
> `8ea54fb`). Priorities come from `docs/architecture.md` §3 and
> `docs/ROADMAP.md`. Detailed per-module deviations live in
> `docs/probing-gaps.md`; this document is the umbrella view.
>
> **How to use:** tick a box when the item is *done and verified*. "Partial"
> items list exactly what exists today; finish them or explicitly mark them
> won't-fix with a note.

## Priority 0 — Real-controller plumbing (gates everything)

> Implementation plan with checkable items, parallelization markers and a
> sequencing summary: **`docs/p0-controller-plumbing-plan.md`**.

Nothing on a real machine works reliably until `RealGrblService` can talk to a
controller. Demo mode masks these gaps in day-to-day use.

- [x] **Status-report read loop** — `RealGrblService` now runs a background reader
      (injectable `IGrblTransport`) that feeds status reports into
      `StatusReportParser` continuously, plus a suspendable `?` poller
      (ioSender `PollGrbl` semantics). Verified on a real grbl 1.2i board @ COM6
      (live status, `$$`, `$I`).
- [x] **Full grbl handshake** (`$I` + `[OPT:...]`, grblHAL build/version
      detection) and an exposed `ControllerCapabilities` on `IGrblService`; on
      connect the axis count is pushed into the legacy `GrblInfo` static and the
      live `MachineState` so the DRO shows the board's real axes
      (ROADMAP top item). `ControllerInfoParser` reads `[VER:]` / `[AXS:]` /
      `[OPT:]` (real 1.2i output verified: axes from `[AXS:]`, tools from `[OPT:]`).
- [x] **Accessory-state parsing** — `StatusReportParser` reads the grblHAL `A:`
      accessory field into `MachineState.Flood`/`Mist`/spindle (cp. ioSender
      `GrblViewModel.cs`); classic grbl leaves them untouched.
- [x] **Connect dialog / port discovery polish** — a PortDialog-style `ConnectView`
      with three tabs (Serial: port list + refresh + baud + On-connect reset action,
      grayed when no ports; Network: TCP endpoint, enabled only when settings allow
      network connectivity; Demo: simulated controller with disclaimer, always
      available) replaces the old auto/first-port string path. The app starts
      disconnected (never in demo mode), opens the connect dialog right after
      startup, and shows an obvious "NOT CONNECTED" state until the user picks a
      mode.
- [x] **On-connect reset action (ioSender Port-dialog dropdown)** — Serial tab has
      "No action / Toggle DTR / Toggle RTS" (persisted globally). `SerialGrblTransport`
      applies the pulse right after the port opens exactly like ioSender
      `SerialStream.cs` connect code: Toggle DTR drops DTR low ~5ms then asserts it
      (resets an 8-bit Arduino bootloader on connect — needed for clean grbl start),
      Toggle RTS asserts for ESP32-style boards. Default is No action; the reset mode
      is forwarded through `RealGrblService` into the transport's `Open` so it also
      re-runs on a reconnect. Verified on hardware: the pulse path runs without error
      and stays non-destructive (this grblHAL board doesn't need it and is slow to
      re-identify after a pulse, which matches ioSender's Arduino-targeted behavior).
- [x] **Network (TCP) transport** — `TcpGrblTransport` implements `IGrblTransport`
      over a plain TCP socket for grblHAL Ethernet boards (`host[:port]`, default
      port 23); it plugs into the existing `RealGrblService`. `NetworkAvailable` /
      `NetworkEndpoint` settings gate the dialog's Network tab. Telnet option
      negotiation and WebSocket remain future work.

## Priority 1 — Base widgets and processes (mill + foam)

The core UI widgets and the processes behind them, per architecture §3.1.
Many already exist; the gaps are listed per widget.

### Done / largely done (maintained, verify occasionally)

- [x] DRO (`Modules/Fixed/DroView`) — positions + zero buttons.
- [x] Jog widget (`Modules/Plugins/Jog`) — cross + linear axes, distances/speeds,
      foam Z/A cross, padlock.
- [x] MPos widget (`Modules/Plugins/Mpos`) — machine-position readouts.
- [x] Outline widget (`Modules/Plugins/Outline`) — feed rate + Go perimeter.
- [x] Coolant (`Modules/Plugins/Coolant`) — Flood/Mist toggles + overrides.
- [x] Cutting (`Modules/Plugins/Cutting`) — Feed/Rapids/Speed overrides, spindle
      CW/CCW/Off + RPM input.
- [x] Program view (`Modules/Fixed/Program`) — line list with status, program
      limits panel, load/send.
- [x] Console (`Modules/Fixed/Console`) — renders the controller's raw output
      with ioSender console semantics: Verbose / Filter-out-realtime /
      Show-all-realtime options (persisted), Clear button, 200-line cap. Demo
      mode publishes no raw lines, so the console no longer shows mocked status
      strings; connected to a real board it shows $I identity/options, $$ dumps,
      `[PRB:...]` and `error:` replies as sent. The proxy relays raw lines as a
      bounded replay so the console still shows the connect burst even when the
      Console tab is realized lazily after connecting. The connect dialog also
      auto-re-enumerates serial ports every second, so plugging in a controller
      revives the otherwise-grayed Serial tab without a manual refresh.
- [x] Offsets (`Modules/Fixed/Offsets` flyout) — controller-backed
      coordinate/tool-table editor: live snapshot updates, G54–G59 G10 write-back,
      and $# readback after writes (verified against a bench grbl 1.1h board).
- [x] Grbl settings (`Modules/Fixed/GrblSettings`) — `$$` view/edit.
- [x] App settings (`Modules/Fixed/AppSettings`).
- [x] MDI row (editable combo + history + Send).
- [x] Signals panel (axis indicators + probe state).
- [x] State indicator (Idle/Run/Alarm/Hold + brush).
- [x] Cycle bar: Cycle Start / Feed Hold / Stop / Rewind — already
      `SendRealtime` (`0x7E`/`0x21`/`0x18`) as of `MainViewModel`; ROADMAP's
      "placeholder strings" note is stale and should be removed.

### Missing widgets (port from ioSender `CNC Controls`)

- [x] **Goto widget** (`GotoControl`/`GotoFlyoutControl`) — implemented as the
      `Plugins/Goto` flyout: G28/G30 "goto position" buttons, G28.1/G30.1
      record-reference-point buttons, numeric per-axis rows + Go (`G53 G0`),
      gated on homed state (tracked via completed `$H`; cleared on alarm/soft
      reset). The visible axis count is a persisted preference (App Settings /
      widget selector, 1–9) capped by the connected controller's reported axis
      count, so a 4-axis preference shows 3 rows on a 3-axis controller.
- [ ] **Tool table → probing integration** (`ToolView`) — the Offsets flyout
      now has a tool table (optional via App Settings, shown only on grblHAL
      controllers — classic grbl 1.1 has no `G10 L1`) with a single "Save tool
      table" button that writes only changed tools, but it is not yet wired
      into probing (the "with tool" selector / `G43 Hn` tool-length feed is
      still missing).
- [x] **Work parameters** — implemented as a SenderNG flyout rather than
      ioSender's right-panel group. It displays the active G54–G59/G59.x selector,
      parsed controller work-coordinate/tool-table snapshots, tool selection,
      TLO/TLO-reference indicators, refresh, and tool-change state. Editable
      offset/tool-table write-back lives in the `Fixed/Offsets` flyout
      (G10 write + $# readback), not here.
- [ ] **File actions** (`FileActionControl`) — Load / Save / Close / Run from a
      toolbar; SenderNG has Open (menu + toolbar) and program send in the
      Program view, but no Save/Close file actions.
- [ ] **Status bar parity** — ioSender status bar shows Blocks, Message,
      RunTime, JogStep; SenderNG's Block/Jog-step texts are still hardcoded
      placeholders and Ready is now bound to the connection status
      (architecture §8 "unbound UI").

### Deferred scope noted here for completeness (do not start without asking)

- [ ] **Foam functional mode** — mode is persisted but only changes the jog
      layout; foam-specific processes (mirrored dual-gantry jog beyond the
      cross, foam probing semantics) are not implemented.
- [ ] **Home / Unlock / Reset buttons** — now bound to `$H`/`$X`/realtime Reset
      in `MainViewModel` (ROADMAP stale); the **Check checkbox** in the left
      panel is still unbound.

## Priority 2 — 3D view + advanced probing

- [ ] **3D program view** — port ioSender's `GCodeViewer` (RenderControl /
      HelixToolkit) as the third center tab (Program / 3D / Console) or a
      flyout.
- [ ] **Height-map 3D preview** — ioSender renders the probed grid as a mesh;
      SenderNG has the map data + XML save/load but no 3D visualization.
- [ ] **Probe-verification preflight** (`ProbeVerify` + `ConfigControl`
      checkboxes) — "Manually validate probe is connected" and "Check probe
      status before start (G59.3)" dialogs.
- [ ] **Pre/post macros around probe runs** (`MacroDialog`, ioSender
      `ProbingMacros`).
- [ ] **Multi-probe selector** (`cbxProbe`) — physical probe vs toolsetter
      (tool table entry), drives which probe the generated program uses.
- [ ] **Preview of generated probe program** before running (program is
      available via `ProbeProgramBuilder.Lines`).
- [ ] **Height-map apply completeness** — line-based `ApplyHeightMap` currently
      skips G91/incremental programs and arc Z correction (see probing-gaps.md
      #5).
- [ ] **Rotation apply origin** — `ProgramTransform.Rotate` always uses (0,0);
      ioSender lets you pick a program-limit corner (probing-gaps.md #6).

## Priority 3 — Steppers calibration

- [ ] **Stepper calibration wizard** (`StepperCalibrationWizard`) — port the
      full measurement/calibration flow.

## Priority 4 — Everything else

Explicitly deferred per architecture §3.4. Tick when deliberately started.

- [ ] **Lathe wizards** (`CNC Controls Lathe`).
- [ ] **DragKnife** (`CNC Controls Dragknife`) — transform + config.
- [ ] **Converters** (`CNC Converters`) — Excellon / HPGL import.
- [ ] **SD card view** (`SDCardView`).
- [ ] **Camera** (`CNC Controls Camera`, AForge/V4L2) + Camera menu.
- [ ] **THC monitor / PID log** (`THCMonitorControl`, `PIDLogView`).
- [ ] **Trinamic view** (`TrinamicView`).
- [ ] **MPG** (manual pulse generator) support.
- [ ] **Errors & alarms dialogs** (`ErrorsAndAlarms`, `GrblAlarmList`,
      `GrblErrorList`) — Help menu currently has About/Libraries placeholders.
- [ ] **G-code transforms menu** (`GCodeRotate`, `ArcsToLines`, `GCodeCompress`,
      `GCodeWrap`) — File → Transform submenu.
- [ ] **Localization** — ioSender ships 9 locales; SenderNG is hardcoded
      en-US (deliberate strip).
- [ ] **Telnet / WebSocket transports** — `TcpGrblTransport` covers plain TCP
      (grblHAL Ethernet); telnet option negotiation and WebSocket remain
      deliberate stubs.

## Cross-cutting tech debt (architecture §8)

- [ ] **`Grbl.cs` stubs** — 288 lines vs upstream 3438; caps parsing ported,
      rest compile-only stubs. Clean up as features touch them.
- [ ] **`GrblInfo` static state** — global mutable static; partly mitigated by
      pure `ParseCaps`.
- [ ] **Build warnings** — CS8602 / CS8618 / xUnit1031 etc. remain; keep the
      count flat while working (architecture §9).

## Reference matrix (ioSender → SenderNG)

| ioSender (CNC Controls) | SenderNG | Status |
| --- | --- | --- |
| DROControl / DROBaseControl | `Fixed/DroView` | done |
| MachinePositionFlyout | `Plugins/Mpos` | done (flyout) |
| OutlineControl / OutlineFlyout | `Plugins/Outline` | done (flyout) |
| JogFlyoutControl | `Plugins/Jog` | done (flyout) |
| CoolantControl | `Plugins/Coolant` | done |
| SpindleControl + FeedControl + OverrideControl | `Plugins/Cutting` | done |
| JobControl / GCodeListControl | `Fixed/Program` | done |
| ConsoleControl | `Fixed/Console` | done |
| OffsetView | `Fixed/Offsets` flyout | done (G10 write + readback) |
| GrblConfigView | `Fixed/GrblSettings` | done |
| AppConfigView | `Fixed/AppSettings` | done |
| MDIControl | MDI row (`MainWindow`) | done |
| SignalsControl / StatusControl | Signals + state indicator | done |
| LimitsControl | Program limits panel | done |
| MacroEditor / MacroExecuteControl | `Plugins/Macro` | done (flyout) |
| ProbingView + 6 tool controls | `Plugins/Probing` | done (flyout) — see `probing-gaps.md` |
| GotoControl / GotoFlyoutControl | `Plugins/Goto` | done (flyout, homed-gated) |
| ToolView | — | **missing** (P1) |
| WorkParametersControl | `Fixed/WorkParameters` flyout | done (read/select; editable write-back in `Fixed/Offsets`) |
| FileActionControl | Open only | partial (P1) |
| StepperCalibrationWizard | — | **missing** (P3) |
| GCodeViewer / RenderControl | — | **missing** (P2) |
| ProbeVerify / ConfigControl / MacroDialog | — | **missing** (P2) |
| SDCardView | — | deferred (P4) |
| Camera | — | deferred (P4) |
| Lathe wizards | — | deferred (P4) |
| Dragknife | — | deferred (P4) |
| Converters | — | deferred (P4) |
| THC monitor / PID log / Trinamic | — | deferred (P4) |

## How this was produced

- ioSender `master` (`3a817cf`) inspected at `MainWindow.xaml`, `JobView.xaml`,
  `UIViewModel.cs` (sidebar/flyout registration), the `CNC Controls` widget
  inventory, and the `CNC Controls Probing` module.
- SenderNG current state read from `HostSetup.cs` (module registration),
  `MainWindow.axaml`, and each module's view/VM.
- ROADMAP/architecture items cross-referenced; stale claims (cycle bar bytes,
  Home/Unlock/Reset unbound) corrected in this plan and in the source docs.
