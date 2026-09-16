# PlotterNexModule Documentation

End-to-end documentation for the 3D G-code toolpath plotter plugin. Written for developers maintaining or modifying this module.

## What is this?

PlotterNexModule is a **3D G-code toolpath plotter** that renders loaded CNC programs in an orbitable 3D viewport. It uses **HelixToolkit.Nex** (Vulkan-based GPU engine) and ships as a **true external plugin** discovered at runtime by the SenderNG host.

It is a sibling to the existing 2D Skia plotter (`SenderNG.Modules.Plotter`), claiming the same Center panel with a different tab ("Plot 3D", order=23, behind Skia's order=22). Hidden by default (`IsVisibleByDefault => false`).

## Documentation Map

| Document | Purpose |
|---|---|
| [Architecture](Architecture.md) | Layer breakdown, DI wiring, data flow, tick loop |
| [CodeMap](CodeMap.md) | Every file and class with purpose, key methods, line counts |
| [Quirks](Quirks.md) | GPU workarounds, threading patterns, non-obvious decisions |
| [Diagnostics](Diagnostics.md) | Env vars, probe tools, pool diagnostics, crash recovery |

## Quick Lookup: "Where is X?"

| Concept | Location |
|---|---|
| Module entry point | `PlotterNexModule.cs` — implements `IModule`, `ILocalizedModule`, `IPluginInfo` |
| Main view model | `PlotterNexViewModel.cs` — geometry pipeline, run-state coloring, row event coalescing |
| Render loop | `PlotterNexView.axaml.cs` — `OnTick()`, 33ms DispatcherTimer |
| Vulkan scene | `Rendering/NexPlotScene.cs` — engine, cameras, chrome |
| Scene abstraction | `Scene/IPlotScene.cs` + siblings — headless interfaces for testing |
| Camera math | `Scene/CameraGestures.cs` — pure math, no GPU dependency |
| Settings persistence | `Settings/PlotterNexConfig.cs` — JSON in MachineProfile.ModuleSettings |
| Vulkan readback | `Rendering/NexGpuReadback.cs` — Vulkan staging download (`IContext.Download`) → WriteableBitmap |
| GPU diagnostics | `Rendering/PoolDiagnostics.cs` — reflection-based Vulkan pool inspection |
| Plane geometry | `Model/PlaneWorldMapper.cs` — plane-local to world coordinate mapping |
| Toolpath building | `Model/ToolpathSceneBuilder.cs` — segments to scene population |
| Localization | `Locale/en.json`, `Locale/ru.json` |

## Project Structure

```
SenderNG.Modules.Plotter.Nex/
    PlotterNexModule.cs              ← entry point
    PlotterNexViewModel.cs           ← core logic
    PlotterNexUi.cs                  ← UI thread marshaling
    PlotterNexView.axaml             ← main view XAML
    PlotterNexView.axaml.cs          ← render loop, navigation
    Settings/
        PlotterNexSettings.cs        ← settings data model
        PlotterNexConfig.cs          ← load/save/defaults
        PlotterNexSettingsViewModel.cs ← settings editor VM
        PlotterNexSettingsView.axaml ← settings editor XAML
        PlotterNexSettingsView.axaml.cs
    Model/
        PlaneWorldMapper.cs          ← coordinate mapping
        ToolpathSceneBuilder.cs      ← segment-to-scene conversion
    Scene/
        IPlotScene.cs                ← top-level scene abstraction
        ISceneFactory.cs             ← scene factory interface
        ICarriagePlaneLayer.cs       ← plane layer abstraction
        IPlotTool.cs                 ← tool abstraction
        ILoftSurface.cs              ← loft surface abstraction
        KinematicSceneFactory.cs     ← headless scene assembly
        CameraGestures.cs            ← camera math
        SceneTypes.cs                ← enums/records
    Rendering/
        NexPlotScene.cs              ← Vulkan scene implementation
        NexCarriagePlaneLayer.cs     ← GPU plane layer
        NexPlotTool.cs               ← GPU tool cone + wire
        NexLoftSurface.cs            ← GPU loft surface
        NexGpuReadback.cs            ← Vulkan CPU readback (IContext.Download)
        PoolDiagnostics.cs           ← Vulkan pool diagnostics
        RebuildGate.cs               ← coalescing rebuild gate
    Locale/
        en.json, ru.json
```

## Key Dependencies

- **HelixToolkit.Nex** 1.3.0-alpha.41 (from GitHub Packages feed — see [Quirks](Quirks.md#helixtoolkitnex-version-pinning))
- **SenderNG.Core** only — no host app references (true plugin isolation)
- **Vulkan 1.3** required at runtime (graceful degradation to text fallback when unavailable)
