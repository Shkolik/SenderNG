# SenderNG

**Lightweight cross-platform (Windows + Linux) grblHAL gcode sender based on [ioSender](https://github.com/terjeio/ioSender) by Io Engineering (Terje Io).**

SenderNG is a diverged, focused fork of ioSender — stripped to foam cutting, CNC milling, probing, and USB/Ethernet connectivity. The sender supports as many axes as the controller reports (axis count comes from the firmware, not the app; producing multi-axis g-code is CAM's scope). Lathe, DragKnife and other heavy modules are deferred. Not affiliated with or endorsed by Io Engineering / upstream ioSender.

> **Status:** Private development. Cross-platform Avalonia UI + .NET 9 — engine and shell functional: demo/offline mode, serial + TCP connect, DRO/Jog/Probing widgets, localization (en/ru). Legacy Windows build remains at [Shkolik/ioSender](https://github.com/Shkolik/ioSender) (branch `foamcut`).

## Scope (v0.1)

- Foam cutting and milling
- Probing (Center/Edge/HeightMap)
- USB serial + Ethernet (Telnet/WebSocket), YModem/SD
- Program-view plotter plugins: 2D Skia plotter (isometric/flat projection on
  Skia) + 3D Vulkan orbit plotter (`SenderNG.Modules.Plotter.Nex`, HelixToolkit.Nex)
  — design + lib comparison: `docs/machine-config-research.md`
- Camera: deferred — if ever wanted, as a 3rd-party plugin (libs live in
  the decision tree: `docs/ROADMAP.md`)
- Deferred: Lathe wizards, DragKnife

## Origin & License

- Based on `ioSender` `2.0.48` (`terjeio/ioSender`) — see `CNC Core/`, `CNC Controls` for original BSD 3-Clause code.
- **License:** BSD 3-Clause — `LICENSE` retains `Copyright (c) 2019-2023 Io Engineering` and adds `Copyright (c) 2024-2026 Shkolik`. See `LICENSE` for full text.
- Avalonia UI (`AvaloniaUI/Avalonia`) is MIT — compatible with BSD.

## Roadmap

1. ✅ `src/SenderNG.Core` (`net9.0`) — engine extracted (`Grbl.cs`, `GCodeParser.cs`, `GCodeEmulator.cs`, `NGCExpr.cs` + foam patches) — done
2. ✅ `src/SenderNG` (`net9.0`) — Avalonia shell, probing UI, settings — done
3. ✅ Program-view plotter plugins — 2D Skia plotter: isometric/flat quadrant
   views + two-carriage iso loft (design: `docs/machine-config-research.md`,
   plan: `docs/plans/2026-09-07-plotter.md`) — done (2026-09-08). 3D Vulkan
   orbit plotter: `src/SenderNG.Modules.Plotter.Nex` (HelixToolkit.Nex), "Plot 3D"
   center tab behind Skia's (order 23), hidden by default. Camera is
   deferred (possible future 3rd-party plugin — see `docs/ROADMAP.md`).
   Linux serial (`/dev/ttyUSB*` / `/dev/ttyACM*`) is already in

## Development

```bash
# .NET 9 SDK + Avalonia 12 required
dotnet build
dotnet run --project src/SenderNG
```

## Running from release assets

Release zips are produced per platform by `tools/ReleaseManager` (see
`tools/ReleaseManager/README.md` for the build runbooks). A release folder
(`output/win64/` on Windows, `output/linux/` on Linux) looks like this:

```
SenderNG-<ver>-<suffix>.zip                  ← core bundle: the whole app
SenderNG-plugin-excellon-<ver>-<suffix>.zip
SenderNG-plugin-hpgl-<ver>-<suffix>.zip
SenderNG-plugin-plotter-<ver>-<suffix>.zip
SenderNG-plugin-plotter-nex-<ver>-<suffix>.zip
SenderNG-plugin-probing-<ver>-<suffix>.zip
SHA256SUMS.txt                              ← <sha256> <relative path> per file
```

`<suffix>` is `win64` on Windows and `linux-x64` on Linux.

**Core bundle zip** — the complete self-contained app: `SenderNG.exe` /
`SenderNG` runner, all .NET + platform natives, locale catalogs, and a
`Modules/` folder holding only the SDK assembly
`SenderNG.Modules.Common.dll`. Extract it anywhere and run; no .NET install
needed.

**Plugin zips** — one per optional module (`excellon`, `hpgl`,
`plotter`, `plotter-nex`, `probing`). Each contains the module's own assembly
(`SenderNG.Modules.<X>.dll`) plus an `<id>/` support folder (its locale
catalog; `plotter-nex` also carries its HelixToolkit.Nex/Vulkan closure). The
app discovers modules by scanning `Modules/*.dll` at startup, so installing a
module is a straight copy of the zip's contents into the extracted bundle's
`Modules/` folder: the DLL lands flat next to the SDK, `<id>/` becomes
`Modules/<id>/`. Skip the ones you don't want — the core bundle runs fine
without them. `probing` is the in-tree first-party module
(`src/SenderNG.Modules.Probing`) holding the Probing flyout — tool-length,
edge, center, rotation and height-map probing cycles.

Verify the downloads before installing: on Windows compare `Get-FileHash`
against `SHA256SUMS.txt`; on Linux run `cd <platform dir> && sha256sum -c
SHA256SUMS.txt`.

### Windows

1. Requires Windows 10/11 x64. Nothing else — the bundle is self-contained.
2. Unzip `SenderNG-<ver>-win64.zip` (Explorer: right-click → Extract All).
   You get a `SenderNG` folder containing `SenderNG.exe`.
3. Optional modules: unzip each plugin zip and copy its contents — the
   `SenderNG.Modules.<X>.dll` plus the `<id>/` folder (skip `README.txt`) —
   into `SenderNG\Modules\`.
4. Run `SenderNG\SenderNG.exe` → Connect → pick the machine's serial/TCP port.

### Linux

1. Requires Avalonia's X11 system libraries. On Debian/Ubuntu:
   ```
   sudo apt install -y libx11-6 libice6 libsm6 libfontconfig1
   ```
2. Unzip `SenderNG-<ver>-linux-x64.zip` and restore the runner's execute bit
   (zips don't always carry it):
   ```
   unzip SenderNG-<ver>-linux-x64.zip
   chmod +x SenderNG/SenderNG
   ```
3. Optional modules: same copy step as Windows — plugin contents into
   `SenderNG/Modules/`.
4. Run `./SenderNG/SenderNG` → Connect → open the machine's port
   (`/dev/ttyUSB*` / `/dev/ttyACM*` for serial, or TCP).

## Acknowledgements

- Terje Io / Io Engineering for ioSender and grblHAL
- AvaloniaUI community
- HelixToolkit / HelixToolkit.Nex contributors for the 3D Vulkan plotter
