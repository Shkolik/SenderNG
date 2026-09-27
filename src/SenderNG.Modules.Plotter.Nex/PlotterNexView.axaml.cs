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
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Input;
using Vector2 = System.Numerics.Vector2;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CNC.Core.Abstractions;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Rendering;
using SenderNG.Core.Localization;
using SenderNG.Modules.Plotter.Nex.Rendering;
using SenderNG.Modules.Plotter.Nex.Scene;
using SenderNG.Modules.Plotter.Nex.Settings;

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
/// UpdateViewport → RenderOffscreen into the readback's Vulkan render-target
/// texture, then a cross-platform IContext.Download into a WriteableBitmap
/// shown by <c>GpuImage</c>. This
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
    private const int RunStateApplyIntervalTicks = 3;

    private PlotterNexViewModel? _vm;
    private NexPlotScene? _scene;
    private int _sceneGeneration;
    private bool _attached;
    private bool _tickSubscribed;

    // Top-left view-toggle bar: the SAME PlotterNexSettingsViewModel the App
    // Settings leaf binds, wired in by the module (see OverlaySettings). Both
    // surfaces are two views onto ONE shared settings VM on the app's DI
    // SettingsService, so a flip anywhere persists per-profile through the
    // shared service and reaches the other surface live — no second service,
    // no second VM clone, no attach-time disk re-read. The AA mode/quality is
    // leaf-only (kept off the overlay bar) but rides the same live push.
    private PlotterNexSettingsViewModel? _overlayToggles;

    private RenderContext? _renderContext;
    private NexGpuReadback? _readback;
    private WriteableBitmap? _bitmap;
    private DispatcherTimer? _tick;
    private uint _wPx;
    private uint _hPx;
    // Render-target size (panel × supersample factor). Decoupled from the
    // navigation _wPx/_hPx above: cursor/navigation math stays in on-screen
    // (panel) px while the engine renders at supersample resolution.
    private uint _rtW;
    private uint _rtH;

    // Latest downloaded frame, kept so the "save screenshot" button can export
    // the currently displayed view at its native readback size (always identical
    // between captures). RGBA, _shotLen = _shotW * _shotH * 4.
    private byte[]? _shotRgba;
    private int _shotW;
    private int _shotH;

    private int _readbackTicks;

    private bool _panActive;
    private bool _orbitActive;
    private Vector2 _lastPanPx;
    private Vector2 _lastOrbitPx;

    private readonly IFileDialogService? _fileDialogs;

    public PlotterNexView() : this(null)
    {
    }

    public PlotterNexView(IFileDialogService? fileDialogs)
    {
        _fileDialogs = fileDialogs;
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Tab switches detach the view from the visual tree; the host caches
        // one view per module for the app lifetime, so the scene lives for the
        // view's lifetime (never rebuilt on re-attach — see
        // OnDetachedFromVisualTree).
        AttachedToVisualTree += (_, _) => OnAttachedToVisualTree();
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        FallbackTitle.Text = LocalizationService.Current?.Resolve(
            "module.plotter-nex.vulkanUnavailable", "3D plotting unavailable") ?? "3D plotting unavailable";
        FallbackHint.Text = LocalizationService.Current?.Resolve(
            "module.plotter-nex.vulkanUnavailableHint",
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

    /// <summary>The ONE <see cref="PlotterNexSettingsViewModel"/> the overlay bar
    /// binds — the same instance the App Settings leaf binds (wired by the module).
    /// Switch it re-binds the bar, re-subscribes the live push, and re-applies
    /// the view chrome + AA settings to the current scene (runtime-only, no
    /// rebuild). A flip on either surface reaches the other through the shared
    /// VM's PropertyChanged — no re-attach, no disk re-read.</summary>
    public PlotterNexSettingsViewModel? OverlaySettings
    {
        get => _overlayToggles;
        set
        {
            if (_overlayToggles is not null)
                _overlayToggles.PropertyChanged -= OnOverlayToggleChanged;
            _overlayToggles = value;
            OverlayViewToggles.DataContext = value;
            if (_overlayToggles is not null)
                _overlayToggles.PropertyChanged += OnOverlayToggleChanged;
            PushOverlayState();
        }
    }

    /// <summary>Push the overlay view toggles and the AA mode/quality to the live
    /// scene as runtime changes (NexPlotScene.SetView + SetAntiAlias) — node-level
    /// Enabled/park flips and AA push-constant edits only, no scene rebuild, no node
    /// recreation, no module reload.</summary>
    private void PushOverlayState()
    {
        var scene = _scene;
        var vm = _overlayToggles;
        if (scene is null || vm is null)
            return;
        scene.SetView(new PlotterNexSettings
        {
            ShowOrigin = vm.ShowOrigin,
            ShowEnvelope = vm.ShowEnvelope,
            ShowRunState = vm.ShowRunState,
            ShowGrid = vm.ShowGrid,
        });
        scene.SetAntiAlias(vm.AaMode, vm.AaQuality);
    }

    private void OnOverlayToggleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(PlotterNexSettingsViewModel.ShowOrigin)
            && e.PropertyName is not nameof(PlotterNexSettingsViewModel.ShowEnvelope)
            && e.PropertyName is not nameof(PlotterNexSettingsViewModel.ShowRunState)
            && e.PropertyName is not nameof(PlotterNexSettingsViewModel.ShowGrid)
            && e.PropertyName is not nameof(PlotterNexSettingsViewModel.AaMode)
            && e.PropertyName is not nameof(PlotterNexSettingsViewModel.AaQuality)
            && e.PropertyName is not nameof(PlotterNexSettingsViewModel.Supersample))
        {
            return;
        }
        PushOverlayState();
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
            // A/B verdict (2026-09-16, on-device): the swept loft leading-edge
            // loop is REMOVED permanently. Production = loft sheet, tool wire,
            // cones, and the progressing green ACK outline only.
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
                    _vm.ReportVulkanUnavailable(Plot3DFailure.Other);
                    return;
                }
                SetupRenderTarget();
                EnableTick();
                // A fresh scene renders all chrome visible + AA at the ctor
                // default; start it from the persisted view toggles and AA
                // settings (runtime-only, no rebuild).
                PushOverlayState();
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
        catch (Exception)
        {
            _readback = null;
            _renderContext = null;
            return;
        }
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
            return;
        }

        // Fold pending row events into the acked-end cache / progress bump at
        // most once per tick — a fast check run must not post one dispatcher
        // action per resolved row, and AdvanceAckedEnd's park-behind-Sent keeps
        // the tool on the acked path even when acks trail the frontier.
        _vm?.PulseAckedEnds();

        // Cadence-tune the run-state color path: apply ~10Hz (as before), then
        // swap-pump the planes into fresh static geometries. The plane layers
        // never do in-place GPU writes anymore (dynamic mapped-buffer color
        // uploads raced the live render loop -> VK_ERROR_DEVICE_LOST); every
        // color change reaches the GPU as a staged static-buffer upload fired
        // BEFORE this tick's frame is submitted.
        if (_readbackTicks % RunStateApplyIntervalTicks == 0)
        {
            _vm?.ApplyRunStateWindow();
            scene.PumpCarriagePlanes();
        }

        // Same swap-pump cure for the tool connector wire and loft surface:
        // their positions stage on status polls (PostUi) and reach the GPU here
        // as fresh staged static geometries instead of in-place host-visible
        // mapped writes racing the in-flight frame.
        scene.PumpToolWire();

        var tl = TopLevel.GetTopLevel(this);
        var scale = tl?.RenderScaling ?? 1.0;
        // GpuImage.Bounds is 0 until the bitmap gives the Image an intrinsic
        // size (a Source-less Image arranges to 0 in a Panel), so fall back to
        // the top-level client rect only before first layout. Once the Image is
        // arranged, size the target to that rect (the presented area) so the
        // framebuffer maps 1:1 to on-screen pixels and cursor px == framebuffer px.
        var bounds = GpuImage.Bounds;
        var client = tl?.ClientSize ?? default(Avalonia.Size);
        var wDip = bounds.Width > 0 ? bounds.Width : client.Width;
        var hDip = bounds.Height > 0 ? bounds.Height : client.Height;
        if (wDip < 8 || hDip < 8)
        {
            return;
        }
        var pw = (uint)Math.Max(1, Math.Round(wDip * scale));
        var ph = (uint)Math.Max(1, Math.Round(hDip * scale));
        var r = RenderSizing.Resolve(pw, ph, _overlayToggles?.Supersample ?? true);
        // Navigation viewport stays in on-screen (panel) px: cursor px and the
        // pivot/pan/zoom NDC all divide by these, so they must equal what the
        // user points at, not the supersampled render target.
        scene.SetViewportSize((int)r.PanelW, (int)r.PanelH);
        UpdateZoomLabel(scene);
        if (_readback is null)
        {
            try
            {
                _readback = new NexGpuReadback(scene.Context, r.RtW, r.RtH);
                _wPx = r.PanelW;
                _hPx = r.PanelH;
                _rtW = r.RtW;
                _rtH = r.RtH;
                _bitmap = new WriteableBitmap(
                    new PixelSize((int)r.RtW, (int)r.RtH),
                    new Vector(96, 96),
                    PixelFormats.Rgba8888,
                    AlphaFormat.Opaque);
                GpuImage.Source = _bitmap;
            }
            catch (Exception)
            {
                _readback = null;
                return;
            }
        }
        else if (r.RtW != _rtW || r.RtH != _rtH)
        {
            try
            {
                _readback.Resize(r.RtW, r.RtH);
                _wPx = r.PanelW;
                _hPx = r.PanelH;
                _rtW = r.RtW;
                _rtH = r.RtH;
                _bitmap = new WriteableBitmap(
                    new PixelSize((int)r.RtW, (int)r.RtH),
                    new Vector(96, 96),
                    PixelFormats.Rgba8888,
                    AlphaFormat.Opaque);
                GpuImage.Source = _bitmap;
            }
            catch (Exception)
            {
                _readback = null;
                return;
            }
        }

        var rc = _renderContext;
        var pipeline = _readback;
        if (rc is null || pipeline is null || _bitmap is null)
        {
            return;
        }

        try
        {
            var cam = scene.UpdateViewport(rc, FixedDeltaTime);
            rc.Update(new HelixToolkit.Nex.Maths.Size((int)r.RtW, (int)r.RtH), cam);

            // Near-white stage: the pipeline applies ACES filmic tone mapping, which
            // compresses highlights (a 0.95 clear reads ~200/255). Feed a
            // brighter clear so the mapped output lands ~250/255 (near-white)
            // while the grey toolpaths and RGB axes stay readable against it.
            rc.RenderParams.BackgroundColor = new HelixToolkit.Nex.Maths.Color4(5.0f, 5.0f, 5.0f, 1.0f);

            // GPU frustum culling stays off permanently: MeshInfo-bound culling was
            // mis-culling valid geometry while chasing the red-fan artifact, and
            // hiding chrome relies on the parked transform, not Enabled.
            var cullNode = scene.Engine.GetRenderNode<HelixToolkit.Nex.Rendering.ComputeNodes.FrustumCullNode>();
            if (cullNode is not null)
                cullNode.Enabled = false;

            // The WaitAlls below stay every tick (the CPU download must read
            // GPU-complete output).
            scene.Context.WaitAll(true);

            scene.Engine.BeginFrame();

            var cmd = scene.Engine.RenderOffscreen(rc, scene.WorldData, pipeline.EngineTarget, null);

            // present MUST be Null: VulkanContext.Submit refuses any texture that
            // is not a swapchain image ("Cannot present texture" → returns
            // SubmitHandle.Null without submitting). Passing EngineTarget here
            // silently skipped the submit, leaked one command buffer per tick,
            // and drained the pool in ~1 s (Queue empty. after 32 ticks).
            // Rendering still goes into EngineTarget — RenderOffscreen bound it as
            // FinalOutputTexture — present only controls swapchain presentation.
            var presentHandle = default(HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>);
            scene.Engine.Submit(cmd, presentHandle);

            // The CPU download (IContext.Download) does its own copy after the
            // render, so the Vulkan render must be GPU-complete before Download
            // runs. Without this wait the staging copy races the just-submitted
            // draws and always sees the stale (black) frame.
            scene.Context.WaitAll(true);

            using var fb = _bitmap.Lock();
            if (!pipeline.CopyFrame(fb.Address, fb.RowBytes))
                return;
            // Keep a copy of the displayed frame so the screenshot button can
            // export the current view (native readback size, stable across
            // captures) without re-rendering.
            var shotLen = checked((int)r.RtW * (int)r.RtH * 4);
            if (_shotRgba == null || _shotRgba.Length != shotLen)
            {
                _shotRgba = new byte[shotLen];
                _shotW = (int)r.RtW;
                _shotH = (int)r.RtH;
            }
            System.Runtime.InteropServices.Marshal.Copy(fb.Address, _shotRgba, 0, shotLen);
            GpuImage.InvalidateVisual();
            _diagRing[_diagHead = (_diagHead + 1) % _diagRing.Length] =
                $"[T{_readbackTicks}] {string.Join(" | ", scene.LiveLayerDumps())} runs={_vm!.DumpRunState()}";
            _readbackTicks++;
        }
        catch (Exception ex)
        {
            var poolSnap = PoolDiagnostics.Dump(scene.Context, "CRASH");
            PoolDiagnostics.LogLine($"[CRASH] tick={_readbackTicks} {ex.GetType().Name}: {ex.Message}");
            PoolDiagnostics.LogLine($"[CRASH] layers {string.Join(" | ", scene.LiveLayerDumps())} runs={_vm?.DumpRunState()}");
            PoolDiagnostics.LogLine($"[CRASH] scene {scene.DumpContent()} vm={_vm?.LastControllerPose}");
            var stack = ex.StackTrace ?? "";
            foreach (var frame in stack.Split('\n').Take(4))
                PoolDiagnostics.LogLine("[CRASH]  " + frame.TrimEnd());
            PoolDiagnostics.LogLine("[CRASH] last 12 ticks: " + string.Join(" ", _diagRing.Where(x => x is not null).Append($"[T{_readbackTicks}] (crash tick, no ring entry — throw before capture)")));
            DisableTick();
            System.Diagnostics.Trace.WriteLine("NEX tick failed: " + poolSnap + "\n" + ex);
        }
    }

    private int _diagHead;
    private readonly string[] _diagRing = new string[12];

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

    /// <summary>Asks the user where to save the currently displayed 3D view (the
    /// latest readback frame at its native size, so every capture is
    /// pixel-aligned) and writes a PNG there. Works for the SMAA debug views too
    /// (they swap the frame contents, not the size). A cancelled dialog is
    /// silent; a write failure surfaces a modal error dialog.</summary>
    private async void OnSaveScreenshot(object? sender, RoutedEventArgs e)
    {
        if (_fileDialogs is null)
            return;
        var rgba = _shotRgba;
        if (rgba is null || _shotW <= 0 || _shotH <= 0)
            return;
        try
        {
            var suggested =
                $"SenderNG_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{_shotW}x{_shotH}.png";
            var path = await _fileDialogs.SaveFileAsync("Save screenshot (PNG)", suggested, "PNG image|*.png");
            if (path is null)
                return;
            using var img = SixLabors.ImageSharp.Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                rgba, _shotW, _shotH);
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, path);
        }
        catch (Exception ex)
        {
            await _fileDialogs.ShowErrorAsync("Save screenshot", $"Save failed: {ex.Message}");
        }
    }

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
            _vm!.ReportVulkanUnavailable(Plot3DFailure.RuntimeError);
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