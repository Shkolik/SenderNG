# Architecture

Layer breakdown, dependency injection wiring, data flow, and the render tick loop.

See also: [CodeMap](CodeMap.md) for per-file details | [Quirks](Quirks.md) for workarounds | [Diagnostics](Diagnostics.md) for debugging

## Layer Diagram

```
┌─────────────────────────────────────────────────────┐
│                  Host App (SenderNG)                 │
│  ModuleCatalog.ScanDirectory → discovers plugins     │
│  calls IModule lifecycle methods                     │
└──────────────────┬──────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────┐
│              PlotterNexModule                        │
│  Implements: IModule, ILocalizedModule,              │
│              IPluginInfo, IModuleSettingsProvider    │
│  Id: "plotter-nex", Order: 23, Panel: Center        │
│  IsVisibleByDefault: false                           │
└──────┬───────────┬──────────────┬───────────────────┘
       │           │              │
       ▼           ▼              ▼
┌────────────┐ ┌────────────┐ ┌──────────────┐
│  ViewModel │ │    View    │ │   Settings   │
│  (VM)      │ │            │ │    View      │
└─────┬──────┘ └─────┬──────┘ └──────────────┘
      │              │
      │    ┌─────────┘
      │    │  late-attach via AttachScene()
      ▼    ▼
┌─────────────────────────────────────────────────────┐
│              Scene Abstraction Layer                 │
│  IPlotScene, ISceneFactory, ICarriagePlaneLayer,     │
│  IPlotTool, ILoftSurface                             │
│  CameraGestures (pure math)                          │
│  KinematicSceneFactory (headless assembly)           │
└──────────────────┬──────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────┐
│              Rendering Layer                         │
│  NexPlotScene, NexCarriagePlaneLayer, NexPlotTool,   │
│  NexLoftSurface, NexGpuReadback                      │
│  PoolDiagnostics, RebuildGate                        │
└─────────────────────────────────────────────────────┘
```

## Dependency Injection Wiring

### What gets registered

`PlotterNexModule.RegisterServices()` registers services that other modules or the host might need. The module itself receives these via `CreateViewModel`:

### What the ViewModel receives

`PlotterNexViewModel` constructor takes:

| Parameter | Source | Purpose |
|---|---|---|
| `ILoadedProgramSource` | DI | Program load/change/row-state events |
| `IGrblService` | DI | Controller state observable |
| `SettingsService` | DI | Per-profile settings persistence |
| `LocalizationService` | DI | String resolution |
| `ISceneFactory` | Late-attached | Scene creation (see below) |

### Late-attach lifecycle

The scene factory is **not** injected via DI. It is late-attached by the view:

1. `PlotterNexView` builds `NexPlotScene` on the thread pool
2. Wraps it in `KinematicSceneFactory` on the UI thread
3. Calls `PlotterNexViewModel.AttachScene(factory)`
4. `AttachScene()` throws if called twice (no re-attach)
5. `ReleaseScene()` drops the reference; no scene rebuild

This design exists because the GPU scene depends on the Vulkan engine, which requires a window handle — only available after the view is constructed. The module runs on both Windows and Linux (Vulkan 1.3 required); the readback is a pure-Vulkan CPU download (`IContext.Download`), so no Windows-only interop is involved.

### Settings view lifecycle

`PlotterNexModule.CreateSettingsView()` resolves `IModuleSettingsProvider` separately. Settings must work **before** the module's own tab is opened (the host creates settings views eagerly at startup). `AttachServices()` stores `IServiceProvider` for this purpose.

## Data Flow: Program Change to GPU Render

```
Program loaded/changed (ILoadedProgramSource)
    │
    ▼
PlotterNexViewModel.OnProgramChanged()
    │
    ├─── Small program (<4000 tokens) → Rebuild() on UI thread
    │
    └─── Large program (≥4000 tokens) → Rebuild() on thread pool
         (generation counter guard; stale results silently discarded)
    │
    ▼
Rebuild()
    │
    ├── ToolpathBuilder.Build() → ToolpathSegment[]
    │
    ▼
BuildGeometry()
    │
    ├── Compute PlaneOutputLines[] (per-plane line counts)
    ├── Compute LineToRow[] (line index → row mapping)
    │
    ▼
ApplyBuild()
    │
    ├── ISceneFactory.BuildScene(MachineType)
    │
    └── For each plane:
        └── ToolpathSceneBuilder.PopulatePlane(planeLayer, segments)
            │
            ├── PlaneWorldMapper.ToWorld() → world coordinates
            ├── ICarriagePlaneLayer.AddSegment(LineSegment)
            └── Compute bounds
```

## Run-State Coloring Pipeline

The run-state coloring system shows which rows have been acknowledged by the controller.

```
Controller acks rows (RowsChanged event, serial thread)
    │
    ▼
_plotterNexViewModel.PulseAckedEnds()  ← coalesced per-tick
    │  (only if _rowsChangedPending == true)
    │
    ▼
ApplyRunStateWindow()
    │
    ├── Bounded sliding window: processes max 4096 rows per tick
    ├── FrontierVersion tracks cumulative ack state
    │
    ▼
RecolorAckedRows()
    │
    ├── For each row in window:
    │   └── ICarriagePlaneLayer.SetSegmentRunState(line, Acked)
    │
    └── RebuildGate throttle (150ms min, 1000-mutation flush)
        │
        ▼
    NexCarriagePlaneLayer.Pump()
        │
        ▼
    GPU geometry re-upload (swap-pump pattern)
```

### Check mode

When the controller is in grbl "Check" mode, the tool position shows the last acked segment endpoint instead of the live position (`PlotterNexViewModel.cs:346-376`).

## The Tick Loop (33ms / ~30Hz)

`PlotterNexView.OnTick()` drives the render pipeline. Every tick:

| Step | Method | Description |
|---|---|---|
| 1 | `PulseAckedEnds()` | Fold pending row events into ack state |
| 2 | `ApplyRunStateWindow()` | Apply run-state colors (every 3rd tick) |
| 3 | `PumpCarriagePlanes()` | Re-upload plane GPU geometry (every 3rd tick) |
| 4 | `PumpToolWireAndLofts()` | Re-upload tool position + loft surface |
| 5 | `UpdateViewport()` | Update Vulkan camera/viewport |
| 6 | `RenderOffscreen()` | Vulkan renders to the readback's render-target texture |
| 7 | `CopyFrame()` | Vulkan staging download → WriteableBitmap |

Steps 2-3 run every 3rd tick to reduce GPU churn. Steps 4-7 run every tick for smooth tool animation.

### Coordinate mapping (cursor ↔ framebuffer)

Navigation uses **physical pixels** end to end: the view converts the pointer with `ToPx(e.GetPosition(PlotInput), PxScale())` (RenderScaling), and the engine viewport is sized in the same physical-px space. For that mapping to stay 1:1 with what is on screen, the readback target must match the **presented** panel, not the window client rect: the tick takes `wDip/hDip` from `GpuImage.Bounds` when valid and falls back to the client rect only before first layout. (A stale client-sized framebuffer was the cause of the off-cursor orbit pivot — see [Quirks](Quirks.md#framebuffer-tracks-the-presented-panel).) Because of this, `BeginOrbit` needs no DIP/px conversion: it unprojects the physical cursor px straight through the render-context snapshot's `InvViewProjection` and intersects the target-depth plane (`CameraGestures.CursorRayPlanePivot`).

## Plugin Discovery

The host does NOT reference the plugin's types at runtime:

1. **Build-time staging** (`SenderNG.csproj`):
   - Plugin DLL staged to `Modules\SenderNG.Modules.Plotter.Nex.dll`
   - NuGet closure (HelixToolkit.Nex, etc.) staged to `Modules\plotter-nex\`
   - Native binaries (vma, spirv-reflect, glslang) staged to `Modules\plotter-nex\` for the current build OS (win-x64 `.dll` on Windows, linux-x64 `.so` on Linux)
   - Custom MSBuild target `StageNexPluginClosure` copies these after build

2. **Runtime discovery** (`ModuleCatalog.ScanDirectory`):
   - Scans `Modules\*.dll` for `IModule` implementations
   - Found via `LoadFrom` assembly context
   - Registered into DI container as singletons

## Threading Model

| Thread | Work |
|---|---|
| UI thread | Tick loop, ViewModel property changes, scene attachment |
| Serial thread | Controller row events, program changes |
| Thread pool | Async geometry builds (>4000 tokens), NexPlotScene construction |

Cross-thread communication:
- `PlotterNexUi.Post(Action)` — posts to UI thread (runs inline in headless tests)
- `_rowsChangedPending` flag — coalesces row events into a single fold per tick
- `_generation` counter — discards stale async build results
