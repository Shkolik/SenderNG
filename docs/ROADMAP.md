# SenderNG Roadmap

Gaps and planned work, roughly in priority order. Items land here when a task
surfaces a known limitation that we deliberately defer.

## Probing as an optional module plugin (P0) — investigating

**Goal:** move the whole Probing module (`Modules/Plugins/Probing` + its Core
piece) out of the shipped app and into a **3rd-party plugin** like the
Plotter / Plotter.Nex / converter modules. Core no longer ships probing, so
users who don't probe get a smaller app; plugins that want a probe-driven view
gain a dependency they can declare. The plugin boundary also re-opens the
**height-map 3D preview** gate: the plugin can render the probed grid with the
same Vulkan/HelixToolkit.Nex engine as Plotter Nex without the core app
shipping it — the "Avalonia host problem" gate is what blocked the 3D preview
in `docs/iosender-gaps.md` P2.

Blocker / gate analysis, what moves across the boundary (VM, views, runner,
profile store, `ProbeProgramBuilder`, hotkeys, locale catalog, settings leaf,
test battery), and the SDK surface that must grow (`IModule` panel vs center
tab, native file dialogs for `.map`, Ionic zip packaging) — **under
investigation**. Design spec:
`docs/superpowers/specs/2026-09-23-probing-plugin-design.md` (approved
2026-09-23; the 3D height-map renderer plugin is a named follow-up the
contract enables).

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

## Keyboard shortcuts / key-map settings — DONE

**Resolved:** a full keymap stack is live with a remap screen in App Settings.
`HotkeyService` (Core) persists action → gesture bindings in `settings.json`;
`KeymapController` dispatches key events by action id; `KeymapDispatcher`
(window-scoped in `MainWindow.axaml.cs`) owns focus/scoping and the
deactivation release. Defaults are declared per module via
`IModule.HotKeys` (`HotKey(Gesture, ActionId)`); executors come from
`IHotkeyActionProvider.CreateHotkeyActions`. Landed defaults: jog directions,
steps/feeds cycling and increments (arrow keys / NumPad, `Ctrl+arrows` for
jog.together, PageUp/PageDown Z, Home/End A), probe Run/Stop/Probe-connected
(`Alt+R`/`Alt+S`/`Alt+C`), and the MainViewModel-registered Home/Reset/etc.
actions. Tooltips render the live gesture via `HotkeyTipExtension`
(re-emits on remap / locale change). Covered by `KeymapControllerTests`,
`KeymapDispatcherHeadlessTests`, `HotkeyServiceTests`, `JogHotkeyProviderTests`,
`MainViewModelHotkeysTests`, `HotkeysSettingsLeafTests`,
`HotkeysEditorHeadlessTests`, `HotkeyTipHeadlessTests`. ioSender's shortcut
set was checked for parity defaults before finalizing.

## Other known deferred items

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
  **schema is designed and decided** (2026-09-07):
  `docs/machine-axis-schema.md` — positional roles + controller letters
  (`[AXS:]` parsing, jog migration, demo seeding are the agreed first
  landing); the plotter consumes it separately. The
  foam plotter has field precedents now — GrblHotWire's two flat canvases
  (XY | ZA, one per carriage plane) and hoWiGS's isometric loft (see the
  research doc). V1 decision: isometric + flat views on a plain canvas,
  no 3D lib; true 3D orbit deferred. **Plotter LANDED (2026-09-08, M0–M5):**
  external plugin (`src/SenderNG.Modules.Plotter`) — flat quadrant views
  (foam: both carriages on one canvas, diagonal quadrants), two-carriage iso
  loft, live frontier coloring + tool glyph, vector render path (30fps
  throttle + interaction decimation — the M4 raster cache was reverted in M5).
  **3D orbit LANDED (2026-09-09+):** `src/SenderNG.Modules.Plotter.Nex` — a
  second external plugin implementing the true 3D orbit view the V1 decision
  deferred: Vulkan-backed (HelixToolkit.Nex), "Plot 3D" center tab (order 23,
  behind Skia's order 22), hidden by default (`IsVisibleByDefault=false`),
  experimental/PoC (see
  `src/SenderNG.Modules.Plotter.Nex/docs/README.md`).
  See  **Storage prerequisite landed:**
  profiles replace the Mill/Foam "mode" split (one file per machine +
  `global.json`, profile manager in App Settings) — see
  `docs/settings-migration.md`. **Schema landed and consumers migrated
  (2026-09-07):** Jog's layout derives from the preset's planes + controller
  letters (M2), fresh profiles seed demo axes from the preset (M3); the
  plotter (P2) and Wrap remain the open consumers.
- **Continuous jog** — hold-to-jog with continuous motion (ioSender's
  Continuous mode), feedrate selection, and stop-on-release (0x85
  JogCancel). Axis layout comes from the existing jog pad, later from
  Machine physical configuration above. **Researched and designed
  (2026-09-07):** `docs/continuous-jog.md` — ioSender's single-command
  model (`$J=G91 <axis><MaxTravel> F<feed>` once per press, 0x85 on
  release; no repeat loop) adopted over the originally sketched
  repeat-`$J=`-every-100ms approach; continuous ships as a 4th distance
  step, not a separate toggle. Distance source settled: per-axis `$130+`
  max travel fetched at connect (ioSender parity) — no fallback constant;
  continuous jog is gated until the handshake reports travel (mega5x 1.2i
  and classic 1.1h 4-axis builds report it for all axes). **Implementation
  plan ready for handoff (2026-09-07):**
  `docs/continuous-jog-implementation.md` — file-by-file change list,
  code shapes, tests, and acceptance checklist. **Implemented (2026-09-17):**
  hold-to-jog continuous step (sentinel 4th distance slot, per-press
  `$J=G91 <axis><travel> F<feed>`, 0x85 JogCancel on release) with travel
  from the connect handshake's per-axis `$130+` settings. The hold is
  scoped to the owning pointer (drag-off / capture-lost ends it) and a
  disconnect or the jog panel closing cancels an active hold.
- **Converters (Excellon `.drl` / HPGL `.plt` → g-code) — LANDED.** No
  separate File menu entry: the native Open dialog advertises the converter
  types and dispatch happens in the open flow (extension match → parameters
  dialog → convert → load through the same engine gate as `.nc`). Each
  converter is a **module plugin** (own DLL discovered via the `Modules/`
  directory scan — no DLL, no converters), implementing the full module
  contract: own App Settings leaf, own locale catalog, own parameters
  dialog with a "Do not show again" checkbox (silent converts afterwards,
  re-enable from settings). App knowledge is a single Core interface
  (`IGCodeFileHandler`); `IViewFactory` lets any plugin host dialogs. Output
  is byte-faithful to ioSender v0.47 incl. documented quirks (see
  `docs/plans/2026-09-04-converters.md`). 46 tests.
- **G-code transforms (Rotate / ArcsToLines / Compress / Wrap)** —
  token-level rewrites over the existing parser/emulator machinery, per
  the verified mechanics in `docs/iosender-gaps.md`. **LANDED (M1–M3):**
  Rotate, ArcsToLines and Compress are in Core (`GCodeTransformers`,
  26 tests) behind the File → Transform menu (dialogs, profile-scoped
  compress comment setting, en/ru — see `docs/plans/2026-09-04-gcode-transforms.md`).
  **Remaining:** Wrap (rotary mapping X/Y → A/B, diameter, Z0-at-center with
  G93 inverse-time feed switching) — **P1, takeable now**: the earlier gate
  ("until the machine physical-configuration presets land") is gone — the
  schema and presets landed 2026-09-07. Probing migration **LANDED (2026-09-05)**:
  height-map apply
  and rotation rebuilt on the token machinery (the line-based
  `ProgramTransform` is deleted), closing the documented G91/arc-Z,
  rotation-origin limits (origin picker included), and the "Determine X0,Y0
  for right-angled workpiece" corner extra — see
  `docs/plans/2026-09-04-gcode-transforms.md` M4–M5.
- **Icon & pictogram asset pass (SVG)** — a design deliverable. Three sets,
  drawn by hand as SVG sources:
  1. **Toolbar / run-control icons** — replace the hand-drawn inline `Path`
     glyphs (Connect plug, Open folder, Save floppy, Close doc-X, cycle bar
     Start/Hold/Stop/Rewind, Home/Unlock/Reset, flyout ✕) with one
     consistent SVG set; the rendering side (shared resource dictionary of
     `Path` geometries vs an SVG asset pipeline) is decided when the first
     set lands.
  2. **Machine-type pictograms** — small diagrams for the profile manager /
     machine preset picker (Mill XYZ, Foam two-carriage XY | ZA, 2D
     cutter/laser); Machine physical configuration presets (above) reuse
     them in the machine list and the future plotter view headers.
  3. **Probing selector visuals — DONE:** the probe direction / touch-off /
     selection illustrations exist — letter-labeled corner/edge diagram with
     red Z-contact markers (functionally equivalent to ioSender's pictograms,
     see `docs/probing-gaps.md`). No longer gates the Probing sign-off.
- Foam CNC mode: functional foam-specific behavior (mode is persisted but only
  changes jog layout today — the layout itself is now schema-driven:
  machine-type preset planes + controller `[AXS:]` letters,
  `docs/machine-axis-schema.md`).
  Continuous jog (hold-to-jog: velocity-based `$J=` + stop-on-release 0x85)
  is implemented (2026-09-17) — design doc at `docs/continuous-jog.md`.
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
> Cross-cutting tech debt: `Grbl.cs` is ~271L vs upstream 3438L (foamcut's full
> `Grbl.cs` is 2535L) — `ParseCaps` + `[OPT:...]` field extraction ported; the
> `Grbl` partial class's `GrblViewModel`, `GrblParserState` (modal state),
> `GrblWorkParameters` (coordinate/tool tables), `GrblSettings` (settings lookup),
> `GrblAuxIO`, `PollGrbl`, and the `AxisLetter`/`Position`/`CoordinateSystem`/`Tool`
> WPF `ViewModelBase` leftovers are compile-only stubs kept so the ported engine
> files that reference them still compile. The real service uses the portable
> `ControllerCapabilities` + `WorkParameters` + `MachineState` model instead.
> `GrblInfo` is a read-only facade over an injectable `GrblInfoState` instance
> (resolved 2026-09-19 — no ambient write path remains).
> **Planned P1 work:** retire the stubbed surface as features touch them
> (a dedicated cleanup pass is queued after the P0 probing-plugin extraction,
> which will force the surviving `Grbl` consumers to declare their Core deps
> explicitly). Clean up as features touch them.
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
