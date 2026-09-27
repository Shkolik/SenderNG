# Diagnostics

Environment variables, probe tools, pool diagnostics, crash recovery, and headless testing.

See also: [Architecture](Architecture.md) for tick loop | [Quirks](Quirks.md) for why things are weird

## Environment Variables

### `NEX_NO_RUN_STATE=1`

Disables all run-state coloring. Used for A/B testing without GPU churn from geometry rebuilds.

When set, `ApplyRunStateWindow()` and `RecolorAckedRows()` are skipped entirely. All lines remain in their default color.

**File:** `PlotterNexViewModel.cs:691-702`

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

**File:** `Rendering/PoolDiagnostics.cs:11-349`

## Tick Crash Recovery

Exceptions in `OnTick()` are caught, logged, and the tick is disabled to prevent cascading failures.

### What gets logged

1. The exception message and stack trace
2. Last 12 ticks from a ring buffer (tick number, duration, what was pumped)
3. Scene content summary (vertex counts, layer counts)
4. Controller pose (last known position, grbl state)
5. Full `PoolDiagnostics.DumpAll()` output

After logging, the tick timer is stopped. The view shows stale content. The user must reload the module tab to retry.

**File:** `PlotterNexView.axaml.cs:535-720`

## Diagnostic Methods

These methods are available on `NexPlotScene` for runtime inspection:

### `DumpCamera()`

Prints camera state: position, target, up vector, orthographic zoom, viewport dimensions.

**File:** `Rendering/NexPlotScene.cs:700-730`

### `DumpContent()`

Prints scene content summary: total vertex count, layer count, tool position.

**File:** `Rendering/NexPlotScene.cs:732-800`

### `LiveLayerDumps()`

Per-layer diagnostic dump: vertex counts, material properties, bounding boxes for each carriage plane layer.

**File:** `Rendering/NexPlotScene.cs:802-947`

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

1. **Set `NEX_NO_RUN_STATE=1`** to isolate whether the issue is run-state related
2. **Call `PoolDiagnostics.DumpAll(engine)`** to inspect GPU pool state
3. **Use `DumpContent()`** to verify vertex counts are as expected
4. **Check tick crash logs** for the ring buffer of recent tick durations
5. **Verify the HelixToolkit.Nex _package_ version** is 1.3.0-alpha.41 (not 1.2.0).
   This is the library version, **not** the Vulkan API version — do not read it as
   a statement about the device. The Plot 3D tab needs a **Vulkan 1.3 device**,
   and the failure hint in the UI is what tells you whether the device falls
   short. See [Why Plot 3D says it is unavailable](#why-plot-3d-says-it-is-unavailable).

## Why Plot 3D says it is unavailable

When the Vulkan context cannot be created, the Plot 3D tab stops trying to render
and shows a **localized** panel with the reason, instead of a blank viewport. It
also writes one `[VULKAN]` record to the pool diagnostics log
(`%LOCALAPPDATA%\SenderNG\pool_diag.log`) — two lines, a summary and its stack:

```
[VULKAN] CONTEXT failed=NotSupported code=NotSupported Plot3DUnavailableException: Vulkan context initialization failed: NotSupported.
[VULKAN] CONTEXT stack:    at SenderNG.Modules.Plotter.Nex.Rendering.NexPlotScene..ctor()
```

- **`failed`** is the user-facing failure *kind* that selects the localized hint:
  `NotSupported`, `RuntimeError`, or `Other`. `n/a` means no verdict was reached —
  see the `READBACK` stage below.
- **`code`** is the engine's own `HelixToolkit.Nex.ResultCode`, or `n/a` for the
  stages that do not come from an engine result.
- The **`stage`** prefix tells you where it happened:
  - `CONTEXT` — creating/initializing the Vulkan context failed
  - `SCENE` — the scene build threw something unexpected (a bug, not a verdict)
  - `ATTACH` — the scene built, but attaching it to the view model failed
  - `RENDERCTX` — the render target / readback failed to initialize
  - `SNAP` — a preset re-frame (Iso/Xy/Xz/Yz) failed on an otherwise live scene
  - `READBACK` — the readback target could not be allocated or resized *after*
    the scene was already live. This one reports `failed=n/a` on purpose: it is
    usually transient host-memory pressure, and this path retries every tick, so
    giving it a verdict would cache `Other` for the session and disable a scene
    whose Vulkan is working perfectly well. It is logged **once per failure
    episode**, not once per retry, so a persistently failing readback does not
    bury the log at 30 lines a second. A `READBACK` line with no panel showing is
    the expected shape — the viewport may recover on the next tick.

The three failure kinds mean different things, and only two of them name a
hardware cause:

- **`NotSupported`** — no usable Vulkan: the loader, instance creation, device
  enumeration, or the device list itself. The driver may be missing or too old to
  install a Vulkan instance at all.
- **`RuntimeError`** — Vulkan works and devices were found, but **none of them
  reports Vulkan 1.3**. This is the one case with a specific, actionable cause;
  the hint points you at `vulkaninfo --summary`.
- **`Other`** — a single operation failed (build, attach, render target, snap),
  or something unexpected threw. This is deliberately *not* a hardware verdict: an
  unexpected engine fault is never relabelled as a version problem, because that
  would tell you your driver is too old for something your driver did not cause.
  See the design's Known limitations #3 — an `Other` failure disables 3D for the
  session (until the app restarts) even if Vulkan is fine, because the failure
  path leaks a `VkInstance` and is not retried.

Exactly **one** `CONTEXT` record is ever written per session: a failed Vulkan
context leaks its instance, so the verdict is cached — including when the failure
lands after the view has already superseded the attempt that produced it, which
is why a tab switch during the build window cannot cause a second Vulkan entry —
and the view never re-enters Vulkan for the life of the module. So:

- **One `[VULKAN] CONTEXT` line and a panel** → a real Vulkan verdict; read
  `failed` above and act on it.
- **No `CONTEXT` line but the tab is blank** → the failure is elsewhere
  (`SCENE`/`ATTACH`/`RENDERCTX`), or the context succeeded and the problem is in
  rendering — check the tick crash log instead.

The exception message and stack go to the diagnostics log **only** — never to the
UI, which stays localized. The panel and the log are the two halves of this
feature: the panel says what the user can act on, the log says exactly what the
engine threw.

