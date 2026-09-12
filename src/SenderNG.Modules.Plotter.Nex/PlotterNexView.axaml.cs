/*
 * PlotterNexView.axaml.cs - HelixToolkit.Nex (Vulkan) host for the plotter
 */

/*

Copyright (c) 2026 Andrew Shkolik
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Input;
using Vector2 = System.Numerics.Vector2;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Rendering;
using SenderNG.Core.Localization;
using SenderNG.Modules.Plotter.Nex.Rendering;
using SenderNG.Modules.Plotter.Nex.Scene;

namespace SenderNG.Modules.Plotter.Nex;

/// <summary>
/// Hosts the Vulkan plot scene. The engine and scene are expensive to build
/// and must not block the UI thread (the Nex scene build is synchronous-over-
/// asynchronous), so creation runs on the thread pool and the result is applied
/// back on the UI thread. A failed creation (no Vulkan 1.3 device, missing
/// runtime) flips <see cref="PlotterNexViewModel.VulkanUnavailable"/> and the
/// XAML swaps in the text fallback.
/// <para>
/// Rendering: a DispatcherTimer tick drives
/// UpdateViewport → RenderOffscreen into a D3D11 shared texture, then a
/// keyed-mutex readback into a WriteableBitmap shown by <c>GpuImage</c>. This
/// deliberately does NOT use <c>HelixViewport</c>: its composition presenter
/// path deadlocks (the Avalonia WinUI composition server never completes the
/// shared-texture update; present backpressure then hangs the render loop
/// forever). The readback keeps the engine's headless custom-target frame.
/// </para>
/// </summary>
public partial class PlotterNexView : UserControl
{
    private const double TickIntervalMs = 33.0;
    private const float FixedDeltaTime = 1f / 30f;

    private PlotterNexViewModel? _vm;
    private NexPlotScene? _scene;
    private int _sceneGeneration;
    private bool _attached;
    private bool _tickSubscribed;

    private RenderContext? _renderContext;
    private NexGpuReadback? _readback;
    private WriteableBitmap? _bitmap;
    private DispatcherTimer? _tick;
    private uint _wPx;
    private uint _hPx;

    private int _readbackTicks;
    private int _windowFrames;
    private long _windowStart;
    private double _fps;
    private int _probeCounter;

    private bool _panActive;
    private bool _orbitActive;
    private Vector2 _lastPanPx;
    private Vector2 _lastOrbitPx;

    public PlotterNexView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Tab switches detach the view from the visual tree; the host caches
        // one view per module for the app lifetime, so the scene lives for the
        // view's lifetime (never rebuilt on re-attach — see
        // OnDetachedFromVisualTree).
        AttachedToVisualTree += (_, _) => OnAttachedToVisualTree();
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        FallbackTitle.Text = LocalizationService.Current?.Resolve(
            "module.plotterNex.vulkanUnavailable", "3D plotting unavailable") ?? "3D plotting unavailable";
        FallbackHint.Text = LocalizationService.Current?.Resolve(
            "module.plotterNex.vulkanUnavailableHint",
            "No Vulkan 1.3 device could be created — 2D plotting stays available.")
            ?? "No Vulkan 1.3 device could be created — 2D plotting stays available.";
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as PlotterNexViewModel;
        if (_vm is null)
            return;
        if (_attached)
            _vm.PropertyChanged += OnVmPropertyChanged;
        EnsureScene();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlotterNexViewModel.VulkanUnavailable)
            && _vm!.VulkanUnavailable && _scene is not null)
        {
            // Engine or downstream reported failure: release the GPU resources
            // and the VM's factory (the scene is gone; a later rebuild reattaches
            // a fresh one) and surface the fallback.
            var scene = _scene;
            _scene = null;
            DisableTick();
            FreeRenderResources();
            scene.Dispose();
            _vm.ReleaseScene();
        }
    }

    private void OnAttachedToVisualTree()
    {
        _attached = true;
        if (_vm is null)
            return;
        _vm.PropertyChanged += OnVmPropertyChanged;
        if (_scene is null)
        {
            EnsureScene();
        }
        else
        {
            // A live scene survives across tab switches: recreate the per-attach
            // render context + readback target at the current view size.
            SetupRenderTarget();
            EnableTick();
        }
    }

    /// <summary>Keep the GPU scene alive across tab switches. Only the
    /// per-attach render context, shared texture and tick are torn down; the
    /// engine and context stay for the view's lifetime (the host caches one
    /// view per module, so this is bounded). Bumping the scene generation still
    /// cancels any in-flight async build whose result lands late.</summary>
    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        DisableTick();
        FreeRenderResources();
        _sceneGeneration++;
    }

    /// <summary>Build the scene on the thread pool; AttachScene on the UI
    /// thread. The scene object lives as long as this view, so preset re-frames
    /// and program rebuilds mutate it in place.</summary>
    private void EnsureScene()
    {
        if (_vm is null || _scene is not null)
            return;

        var generation = ++_sceneGeneration;
        Task.Run(() =>
        {
            var scene = new NexPlotScene();
            Dispatcher.UIThread.Post(() =>
            {
                if (_vm is null || generation != _sceneGeneration)
                {
                    scene.Dispose();
                    return;
                }
                _scene = scene;
                try
                {
                    _vm.AttachScene(new KinematicSceneFactory(scene));
                }
                catch
                {
                    // CreateHeadless threw (no Vulkan) or the attach failed.
                    _scene = null;
                    scene.Dispose();
                    FreeRenderResources();
                    _vm.VulkanUnavailable = true;
                    return;
                }
                SetupRenderTarget();
                EnableTick();
            });
        });
    }

    /// <summary>Create (or re-create) the render context and readback target
    /// for the current view size.</summary>
    private void SetupRenderTarget()
    {
        var scene = _scene;
        if (scene is null || _readback is not null)
            return;
        try
        {
            _renderContext = scene.Engine.CreateRenderContext();
            _renderContext.Initialize();
        }
        catch (Exception ex)
        {
            _readback = null;
            _renderContext = null;
            CtxText.Text = $"ctx fail: {ex.GetType().Name}";
            return;
        }
        CtxText.Text = "ctx ok";
        _windowStart = Stopwatch.GetTimestamp();
    }

    /// <summary>Dispose the per-attach render context and shared texture.</summary>
    private void FreeRenderResources()
    {
        _readback?.Dispose();
        _readback = null;
        _renderContext?.Dispose();
        _renderContext = null;
        _bitmap = null;
        GpuImage.Source = null;
        _readbackTicks = 0;
        _windowFrames = 0;
        _fps = 0;
        _lastDiag = null;
    }

    private void EnableTick()
    {
        if (_tick is null)
        {
            // Normal (not Render) priority: fire independently of Avalonia's
            // render pass — this machine's WinUI composition server stalls
            // commit/request, so Render-priority timers starve.
            _tick = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(TickIntervalMs),
            };
        }
        // The timer survives tab switches (DisableTick stops + unsubscribes it),
        // so re-subscribe explicitly — otherwise the restarted timer fires with
        // no handler and the view goes dead (nothing drawn, diag frozen).
        if (!_tickSubscribed)
        {
            _tick.Tick += OnTick;
            _tickSubscribed = true;
        }
        _tick.Start();
        CtxText.Text = "ctx ok (tick on)";
    }

    private void DisableTick()
    {
        if (_tick is null)
            return;
        _tick.Stop();
        if (_tickSubscribed)
        {
            _tick.Tick -= OnTick;
            _tickSubscribed = false;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var scene = _scene;
        if (scene is null || _vm?.VulkanUnavailable == true)
        {
            SetDiag($"skip: scene={scene is null} vulkan={_vm?.VulkanUnavailable}");
            return;
        }

        var tl = TopLevel.GetTopLevel(this);
        var scale = tl?.RenderScaling ?? 1.0;
        // GpuImage.Bounds is 0 until the bitmap gives the Image an intrinsic
        // size (a Source-less Image arranges to 0 in a Panel), so size the
        // target from the top-level client rect instead.
        var bounds = GpuImage.Bounds;
        var client = tl?.ClientSize ?? default(Avalonia.Size);
        var wDip = Math.Max(bounds.Width, client.Width);
        var hDip = Math.Max(bounds.Height, client.Height);
        if (wDip < 8 || hDip < 8)
        {
            SetDiag($"skip: img {bounds.Width:0.#}x{bounds.Height:0.#} client {client.Width:0.#}x{client.Height:0.#} scl {scale:0.##}");
            return;
        }
        var w = (uint)Math.Max(1, Math.Round(wDip * scale));
        var h = (uint)Math.Max(1, Math.Round(hDip * scale));
        scene.SetViewportSize((int)w, (int)h);
        UpdateZoomLabel(scene);
        if (_readback is null)
        {
            try
            {
                _readback = new NexGpuReadback(scene.Context, w, h);
                _wPx = w;
                _hPx = h;
                _bitmap = new WriteableBitmap(
                    new PixelSize((int)w, (int)h),
                    new Vector(96, 96),
                    PixelFormats.Rgba8888,
                    AlphaFormat.Opaque);
                GpuImage.Source = _bitmap;
                CtxText.Text = $"ctx {w}x{h}";
            }
            catch (Exception ex)
            {
                _readback = null;
                CtxText.Text = $"rb fail: {ex.Message}";
                RenderText.Text = "t - / - fps";
                return;
            }
        }
        else if (w != _wPx || h != _hPx)
        {
            try
            {
                _readback.Resize(w, h);
                _wPx = w;
                _hPx = h;
                _bitmap = new WriteableBitmap(
                    new PixelSize((int)w, (int)h),
                    new Vector(96, 96),
                    PixelFormats.Rgba8888,
                    AlphaFormat.Opaque);
                GpuImage.Source = _bitmap;
                CtxText.Text = $"ctx {w}x{h}";
            }
            catch (Exception ex)
            {
                _readback = null;
                CtxText.Text = $"resize fail: {ex.Message}";
                RenderText.Text = "t - / - fps";
                return;
            }
        }

        var rc = _renderContext;
        var pipeline = _readback;
        if (rc is null || pipeline is null || _bitmap is null)
        {
            SetDiag($"skip: rc={rc is null} rb={pipeline is null} bmp={_bitmap is null}");
            return;
        }

        try
        {
            var cam = scene.UpdateViewport(rc, FixedDeltaTime);
            rc.Update(new HelixToolkit.Nex.Maths.Size((int)w, (int)h), cam);
            if (_probeCounter % 60 == 1)
            {
                PoolDiagnostics.LogLine(
                    $"[T{_readbackTicks}.cam] {scene.DumpCamera()} vp={rc.CameraParams.ViewProjection}"
                );
            }
            // Near-white stage: the pipeline applies ACES filmic tone mapping, which
            // compresses highlights (a 0.95 clear reads ~200/255). Feed a
            // brighter clear so the mapped output lands ~250/255 (near-white)
            // while the grey toolpaths and RGB axes stay readable against it.
            rc.RenderParams.BackgroundColor = new HelixToolkit.Nex.Maths.Color4(5.0f, 5.0f, 5.0f, 1.0f);

            // DIAGNOSTIC (temporary): A/B the GPU frustum culling — with it
            // disabled, visibility no longer depends on MeshInfo bounds, so we
            // can tell empty/stale-bound content apart from a real draw issue.
            var cullNode = scene.Engine.GetRenderNode<HelixToolkit.Nex.Rendering.ComputeNodes.FrustumCullNode>();
            if (cullNode is not null)
                cullNode.Enabled = !scene.DiagnosticDisableCulling;

            Trace.WriteLine(PoolDiagnostics.Dump(scene.Context, $"T{_readbackTicks}.pre-waitall"));
            scene.Context.WaitAll(true);
            Trace.WriteLine(PoolDiagnostics.Dump(scene.Context, $"T{_readbackTicks}.post-waitall"));

            scene.Engine.BeginFrame();
            Trace.WriteLine(PoolDiagnostics.Dump(scene.Context, $"T{_readbackTicks}.post-beginframe"));

            var cmd = scene.Engine.RenderOffscreen(rc, scene.WorldData, pipeline.EngineTarget, null);
            var lineDraws = 0;
            foreach (var s in scene.WorldData.LineDrawStreams.AllStreams)
                lineDraws += (int)s.Count;
            Trace.WriteLine(PoolDiagnostics.Dump(scene.Context, $"T{_readbackTicks}.post-renderoffscreen"));
            PoolDiagnostics.LogLine($"[T{_readbackTicks}.draws] drawCalls={cmd.DrawCallCount} dispatchCalls={cmd.DispatchCallCount} lineStreamDraws={lineDraws} targetValid={pipeline.EngineTarget.Valid} statsDc={rc.Statistics.DrawCalls}");
            if (_probeCounter % 60 == 1)
            {
                // DIAGNOSTIC: render-time line material pipeline state. If
                // LineMaterialManager.GetPipeline(0) is Null the line draw binds a
                // null pipeline and produces zero fragments (matches nonbg=0) even
                // though the stream data is perfect. Count=0 means
                // CreatePipelinesFromRegistry never ran or failed.
                PoolDiagnostics.LogLine($"[T{_readbackTicks}.linemat] {DumpLineMaterials(scene.Engine, scene.WorldData)}");
            }
            if (_probeCounter % 60 == 1)
            {
                PoolDiagnostics.LogLine($"[T{_readbackTicks}.scene] culling={!scene.DiagnosticDisableCulling} {scene.DumpContent()}");
                PoolDiagnostics.LogLine($"[T{_readbackTicks}.streams] {DumpLineStreams(scene.Context, scene.WorldData)}");
                PoolDiagnostics.LogLine($"[T{_readbackTicks}.fetch] {DumpFetchInputs(scene.Context, scene.WorldData)}");
            }

            // present MUST be Null: VulkanContext.Submit refuses any texture that
            // is not a swapchain image ("Cannot present texture" → returns
            // SubmitHandle.Null without submitting). Passing EngineTarget here
            // silently skipped the submit, leaked one command buffer per tick,
            // and drained the pool in ~1 s (Queue empty. after 32 ticks).
            // Rendering still goes into EngineTarget — RenderOffscreen bound it as
            // FinalOutputTexture — present only controls swapchain presentation.
            var presentHandle = default(HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>);
            scene.Engine.Submit(cmd, presentHandle, pipeline.EngineSyncInfo);
            Trace.WriteLine(PoolDiagnostics.Dump(scene.Context, $"T{_readbackTicks}.post-submit"));

            // The D3D11 readback has no inter-API fence (NexGpuReadback sync is
            // intentionally NONE), so the Vulkan render must be GPU-complete
            // before CopyResource reads the shared texture. Without this wait the
            // copy races the just-submitted draws and always sees the stale
            // (black) frame — this is exactly why the view stayed black even
            // after the geometry fix restored the draw submissions.
            scene.Context.WaitAll(true);
            Trace.WriteLine(PoolDiagnostics.Dump(scene.Context, $"T{_readbackTicks}.post-waitall-before-readback"));

            using var fb = _bitmap.Lock();
            if (!pipeline.CopyFrame(fb.Address, fb.RowBytes))
                return;
            // DIAGNOSTIC: probe the engine-produced frame EVERY tick (not just
            // every 30th) and export to pool_diag.log so we can tell whether the
            // Vulkan render actually reaches the readback. Also run the reverse
            // test every 30th tick: D3D11 clears the shared texture red and reads
            // it back — red means the D3D11 readback path works and the missing
            // link is the Vulkan import/write; black means readback itself fails.
            var probe = ProbeFrame(fb.Address, fb.RowBytes, (int)w, (int)h);
            _probeCounter++;
            if (_probeCounter % 30 == 1)
            {
                PoolDiagnostics.LogLine(pipeline.DiagTestD3D11Write());
            }
            PoolDiagnostics.LogLine($"[T{_readbackTicks}.probe] max={probe} nonbg={CountNonBackground(fb.Address, fb.RowBytes, (int)w, (int)h)} counter={_probeCounter}");
            if (_probeCounter % 60 == 1)
            {
                PoolDiagnostics.LogLine(DumpFrameGrid(fb.Address, fb.RowBytes, (int)w, (int)h));
            }
            GpuImage.InvalidateVisual();
            _readbackTicks++;
            AccumulateFps();
            RenderText.Text = $"t {_readbackTicks} / {_fps:F0} fps [p{probe}]";
        }
        catch (Exception ex)
        {
            var poolSnap = PoolDiagnostics.Dump(scene.Context, "CRASH");
            DisableTick();
            RenderText.Text = $"tick failed: {ex.Message} | {poolSnap}";
            System.Diagnostics.Trace.WriteLine("NEX tick failed: " + poolSnap + "\n" + ex);
        }
    }

    /// <summary>Reports a skip/failure reason in RenderText without spamming it
    /// every 33 ms when nothing changes.</summary>
    private string? _lastDiag;

    private void SetDiag(string text)
    {
        if (_lastDiag == text)
            return;
        _lastDiag = text;
        RenderText.Text = text;
    }

    /// <summary>Percent readout above the zoom cluster — hidden while zoom is
    /// effectively the fitted 100%.</summary>
    private void UpdateZoomLabel(NexPlotScene scene)
    {
        var pct = scene.ZoomPercent;
        if (Math.Abs(pct - 100.0) < 0.5)
        {
            if (ZoomPctText.IsVisible)
                ZoomPctText.IsVisible = false;
            return;
        }
        ZoomPctText.Text = $"{pct:0}%";
        if (!ZoomPctText.IsVisible)
            ZoomPctText.IsVisible = true;
    }

    private void AccumulateFps()
    {
        var now = Stopwatch.GetTimestamp();
        _windowFrames++;
        var elapsed = now - _windowStart;
        if (elapsed >= Stopwatch.Frequency)
        {
            _fps = _windowFrames * (double)Stopwatch.Frequency / elapsed;
            _windowFrames = 0;
            _windowStart = now;
        }
    }

    /// <summary>DIAGNOSTIC (temporary): CPU-side content of every line draw
    /// stream followed by the GPU-side records read back from mapped memory
    /// (post-culling instanceCount is exactly what DrawIndirect consumes).
    /// LineDraw std430 layout (u32 each): 0 vertexCount, 1 instanceCount,
    /// 4 meshId, 7 lineCount, 8 cullable, 9 nodeInfoIndex.</summary>
    private static string DumpLineMaterials(Engine engine, IRenderDataProvider world)
    {
        var sb = new StringBuilder();
        try
        {
            var mgr = engine.ResourceManager.LineMaterialManager;
            sb.Append($"count={mgr.Count} ");
            sb.Append($"defaultId={mgr.GetMaterialId("Default")} ");
            var p = mgr.GetPipeline(new MaterialTypeId(0));
            sb.Append($"p0.valid={p.Valid} p0.idx={p.Index} p0.gen={p.Gen} ");
            // Also check the material ID actually used by the live streams.
            var used = new HashSet<uint>();
            foreach (var s in world.LineDrawStreams.AllStreams)
            {
                foreach (var mat in s.GetMaterialTypes())
                    used.Add(mat.Id);
            }
            sb.Append("used=[");
            foreach (var id in used)
            {
                var q = mgr.GetPipeline(new MaterialTypeId(id));
                sb.Append($"{id}:valid={q.Valid}:idx={q.Index} ");
            }
            sb.Append("]");
        }
        catch (Exception ex)
        {
            sb.Append($"err({ex.GetType().Name}:{ex.Message})");
        }
        return sb.ToString();
    }

    private static string DumpLineStreams(IContext ctx, IRenderDataProvider world)
    {
        var sb = new StringBuilder();
        foreach (var s in world.LineDrawStreams.AllStreams)
        {
            sb.Append($"{s.StreamName}:{s.Count} [ ");
            for (var i = 0; i < s.Count; i++)
            {
                if (s.TryGetDraw(i, out var d))
                {
                    sb.Append(
                        $"i{i}(mesh={d.MeshId} inst={d.InstanceCount} lc={d.LineCount} cull={d.Cullable} mat={d.MaterialType} node={d.NodeInfoIndex} " +
                        $"col={d.LineColor:X8} w={d.LineWidth:0.###}) "
                    );
                }
                else
                {
                    sb.Append($"i{i}(UNAVAILABLE) ");
                }
            }
            sb.Append("] gpu[ ");
            if (s.Count > 0)
            {
                try
                {
                    unsafe
                    {
                        var p = (uint*)ctx.GetMappedPtr(s.Buffer);
                        if (p == null)
                        {
                            sb.Append("no-map ");
                        }
                        else
                        {
                            var stride = (int)(s.Stride / 4u);
                            for (var i = 0; i < s.Count; i++)
                            {
                                var d = p + (long)i * stride;
                                sb.Append(
                                    $"g{i}(mesh={d[4]} inst={d[1]} lc={d[7]} cull={d[8]} node={d[9]} " +
                                    $"col={d[5]:X8} w={*(float*)(d + 11):0.###} mat={d[6]} dt={d[12]} tex={d[13]} samp={d[14]}) "
                                );
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.Append($"map-failed({ex.GetType().Name}) ");
                }
            }
            sb.Append("] ");
        }
        return sb.ToString();
    }

    /// <summary>DIAGNOSTIC (temporary): GPU contents of the MeshInfo and
    /// NodeInfo buffers that the line vertex shader consumes (vsLine.glsl):
    /// MeshInfo[meshId].vertexBufferAddress (the vertex-fetch source) and
    /// NodeInfo[nodeInfoIndex].transform (world transform in clipMVP).
    /// MeshInfo std430: u64 vertexBufferAddress @0, vertexProps @8,
    /// vertexColor @16, indexBuffer @24, then bounds. GpuNodeInfo std430:
    /// mat4 transform @0 (m12..m14 = translation), u32 enabled @64,
    /// worldId @68, entityId @72, renderMask @76.</summary>
    private static string DumpFetchInputs(IContext ctx, IRenderDataProvider world)
    {
        var sb = new StringBuilder();
        foreach (var s in world.LineDrawStreams.AllStreams)
        {
            sb.Append($"{s.StreamName} [ ");
            for (var i = 0; i < s.Count; i++)
            {
                if (!s.TryGetDraw(i, out var d))
                {
                    sb.Append($"rec{i}(UNAVAILABLE) ");
                    continue;
                }
                try
                {
                    unsafe
                    {
                        var pMesh = (byte*)ctx.GetMappedPtr(world.MeshInfos.Buffer);
                        var pNode = (byte*)ctx.GetMappedPtr(world.NodeInfos.Buffer);
                        if (pMesh == null || pNode == null)
                        {
                            sb.Append($"rec{i}(meshno-map) ");
                            continue;
                        }
                        var meshStride = (int)world.MeshInfos.Stride;
                        var nodeStride = (int)world.NodeInfos.Stride;
                        var m = pMesh + (long)d.MeshId * meshStride;
                        var n = pNode + (long)d.NodeInfoIndex * nodeStride;
                        var vbAddr = *(ulong*)m;
                        var vcAddr = *(ulong*)(m + 16);
                        var sphereRadius = *(float*)(m + 76);
                        var t12 = *(float*)(n + 48);
                        var t13 = *(float*)(n + 52);
                        var t14 = *(float*)(n + 56);
                        var enabled = *(uint*)(n + 64);
                        // Full 16-float row-major world transform: identity reads
                        // diag(1,1,1,1); a zero matrix (broken propagation) reads
                        // all zeros and collapses every clip position to w=0,
                        // which the rasterizer discards — silently nothing renders.
                        var m00 = *(float*)(n + 0);
                        var m11 = *(float*)(n + 20);
                        var m22 = *(float*)(n + 40);
                        var m33 = *(float*)(n + 60);
                        // Read the actual vertex content of the geometry's GPU
                        // buffer (dynamic geometry is HostVisible). P0/P1 are the
                        // endpoints the shader fetches for segment s — zeros here
                        // mean the upload never happened despite the valid address.
                        var p0 = 0f;
                        var p1 = 0f;
                        var p2 = 0f;
                        var p3 = 0f;
                        var p4 = 0f;
                        var p5 = 0f;
                        if (d.InstanceCount > 0)
                        {
                            var geom = world.GetGeometry(d.MeshId);
                            if (geom is not null && geom.VertexBuffer.Valid)
                            {
                                var pVb = (float*)ctx.GetMappedPtr(geom.VertexBuffer);
                                if (pVb != null)
                                {
                                    p0 = pVb[0];
                                    p1 = pVb[1];
                                    p2 = pVb[2];
                                    p3 = pVb[4];
                                    p4 = pVb[5];
                                    p5 = pVb[6];
                                }
                            }
                        }
                        sb.Append(
                            $"rec{i}(meshId={d.MeshId} node={d.NodeInfoIndex} vb=0x{vbAddr:X} vc=0x{vcAddr:X} r={sphereRadius:0.00} " +
                            $"nodeEnabled={enabled} trans=({t12:0.0##},{t13:0.0##},{t14:0.0##}) " +
                            $"mat00={m00:0.000} m11={m11:0.000} m22={m22:0.000} m33={m33:0.000} " +
                            $"P0=({p0:0.###},{p1:0.###},{p2:0.###}) P1=({p3:0.###},{p4:0.###},{p5:0.###})) "
                        );
                    }
                }
                catch (Exception ex)
                {
                    sb.Append($"rec{i}(fetch-fail:{ex.GetType().Name}) ");
                }
            }
            sb.Append("] ");
        }
        return sb.ToString();
    }

    /// <summary>Brightness probe of the produced frame (every 30th tick,
    /// reported as <c>[pN]</c>): max luminance across a few sample pixels.
    /// 0 means the engine produced only black; a nonzero reading means real
    /// content is reaching the bitmap and only presentation differs.</summary>
    private static unsafe int ProbeFrame(IntPtr addr, int pitch, int w, int h)
    {
        var px = (byte*)addr;
        var max = 0;
        var lit = 0;
        var litSum = 0;
        var step = Math.Max(1, Math.Min(w, h) / 32);
        for (var y = 0; y < h; y += step)
        {
            var row = px + (long)y * pitch;
            for (var x = 0; x < w; x += step)
            {
                var p = row + (long)x * 4;
                var lum = (p[0] + p[1] + p[2]) / 3;
                if (lum > max)
                    max = lum;
                if (p[0] > 64 && p[1] < 32 && p[2] < 32)
                    lit++; // red-ish pixels (diagnostic background)
                if (lum > 16)
                {
                    litSum++;
                }
            }
        }
        return max;
    }

    /// <summary>DIAGNOSTIC (temporary): count of pixels that differ from the
    /// pure-red diagnostic background. Background (1,0,0,tone-mapped to ~CD) has
    /// G=0 and B=0, so any pixel with a non-zero green or blue byte is content
    /// (lines, cones, plane). Full-resolution so a small on-screen object cannot
    /// be missed by sparse sampling.</summary>
    private static unsafe int CountNonBackground(IntPtr addr, int pitch, int w, int h)
    {
        var px = (byte*)addr;
        var count = 0;
        for (var y = 0; y < h; y++)
        {
            var row = px + (long)y * pitch;
            for (var x = 0; x < w; x++)
            {
                var p = row + (long)x * 4;
                if (p[1] > 0 || p[2] > 0)
                    count++;
            }
        }
        return count;
    }

    /// <summary>DIAGNOSTIC (temporary): raw RGBA of a coarse grid of the
    /// readback frame, so we can see exactly which pixels the engine produced
    /// (red background everywhere = lines never rendered; any non-red pixel =
    /// the cone/plane DID reach the target).</summary>
    private static unsafe string DumpFrameGrid(IntPtr addr, int pitch, int w, int h)
    {
        var px = (byte*)addr;
        var cols = 8;
        var rows = 8;
        var sb = new StringBuilder("grid[ ");
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var x = (int)((long)(c + 0) * w / cols + w / cols / 2);
                var y = (int)((long)(r + 0) * h / rows + h / rows / 2);
                var p = px + (long)y * pitch + (long)x * 4;
                sb.Append($"({p[0]:X2}{p[1]:X2}{p[2]:X2})");
            }
            sb.Append(' ');
        }
        sb.Append(']');
        return sb.ToString();
    }

    private float PxScale() => (float)(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0);

    private Vector2 ToPx(Point dip, float scale) => new((float)(dip.X * scale), (float)(dip.Y * scale));

    private void OnPlotInputPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var scene = _scene;
        if (scene is null)
            return;
        var px = ToPx(e.GetPosition(PlotInput), PxScale());
        if (e.GetCurrentPoint(PlotInput).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            _panActive = true;
            _lastPanPx = px;
            e.Pointer.Capture(PlotInput);
        }
        else if (e.GetCurrentPoint(PlotInput).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            _orbitActive = true;
            _lastOrbitPx = px;
            scene.BeginOrbit(px);
            e.Pointer.Capture(PlotInput);
        }
    }

    private void OnPlotInputPointerMoved(object? sender, PointerEventArgs e)
    {
        var scene = _scene;
        if (scene is null)
            return;
        var px = ToPx(e.GetPosition(PlotInput), PxScale());
        if (_panActive)
        {
            scene.PanByScreenDelta(px - _lastPanPx);
            _lastPanPx = px;
        }
        else if (_orbitActive)
        {
            scene.RotateByScreenDelta(px - _lastOrbitPx);
            _lastOrbitPx = px;
        }
    }

    private void OnPlotInputPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _panActive = false;
        _orbitActive = false;
        _scene?.EndOrbit();
        e.Pointer.Capture(null);
    }

    private void OnPlotInputPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _panActive = false;
        _orbitActive = false;
        _scene?.EndOrbit();
        e.Pointer.Capture(null);
    }

    private void OnPlotInputPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var scene = _scene;
        if (scene is null)
            return;
        var px = ToPx(e.GetPosition(PlotInput), PxScale());
        scene.ZoomAround(px, Math.Pow(1.25, e.Delta.Y));
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => CenterZoom(1.25);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => CenterZoom(1.0 / 1.25);

    private void CenterZoom(double factor)
    {
        var scene = _scene;
        if (scene is null)
            return;
        scene.ZoomAround(new Vector2(_wPx * 0.5f, _hPx * 0.5f), factor);
    }

    private void OnReframe(object? sender, RoutedEventArgs e) => _scene?.Reframe();

    private void Snap(ViewPreset preset)
    {
        var scene = _scene;
        if (scene is null)
            return;
        try
        {
            scene.SetViewPreset(preset);
            UpdateSnapButtons(preset);
        }
        catch
        {
            _vm!.VulkanUnavailable = true;
        }
    }

    private void UpdateSnapButtons(ViewPreset preset)
    {
        SnapXyButton.IsChecked = preset == ViewPreset.XY;
        SnapXzButton.IsChecked = preset == ViewPreset.XZ;
        SnapYzButton.IsChecked = preset == ViewPreset.YZ;
        SnapIsoButton.IsChecked = preset == ViewPreset.Isometric3D;
    }

    private void OnSnapXy(object? sender, RoutedEventArgs e) => Snap(ViewPreset.XY);
    private void OnSnapXz(object? sender, RoutedEventArgs e) => Snap(ViewPreset.XZ);
    private void OnSnapYz(object? sender, RoutedEventArgs e) => Snap(ViewPreset.YZ);
    private void OnSnapIso(object? sender, RoutedEventArgs e) => Snap(ViewPreset.Isometric3D);
}