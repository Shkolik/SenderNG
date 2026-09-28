# Diagnostics

Environment variables, probe tools, pool diagnostics, crash recovery, and headless testing.

See also: [Architecture](Architecture.md) for tick loop | [Quirks](Quirks.md) for why things are weird

## Environment Variables

### `NEX_NO_RUN_STATE=1`

Disables all run-state coloring. Used for A/B testing without GPU churn from geometry rebuilds.

When set, `ApplyRunStateWindow()` and `RecolorAckedRows()` are skipped entirely. All lines remain in their default color.

**File:** `PlotterNexViewModel.cs:703-742` (`ApplyRunStateWindow`)

## Pool Diagnostics

`PoolDiagnostics` uses **reflection** to read Vulkan command buffer pool internals without depending on any public API. This is intentional — the public API doesn't expose the information needed for diagnostics.

### What it reads

| Data | Source | Purpose |
|---|---|---|
| Free buffer count | `_freeStack` field | Detect pool exhaustion |
| In-flight queue length | `_inFlightQueue` field | Detect GPU backpressure |
| Staging region count | `_stagingDevice` → `_regions` | Detect staging buffer exhaustion |
| Queue family topology | Queue properties | Verify Vulkan queue configuration |

### Usage

Call `PoolDiagnostics.Dump(context, label)` with the scene's **context**
(`scene.Context`) — it reflects over the context's command pools, not the engine.
The result is a string; the caller logs it. This is called automatically on tick
crash (see below), which passes the label `"CRASH"`.

**File:** `Rendering/PoolDiagnostics.cs:11-349`

## Tick Crash Recovery

Exceptions in `OnTick()` are caught, logged, and the tick is disabled to prevent cascading failures.

### What gets logged

1. The exception message and the first 4 stack frames
2. Last 12 ticks from a ring buffer (tick number, the per-layer dump, run state)
3. Scene content summary (per-node vertex counts, chrome, tool count)
4. Controller pose (last known position, grbl state)
5. Full `PoolDiagnostics.Dump(scene.Context, "CRASH")` output

After logging, the tick timer is stopped, so the view shows stale content and
stays there. **Switching to another tab and back restarts it:** detach keeps the
scene and the engine alive, and re-attaching rebuilds the per-attach render
context and re-arms the tick. A full app restart is only needed to clear a
*cached* Vulkan verdict, which disables 3D for the session (see the `Other`
bullet under [Why Plot 3D says it is
unavailable](#why-plot-3d-says-it-is-unavailable) below).

**File:** `PlotterNexView.axaml.cs:618-809` (`OnTick`)

## Diagnostic Methods

These methods are available on `NexPlotScene` for runtime inspection:

### `DumpCamera()`

Prints camera state: camera type, orthographic width (or perspective fov),
position, target, near/far planes, and the content bounds — centre, radius,
span, and the vertex extent. It does **not** print the up vector or the viewport
dimensions.

**File:** `Rendering/NexPlotScene.cs:933` (`DumpCamera`)

### `DumpContent()`

Prints scene content summary: the chrome block, a per-node vertex count for the
content block, and the tool count with each tool's own content dump. There is no
single total vertex count and no layer count.

**File:** `Rendering/NexPlotScene.cs:980` (`DumpContent`)

### `LiveLayerDumps()`

Per-layer counters — done-overlay vertices, executed segments, plane colour
writes. It prints no material properties and no bounding boxes — for geometry
detail use `DumpContent()`. The caller joins the layers onto a single line with
`" | "`, and `done verts` counts the finished-overlay vertices (two per executed
segment), not vertices submitted.

**File:** `Rendering/NexPlotScene.cs:1006` (`LiveLayerDumps`)

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
| `PlotterNexViewModelTests.cs` | 1125 | Geometry pipeline, run-state coloring, async builds |
| `PlotterNexSettingsViewModelTests.cs` | 259 | Settings editor, axis deduplication |
| `PlotterNexModuleTests.cs` | 209 | Module contract, discovery, service registration |
| `PlotterNexLocaleTests.cs` | 273 | Catalog parity, non-blank values, untranslated-value guard |
| `PlotterNexConfigTests.cs` | 143 | Config load/save, defaults, mismatch handling |
| `PlotterNexViewTests.cs` | 108 | Scene-generation ownership guard, verdict caching |

Line counts drift; treat the file name as authoritative.

### Running tests

```bash
dotnet test tests/SenderNG.Core.Tests/
```

## Debugging GPU Issues

When investigating rendering problems:

1. **Set `NEX_NO_RUN_STATE=1`** to isolate whether the issue is run-state related
2. **Call `PoolDiagnostics.Dump(scene.Context, label)`** to inspect GPU pool state
3. **Use `DumpContent()`** to verify vertex counts are as expected
4. **Check tick crash logs** for the ring buffer of recent ticks
5. **Verify the HelixToolkit.Nex _package_ version** is 1.3.0-alpha.41 (not 1.2.0).
   This is the library version, **not** the Vulkan API version — do not read it as
   a statement about the device. The Plot 3D tab needs a **Vulkan 1.3 device**,
   and the failure hint in the UI is what tells you whether the device falls
   short. See [Why Plot 3D says it is unavailable](#why-plot-3d-says-it-is-unavailable).

## Why Plot 3D says it is unavailable

When the Vulkan context cannot be created, the Plot 3D tab stops trying to render
and shows a **localized** panel with the reason, instead of a blank viewport. It
also writes `[VULKAN]` records to the pool diagnostics log
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
  - `RENDERCTX` — the render context failed to create/initialize
  - `SNAP` — a preset re-frame (Iso/Xy/Xz/Yz) failed on an otherwise live scene
  - `READBACK` — the readback target could not be allocated or resized *after*
    the scene was already live. This one reports `failed=n/a` on purpose: it is
    usually transient host-memory pressure, and this path retries every tick, so
    giving it a verdict would cache `Other` for the session and disable a scene
    whose Vulkan is working perfectly well. It is logged **once per failure
    episode**, not once per retry, so a persistently failing readback does not
    bury the log at roughly sixty lines a second. A `READBACK` line with no panel
    showing is the expected shape — the viewport may recover on the next tick.

The three failure kinds mean different things, and only two of them name a
hardware cause:

- **`NotSupported`** — no usable Vulkan: the loader, instance creation, device
  enumeration, or the device list itself. The driver may be missing or too old to
  install a Vulkan instance at all.
- **`RuntimeError`** — Vulkan works and devices were found, but **none of them
  reports Vulkan 1.3**. This is the one case with a specific, actionable cause;
  the hint points you at `vulkaninfo --summary`.
- **`Other`** — a single operation failed (build, attach, render context, snap),
  or something unexpected threw. This is deliberately *not* a hardware verdict: an
  unexpected engine fault is never relabelled as a version problem, because that
  would tell you your driver is too old for something your driver did not cause.
  See the design's Known limitations #3 — an `Other` failure disables 3D for the
  session (until the app restarts) even if Vulkan is fine. Two separate things
  make that stick, and they do not apply equally: a cached verdict is never
  cleared and nothing retries these paths, and a `build` (`SCENE`) failure also
  leaks, because a constructor that throws partway leaves a half-built scene
  that nothing holds a reference to. The later stages that fail *after* a
  complete scene — attach, render context, snap — dispose theirs properly.

Which path a verdict came from matters, because the leak rationale only covers
some. `NotSupported` and `RuntimeError` are decided by the capability probe,
which runs before any engine context exists and destroys its own instance in a
`finally`, so nothing leaks. A genuine engine context-init failure (`CONTEXT`)
does leak its `VkInstance`, because upstream teardown is a no-op after a failed
init — and so does a `SCENE` failure, for the unreachable-object reason above.
The cache is what bounds those two: it is why only one attempt happens.
there to bound.

A failed Vulkan context leaks its `VkInstance`, so the verdict is cached and the
view does not re-enter Vulkan once it knows about the failure. The cache is
written even when the failure lands after the view has already superseded the
attempt that produced it — remembering is unconditional, and only *reporting* is
gated, because reporting mutates the view model and the panel, which the newer
attempt owns.

**That distinction is visible in the log, and worth knowing before you go
looking for a panel.** A superseded attempt's failure is remembered but never
reported, so no panel appears *for that verdict*. If a later attempt succeeded,
the viewport is drawing normally and that cached verdict can never reach the
panel, because a live scene suppresses further scene builds. The `CONTEXT` line
is then the only record that the failure happened.

Be precise about what that does and does not mean. A `SNAP` re-frame or a
`RENDERCTX` failure on the live scene reports independently of the cache (see the
stage table above), so the first *reported* failure is not always the first one
in the log. Those later failures tear the scene down as they report, so a panel
from them arrives together with the viewport going blank, not alongside a
drawing one. What you can rely on: the panel's hint comes from the first
*reported* verdict and never changes afterwards, and a superseded `CONTEXT` line
with no panel is normal.

**The cache can also only stop attempts it already knows about**, and the window
is real: any re-attach before the failing attempt's result has been *processed on
the UI thread* starts a second attempt, because at re-attach the cache is still
empty. That window is wider than "before the attempt failed" — the cache is
written when the dispatcher runs the completion, not when the attempt fails.
Tab switching is the usual trigger; a data-context change also re-enters
`EnsureScene`, though in the shipped app the context is assigned once, so that
route fires at startup rather than on demand. So a `CONTEXT` line per attempt in
that window is possible. In the shipped build these lines are cheap rather than
leaky: they come from the memoized capability probe, which leaks nothing, and
the verdict is memoized too — so every line in the window carries the **same**
`failed` kind, and they do not contradict each other. So:

- **One `[VULKAN] CONTEXT` line and a panel** → a real Vulkan verdict; read
  `failed` above and act on it.
- **Several `CONTEXT` lines, seconds apart** → repeated attach attempts inside
  that window, not a new problem. All of them should name the same `failed`
  kind; if one does not, say so, because the probe result is memoized and a
  differing kind is not expected. The panel shows whichever attempt first
  *reported*.
- **A `CONTEXT` line and no panel** → that failure was superseded and cached
  without being reported. Fine if 3D is rendering; if the view is blank, the live
  attempt failed at a later stage and its own line will say so.
- **No `CONTEXT` line but the tab is blank** → the failure is elsewhere
  (`SCENE`/`ATTACH`/`RENDERCTX`), or the context succeeded and the problem is in
  rendering — check the tick crash log instead.

The exception message and stack go to the diagnostics log **only** — never to the
UI, which stays localized. The panel and the log are the two halves of this
feature: the panel says what the user can act on, the log says exactly what the
engine threw.

