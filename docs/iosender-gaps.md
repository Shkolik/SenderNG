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
controller. Demo mode masks these gaps in day-to-day use.- [x] **Status-report read loop** — `RealGrblService` now runs a background reader
      (injectable `IGrblTransport`) that feeds status reports into
      `StatusReportParser` continuously, plus a suspendable `?` poller
      (ioSender `PollGrbl` semantics). Verified on a real grbl 1.2i board @ COM6
      (live status, `$$`, `$I`). The poller backs off to
      `IdlePollIntervalMs` (1.5 s) once the controller is settled in Idle with
      fresh reports and no recent send, so the console isn't spammed with
      identical `<Idle|...>` lines; jog/run/hold, a send in the 2 s linger
      window, or a controller that stopped reporting all keep the fast rate.

SenderNG's jog is **click-to-jog** (each button press sends one `$J=G91 …`
command); **continuous jog** (hold-to-jog with velocity-based `$J=` and
stop-on-release via 0x85 JogCancel) is now implemented (2026-09-17) — see
`docs/continuous-jog.md`. The hold is scoped to the owning pointer, so
drag-off / capture-lost ends the jog, and a disconnect or the jog panel
closing cancels an active hold.


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

- [x] DRO (`Modules/Fixed/DroView`) — positions + zero buttons (zero writes the
      active work offset via `G10 L20 P0`, persisted on the controller — iSender
      parity; a G92 shift would read 0 on screen but never reach Work Parameters).
- [x] Work Parameters — selecting a coordinate system in the editor activates it
      on the controller (the combo sends `G54`..`G59.x`, iSender parity), so the
      DRO zero and subsequent writes land in the row the user is looking at;
      programmatic selection (editor construction, row-rebuild fallback) never
      sends an activation. The ACTIVE system is never assumed: connect reads the
      `$G` modal dump (`[GC:...]`, fallback G54) and grblHAL's live `WCS:` report
      field, and the editor follows report-driven changes without echoing.
      After a DRO zero the app re-reads `$#` so the offsets table reflects the
      controller immediately; positions always come from the controller's MPos /
      WCO report fields (WPos is derived controller-style, never local math).
- [x] Jog widget (`Modules/Plugins/Jog`) — cross + linear axes, distances/speeds,
      foam Z/A cross, padlock. Layout is schema-driven (2026-09-07,
      `docs/machine-axis-schema.md`): crosses from the machine-type preset's
      planes, letters from the controller's `[AXS:]` report (XYUV-style boards
      display correctly), and an under-axed multi-plane machine hides its
      second plane with a status hint instead of showing a dead cross.
- [x] MPos widget (`Modules/Plugins/Mpos`) — machine-position readouts.
- [x] Outline widget (`Modules/Plugins/Outline`) — feed rate + Go perimeter.
- [x] Coolant (`Modules/Plugins/Coolant`) — Flood/Mist toggles + overrides.
- [x] Cutting (`Modules/Plugins/Cutting`) — Feed/Rapids/Speed overrides, spindle
      CW/CCW/Off + RPM input, plus the live feed/speed readout (FS: report field).
      The readout mirrors the controller (ioSender parity) — a cycle stop shows
      100% because grbl resets overrides on soft reset, an app restart shows the
      controller's *actual* overrides, and the app never invents values. While the
      machine is in a feed hold it is physically stopped, but grblHAL keeps
      reporting the programmed feed (F word × override) in FS:, so the widget
      reads 0 during Hold/Hold:0/Hold:1 and restores the live value on resume.
      Speed is not zeroed on a hold — a real spindle keeps running through one.
- [x] Program view (`Modules/Fixed/Program`) — line list with status, program
      limits panel, load/send. 2026-08-31: now backed by the engine: Open File
      runs the file through `GCodeProgram` (the same `GCodeParser` pass the
      streamer consumes) and REFUSES any file with errors (`Line n: …` dialog —
      no skip-and-load, a partial program is a different program). Streaming
      sends lines awaiting each ok via `SendProgramLineAsync` (status polls
      stay live during a run), rows show `·/@/✓/✕` with per-line error detail,
      and the run stops on the first controller rejection or Alarm (never
      skips). 2026-08-31 (single-run-surface consolidation): the in-view
      Run/Pause/Resume/Stop transport bar is gone — the cycle bar is the only
      run control, so the run stays drivable while watching the Console tab,
      and block progress reads from the status bar (`StatusBarBlocks`). The
      grid is header-aligned: vertical 1px gridlines on shared
      `44|1|28|1|1*` columns, header text matching the cells' alignment,
      zero-padded rows so the hover highlight covers the row exactly, and the
      last-sent line auto-scrolls to the vertical center so the next commands
      stay in view. 2026-09-02: the limits panel re-computes when controller
      work parameters arrive — a program loaded before connecting was computed
      with an empty emulator table (zero offsets, no G28/G30 rows), so the
      first `$#` snapshot (and every offset change after that) re-runs the
      emulator-seeded computation and re-bases the machine-coordinate moves
      (G53/G28/G30); plain work-coordinate moves are unchanged by design.
      `GCodeValidator` stays as the stateless re-validation
      probe for future edit/save work (no ioSender counterpart exists).
- [x] Console (`Modules/Fixed/Console`) — renders the controller's raw output
      with ioSender console semantics: Verbose / Filter-out-realtime /
      Show-all-realtime options (persisted), Clear button, 200-line cap, and
      Always-scroll-to-end autoscroll (ioSender's TextBoxUtilities semantics,
      realized on the ScrollViewer's extent change so the pin lands after the
      new text is measured rather than one line behind). Demo
      mode answers every sent command with `ok`/`error:22` on the raw stream
      (2026-08-31 — the program streamer needs responses offline), so the
      console shows command replies but no mocked status reports; connected to
      a real board it shows $I identity/options, $$ dumps,
      `[PRB:...]` and `error:` replies as sent. The proxy relays raw lines as a
      bounded replay so the console still shows the connect burst even when the
      Console tab is realized lazily after connecting. The connect dialog also
      auto-re-enumerates serial ports every second, so plugging in a controller
      revives the otherwise-grayed Serial tab without a manual refresh.
- [x] Offsets (`Modules/Fixed/Offsets` flyout) — controller-backed
      coordinate/tool-table editor: live snapshot updates, G10 write-back (G54–G59
      on classic boards, G54–G59.3 on grblHAL), and `$#` readback after writes.
      Capability-aware: only systems the firmware supports are offered, the editor
      disables itself while the controller is in Alarm, and the tool table shows
      only when the controller is grblHAL with tool slots (classic grbl 1.1 /
      1.2i forks have no `G10 L1`). Verified on all three boards (1.1h, mega5x
      1.2i, grblHAL RP2040) via the opt-in `RealGrblHardwareTests` battery —
      incl. a G10 L1 tool-table save/readback on grblHAL.
- [x] Grbl settings (`Modules/Fixed/GrblSettings`) — `$$` view/edit.
- [x] App settings (`Modules/Fixed/AppSettings`).
- [x] MDI row (editable combo + history + Send). The static signals/limits
      summary right of the Send button was removed as redundant — the signals
      panel already shows live state (the summary text never updated).
- [x] Signals panel (axis indicators + probe state).
- [x] State indicator (Idle/Run/Alarm/Hold + brush).
- [x] Cycle bar (Cycle Start / Feed Hold / Stop / Rewind) — the **only** run
      control surface (the Program view's transport bar was removed: one
      session, one control set, usable while watching the Console tab).
      Gated on the session state rather than the machine state — buttons stay
      visible and enable/disable: Start = run or resume, Hold = pause, Stop =
      hard reset while Held (the only state a reset makes sense from), Rewind
      = local rewind whenever no session is active. The session is purely
      local orchestration — it only decides *when*; the controller is still
      driven **exclusively** by the same realtime bytes
      (`0x7E`/`0x21`/`0x18`) and the G-code line stream awaiting each `ok`.
      What changed is the timing: each state transition (start/hold/resume/
      reset) issues its byte once at the right moment instead of each button
      blindly sending it — MDI without a loaded program still passes the raw
      bytes straight through. ROADMAP's "placeholder strings" note is stale
      and should be removed.

### Missing widgets (port from ioSender `CNC Controls`)

- [x] **Goto widget** (`GotoControl`/`GotoFlyoutControl`) — implemented as the
      `Plugins/Goto` flyout: G28/G30 "goto position" buttons, G28.1/G30.1
      record-reference-point buttons, numeric per-axis rows + Go (`G53 G0`),
      gated on homed state (tracked via completed `$H`; cleared on alarm/soft
      reset). The visible axis count is a persisted preference (App Settings,
      1–9) capped by the connected controller's reported axis count, so a
      4-axis preference shows 3 rows on a 3-axis controller.
- [x] **Tool table → probing integration** (`ToolView`) — the Offsets flyout
      tool table is a full editor mirroring the offsets table: a **Tool**
      column with **editor-local names** (grblHAL has no name field —
      maintainer-confirmed; names persist in `settings.json` keyed by a
      **controller fingerprint** = the `$I` firmware identity, deliberately
      without the COM port, so byte-identical boards share names and moving a
      machine to another port keeps them), **X/Y/Z/… + tip-radius cells**
      (radius parsed from the real `[T:id|axes|radius]` `$#` field and
      round-tripped via `G10 L1 R`), **Get current position** (touch-off from
      machine position), **Clear all / Set all** (dirty rows only, names
      persisted on save), rows = **T1..N** where N = min(mode's "Visible
      tools" setting, the controller's slot count) — T0 is excluded (a no-op
      tool: `G43 H0` cancels compensation). Shown only when the controller is
      grblHAL with tool slots (classic grbl 1.1 has no `G10 L1`); the editor
      disables itself in Alarm and offers only firmware-supported systems. The
      **tool-length probe** "with tool:" selector and the **Work Parameters**
      tool strip both list **`[None, T1, T2 (name), …]`** (names in
      parentheses when saved), default **None**, slot count following the same
      VisibleTools rule; the probe stores the length via `G10 L1 Pn Z` +
      `G43 Hn`, falling back to `G43.1Z<tlo>` on None/no table. ioSender's
      standalone `ToolView` control (I/J/Q geometry, per-click writes, its
      disabled radius field) is deliberately **not** ported — see
      `docs/plans/2026-08-31-toolview.md` (execution plan) and the plan's
      "NOT porting" list.
- [x] **Work parameters** — implemented as a SenderNG flyout rather than
      ioSender's right-panel group. It displays the active G54–G59/G59.x selector
      (clamped to the systems the firmware supports, so classic boards never offer
      G59.x), parsed controller work-coordinate/tool-table snapshots, tool
      selection, TLO/TLO-reference indicators, refresh, and tool-change state.
      Editable offset/tool-table write-back lives in the `Fixed/Offsets` flyout
      (G10 write + $# readback), not here.
- [x] **File actions (scoped)** (`FileActionControl`) — ioSender's strip is
      Load / Save / Close / Run; the SenderNG scope decision: **Open + Save +
      Close now**, **Run not ported**. File → Save / toolbar Save / Ctrl+S
      always goes through the **save picker, pre-filled with the current file
      name** (Save As is the default action). Rationale: with no editor there
      is no dirty document, so overwriting the source is never implied — a
      converter or transform must not silently rewrite the `.nc`/`.drl` it
      came from — and overwriting in place is one accept-click in the dialog
      when that *is* what the user wants. A picked path becomes the program's
      home (title, next prefill). When an editor (dirty flag) lands, quick
      Save can return. File → Close and the toolbar Close unload the loaded
      program (rows, limits panel, title) via the same path as the
      machine-profile switch; both are gated on a loaded program with no
      active session. Run control stays exclusively on the cycle bar, so a
      toolbar Run would duplicate it. Verified: ioSender's converters
      (Excellon `.drl`, HPGL `.plt`) are **open-file types**, not a separate
      flow — `GCode.File.Load` matches the extension, runs the converter
      (with a `JobParametersDialog` for feeds/speeds) and writes g-code into
      the loaded program; **Save persists that output**. SenderNG plan:
      converters (pure C# parsers, BSD) slot in as extra open types feeding
      the existing `GCodeProgram` pipeline and save through the same picker.
      Transform menu (Rotate/ArcsToLines/Compress/Wrap) is separate —
      operates on loaded g-code, see the transforms entry.
- [x] **Status bar parity** — the status bar shows exactly ioSender's four
      slots: Blocks (executed/total of the loaded program), Message (the live
      status/alarm line, star column), RunTime (elapsed while a cycle runs) and
      JogStep (the active jog distance). The SenderNG-only centered connection
      slot is gone — it duplicated Message's "Connected: <port>" text (ioSender
      has no such slot), so connection info flows through Message like upstream.
      All slots are fixed-width so Block/RunTime/JogStep changes never shift the
      other texts (Message absorbs the slack in the star column).

### Deferred scope noted here for completeness (do not start without asking)

- [ ] **Foam functional mode** — mode is persisted but only changes the jog
      layout; continuous jog works on the cross incl. Together (2026-09-17,
      see `docs/continuous-jog.md`), but foam-specific processes beyond the
      jog pad (mirrored dual-gantry jog beyond the cross, foam probing
      semantics) are not implemented.
- [x] **Home / Unlock / Reset / Check** — `$H`/`$X`/realtime Reset and the
      **Check checkbox** (`$C`, dry-run) are all bound in `MainViewModel`.
      Nothing left unbound in this area.

## Priority 2 — 3D view + advanced probing

- [ ] **3D program view** — the unified plotter: one layers+tool engine
      (2D affine projection on a plain canvas — isometric + flat views,
      no 3D lib — HelixToolkit's Avalonia package exists but is
      DX11/SharpDX (Windows-only, abandoned dep), see machine-config-
      research.md) as the third center tab (Program / 3D /
      Console) or a flyout. Mill = XY flat + XYZ isometric (no XZ/YZ
      selector — side flats are CAM-verification views, deferred) with a
      cylinder+tip tool; laser/drag-knife = flat + tool glyph (dot /
      blade circle with direction tick); foam = two carriage layers
      (isometric loft | XY/ZA flats) with the wire as the tool.      Depends
      on the machine axis-role schema from ROADMAP's "Machine physical
      configuration" item — **designed, decided and landed**
      (`docs/machine-axis-schema.md`, 2026-09-07): the plotter's schema
      blocker is gone; it consumes `MachineType.Planes` (layer decomposition)
      and `GrblInfo.AxisLetters` (labels). **A true 3D orbit view LANDED
      (2026-09-09+)** as the `SenderNG.Modules.Plotter.Nex` plugin — Vulkan
      (HelixToolkit.Nex), hidden by default, experimental — so the 3D-view
      gap is now "orbit exists, not yet the unified productized layer+tool
      engine"; see `src/SenderNG.Modules.Plotter.Nex/docs/README.md`.
- [ ] **Height-map 3D preview** — ioSender renders the probed grid as a mesh;
      SenderNG has the map data + XML save/load but no 3D visualization.
      Renderer candidates (researched with machine-config-research.md): the
      real gate is the **Avalonia host problem**, not the engine — how GPU
      output gets into a control. Windows-only → HelixToolkit.Avalonia.
      SharpDX (hosted DX11 swapchain, scene graph; SharpDX is abandoned but
      wrapped). Cross-platform → low-level engine with official C# bindings
      (Diligent, Apache-2.0) rendering offscreen + GPU→CPU readback blit;
      native island embedding (HWND/X11) only if the viewport becomes a
      core surface. Height map is small (one grid mesh) — engine choice is
      interchangeable behind a thin interface; decide the host strategy
      when this item is scoped.
- [ ] **Probe-verification preflight** (`ProbeVerify` + `ConfigControl`
      checkboxes) — "Manually validate probe is connected" and "Check probe
      status before start (G59.3)" dialogs.
- [ ] **Pre/post macros around probe runs** (`MacroDialog`, ioSender
      `ProbingMacros`).
- [ ] **Multi-probe selector** (`cbxProbe`) — physical probe vs toolsetter
      (tool table entry), drives which probe the generated program uses.
- [x] **Preview of generated probe program** before running — every probing
      tool panel has a "Show preview" checkbox + read-only textbox (ioSender
      layout); regenerates live on input changes (2026-09-05).
- [x] **Height-map apply completeness** — rebuilt on the token machinery
      (`GCodeTransformers.ApplyHeightMap`, 2026-09-05): feeds split at the
      map grid spacing, arcs split into sub-arcs with helix Z, G91 supported
      (see probing-gaps.md #5).
- [x] **Rotation apply origin** — token-level `GCodeTransformers.Rotate` with
      the in-panel origin selector (program-limits corners / center / edge
      midpoints / DRO / 0,0), probed slope converted with Atan (2026-09-05,
      probing-gaps.md #6).
- [x] **"Determine X0,Y0 for right-angled workpiece"** — third left-edge probe
      computes the corner (`ProbeMath.ComputeCorner`); corner becomes work
      zero (G92/G10) and the rotation applies about it (2026-09-05, closes
      probing-gaps.md "Rotation extras").

## Priority 3 — Steppers calibration

- [ ] **Stepper calibration wizard** (`StepperCalibrationWizard`) — port the
      full measurement/calibration flow.

## Priority 4 — Everything else

Explicitly deferred per architecture §3.4. Tick when deliberately started.

- [ ] **Lathe wizards** (`CNC Controls Lathe`).
- [ ] **DragKnife** (`CNC Controls Dragknife`) — transform + config.
- [x] **Converters** (`CNC Converters`) — Excellon / HPGL import. **LANDED
  (2026-09-04):** module plugins — one DLL per converter discovered via the
  `Modules/` directory scan, each with its own App Settings leaf, locale
  catalog and parameters dialog ("Do not show again" → silent converts);
  app knows only the `IGCodeFileHandler` contract; `IViewFactory` hosts
  plugin dialogs — see `docs/plans/2026-09-04-converters.md`.
- [ ] **SD card view** (`SDCardView`).
- [ ] **Camera** (`CNC Controls Camera`, AForge/V4L2) + Camera menu.
- [ ] **THC monitor / PID log** (`THCMonitorControl`, `PIDLogView`).
- [ ] **Trinamic view** (`TrinamicView`).
- [ ] **MPG** (manual pulse generator) support.
- [-] **Errors & alarms dialogs** (`ErrorsAndAlarms`, `GrblAlarmList`,
      `GrblErrorList`) — deferred: grbl errors/alarms are reported in place
      (console decoration via `GrblErrorCodes.Decorate`, status bar message,
      program-row error detail). A browseable alarm/error history dialog is a
      nice-to-have, not a core gap; Help menu has About + Libraries tabs + a
      Modules tab surfaced from `IPluginInfo` metadata.
- [~] **G-code transforms menu** (`GCodeRotate`, `ArcsToLines`, `GCodeCompress`,
      `GCodeWrap`) — File → Transform submenu. **Rotate / ArcsToLines /
      Compress LANDED (2026-09-04)** in Core (`GCodeTransformers`) behind the
      File → Transform menu (Rotate… / Arcs to lines… / Compress dialogs;
      compress comment handling is a profile-scoped Program setting — the
      "strip ;-comments" middle mode was dropped because `;` lines never
      tokenize, see `docs/plans/2026-09-04-gcode-transforms.md`).
      **Remaining:** Wrap = rotary mapping (X/Y → A/B, diameter,
      Z0-at-center) with **G93 inverse-time feed switching** for combined
      moves — deferred until machine presets land. Probing migration
      **LANDED (2026-09-05)**: height-map apply and rotation rebuilt at this
      token level (instead of the line-based rewriting), closing the
      documented G91/arc-Z and rotation-origin limitations, plus the
      "Determine X0,Y0" corner extra — see
      `docs/plans/2026-09-04-gcode-transforms.md` M4–M5.
- [x] **Localization** — ioSender ships 9 locales; SenderNG ships an en/ru
      JSON catalog with per-plugin overrides and live switching — see
      `docs/localization.md`; more locales are catalog-only work.
- [ ] **Telnet / WebSocket transports** — `TcpGrblTransport` covers plain TCP
      (grblHAL Ethernet); telnet option negotiation and WebSocket remain
      deliberate stubs.

## Cross-cutting tech debt (architecture §8)

- [ ] **`Grbl.cs` stubs** — ~320 lines vs upstream 3438; caps parsing ported,
      rest compile-only stubs. Clean up as features touch them.
- [ ] **`GrblInfo` static state** — global mutable static; partly mitigated by
      pure `ParseCaps`.- [x] **Build warnings** — the nullability cluster (CS8602/CS8600/CS8601/CS8603/
      CS8604/CS8618/CS8620/CS8622/CS8625) was swept to zero, and the follow-up
      "~18 warnings remain (mostly xUnit1031, a few CS86xx)" claim proved stale:
      a full rebuild on 2026-09-07 showed only two CS8602 in `Converters.cs`
      (the `EnumAndBoolConverter` guard didn't flow null-state into the indexer
      accesses — fixed by hoisting `values[0]` into the pattern local). The
      build is warning-free; keep it that way (architecture §9). Verified again
      by full rebuild 2026-09-17 after sweeping the CS8601 in
      `PlotterNexConfig.cs`, the CS8605 cluster in `PlotterLiveRunHeadlessTests.cs`,
      the CS1998 in `ProbeViewModelTests.cs` and the xUnit2013 in
      `ConnectViewModelTests.cs`.

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
| ToolView | `Fixed/Offsets` tool table + Work Parameters/Probe selectors | done — editor (names, touch-off, radius, VisibleTools) + `[None, Tx (name)]` selectors; ioSender's standalone control deliberately not ported |
| WorkParametersControl | `Fixed/WorkParameters` flyout | done (read/select; editable write-back in `Fixed/Offsets`) |
| FileActionControl | File → Open/Save/Close | P1: Save = picker pre-filled with current name (no implicit overwrite) |
| StepperCalibrationWizard | — | **missing** (P3) |
| GCodeViewer / RenderControl | `Modules/Plotter` plugin | done (2026-09-08) — flat quadrant views + iso loft, frontier coloring (P2); sibling `Modules/Plotter.Nex` adds an experimental Vulkan 3D orbit view (2026-09-09+, hidden by default) |
| ProbeVerify / ConfigControl / MacroDialog | — | **missing** (P2) |
| SDCardView | — | deferred (P4) |
| Camera | — | deferred (P4) |
| Lathe wizards | — | deferred (P4) |
| Dragknife | — | deferred (P4) |
| Converters | `SenderNG.Modules.Excellon` + `.Hpgl` (module plugins; shared converter scaffolding in the internal `SenderNG.Shared` folder, SDK in `SenderNG.Modules.Common`) | done — open-flow dispatch by extension, per-plugin parameters + ShowDialog, Modules/ scan (see `docs/plans/2026-09-04-converters.md`, `docs/plugin-development.md`) |
| THC monitor / PID log / Trinamic | — | deferred (P4) |

## How this was produced

- ioSender `master` (`3a817cf`) inspected at `MainWindow.xaml`, `JobView.xaml`,
  `UIViewModel.cs` (sidebar/flyout registration), the `CNC Controls` widget
  inventory, and the `CNC Controls Probing` module.
- SenderNG current state read from `HostSetup.cs` (module registration),
  `MainWindow.axaml`, and each module's view/VM.
- ROADMAP/architecture items cross-referenced; stale claims (cycle bar bytes,
  Home/Unlock/Reset unbound) corrected in this plan and in the source docs.
