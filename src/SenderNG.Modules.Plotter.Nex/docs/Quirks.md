# Quirks

GPU workarounds, threading patterns, and non-obvious decisions that will bite you if you don't know about them.

See also: [Architecture](Architecture.md) for data flow | [Diagnostics](Diagnostics.md) for debugging tools

## GPU Workarounds

### No in-place dynamic GPU writes

**Problem:** Host-visible mapped buffer writes race the live render loop and produce `VK_ERROR_DEVICE_LOST`.

**Solution:** The entire rendering layer uses a **swap-pump** pattern. CPU staging lists are mutated in-place, then a `RebuildGate`-coalesced `Pump()` re-uploads them as fresh static geometries. The old geometry is discarded after the new one is uploaded.

**Files:** `NexCarriagePlaneLayer.cs:172-250`, `NexPlotTool.cs:142-200`, `NexLoftSurface.cs:152-200`

### Static index buffer repack avoidance

**Problem:** The HelixToolkit.Nex engine packs every static draw's `FirstIndex` from `IndexOffset` into a shared static index buffer and REPACKS it whenever ANY static geometry is Added. This caused a "giant red fan" artifact — a triangle fan rendered across the entire scene.

**Solution:** Register geometries ONCE at full capacity at build time. Never Add/Dispose during runs. Unused slots are parked at `(-50000, 0, 0)` — a degenerate line far below the table where it's invisible.

**Files:** `NexCarriagePlaneLayer.cs:52-68,286-300`

### HelixViewport composition presenter deadlock

**Problem:** The Avalonia WinUI composition server never completes its composition update, causing permanent black views.

**Solution:** The pipeline deliberately bypasses the composition presenter entirely: the Vulkan scene renders into a plain Vulkan render-target texture (created by `NexGpuReadback`), and the tick loop downloads it to the CPU via `IContext.Download` (a Vulkan-internal staging copy, `vkCmdCopyImageToBuffer`), then copies it row-by-row into a `WriteableBitmap`.

**Files:** `PlotterNexView.axaml.cs:68-76`, `Rendering/NexGpuReadback.cs:93-129`

### Readback synchronization

**Problem:** The CPU-side `IContext.Download` must read GPU-complete output; launching the staging copy while the just-submitted draws are still in flight reads a stale (black) frame.

**Solution:** The tick loop calls `Engine.WaitForIdle()` (`Context.WaitAll`) after every `Submit` before `CopyFrame()`, so the render is GPU-complete before the download runs. `IContext.Download` then performs its own immediate-queue submit + wait for the copy. No keyed mutex, shared NT handle, or inter-API fence exists anymore — the readback is pure Vulkan.

**Files:** `NexGpuReadback.cs:50-53`, `PlotterNexView.axaml.cs:498`

### HelixToolkit.Nex version pinning

**Problem:** Version 1.2.0 (last NuGet.org release) throws `TypeLoadException` in `WorldDataProvider.Update()`.

**Solution:** Pin to version 1.3.0-alpha.41 from the GitHub Packages feed. The csproj includes a `<PackageReference>` with an explicit `Version` and a `<PackageSource>` mapping to the GitHub Packages URL.

**Files:** `SenderNG.Modules.Plotter.Nex.csproj:20-27`

## Threading Patterns

### Row event coalescing

**Problem:** `RowsChanged` events arrive on the serial thread at stream speed. Posting one dispatcher action per event starves the UI during fast check runs.

**Solution:** The events only flip `_rowsChangedPending`. The view's 30Hz tick calls `PulseAckedEnds()` which folds all pending events once. This is a classic "dirty flag" pattern applied to cross-thread event folding.

**Files:** `PlotterNexViewModel.cs:283-297`

### Async geometry build with generation counter

**Problem:** Large programs (>4000 tokens) take significant time to build. Building on the UI thread causes jank.

**Solution:** Programs above the threshold build on a thread pool with a `_generation` counter guard. If the program changes during the build, the generation counter increments and the stale result is silently discarded. Small programs (<4000) build synchronously for instant display.

**Files:** `PlotterNexViewModel.cs:388-426`

### Scene lifecycle across tab switches

**Problem:** Recreating the GPU scene on every tab switch is expensive.

**Solution:** The GPU scene persists across tab switches (the host caches one view per module). Only the per-attach render context + readback target are torn down and recreated. `EnsureScene()` is a no-op when a scene already exists.

**Files:** `PlotterNexView.axaml.cs:115-189`

## Design Decisions

### Headless scene abstraction

The entire `Scene/` folder contains engine-free interfaces + pure math. The `Rendering/` folder provides the Vulkan implementation. This enables:
- Fully headless unit testing (no GPU required)
- Swappable rendering backends
- The `CameraGestures` class is pure math with zero GPU dependencies

**Files:** `Scene/` directory, `CameraGestures.cs`

### Plugin isolation

The module depends ONLY on `SenderNG.Core` types. It knows nothing about the host app's concrete types. This enables true swappable plugins — the host discovers and loads the plugin without compile-time references.

**Files:** `PlotterNexModule.cs` (no host app `using` statements)

### Static geometry lifecycle

All GPU geometries are registered ONCE at build time and re-uploaded in place. No geometry Add/Dispose during runs. This prevents shared static index buffer repacking artifacts (see [Static index buffer repack avoidance](#static-index-buffer-repack-avoidance) above).

### Sync-then-async build

Small programs build synchronously for instant display. Large programs build asynchronously with generation-counter guards. The threshold (4000 tokens) was chosen empirically — programs below this build in under 16ms on typical hardware.

**Files:** `PlotterNexViewModel.cs:69` (`AsyncBuildThreshold`)

### Coalesced run-state coloring

Instead of per-row GPU updates (which starve the UI), the tick coalesces all row events and applies them in a bounded sliding window every ~100ms (~10Hz), with a `RebuildGate` preventing too-frequent geometry rebuilds. The window cap (4096 rows) prevents single-tick stalls on large ack bursts.

**Files:** `PlotterNexViewModel.cs:74` (`RunStateWindowCap`), `RebuildGate.cs`
