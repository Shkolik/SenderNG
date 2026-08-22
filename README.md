# SenderNG

**Lightweight cross-platform (Windows + Linux) grblHAL gcode sender based on [ioSender](https://github.com/terjeio/ioSender) by Io Engineering (Terje Io).**

SenderNG is a diverged, focused fork of ioSender — stripped to foam cutting, 3–4 axis CNC milling, probing, and USB/Ethernet connectivity. Lathe, DragKnife and other heavy modules are deferred. Not affiliated with or endorsed by Io Engineering / upstream ioSender.

> **Status:** Early infancy — private development. Cross-platform Avalonia UI + .NET 8 Core in progress. Legacy Windows build remains at [Shkolik/ioSender](https://github.com/Shkolik/ioSender) (branch `foamcut`).

## Scope (v0.1)
- Foam cutting (5-axis foam mode), 3–4 axis milling
- Probing (Center/Edge/HeightMap)
- USB serial + Ethernet (Telnet/WebSocket), YModem/SD
- 3D viewer and camera will be substituted (Avalonia-compatible libs) — not `HelixToolkit.Wpf` / `AForge.Video.DirectShow`
- Deferred: Lathe wizards, DragKnife, Converters

## Origin & License
- Based on `ioSender` `2.0.48` (`terjeio/ioSender`) — see `CNC Core/`, `CNC Controls` for original BSD 3-Clause code.
- **License:** BSD 3-Clause — `LICENSE` retains `Copyright (c) 2019-2023 Io Engineering` and adds `Copyright (c) 2024-2026 Shkolik`. See `LICENSE` for full text.
- Avalonia UI (`AvaloniaUI/Avalonia`) is MIT — compatible with BSD.

## Roadmap
1. `src/SenderNG.Core` (`net8.0`) — extract `Grbl.cs`, `GCodeParser.cs`, `GCodeEmulator.cs`, `NGCExpr.cs` + foam patches from `foamcut`
2. `src/SenderNG.Avalonia` (`net8.0`) — Avalonia shell, probing UI, settings
3. Replace viewer/camera, add Linux serial (`/dev/ttyUSB*`, `/dev/ttyACM*`) and V4L2

## Development
```bash
# .NET 8 SDK required
dotnet build
dotnet run --project src/SenderNG.Avalonia
```

## Acknowledgements
- Terje Io / Io Engineering for ioSender and grblHAL
- AvaloniaUI, HelixToolkit, OpenCvSharp communities
