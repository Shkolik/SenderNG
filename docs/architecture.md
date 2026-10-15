# SenderNG Architecture

> **Living document:** update this file as the architecture evolves and priorities shift. Priorities, planned work and the deferred-gaps list live in `docs/ROADMAP.md`.

## 1. Goal

SenderNG is a reimplementation of ioSender decoupled from the Windows environment: cross-platform (Windows + Linux x64 + Linux ARM64), .NET 9 + Avalonia 12, with no WPF / `PresentationCore` / `System.Windows.Forms` / `System.Management` dependencies. The rewrite follows the philosophy already defined in this project: modular plugin architecture, MVVM + DI, portable settings, works without a controller (demo mode), and a panel-based layout — `PanelHint {Left, Center, Right, Flyout, Menu, Bottom, Dialog, None}`, with the visible UI organized as Left / Center tabs / Right / Flyout.

## 2. Reference projects

- **Upstream ioSender** (`terjeio/ioSender`, local `master` at `3a817cf`) — the source of truth for behavior. When a process already exists upstream, port it; do not invent it.
- **`Shkolik/ioSender` branch `foamcut`** (`8ea54fb`) — the production working copy used for foam cutting, and the lineage SenderNG Core was extracted from (with foam patches `9de5a5f`, `63c930c`, `0f7fae8`).
  - ⚠️ `foamcut` is based on an **older upstream revision**. Features absent from it are usually not "deletions" — they simply postdate the revision it forked from. Do not read its diff against upstream `master` as a removal list.
  - It is a **frozen production reference**: it will not be rebased or merged with upstream while SenderNG work is ongoing. Treat it as a stable snapshot, not a moving base.

## 3. Priorities

Feature priorities, planned work and known deferred scope live in [`docs/ROADMAP.md`](ROADMAP.md); the checkable gap map vs upstream lives in [`docs/iosender-gaps.md`](iosender-gaps.md). This file describes how the code is put together, not what ships next.

## 4. Porting policy

Implementation is based on the original ioSender **when possible**: if a process already exists upstream (probing flow, jog logic, settings tree, override handling, steppers calibration wizard, …), port it rather than reinvent. Deviate only where the platform forces it (WPF → Avalonia, `Properties.Settings` / `App.config` → portable `settings.json`, custom stream classes → `System.IO.Ports`) or where this project's specs deliberately diverge (demo mode, DI module system, cutting overrides panel).

## 5. Current architecture (SenderNG)

```
src/SenderNG.Core          pure net9.0 engine + service layer (no UI)
src/SenderNG               shell: HostSetup DI wiring, MainWindow, fixed modules + in-app plugin modules
src/SenderNG.Shared        converter scaffolding, source-linked into the Excellon/Hpgl plugins
src/SenderNG.Modules.*     external plugin assemblies: Common (the plugin SDK), Common.Render (shared headless GL engine), Probing, HeightMapViewer, Plotter, Plotter.GL, Excellon, Hpgl
tests/SenderNG.Core.Tests  xUnit suite (engine, services, view-models)
tests/SenderNG.Tests       headless UI suite (xUnit v3 + Avalonia.Headless)
tests/SenderNG.Modules.Probing.Tests  xUnit probing suite
tests/SenderNG.Modules.HeightMapViewer.Tests  xUnit height-map viewer suite
tests/ReleaseManager.Tests xUnit release-tooling suite
```

- **Engine (ported from ioSender/foamcut, kept nearly whole):** `GCodeParser.cs` (~3600 L), `NGCExpr.cs` (~1520 L), `GCodeEmulator.cs` (~680 L), `GCode.cs`, `Machine.cs`. `Grbl.cs` is deliberately slim (203 L vs upstream 3438 L): caps parsing (`ParseCaps`, `[OPT:...]`) ported; the rest are compile-only stubs — marked tech debt.
- **Service layer (`IGrblService`):** `DemoGrblService` (in-memory grbl simulation — jog `$J=`, G90/G91/G92, M3/M4/M5, coolant + override realtime bytes, persistent `$$` store) vs `RealGrblService` (serial via `System.IO.Ports` or network via `TcpGrblTransport` — grblHAL Ethernet; telnet/WebSocket not yet implemented), swapped behind `GrblServiceProxy` so subscribers survive demo↔real↔network transitions. Realtime command bytes live in `GrblRealtime`. A `ConnectionChanged` event on `IGrblService` (forwarded by the proxy, raised on inner swaps) drives the UI's connected state.
- **Module contract (`IModule`):** `Id` / `Name` / `Order` / `PanelHint {Left, Center, Right, Flyout, Menu, Bottom, Dialog, None}` / `IsVisibleByDefault` / `CanHide` / `HotKeys` / `RegisterServices` / `CreateViewModel` / `CreateView`. `ModuleCatalog` scans assemblies; built-ins are registered explicitly in `HostSetup`. Settings surfaces live on a second seam, `IModuleSettingsProvider`: generic numeric/boolean fields or a custom settings view for the App Settings card, `AttachServices(IServiceProvider)` (called once after host build), and a `SettingsScope` declaration — `Profile` (default: section in the active machine profile) or `Global` (section in `global.json`; the Console module is the built-in example).
- **Modules:**
  - **Fixed (core, always visible — `CanHide = false`, no hide checkbox):** DRO, Program, Console, GrblSettings, AppSettings, Offsets (flyout), Work Parameters.
  - **In-app plugin modules** (compiled into the app assembly): Jog, Cutting, Coolant, Macro, Mpos, Outline, Goto, StepperCalibration.
  - **External plugin assemblies** (discovered by the `Modules/*.dll` scan): Probing, HeightMapViewer (3D height-map dialog), Plotter (2D Skia), Plotter.GL (3D OpenGL orbit view), Excellon and Hpgl (converters — scaffolding source-linked from `SenderNG.Shared`, generic machinery from the `SenderNG.Modules.Common` SDK). The desktop GL stack is shared: `SenderNG.Modules.Common.Render` is a UI-free headless engine (offscreen context, readback, camera, shaders, mesh primitives) extracted from Plotter.GL and consumed by both the 3D plotter and the height-map viewer; it ships flat in the core bundle's `Modules/` alongside the SDK and Silk.NET (Model A — referenced by plugins, never bundled per plugin).
  - **Visibility & App Settings tree:** plugins are hideable by default (`CanHide = true`), except Jog and the converters (`false`). Settings dialogs (App Settings, GRBL Settings) and modules with nothing to configure (DRO, Console, Work Parameters) get no Modules-tree leaf — the tree lists hideable plugins plus non-hideable modules with settings (Program, Offsets, Jog, converters).
  - **Capability-aware widgets:** the Goto widget's numeric rows track the connected controller's axis count (one row per axis, like the DRO / mPos read-outs); the Outline widget sweeps the loaded program's limits rectangle on every sweepable carriage plane of the active machine type (X/Y | Z/A for foam, in sync); Offsets and Work Parameters offer only the coordinate systems the firmware can actually write (classic grbl: G54–G59; grblHAL: also G59.1–G59.3, which classic firmware rejects with error 29), the Offsets editor disables itself while the controller is in Alarm, its tool-table visibility toggle lives on an App Settings card, and the tool table shows only when the user enabled it AND the controller is grblHAL with tool slots (classic grbl 1.1 / 1.2i forks have no `G10 L1`).
- **State model:** `MachineState` (NumAxes, Status, Positions, Signals, OptionalSignals, Flood/Mist, Feed/Speed, FeedOvr/RapidOvr/SpindleOvr) is the portable replacement for ioSender's WPF-bound `GrblViewModel` (1434 L). `OptionalSignals` mirrors ioSender's `OptionalSignals` — the signal set discovered from `$I` (plus dynamic e-stop observation) that gates which optional LEDs the signals panel renders.
- **Settings:** files sit next to the executable (fallback to `~/.config` / `%AppData%`): `global.json` (app-level: language, active profile pointer, recent files, profile registry, global-scoped module sections like Console) plus one file per machine profile under `profiles/` (`MachineProfile`: connection, machine-type preset, per-module sections — jog, macros, tools, probing …). The legacy Mill/Foam mode switch is gone: a machine is a named profile managed in App Settings (profile manager); legacy `settings.json` migrates on first run.
- **Namespaces:** the original engine code keeps the upstream `CNC.Core` / `CNC.GCode` namespaces **unchanged** — namespace migration is not planned, so the original core stays intact and diffable against upstream. New code uses `SenderNG.Core` / `SenderNG`.

## 6. Feature map vs ioSender

| Relation | Items |
| --- | --- |
| **Kept (ported)** | GCodeParser, NGCExpr, GCodeEmulator, Machine, caps parsing, foam patches, Macro editor (foamcut), Probing, Steppers calibration wizard (landed 2026-09-19), DRO/Jog/Coolant/Offsets concepts |
| **Slimmed** | Grbl.cs (3438 → 203 L, caps only + stubs), GrblViewModel (1434 L → `MachineState`) |
| **Stripped** | WPF everywhere; custom streams (Serial/Telnet/WebSocket/YModem/Eltima) → `System.IO.Ports`; `Properties.Settings`/`App.config` → portable settings; localization (9 locales → JSON catalogs en/ru/fr/es/de with per-plugin overrides — see `docs/localization.md`) |
| **Deferred (lowest priority)** | camera (AForge/V4L2), Lathe, DragKnife, SD card view, MPG, PID log, THC monitor, Trinamic view, Tool view |
| **Added** | Demo/offline mode, DI plugin system + assembly scan, 4-panel layout with per-module visibility, cutting realtime overrides panel (0x90–0x9E), live status parsing (WPos/FS:/Ov:), MDI history, program limits, state indicator, JSON height-map format + 3D height-map viewer plugin, xUnit test suite (upstream had none) |

> **Caveat:** the "stripped" rows reflect SenderNG's scope decisions and the foamcut snapshot. Any feature present in newer upstream `master` but absent in foamcut is likely just newer upstream work, not something SenderNG removed.

## 7. License & attribution

- **License:** BSD 3-Clause. `LICENSE` retains `Copyright (c) 2019-2023, Io Engineering (Terje Io)` and adds `Copyright (c) 2024-2026, Andrew Shkolik`. Condition 1 requires retaining the copyright notice in redistributed source.
- **Ported / derived code:** keeps the **original copyright header** of the file it came from (every engine file ported from ioSender/foamcut still carries its original Io Engineering BSD header). Files derived from ioSender carry **double copyright** — Io Engineering + Andrew Shkolik.
- **Completely new code:** single copyright — Andrew Shkolik.
- **Consequence for namespaces:** because the original core must remain unmodified (headers and `CNC.Core` / `CNC.GCode` namespaces intact) to satisfy the license terms, we do not migrate its namespaces. Attribution stays attached to the exact files it belongs to.

## 8. Known gaps and tech debt

> The complete checkable gap map vs ioSender lives in `docs/iosender-gaps.md`; priorities and planned work live in `docs/ROADMAP.md`.

### Open gaps

- **Foam CNC depth** — the machine-type preset (Mill / Foam) chosen per profile drives the jog layout (schema: preset planes + controller `[AXS:]` letters; under-axed multi-plane machines hide the second plane), the plotter's two-carriage views and the Outline sweep, but foam-specific behavior beyond that is still limited. Hold-to-jog continuous works across these layouts including foam Together; what remains is tracked in the roadmap.
- **Transports** — `TcpGrblTransport` covers plain TCP; telnet option negotiation and WebSocket are not implemented.
- **Machine world mapping bakes in a horizontal convention** — the 3D views place a plane's axes by each axis's direction (Width → world X, Length → world Y, Height → world Z). Only `Height → Z` is physics, because a height is vertical by definition; the two horizontal assignments are naming conventions. A machine whose plane horizontal runs along the *other* horizontal axis — a sideways or gantry-rotated frame — therefore cannot be expressed and would need a per-machine override, which is the one thing the deleted per-plotter `Planes[].WorldAxes` table allowed. Deferred; tracked in the roadmap.

### Landed recently (context)

- **Controller connect** — three-tab `ConnectView` (Serial / Network / Demo); the app starts disconnected and never in demo mode — the dialog opens right after startup and the UI shows `NOT CONNECTED` until the user picks a controller. The connect-time `$I` handshake fills an immutable `ControllerCapabilities` (Coolant Mist visibility follows it) and pushes the discovered axis count into the injected `GrblInfoState` (the read path for the gcode parser and DRO) and the live `MachineState`. Serial ports re-enumerate every second; disconnects (reader error, failed status write, `IPortPresenceCheck` watchdog) flip the app to NOT CONNECTED and raise `ConnectionChanged`; reconnecting works even when the board comes back under a different COM number.
- **Cycle bar** — the single run-control surface (the Program view's own bar was removed): Start = run/resume, Hold = pause, Stop = hard reset while Held, Rewind = local rewind when no session is active; buttons stay visible and gate on session state. The session is local orchestration only — the controller is driven exclusively by realtime bytes (`0x7E`/`0x21`/`0x18`) plus the G-code line stream awaiting each `ok`, one byte per transition instead of per button press. Home/Unlock/Reset are bound (`$H`/`$X`/realtime reset).
- **Status bar, MDI, Console, Cutting** — the status bar keeps ioSender's exact four-slot layout (Blocks | Message | RunTime | JogStep, fixed-width) with no separate connection slot (connection info flows through Message like upstream); the MDI row is label + input + Send. The Cutting widget mirrors live feed/speed from the `FS:` field but reads feed as 0 during Hold — grblHAL reports the programmed feed while the machine is actually stopped; speed is untouched because a spindle keeps running through a feed hold. The Console tab renders the controller's raw output (silent in demo mode; a bounded replay shows the connect burst even if the Console tab is realized after connecting); the Check checkbox (grbl check mode `$C` — dry-run g-code without moving the machine) is bound.
- **Accessory-state parsing** — `StatusReportParser` reads the grblHAL `A:` field into `MachineState.Flood`/`Mist`/spindle; classic grbl leaves it untouched.
- **Height map** — the probing module persists maps as JSON (`.hmap.json`, replacing the legacy XML `.map`; no ioSender interchange) and exposes them through the Core `IHeightMapSource` contract (`HeightMapLayer`: row-major Z samples with `NaN` = unprobed, plus `ZOffset`; the stream is a `BehaviorSubject`, so a viewer opened after a probe still receives the current map). A separate first-party plugin, `HeightMapViewer`, consumes that contract (never referencing Probing) and registers Core's `IHeightMap3DRenderer` (`Task ShowAsync()`); the Probing widget's **3D View** action resolves it and is gated on the module's per-profile visibility. The viewer renders the surface, control-point markers and X/Y/Z arrows through `Common.Render`, with per-profile AA settings (2× supersample + MSAA).

### Tech debt

- **Grbl.cs stubs** are compile-only; `GrblInfo` is a read-only facade over an injectable `GrblInfoState` instance (`GrblInfo.Current`, bound to the DI singleton in HostSetup) — no process-global write path remains (2026-09-19). Retire the stubbed surface as features touch it.

## 9. Verification

```bash
dotnet build SenderNG.sln
dotnet test  SenderNG.sln   # or --filter <name> for a targeted run
```

The build is **warning-free** — keep it that way.
