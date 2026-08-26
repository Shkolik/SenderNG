# SenderNG Architecture — Living Document

> **Living document:** update this file as the rewrite progresses and priorities shift. Companion specs live in `docs/superpowers/specs/`; the deferred-gaps list lives in `docs/ROADMAP.md`.

## 1. Goal

SenderNG is a reimplementation of ioSender decoupled from the Windows environment: cross-platform (Windows + Linux), .NET 10 + Avalonia 12, with no WPF / `PresentationCore` / `System.Windows.Forms` / `System.Management` dependencies. The rewrite follows the philosophy already defined in this project:

- `docs/superpowers/specs/2026-08-22-senderng-design.md` — modular plugin architecture, MVVM + DI, portable settings, works without a controller (demo mode).
- `docs/superpowers/specs/2026-08-22-layout-design.md` — the 4-panel layout (Left / Center tabs / Right / Flyout).

## 2. Reference projects

- **Upstream ioSender** (`terjeio/ioSender`, local `master` at `3a817cf`) — the source of truth for behavior. When a process already exists upstream, port it; do not invent it.
- **`Shkolik/ioSender` branch `foamcut`** (`8ea54fb`) — the production working copy used for foam cutting, and the lineage SenderNG Core was extracted from (with foam patches `9de5a5f`, `63c930c`, `0f7fae8`).
  - ⚠️ `foamcut` is based on an **older upstream revision**. Features absent from it are usually not "deletions" — they simply postdate the revision it forked from. Do not read its diff against upstream `master` as a removal list.
  - It is a **frozen production reference**: it will not be rebased or merged with upstream while SenderNG work is ongoing. Treat it as a stable snapshot, not a moving base.

## 3. Priorities

1. **Base widgets and processes** for foam and mill mode — current focus. Recreate the core UI widgets and the processes behind them so the app is genuinely usable in both modes.
2. **3D view + advanced probing** — next.
3. **Steppers calibration** — after that.
4. **Everything else** — lowest priority: lathe, dragknife, converters, and the rest of the deferred scope.

## 4. Porting policy

Implementation is based on the original ioSender **when possible**: if a process already exists upstream (probing flow, jog logic, settings tree, override handling, steppers calibration wizard, …), port it rather than reinvent. Deviate only where the platform forces it (WPF → Avalonia, `Properties.Settings` / `App.config` → portable `settings.json`, custom stream classes → `System.IO.Ports`) or where this project's specs deliberately diverge (demo mode, DI module system, cutting overrides panel).

## 5. Current architecture (SenderNG)

```
src/SenderNG.Core          pure net10.0 engine + service layer (no UI)
src/SenderNG.Avalonia      shell: HostSetup DI wiring, MainWindow, fixed modules + plugins
src/SenderNG.Modules.Dummy separate assembly proving the plugin-discovery path
tests/SenderNG.Core.Tests  xUnit suite (dotnet test)
```

- **Engine (ported from ioSender/foamcut, kept nearly whole):** `GCodeParser.cs` (~3610 L), `NGCExpr.cs` (~1518 L), `GCodeEmulator.cs` (~658 L), `GCode.cs`, `GCodeJob.cs`, `Machine.cs`. `Grbl.cs` is deliberately slim (288 L vs upstream 3438 L): caps parsing (`ParseCaps`, `[OPT:...]`) ported; the rest are compile-only stubs — marked tech debt.
- **Service layer (`IGrblService`):** `DemoGrblService` (in-memory grbl simulation — jog `$J=`, G90/G91/G92, M3/M4/M5, coolant + override realtime bytes, persistent `$$` store) vs `RealGrblService` (serial via `System.IO.Ports`; Telnet/WebSocket stubbed), swapped behind `GrblServiceProxy` so subscribers survive demo↔real transitions. Realtime command bytes live in `GrblRealtime`.
- **Module contract (`IModule`):** `Id` / `Name` / `Order` / `PanelHint {Left, Center, Right, Flyout, Menu, Bottom}` / `IsVisibleByDefault` / `CanHide` / `HotKeys` / `RegisterServices` / `RegisterSettings` / `CreateViewModel` / `CreateView`. `ModuleCatalog` scans assemblies; built-ins are registered explicitly in `HostSetup`.
- **Modules today:** fixed — DRO, Offsets, Program, Console, GrblSettings, AppSettings; plugins — Jog, Cutting, Coolant, Macro, Probing; plus Dummy.
- **State model:** `MachineState` (NumAxes, Status, Positions, Signals, Flood/Mist, Feed/Speed, FeedOvr/RapidOvr/SpindleOvr) is the portable replacement for ioSender's WPF-bound `GrblViewModel` (1434 L).
- **Settings:** portable `./settings.json` next to the executable (fallback to `~/.config` / `%AppData%`), composite `AppSettings` with per-module sections, single `SettingsService`.
- **Namespaces:** the original engine code keeps the upstream `CNC.Core` / `CNC.GCode` namespaces **unchanged** — namespace migration is not planned, so the original core stays intact and diffable against upstream. New code uses `SenderNG.Core` / `SenderNG.Avalonia`.

## 6. Feature map vs ioSender

| Relation | Items |
| --- | --- |
| **Kept (ported)** | GCodeParser, NGCExpr, GCodeJob, GCodeEmulator, Machine, caps parsing, foam patches, Macro editor (foamcut), Probing, DRO/Jog/Coolant/Offsets concepts |
| **Slimmed** | Grbl.cs (3438 → 288 L, caps only + stubs), GrblViewModel (1434 L → `MachineState`) |
| **Stripped** | WPF everywhere; custom streams (Serial/Telnet/Websocket/YModem/Eltima) → `System.IO.Ports`; `Properties.Settings`/`App.config` → portable settings; localization (9 locales → hardcoded en-US for now) |
| **Deferred (lowest priority)** | 3D view (HelixToolkit), camera (AForge/V4L2), Lathe, DragKnife, Converters (Excellon/HPGL), SD card view, MPG, PID log, THC monitor, Trinamic view, steppers calibration wizard, Tool view |
| **Added** | Demo/offline mode, DI plugin system + assembly scan, 4-panel layout with per-module visibility, cutting realtime overrides panel (0x90–0x9E), live status parsing (WPos/FS:/Ov:), MDI history, program limits, state indicator, xUnit test suite (upstream had none) |

> **Caveat:** the "stripped" rows reflect SenderNG's scope decisions and the foamcut snapshot. Any feature present in newer upstream `master` but absent in foamcut is likely just newer upstream work, not something SenderNG removed.

## 7. License & attribution

- **License:** BSD 3-Clause. `LICENSE` retains `Copyright (c) 2019-2023, Io Engineering (Terje Io)` and adds `Copyright (c) 2024-2026, Andrew Shkolik`. Condition 1 requires retaining the copyright notice in redistributed source.
- **Ported / derived code:** keeps the **original copyright header** of the file it came from (every engine file ported from ioSender/foamcut still carries its original Io Engineering BSD header). Files derived from ioSender carry **double copyright** — Io Engineering + Andrew Shkolik.
- **Completely new code:** single copyright — Andrew Shkolik.
- **Consequence for namespaces:** because the original core must remain unmodified (headers and `CNC.Core` / `CNC.GCode` namespaces intact) to satisfy the license terms, we do not migrate its namespaces. Attribution stays attached to the exact files it belongs to.

## 8. Known gaps and tech debt

> The complete checkable gap map vs ioSender, organized by priority, lives in
> `docs/iosender-gaps.md`.

- **Full grbl handshake** (`$I` + `[OPT:...]`, grblHAL build/version detection) missing; capability-driven UI (e.g. hiding unsupported coolant Mist) pending. `RealGrblService` does only a minimal `$I` probe, no status polling loop.
- **Cycle bar** still sends placeholder strings via `SendAsync`; should use `SendRealtime` (`0x7E` / `0x21` / `0x18`).
- **Flood/Mist and machine state parsing** are demo-only until the status-report parser reads accessory-state fields.
- **Foam CNC mode** is persisted but only changes the jog layout today — no functional foam behavior yet.
- **Unbound UI:** Home / Unlock / Reset buttons and the Check checkbox in `MainWindow.axaml` have no commands; status-bar Block/Ready/Jog-step values are hardcoded placeholders.
- **Grbl.cs stubs** are compile-only; `GrblInfo` static state is global (partly mitigated by pure `ParseCaps`).

## 9. Verification

```bash
dotnet build SenderNG.sln
dotnet test  SenderNG.sln   # or --filter <name> for a targeted run
```

The build currently emits warnings (CS8602, AVLN5001, xUnit1031, …); do not add new ones beyond a change's scope.
