# CodeMap

Every file in the project with purpose, key classes/methods, and line counts. Organized by directory.

See also: [Architecture](Architecture.md) for data flow | [Quirks](Quirks.md) for non-obvious patterns

## Root

### `PlotterNexModule.cs` (119 lines)

Module entry point. Implements `IModule`, `ILocalizedModule`, `IPluginInfo`, `IModuleSettingsProvider`.

| Member | Line | Purpose |
|---|---|---|
| `Id` | 69 | `"plotter-nex"` |
| `Name` | 71 | Localized display name |
| `Description` | 72 | Localized description |
| `Order` | 73 | `23` (behind Skia's 22) |
| `Panel` | 74 | `Center` |
| `IsVisibleByDefault` | 75 | `false` |
| `RegisterServices()` | 77-85 | Registers services into host DI |
| `CreateViewModel()` | 101-106 | Resolves dependencies, constructs `PlotterNexViewModel` |
| `AttachServices()` | 109-110 | Stores `IServiceProvider` for settings view |
| `CreateSettingsView()` | 112-115 | Creates `PlotterNexSettingsView` with its VM |
| `CreateView()` | 117-118 | Creates `PlotterNexView`, sets DataContext |
| `LocalizationKeys` | 88-95 | English + Russian string keys |

### `PlotterNexViewModel.cs` (791 lines)

Core view model. The heart of the module.

| Member | Line | Purpose |
|---|---|---|
| `AsyncBuildThreshold` | 69 | `4000` — programs above this build off-UI-thread |
| `RunStateWindowCap` | 74 | `4096` — max rows processed per tick pass |
| Constructor | 81-141 | DI injection, program change subscription |
| `AttachScene()` | 202-230 | Late-attach ISceneFactory; throws on re-attach |
| `ReleaseScene()` | 233-237 | Drops scene factory reference |
| `OnProgramChanged()` | 247-281 | Program load handler; dispatches to Rebuild |
| `PulseAckedEnds()` | 289-297 | Coalesced per-tick row event fold |
| `RecolorAckedRows()` | 299-337 | Late ack handling behind the frontier |
| `ApplyRunStateWindow()` | 601-635 | Per-frame frontier apply with flood bounding |
| `Rebuild()` | 388-426 | Async/sync build dispatch with generation guard |
| `BuildGeometry()` | 513-553 | Calls ToolpathBuilder.Build, computes line-to-row mapping |
| `ApplyBuild()` | 428-487 | Scene construction + per-plane population |
| `SetToolPosition()` | 346-376 | Check mode: last acked endpoint vs live position |
| `Dispose()` | 780-791 | Cleanup: subscriptions, scene, UI timer |

### `PlotterNexUi.cs` (69 lines)

Static UI thread marshaling utility.

| Member | Line | Purpose |
|---|---|---|
| `Initialize()` | 51-54 | Captures Avalonia `Dispatcher.UIThread` |
| `Post()` | 56-67 | Posts to UI thread; runs inline when no dispatcher or already on UI thread |

### `PlotterNexView.axaml` (255 lines)

Main view XAML. Two content states:
- Normal: `HelixViewport3D` with the Vulkan scene
- Fallback: `TextBlock` when `VulkanUnavailable == true`

### `PlotterNexView.axaml.cs` (729 lines)

View code-behind. The largest file. Hosts the Vulkan render pipeline.

| Member | Line | Purpose |
|---|---|---|
| `EnsureScene()` | 264-313 | Builds `NexPlotScene` on thread pool, attaches via `KinematicSceneFactory` |
| `SetupRenderTarget()` | 315-332 | Creates `RenderContext` + readback target |
| `FreeRenderResources()` | 334-343 | Disposes render context/readback on detach |
| `EnableTick()` / `DisableTick()` | 345-378 | Start/stop the 30Hz render loop |
| `OnTick()` | 380-561 | 33ms DispatcherTimer render loop (see [Architecture](Architecture.md#the-tick-loop-33ms--30hz)); sizes the readback target to the *presented* `GpuImage` bounds (1:1 cursor px), renders + reads back, keeps the last frame for screenshots; on tick crash logs to `pool_diag.log` via `PoolDiagnostics` and disables the tick |
| `UpdateZoomLabel()` | 568-580 | Zoom % readout |
| `PxScale()` / `ToPx()` | 582-584 | DIP → physical px (RenderScaling) for all navigation |
| `OnPlotInputPointerPressed()` | 586-605 | Pan (LMB) / orbit (RMB) start; `BeginOrbit(px)` with cursor px |
| `OnPlotInputPointerMoved()` | 607-623 | Live pan / orbit drag deltas |
| `OnPlotInputPointerReleased()` / `CaptureLost` | 625-639 | Gesture end; `EndOrbit()` |
| `OnPlotInputPointerWheelChanged()` | 641-648 | Zoom anchored to cursor |
| `OnZoomIn()` / `OnZoomOut()` | 650-652 | Center zoom buttons |
| `CenterZoom()` | 654-660 | Zoom-to-center |
| `OnReframe()` | 662 | Re-fit view |
| `OnSaveScreenshot()` | 668-699 | Save PNG of the displayed readback frame to `%LOCALAPPDATA%\SenderNG\screenshots` |
| `Snap()` / `UpdateSnapButtons()` / snap buttons | 701-728 | Zoom-to-center, view presets |

## Settings/

### `PlotterNexSettings.cs` (57 lines)

Settings data model.

| Member | Line | Purpose |
|---|---|---|
| `WireLength` | 38 | `double` — wire visual length |
| `Planes` | 40 | `List<NexPlaneGeometry>` — per-slot axis/origin config |

### `PlotterNexConfig.cs` (151 lines)

Static settings persistence.

| Member | Line | Purpose |
|---|---|---|
| `SectionKey` | 44 | `"plotter-nex"` — MachineProfile.ModuleSettings key |
| `Load()` | 48-75 | Load from profile; falls back to defaults on mismatch |
| `Save()` | 77-97 | Save to profile |
| `Defaults()` | 99-131 | Synthesize defaults from resolved MachineType |
| `RecenterOnWire()` | 133-151 | Recalculate plane origins around wire center |

### `PlotterNexSettingsViewModel.cs` (239 lines)

Settings editor view model.

| Member | Line | Purpose |
|---|---|---|
| `ShowWireLength` | 47 | Display flag for wire length |
| `WireLength` | 52-56 | Bindable wire length |
| `PlaneRows` | 60 | Collection of per-slot editor rows |
| `EnforceDistinct()` | 132-150 | Prevents duplicate axis mappings |
| `Save()` | 228-239 | Persists via `PlotterNexConfig.Save()` |

### `PlotterNexSettingsView.axaml` (91 lines)

Settings editor XAML: wire length textbox, expanders per plane with ComboBoxes + origin textboxes.

### `PlotterNexSettingsView.axaml.cs` (44 lines)

Settings view code-behind. Minimal — just sets DataContext.

## Model/

### `PlaneWorldMapper.cs` (67 lines)

Pure static coordinate mapping.

| Member | Line | Purpose |
|---|---|---|
| `ToWorld()` | 47-67 | Maps plane-local machine positions to world 3D coordinates using `NexPlaneGeometry` |

### `ToolpathSceneBuilder.cs` (121 lines)

Pure static segment-to-scene conversion.

| Member | Line | Purpose |
|---|---|---|
| `PopulatePlane()` | 50-93 | Converts `ToolpathSegment[]` into `LineSegment` entries on a plane layer |
| `ComputeBounds()` | 95-121 | Computes bounding box from populated segments |

## Scene/

### `IPlotScene.cs` (102 lines)

Top-level headless scene interface.

| Member | Line | Purpose |
|---|---|---|
| `SetView()` | 51 | Apply a view preset |
| `Pan()` | 56 | Pan camera |
| `Orbit()` | 61 | Orbit camera |
| `Zoom()` | 66 | Zoom camera |
| `AddCarriagePlaneLayer()` | 71 | Create a plane layer |
| `AddTool()` | 76 | Create a tool |
| `AddLoftSurface()` | 81 | Create a loft surface |
| `RebuildGrid()` | 86 | Rebuild grid/axes chrome |
| `RebuildEnvelopes()` | 91 | Rebuild machine envelopes |

### `ISceneFactory.cs` (55 lines)

Scene factory interface.

| Member | Line | Purpose |
|---|---|---|
| `BuildScene()` | 41 | Create a scene for a machine type |
| `PlaneLayer()` | 46 | Get a plane layer by index |
| `Tool` | 51 | The tool instance |
| `Loft` | 53 | The loft surface instance |

### `ICarriagePlaneLayer.cs` (51 lines)

Per-plane layer abstraction.

| Member | Line | Purpose |
|---|---|---|
| `AddSegment()` | 43 | Add a line segment |
| `SetSegmentRunState()` | 47 | Set run state for a segment |
| `RebuildExecuted` | 50 | Event fired after GPU rebuild |

### `IPlotTool.cs` (47 lines)

Tool abstraction.

| Member | Line | Purpose |
|---|---|---|
| `UpdatePosition()` | 43 | Move tool to new endpoint |
| `Shape` | 46 | Tool shape enum |

### `ILoftSurface.cs` (53 lines)

Loft surface abstraction.

| Member | Line | Purpose |
|---|---|---|
| `SetWirePlane()` | 45 | Set wire plane vertices |
| `SetLoftSheet()` | 49 | Set loft sheet geometry |

### `KinematicSceneFactory.cs` (85 lines)

Headless scene assembly. Creates carriage planes + loft + cone tool per machine type.

### `CameraGestures.cs` (282 lines)

Pure camera math. No GPU dependency — fully unit-testable.

| Member | Line | Purpose |
|---|---|---|
| `GetCameraBasis()` | 51-78 | Compute camera right/up/forward from spherical state |
| `ScreenPanDelta()` | 80-116 | Convert screen drag to world pan delta |
| `AnchorZoom()` | 118-156 | Zoom anchored to a screen point |
| `SphericalState()` | 158-175 | Extract spherical coords from camera matrix |
| `ApplySpherical()` | 177-199 | Apply spherical state to camera |
| `RotateAroundPoint()` | 201-227 | Orbit rotation around a pivot |
| `CursorRayPlanePivot()` | 236-273 | Cursor ray → target-depth plane pivot from the engine's `InvViewProjection` (mirrors `HitTest.cs:120-134`: near z=1 / far z=0.95, dpi-normalized) |

### `SceneTypes.cs` (66 lines)

Enums and records.

| Type | Purpose |
|---|---|
| `ViewPreset` | XY, XZ, YZ, 3D camera presets |
| `MotionType` | Rapid, linear, arc |
| `RunState` | Default, Acked |
| `ToolShape` | Cone, None |
| `BoundingBox` | Min/max vectors |
| `LineSegment` | Start/end + motion type + row index |
| `Endpoint` | Position + motion type |
| `PanDelta` | Screen-space pan vector |

## Rendering/

### `NexPlotScene.cs` (1111 lines)

Vulkan `IPlotScene` implementation. The largest rendering file.

| Member | Line | Purpose |
|---|---|---|
| Constructor | 158-270 | Creates engine, cameras, chrome (grid/axes/origin/envelopes) |
| `AddCarriagePlane()` / `AddLoftSurface()` / `CreateTool()` | 271-295 | Content factory (planes, loft, tool) |
| `SetGrid()` / `AddAxisArrows()` / `SetOrigin()` | 307-383 | Chrome setup |
| `SetWorkEnvelope()` / `SetJobEnvelope()` | 384-393 | Envelopes |
| `SetViewPreset()` | 467-521 | Fit / XY / XZ / YZ / 3D presets |
| `Pan()` / `ZoomAround()` / `SetViewportSize()` | 522-550 | Navigation wiring; viewport size in px |
| `PanByScreenDelta()` / `RotateByScreenDelta()` | 552-572 | Drag deltas (pan LMB, orbit RMB) |
| `BeginOrbit()` | 574-612 | RMB pivot placement: cursor-ray through the render-context snapshot (`_orbitCameraParams`/`_orbitViewportWpx`/`_orbitDpiScale`, fields at 125-134) |
| `EndOrbit()` / `Reframe()` | 614-642 | Gesture end / re-fit |
| `Dispose()` | 644 | Fenced teardown |
| `DumpCamera()` | 880-926 | Diagnostic: camera state |
| `DumpContent()` | 927-951 | Diagnostic: scene content summary |
| `LiveLayerDumps()` | 953 | Per-layer dumps |
| `PumpCarriagePlanes()` / `PumpToolWire()` | 959-971 | Swap-pump GPU re-uploads |
| `UpdateViewport()` | 978-1111 | Camera update for tick; snapshots `renderContext.CameraParams`/`WindowSize`/`DpiScale` for `BeginOrbit` |

### `NexCarriagePlaneLayer.cs` (432 lines)

GPU plane layer with swap-pump pattern.

| Member | Line | Purpose |
|---|---|---|
| Constructor | 76-120 | CPU staging lists + static geometry registration at full capacity |
| `AddSegment()` | 122-145 | Append to staging list |
| `SetSegmentRunState()` | 147-170 | Update color in staging list |
| `Pump()` | 172-250 | Re-upload staging lists as fresh static geometry |
| Static index repack fix | 286-300 | Park unused slots at degenerate coords |

### `NexPlotTool.cs` (317 lines)

GPU tool: filled cone mesh + static connector wire.

| Member | Line | Purpose |
|---|---|---|
| Constructor | 56-100 | Two cone instances sharing one static geometry |
| `UpdatePosition()` | 102-140 | Move cones via node transforms |
| `PumpWire()` | 142-200 | Re-upload wire line geometry |

### `NexLoftSurface.cs` (201 lines)

GPU loft: translucent swept sheet mesh. The wire-plane line loop was permanently removed (fork #7 verdict — it was the red-fan culprit); `SetWirePlane` is a retained interface no-op.

| Member | Line | Purpose |
|---|---|---|
| `SetLoftSheet()` | 87-109 | Build swept sheet from side A/B paths |
| `DisposeMesh()` | 126-136 | Release sheet mesh node + material |

### `NexGpuReadback.cs` (149 lines)

Pure-Vulkan CPU readback. The engine renders into the readback's Vulkan render-target texture (`EngineTarget`), and `IContext.Download` copies it to a tightly-packed RGBA8 CPU buffer the caller rows into a WriteableBitmap. Cross-platform: no D3D11, no shared textures, no keyed mutex.

| Member | Line | Purpose |
|---|---|---|
| `EngineTarget` | 66 | The Vulkan render-target texture the engine renders into |
| Constructor | 68-72 | Create the render-target texture + scratch buffer |
| `CopyFrame()` | 93-129 | `IContext.Download` → row copy (opaque-force) into the bitmap |
| `Resize()` | 133-138 | Recreate the render target for a new pixel size |
| `Dispose()` | 140-148 | Release target + scratch buffer |

### `PoolDiagnostics.cs` (333 lines)

Reflection-based Vulkan command buffer pool inspection.

| Member | Line | Purpose |
|---|---|---|
| `DumpPoolState()` | 30-120 | Read free/in-flight counts, staging regions |
| `DumpQueueTopology()` | 122-180 | Read queue family info |
| `DumpAll()` | 200-333 | Full diagnostic dump |

### `RebuildGate.cs` (49 lines)

Coalescing gate for mutation bursts.

| Member | Line | Purpose |
|---|---|---|
| `ShouldRebuild()` | 20-35 | Returns true if enough time/mutations have elapsed |
| Constants | 14-15 | 150ms min interval, 1000-mutation flush threshold |

## Locale/

### `en.json` (13 lines), `ru.json` (13 lines)

Localization strings. Keys: `PlotterNex_Name`, `PlotterNex_Description`, `PlotterNex_SettingsTab`.

## Tests

### `PlotterNexModuleTests.cs` (158 lines)

Module contract and discovery tests.

### `PlotterNexViewModelTests.cs` (846 lines)

Headless VM tests using `RecorderScene` implementations.

### `PlotterNexSettingsViewModelTests.cs` (104 lines)

Settings editor tests.

### `PlotterNexConfigTests.cs` (143 lines)

Config load/save tests.
