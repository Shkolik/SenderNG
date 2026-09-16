# Diagnostics

Environment variables, probe tools, pool diagnostics, crash recovery, and headless testing.

See also: [Architecture](Architecture.md) for tick loop | [Quirks](Quirks.md) for why things are weird

## Environment Variables

### `NEX_NO_RUN_STATE=1`

Disables all run-state coloring. Used for A/B testing without GPU churn from geometry rebuilds.

When set, `ApplyRunStateWindow()` and `RecolorAckedRows()` are skipped entirely. All lines remain in their default color.

**File:** `PlotterNexViewModel.cs:691-702`

## Fan Artifact Detection

The view includes a ring-median spike detector that catches the "giant red fan" artifact caused by static index buffer repacking (see [Quirks](Quirks.md#static-index-buffer-repack-avoidance)).

### How it works

1. Every 5th probe tick, `ProbeFrame()` runs
2. `CountConeRed()` counts pixels matching the tool cone's red color
3. `CountNonBackground()` counts non-background pixels
4. If red pixels exceed a threshold relative to non-background pixels, a fan is detected
5. `SaveFrameBmp()` saves a BMP snapshot

### Snapshot location

```
%LOCALAPPDATA%\SenderNG\pool_diag\fans\
```

Files are named with a timestamp. Check this directory when investigating rendering artifacts.

**File:** `PlotterNexView.axaml.cs:532-558`

## Pool Diagnostics

`PoolDiagnostics` uses **reflection** to read Vulkan command buffer pool internals without depending on any public API. This is intentional — the public API doesn't expose the information needed for diagnostics.

### What it reads

| Data | Source | Purpose |
|---|---|---|
| Free buffer count | `_freeStack` field | Detect pool exhaustion |
| In-flight queue length | `_inFlightQueue` field | Detect GPU backpressure |
| Staging region count | `_stagingRegions` field | Detect staging buffer exhaustion |
| Queue family topology | Queue properties | Verify Vulkan queue configuration |

### Usage

Call `PoolDiagnostics.DumpAll(engine)` with the Vulkan engine instance. Output is logged to the diagnostic stream. This is called automatically on tick crash (see below).

**File:** `Rendering/PoolDiagnostics.cs:12-333`

## Tick Crash Recovery

Exceptions in `OnTick()` are caught, logged, and the tick is disabled to prevent cascading failures.

### What gets logged

1. The exception message and stack trace
2. Last 12 ticks from a ring buffer (tick number, duration, what was pumped)
3. Scene content summary (vertex counts, layer counts)
4. Controller pose (last known position, grbl state)
5. Full `PoolDiagnostics.DumpAll()` output

After logging, the tick timer is stopped. The view shows stale content. The user must reload the module tab to retry.

**File:** `PlotterNexView.axaml.cs:566-579`

## Diagnostic Methods

These methods are available on `NexPlotScene` for runtime inspection:

### `DumpCamera()`

Prints camera state: position, target, up vector, orthographic zoom, viewport dimensions.

**File:** `Rendering/NexPlotScene.cs:700-730`

### `DumpContent()`

Prints scene content summary: total vertex count, layer count, tool position, loft state.

**File:** `Rendering/NexPlotScene.cs:732-800`

### `LiveLayerDumps()`

Per-layer diagnostic dump: vertex counts, material properties, bounding boxes for each carriage plane layer.

**File:** `Rendering/NexPlotScene.cs:802-947`

### View-level diagnostics

On `PlotterNexView`:

| Method | Purpose |
|---|---|
| `DumpLineMaterials()` | Print material properties for all line geometries |
| `DumpLineStreams()` | Print vertex stream data (positions, colors, motion types) |
| `DumpFetchInputs()` | Print input fetch state for the current render pass |

These are invoked from the diagnostic button in the view or programmatically during debugging.

## Headless Testing

The entire scene interface is abstract (`Scene/` folder), allowing headless tests without a GPU.

### Test implementations

| Class | Replaces | Purpose |
|---|---|---|
| `RecorderScene` | `IPlotScene` | Records all camera/scene operations for assertion |
| `RecorderPlaneLayer` | `ICarriagePlaneLayer` | Records segment additions and run-state changes |
| `RecorderTool` | `IPlotTool` | Records position updates |
| `RecorderLoft` | `ILoftSurface` | Records wire plane and loft sheet operations |

### Headless ViewModel

`PlotterNexViewModel` can be tested without a view. The `PlotterNexUi.Post()` method runs inline when no Avalonia dispatcher is initialized (headless test environment).

### Test files

| File | Lines | Tests |
|---|---|---|
| `PlotterNexModuleTests.cs` | 158 | Module contract, discovery, service registration |
| `PlotterNexViewModelTests.cs` | 846 | Geometry pipeline, run-state coloring, async builds |
| `PlotterNexSettingsViewModelTests.cs` | 104 | Settings editor, axis deduplication |
| `PlotterNexConfigTests.cs` | 143 | Config load/save, defaults, mismatch handling |

### Running tests

```bash
dotnet test tests/SenderNG.Core.Tests/
```

## Debugging GPU Issues

When investigating rendering problems:

1. **Check fan snapshots** in `%LOCALAPPDATA%\SenderNG\pool_diag\fans\`
2. **Set `NEX_NO_RUN_STATE=1`** to isolate whether the issue is run-state related
3. **Call `PoolDiagnostics.DumpAll()`** to inspect GPU pool state
4. **Use `DumpContent()`** to verify vertex counts are as expected
5. **Use `DumpLineStreams()`** to verify vertex data is correct
6. **Check tick crash logs** for the ring buffer of recent tick durations
7. **Verify HelixToolkit.Nex version** is 1.3.0-alpha.41 (not 1.2.0)
