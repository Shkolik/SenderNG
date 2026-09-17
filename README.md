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

## Acknowledgements

- Terje Io / Io Engineering for ioSender and grblHAL
- AvaloniaUI community
