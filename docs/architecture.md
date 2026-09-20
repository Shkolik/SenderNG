# SenderNG Architecture — Living Document

> **Living document:** update this file as the rewrite progresses and priorities shift. Companion specs live in `docs/superpowers/specs/`; the deferred-gaps list lives in `docs/ROADMAP.md`.

## 1. Goal

SenderNG is a reimplementation of ioSender decoupled from the Windows environment: cross-platform (Windows + Linux), .NET 9 + Avalonia 12, with no WPF / `PresentationCore` / `System.Windows.Forms` / `System.Management` dependencies. The rewrite follows the philosophy already defined in this project:

- `docs/superpowers/specs/2026-08-22-senderng-design.md` — modular plugin architecture, MVVM + DI, portable settings, works without a controller (demo mode).
- `docs/superpowers/specs/2026-08-22-layout-design.md` — the 4-panel layout (Left / Center tabs / Right / Flyout).

## 2. Reference projects

- **Upstream ioSender** (`terjeio/ioSender`, local `master` at `3a817cf`) — the source of truth for behavior. When a process already exists upstream, port it; do not invent it.
- **`Shkolik/ioSender` branch `foamcut`** (`8ea54fb`) — the production working copy used for foam cutting, and the lineage SenderNG Core was extracted from (with foam patches `9de5a5f`, `63c930c`, `0f7fae8`).
  - ⚠️ `foamcut` is based on an **older upstream revision**. Features absent from it are usually not "deletions" — they simply postdate the revision it forked from. Do not read its diff against upstream `master` as a removal list.
  - It is a **frozen production reference**: it will not be rebased or merged with upstream while SenderNG work is ongoing. Treat it as a stable snapshot, not a moving base.

## 3. Priorities

1. **Base widgets and processes** for foam and mill mode — current focus. Recreate the core UI widgets and the processes behind them so the app is genuinely usable in both modes.
2. **Advanced probing** — next.
3. **Steppers calibration** — after that.
4. **Everything else** — lowest priority: lathe, dragknife, converters, and the rest of the deferred scope.

## 4. Porting policy

Implementation is based on the original ioSender **when possible**: if a process already exists upstream (probing flow, jog logic, settings tree, override handling, steppers calibration wizard, …), port it rather than reinvent. Deviate only where the platform forces it (WPF → Avalonia, `Properties.Settings` / `App.config` → portable `settings.json`, custom stream classes → `System.IO.Ports`) or where this project's specs deliberately diverge (demo mode, DI module system, cutting overrides panel).

## 5. Current architecture (SenderNG)

```
src/SenderNG.Core          pure net9.0 engine + service layer (no UI)
src/SenderNG      shell: HostSetup DI wiring, MainWindow, fixed modules + plugins
tests/SenderNG.Core.Tests  xUnit suite (dotnet test)
```

- **Engine (ported from ioSender/foamcut, kept nearly whole):** `GCodeParser.cs` (~3610 L), `NGCExpr.cs` (~1520 L), `GCodeEmulator.cs` (~670 L), `GCode.cs`, `GCodeJob.cs`, `Machine.cs`. `Grbl.cs` is deliberately slim (~320 L vs upstream 3438 L): caps parsing (`ParseCaps`, `[OPT:...]`) ported; the rest are compile-only stubs — marked tech debt.
- **Service layer (`IGrblService`):** `DemoGrblService` (in-memory grbl simulation — jog `$J=`, G90/G91/G92, M3/M4/M5, coolant + override realtime bytes, persistent `$$` store) vs `RealGrblService` (serial via `System.IO.Ports` or network via `TcpGrblTransport` — grblHAL Ethernet; Telnet/WebSocket still stubbed), swapped behind `GrblServiceProxy` so subscribers survive demo↔real↔network transitions. Realtime command bytes live in `GrblRealtime`. A `ConnectionChanged` event on `IGrblService` (forwarded by the proxy, raised on inner swaps) drives the UI's connected state.
- **Module contract (`IModule`):** `Id` / `Name` / `Order` / `PanelHint {Left, Center, Right, Flyout, Menu, Bottom}` / `IsVisibleByDefault` / `CanHide` / `HotKeys` / `RegisterServices` / `CreateViewModel` / `CreateView`. `ModuleCatalog` scans assemblies; built-ins are registered explicitly in `HostSetup`. Settings surfaces live on a second seam, `IModuleSettingsProvider`: generic numeric/boolean fields or a custom settings view for the App Settings card, `AttachServices(IServiceProvider)` (called once after host build), and a `SettingsScope` declaration — `Profile` (default: section in the active machine profile) or `Global` (section in `global.json`; the Console module is the built-in example). The old `IModule.RegisterSettings` seam was removed (never invoked).
- **Modules today:** fixed — DRO, Program, Console, GrblSettings, AppSettings, Offsets (flyout), Work Parameters; plugins — Jog, Cutting, Coolant, Macro, Probing, Mpos, Outline, Goto, Plotter (2D Skia), Plotter.Nex (3D Vulkan orbit view), Excellon, Hpgl (converters). Fixed modules are core and always visible (`CanHide = false`, no hide checkbox in App Settings); plugins are hideable. Settings dialogs (App Settings, GRBL Settings) and fixed modules that expose nothing to configure (DRO, Console, Work Parameters) get no Modules-tree leaf — the tree lists hideable plugins plus fixed modules with settings (Program, Offsets, Jog, the Excellon/Hpgl converters). The Offsets module exposes its tool-table visibility toggle via an App Settings card; the Goto widget's numeric rows track the connected controller's axis count (one row per axis, like the DRO / mPos read-outs). The Outline widget sweeps the loaded program's limits rectangle on every sweepable carriage plane of the active machine type (X/Y | Z/A for foam, in sync). Offsets and Work Parameters are capability-aware: they offer only the coordinate systems the firmware can actually write (classic grbl: G54–G59; grblHAL: also G59.1–G59.3, which classic firmware rejects with error 29) and the Offsets editor disables itself while the controller is in Alarm. The Offsets tool table is shown only when the user enabled it AND the controller is grblHAL with tool slots (classic grbl 1.1 / 1.2i forks have no `G10 L1`).
- **State model:** `MachineState` (NumAxes, Status, Positions, Signals, OptionalSignals, Flood/Mist, Feed/Speed, FeedOvr/RapidOvr/SpindleOvr) is the portable replacement for ioSender's WPF-bound `GrblViewModel` (1434 L). `OptionalSignals` mirrors ioSender's `OptionalSignals` — the signal set discovered from `$I` (plus dynamic e-stop observation) that gates which optional LEDs the signals panel renders.
- **Settings:** files sit next to the executable (fallback to `~/.config` / `%AppData%`): `global.json` (app-level: language, active profile pointer, recent files, profile registry, global-scoped module sections like Console) plus one file per machine profile under `profiles/` (`MachineProfile`: connection, machine-type preset, per-module sections — jog, macros, tools, probing …). The legacy Mill/Foam mode switch is gone: a machine is a named profile managed in App Settings (profile manager); legacy `settings.json` migrates on first run. See `docs/settings-migration.md`.
- **Namespaces:** the original engine code keeps the upstream `CNC.Core` / `CNC.GCode` namespaces **unchanged** — namespace migration is not planned, so the original core stays intact and diffable against upstream. New code uses `SenderNG.Core` / `SenderNG`.

## 6. Feature map vs ioSender

| Relation | Items |
| --- | --- |
| **Kept (ported)** | GCodeParser, NGCExpr, GCodeJob, GCodeEmulator, Machine, caps parsing, foam patches, Macro editor (foamcut), Probing, DRO/Jog/Coolant/Offsets concepts |
| **Slimmed** | Grbl.cs (3438 → ~320 L, caps only + stubs), GrblViewModel (1434 L → `MachineState`) |
| **Stripped** | WPF everywhere; custom streams (Serial/Telnet/Websocket/YModem/Eltima) → `System.IO.Ports`; `Properties.Settings`/`App.config` → portable settings; localization (9 locales → en/ru JSON catalog with per-plugin overrides — see `docs/localization.md`) |
| **Deferred (lowest priority)** | camera (AForge/V4L2), Lathe, DragKnife, SD card view, MPG, PID log, THC monitor, Trinamic view, ~~steppers calibration wizard~~ (**LANDED 2026-09-19**, Settings-menu wizard with measured backlash — see `docs/superpowers/plans/2026-09-19-stepper-calibration.md`), Tool view |
| **Added** | Demo/offline mode, DI plugin system + assembly scan, 4-panel layout with per-module visibility, cutting realtime overrides panel (0x90–0x9E), live status parsing (WPos/FS:/Ov:), MDI history, program limits, state indicator, xUnit test suite (upstream had none) |

> **Caveat:** the "stripped" rows reflect SenderNG's scope decisions and the foamcut snapshot. Any feature present in newer upstream `master` but absent in foamcut is likely just newer upstream work, not something SenderNG removed.

## 7. License & attribution

- **License:** BSD 3-Clause. `LICENSE` retains `Copyright (c) 2019-2023, Io Engineering (Terje Io)` and adds `Copyright (c) 2024-2026, Andrew Shkolik`. Condition 1 requires retaining the copyright notice in redistributed source.
- **Ported / derived code:** keeps the **original copyright header** of the file it came from (every engine file ported from ioSender/foamcut still carries its original Io Engineering BSD header). Files derived from ioSender carry **double copyright** — Io Engineering + Andrew Shkolik.
- **Completely new code:** single copyright — Andrew Shkolik.
- **Consequence for namespaces:** because the original core must remain unmodified (headers and `CNC.Core` / `CNC.GCode` namespaces intact) to satisfy the license terms, we do not migrate its namespaces. Attribution stays attached to the exact files it belongs to.

## 8. Known gaps and tech debt

> The complete checkable gap map vs ioSender, organized by priority, lives in
> `docs/iosender-gaps.md`; the P0 implementation plan (checkable items,
> parallelization markers) lives in `docs/p0-controller-plumbing-plan.md`.

- **P0 controller plumbing is done**: `RealGrblService` owns a background reader + suspendable `?` poller feeding `StatusReportParser`; the `$I` handshake fills an immutable `ControllerCapabilities` (exposed on `IGrblService`) that drives Coolant Mist visibility. The connect dialog is a three-tab `ConnectView` — Serial (grayed with no ports) / Network (TCP endpoint, gated by the global `AppSettings.NetworkAvailable`) / Demo (always available, with a disclaimer) — and network mode works via `TcpGrblTransport`. The app starts disconnected and never in demo mode: the connect dialog opens right after startup and the UI shows an obvious `NOT CONNECTED` state (gray indicator, status-bar message text) until the user picks a controller. The connect dialog auto-re-enumerates serial ports every second (plug in a controller and the grayed Serial tab revives), and the `$I` handshake pushes the discovered axis count into both the injected `GrblInfoState` (bound to the DI singleton and mirrored to `GrblInfo.Current`, the read path for the gcode parser and DRO) and the live `MachineState`, so the DRO and gcode parser reflect the board's real axes. Disconnects are detected and reported: a reader error, a failed status write, or a transport-health watchdog (transports implement `IPortPresenceCheck` — the COM port vanished / the socket died) flips the app to NOT CONNECTED and raises `ConnectionChanged`; the `$I` handshake retries so a freshly booted board (right after re-plug) still identifies itself, and reconnecting after an unplug works even when the board comes back under a different COM number.
- **Cycle bar** is the **single run-control surface** (the Program view's own Run/Pause/Resume/Stop bar was removed — one session, one control set, drivable while watching the Console tab). It gates on the session state, keeping buttons visible and enabling/disabling: Start = run/resume, Hold = pause, Stop = hard reset while Held, Rewind = local rewind when no session is active. The session is internal orchestration only — it decides *when*; the controller is still driven exclusively by the same realtime bytes (`0x7E`/`0x21`/`0x18`) plus the G-code line stream awaiting each `ok`, each transition issuing its byte once instead of per button press. Home/Unlock/Reset are bound (`$H`/`$X`/realtime reset).
- **Accessory-state parsing** — `StatusReportParser` reads the grblHAL `A:` field into `MachineState.Flood`/`Mist`/spindle; classic grbl leaves it untouched.
- **Foam CNC mode** is persisted but only changes the jog layout today — no
  functional foam behavior yet (hold-to-jog continuous, 2026-09-17,
  works across these layouts incl. foam Together — see
  `docs/continuous-jog.md`). The layout is no longer a `"Foam"` string
  check: it derives from the machine-axis schema
  (`docs/machine-axis-schema.md`, 2026-09-07 — preset planes + controller
  `[AXS:]` letters; under-axed multi-plane machines hide the second plane).
- **Unbound UI:** Home / Unlock / Reset and the **Check checkbox** (which toggles grbl check mode `$C` — dry-run g-code without moving the machine) are bound, and the status bar shows real Block / Message / RunTime / JogStep values in ioSender's exact layout — fixed-width slots (Message owns the star column) so Block/RunTime/JogStep changes never shift the other texts, and no separate connection slot (its "Connected: <port>" duplicated Message; connection info flows through Message like upstream). The MDI row is just label + input + Send after the static signals/limits summary was removed as redundant. The Cutting widget mirrors live feed/speed from the `FS:` field but reads feed as 0 during Hold — grblHAL reports the programmed feed while the machine is actually stopped; speed is untouched because a spindle keeps running through a feed hold. The Console tab renders the controller's raw output with ioSender's verbose/filter options; demo mode publishes no raw lines, so the console is silent (no mocked status flood) until a real board is connected; the proxy relays raw lines through a bounded replay so the console shows the connect burst even if the Console tab is realized after connecting.
- **Grbl.cs stubs** are compile-only; `GrblInfo` is a read-only facade over an injectable
  `GrblInfoState` instance (`GrblInfo.Current`, bound to the DI singleton in HostSetup) — no
  process-global write path remains (2026-09-19).

## 9. Verification

```bash
dotnet build SenderNG.sln
dotnet test  SenderNG.sln   # or --filter <name> for a targeted run
```

The build is **warning-free** (verified by full rebuild 2026-09-17; earlier clean build 2026-09-07); keep it that way.
