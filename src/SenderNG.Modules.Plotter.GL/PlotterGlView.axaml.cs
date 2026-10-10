/*
 * PlotterGlView.axaml.cs - OpenGL host for the plotter
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
using SenderNG.Core.Localization;
using SenderNG.Modules.Plotter.GL.Rendering;
using SenderNG.Modules.Plotter.GL.Scene;
using SenderNG.Modules.Plotter.GL.Settings;

namespace SenderNG.Modules.Plotter.GL;

/// <summary>
/// Hosts the GL plot scene. Context creation is synchronous on the UI thread —
/// a GL context renders on the thread that owns it — so there is no thread-pool
/// build to race. A failed creation (no GL library, driver too old) flips
/// <see cref="PlotterGlViewModel.RenderUnavailable"/> and the XAML swaps in the
/// text fallback.
/// <para>
/// Scene and context are one lifetime (both per-attach: detach disposes them
/// together, re-attach rebuilds both — see FreeRenderResources / EnsureScene),
/// because the scene's GL objects only exist on the context that created them.
/// Each tick: make current → clear the readback FBO → scene draw →
/// glReadPixels row copy into a WriteableBitmap shown by <c>GpuImage</c>.
/// </para>
/// </summary>
public partial class PlotterGlView : UserControl
{
    private const double TickIntervalMs = 33.0;
    private const float FixedDeltaTime = 1f / 30f;
    private const int RunStateApplyIntervalTicks = 3;

    private PlotterGlViewModel? _vm;
    private GlPlotScene? _scene;
    // The context _scene renders on; nulled by FreeRenderResources on detach
    // (the tick gates on it — see EnableTick).
    private GlContext? _gl;
    private int _sceneGeneration;
    // Why GL creation failed, remembered for the view's lifetime so a failed
    // context is never re-attempted. Null = healthy or not yet tried.
    private PlotGlFailure? _glVerdict;
    private bool _attached;
    private bool _tickSubscribed;

    // Top-left view-toggle bar: the SAME PlotterGlSettingsViewModel the App
    // Settings leaf binds, wired in by the module (see OverlaySettings). Both
    // surfaces are two views onto ONE shared settings VM on the app's DI
    // SettingsService, so a flip anywhere persists per-profile through the
    // shared service and reaches the other surface live — no second service,
    // no second VM clone, no attach-time disk re-read. Supersample is
    // leaf-only (kept off the overlay bar) but rides the same live push.
    private PlotterGlSettingsViewModel? _overlayToggles;

    private GlReadback? _readback;
    // True while the render loop has been failing, so the failure is logged
    // once per episode rather than once per retry tick. See LogGlFailure.
    private bool _readbackFailed;
    // Clear color painted every tick: {0.98, 0.98, 0.98} × 255 = 250 — the
    // near-white that matches Nex's visible output (its clear is ACES-mapped
    // to ~250/255; Nex is opaque too — CopyFrame forces alpha 255). Bytes
    // because GlReadback.BeginFrame takes a straight RGBA quad, the same
    // shape a screenshot row has. (Checkpoint-1 ruling; original mandate was
    // dark Skia-ish {0.13, 0.14, 0.16}.)
    private static readonly byte[] _backgroundRgba = [250, 250, 250, 255];
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

    public PlotterGlView() : this(null)
    {
    }

    public PlotterGlView(IFileDialogService? fileDialogs)
    {
        _fileDialogs = fileDialogs;
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Tab switches detach the view from the visual tree; the host caches
        // one view per module for the app lifetime, but the GL scene and its
        // context are per-attach: they die together on detach and re-attach
        // rebuilds both (see OnDetachedFromVisualTree / EnsureScene).
        AttachedToVisualTree += (_, _) => OnAttachedToVisualTree();
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        // No dispatcher post: Changed is raised by the language picker, which
        // is already on the UI thread, and this is two dictionary lookups and
        // two property assignments. Matches the module's own settings view
        // (Settings/PlotterGlSettingsViewModel.cs).
        var loc = LocalizationService.Current;
        if (loc is not null)
            loc.Changed += (_, _) => ApplyFallbackTexts();
        ApplyFallbackTexts();
    }

    /// <summary>Resolve both panel labels and the save-screenshot tooltip in
    /// the current locale. Called from the
    /// constructor, whenever a verdict is reported, and on every locale change.
    /// <para>Assigning from code-behind rather than binding is deliberate; see
    /// the comment on ReportRenderFailure. Re-reading the view model's detail
    /// here is what makes a live language change work: the property resolves on
    /// every read, so it returns the new locale without the view model raising
    /// anything.</para>
    /// <para>The hint is never left empty. A verdict-less panel is invisible, but
    /// an empty TextBlock would break the invariant that this element always
    /// holds a real localized string, so the generic hint is used as the floor
    /// for both "no view model yet" and "no verdict yet".</para></summary>
    private void ApplyFallbackTexts()
    {
        var loc = LocalizationService.Current;

        FallbackTitle.Text = loc?.Resolve(
            "module.plotter-gl.renderUnavailable", "3D plotting unavailable")
            ?? "3D plotting unavailable";

        var detail = _vm?.RenderFailureDetail;
        FallbackHint.Text = !string.IsNullOrEmpty(detail)
            ? detail
            : loc?.Resolve(
                "module.plotter-gl.renderUnavailableHint",
                "No OpenGL context could be created.")
              ?? "No OpenGL context could be created.";

        ToolTip.SetTip(SaveScreenshotButton, loc?.Resolve(
            "module.plotter-gl.saveScreenshot", "Save screenshot (PNG)")
            ?? "Save screenshot (PNG)");
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as PlotterGlViewModel;
        if (_vm is null)
            return;
        if (_attached)
            _vm.PropertyChanged += OnVmPropertyChanged;
        EnsureScene();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlotterGlViewModel.RenderUnavailable)
            && _vm!.RenderUnavailable && _scene is not null)
        {
            // Engine or downstream reported failure: release the GPU resources
            // (readback, scene, context — scene before context, factory
            // released too, all inside FreeRenderResources) and surface the
            // fallback.
            DisableTick();
            FreeRenderResources();
        }
    }

    /// <summary>The ONE <see cref="PlotterGlSettingsViewModel"/> the overlay bar
    /// binds — the same instance the App Settings leaf binds (wired by the module).
    /// Switch it re-binds the bar, re-subscribes the live push, and re-applies
    /// the view chrome to the current scene (runtime-only, no
    /// rebuild). A flip on either surface reaches the other through the shared
    /// VM's PropertyChanged — no re-attach, no disk re-read.</summary>
    public PlotterGlSettingsViewModel? OverlaySettings
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

    /// <summary>Push the overlay view toggles to the live scene as runtime
    /// changes (GlPlotScene.SetView) — node-level Enabled/park flips only, no
    /// scene rebuild, no node recreation, no module reload.</summary>
    private void PushOverlayState()
    {
        var scene = _scene;
        var vm = _overlayToggles;
        if (scene is null || vm is null)
            return;
        scene.SetView(new PlotterGlSettings
        {
            ShowOrigin = vm.ShowOrigin,
            ShowEnvelope = vm.ShowEnvelope,
            ShowRunState = vm.ShowRunState,
            ShowGrid = vm.ShowGrid,
            ShowLoft = vm.ShowLoft,
        });
    }

    private void OnOverlayToggleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(PlotterGlSettingsViewModel.ShowOrigin)
            && e.PropertyName is not nameof(PlotterGlSettingsViewModel.ShowEnvelope)
            && e.PropertyName is not nameof(PlotterGlSettingsViewModel.ShowRunState)
            && e.PropertyName is not nameof(PlotterGlSettingsViewModel.ShowGrid)
            && e.PropertyName is not nameof(PlotterGlSettingsViewModel.ShowLoft)
            && e.PropertyName is not nameof(PlotterGlSettingsViewModel.Supersample))
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
            // Detach disposed the scene and its context together (one
            // lifetime), so every attach comes through the full rebuild here.
            EnsureScene();
        }
    }

    /// <summary>Tear the per-attach render state down on tab switch: tick off,
    /// then readback + scene + context disposed together (scene before context —
    /// its GL objects only delete through a live context) and the VM's factory
    /// released, so re-attach runs the full EnsureScene build against a fresh
    /// context. Bumping the scene generation still cancels any in-flight
    /// creation whose result lands late.</summary>
    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        DisableTick();
        FreeRenderResources();
        _sceneGeneration++;
    }

    /// <summary>Create the GL context and scene synchronously on the UI thread
    /// (context affinity: a GL context renders on the thread that owns it),
    /// then attach and start the tick. Re-attach re-runs this after the detach
    /// teardown, so every attach gets a scene bound to its own fresh context —
    /// GL objects never outlive the context that created them. Preset re-frames
    /// and program rebuilds still mutate the scene in place for the attach's
    /// duration.</summary>
    private void EnsureScene()
    {
        if (_vm is null || _scene is not null)
            return;
        if (_glVerdict is not null)
            return;                      // one GPU-init attempt per process (parity with Nex)
        var generation = ++_sceneGeneration;
        GlContext? ctx = null;
        GlPlotScene? built = null;
        try
        {
            ctx = GlContext.Create();
            built = new GlPlotScene(ctx);
        }
        catch (Exception ex)
        {
            ctx?.Dispose();
            var cause = GlFailureCause.ContextCreate;
            _glVerdict = PlotGlFailures.Classify(ex, cause);
            _vm.ReportRenderUnavailable(_glVerdict.Value, ex.Message);
            return;
        }
        if (generation != _sceneGeneration) { built.Dispose(); ctx?.Dispose(); return; }   // stale guard: GL objects die with their context
        _gl = ctx;
        _scene = built;
        _vm.AttachScene(new KinematicSceneFactory(built));
        EnableTick();                       // the first tick creates the readback at the live size
        PushOverlayState();
    }

    private void LogGlFailure(Exception ex)
    {
        // This method never creates a verdict, deliberately. A readback that
        // will not allocate is usually transient (host memory pressure) and
        // SetupReadback RETRIES on every tick, so reporting a failure kind
        // would cache Other for the session and disable a scene the renderer
        // is running perfectly well - the same over-reach the taxonomy forbids
        // elsewhere. What was missing before is the explanation: this used to
        // swallow the exception, so a permanently blank viewport was
        // indistinguishable in the log from a blank one of unknown cause.
        // `failed=` is n/a because no verdict was reached; GLFRAME says where
        // it happened. (The render-loop catch reports a verdict itself, after
        // logging here - this method is just the record.)
        //
        // Logged once per episode, not once per tick: a readback that keeps
        // throwing would otherwise write two lines every 33ms - about sixty a
        // second - and bury everything else in the file. The flag is cleared
        // when a readback finally succeeds, so a later distinct episode is
        // reported again.
        if (_readbackFailed)
            return;
        _readbackFailed = true;
        System.Diagnostics.Trace.WriteLine(
            $"[GL] GLFRAME failed=n/a code=n/a {ex.GetType().Name}: {ex.Message}");
        System.Diagnostics.Trace.WriteLine($"[GL] GLFRAME stack: {ex.StackTrace}");
    }

    /// <summary>Cache the verdict and hand it to the view model, which resolves
    /// the localized detail from it. Safe to call more than once: the view
    /// model keeps the first reason, and re-attempting creation is prevented by the
    /// cached verdict. A null view model is not an error — the tab may have
    /// been torn down between the pool work and this UI-thread post.</summary>
    private void ReportRenderFailure(PlotGlFailure failure)
    {
        _glVerdict = failure;
        var vm = _vm;
        if (vm is null)
            return;
        vm.ReportRenderUnavailable(failure);
        // Set here rather than bound in XAML, deliberately. RenderFailureDetail
        // is module-internal, keeping the failure vocabulary off this module's
        // public surface on purpose, and the view reads it directly because that
        // is guaranteed by the compiler. An Avalonia reflection binding does
        // reach non-public members (verified against the pinned Avalonia by
        // running a headless probe), so binding would work today - but that is
        // an implementation detail of the binding engine rather than a documented
        // contract, and a blank hint is a silent user-visible failure that no
        // test here could catch, since the view needs a GPU. Reading it here also
        // covers paths that never raise PropertyChanged, such as re-attaching to
        // a view model that already holds a verdict. Read back from the model
        // rather than echoing `failure`, because the model keeps the FIRST
        // reason and this keeps the two in agreement.
        ApplyFallbackTexts();
    }

    /// <summary>One diagnostics record per distinct failure, always with the
    /// exception type, message, and stack. A
    /// re-report from the verdict cache deliberately does NOT log a second
    /// time — the cache re-pushes the panel but the failure already has its
    /// record. The UI panel never shows this — it stays localized, and raw
    /// exception text is noise there. Two lines because a single message keeps
    /// the summary greppable — the stack goes on
    /// its own line.</summary>
    private static void LogRenderFailure(string stage, PlotGlFailure failure, string code, Exception ex)
    {
        System.Diagnostics.Trace.WriteLine($"[GL] {stage} failed={failure} code={code} {ex.GetType().Name}: {ex.Message}");
        System.Diagnostics.Trace.WriteLine($"[GL] {stage} stack: {ex.StackTrace}");
    }

    /// <summary>Create (or re-create) the readback target and its bitmap for
    /// the render size of this tick — the same create-on-first-tick /
    /// recreate-on-resize logic Nex runs inline in its tick (NexGpuReadback
    /// creation at PlotterNexView 673–721), lifted into a method so the size
    /// bookkeeping and the bitmap stay in step with the FBO. A failure is
    /// logged once per episode and RETRIED next tick — not a verdict, usually
    /// memory pressure (see LogGlFailure).</summary>
    /// <returns>False when the readback is not usable this tick — the caller
    /// must not render.</returns>
    private bool SetupReadback(in RenderSize r)
    {
        try
        {
            // MSAA rides the same tick as the render size, but on its own axis
            // (sample count vs supersample scale). The first create takes the
            // configured mode straight to the ctor — no default-then-rebuild
            // double allocation on the first tick. SetMsaa is a no-op when the
            // mode already matches; a mode this driver cannot build falls back
            // to the plain path rather than losing the whole panel.
            var msaa = _overlayToggles?.MsaaMode ?? MsaaMode.None;
            var created = false;
            if (_readback is null)
            {
                _readback = new GlReadback(_gl!, r.RtW, r.RtH, msaa);
                created = true;
            }
            else if (r.RtW != _rtW || r.RtH != _rtH)
            {
                _readback.Resize(r.RtW, r.RtH);
                created = true;
            }

            if (_readback.Msaa != msaa)
            {
                try
                {
                    _readback.SetMsaa(msaa);
                }
                catch (Exception ex) when (msaa != MsaaMode.None)
                {
                    Console.WriteLine(
                        $"plotter-gl spike: MSAA {msaa} unavailable, falling back to None: {ex.Message}");
                    _readback.SetMsaa(MsaaMode.None);
                }
            }

            if (created)
            {
                // The previous bitmap is replaced WITHOUT an explicit Dispose
                // on purpose: byte-for-byte parity with Nex, which does the
                // same on both its create and resize paths
                // (PlotterNexView.axaml.cs:682, :707 replace; :567 nulls it
                // without disposing). GpuImage.Source is reassigned here, so
                // the old bitmap loses its last reference and the GC's
                // finalizer reclaims it — matching Nex's lifetime model.
                _bitmap = new WriteableBitmap(
                    new PixelSize((int)r.RtW, (int)r.RtH),
                    new Vector(96, 96),
                    PixelFormats.Rgba8888,
                    AlphaFormat.Opaque);
                GpuImage.Source = _bitmap;
            }

            _rtW = r.RtW;
            _rtH = r.RtH;
            // The readback is live, so any earlier failure episode is over:
            // clear the latch so a later one is logged again rather than
            // swallowed as a duplicate.
            _readbackFailed = false;
            return true;
        }
        catch (Exception ex)
        {
            // Retried next tick, by design - see LogGlFailure. Not a verdict,
            // so the panel does not appear; FreeRenderResources owns
            // disposal, so a failed attempt must let it go.
            _readback?.Dispose();
            _readback = null;
            LogGlFailure(ex);
            return false;
        }
    }

    /// <summary>Dispose the per-attach render state: readback, scene, context —
    /// in that order (each step needs the next one alive: the FBO and the
    /// scene's GL objects only delete through a live context) — then drop the
    /// VM's factory so a later attach may build a fresh scene. Idempotent:
    /// detach and the VM-failure handler both call it.</summary>
    private void FreeRenderResources()
    {
        _readback?.Dispose();
        _readback = null;
        _scene?.Dispose();
        _scene = null;
        _gl?.Dispose();
        _gl = null;
        _vm?.ReleaseScene();
        _bitmap = null;
        GpuImage.Source = null;
        _readbackTicks = 0;
    }

    private void EnableTick()
    {
        // The tick drives the GL context, so that is what gates it. It used
        // to be gated on the cached verdict instead, at each call site, on the
        // reasoning that only a failure reachable before an EnableTick call
        // could have set one. That stopped being true when a superseded build
        // attempt started caching verdicts too: a live scene could be denied
        // its tick - attached and never drawn, which looks healthy because the
        // scene is alive and the failure behind the cache is logged, not shown.
        // Keying on the precondition, in the one place that starts the tick,
        // cannot be fooled by who else has written the verdict.
        if (_gl is null)
            return;
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
        if (scene is null || _vm?.RenderUnavailable == true)
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
        // uploads raced the live render loop -> device loss); every
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
        if (_gl is null)
            return;
        _wPx = r.PanelW;
        _hPx = r.PanelH;
        if (!SetupReadback(r))
            return;

        try
        {
            _gl.MakeCurrent();
            _readback!.BeginFrame(_backgroundRgba);
            _scene!.RenderFrame(_readback.Width, _readback.Height);

            // CopyFrame's glReadPixels synchronizes with the draws above, so
            // the locked bitmap now sees this frame (Nexus's "second WaitAll"
            // equivalent lives inside CopyFrame).
            using var fb = _bitmap!.Lock();
            if (!_readback.CopyFrame(fb.Address, fb.RowBytes))
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
                $"[T{_readbackTicks}] runs={_vm!.DumpRunState()}";
            _readbackTicks++;
        }
        catch (Exception ex)
        {
            // Terminal for this tick loop: record the failure, report the
            // verdict (the VM keeps the first reason; its PropertyChanged
            // handler runs FreeRenderResources — scene, context and readback
            // go together — and the XAML swaps in the fallback panel), stop.
            LogGlFailure(ex);
            // Nex's crash handler dumps scene/layer/run state plus the last
            // 12 ticks to PoolDiagnostics; the GL module has no pool, so the
            // same dump goes to Debug.WriteLine (Task 6 brief pins it there,
            // never PoolDiagnostics). The diag ring entries already count
            // readback ticks ([T{n}]).
            System.Diagnostics.Debug.WriteLine(
                $"[GL] tick crash T{_readbackTicks}: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine(
                $"[GL] crash dump runs={_vm?.DumpRunState()} "
                + $"ring={string.Join(" | ", _diagRing.Where(x => x is not null))}");
            _vm?.ReportRenderUnavailable(PlotGlFailures.Classify(ex, GlFailureCause.Runtime), ex.Message);
            DisableTick();
        }
    }

    private int _diagHead;
    private readonly string[] _diagRing = new string[12];

    /// <summary>Percent readout above the zoom cluster — hidden while zoom is
    /// effectively the fitted 100%.</summary>
    private void UpdateZoomLabel(GlPlotScene scene)
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
            var loc = LocalizationService.Current;
            var saveTitle = loc?.Resolve(
                "module.plotter-gl.saveScreenshot", "Save screenshot (PNG)")
                ?? "Save screenshot (PNG)";
            var suggested =
                $"SenderNG_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{_shotW}x{_shotH}.png";
            var path = await _fileDialogs.SaveFileAsync(saveTitle, suggested, "PNG image|*.png");
            if (path is null)
                return;
            // Encode via Avalonia rather than ImageSharp: a direct ImageSharp
            // reference is what put the package under NuGet's direct-dependency
            // audit and raised its NU1902/NU1903 advisories (see the csproj),
            // and the Skia plotter already exports PNG this way (same pattern
            // as main's dd9bcdd on the Nex view). A fresh bitmap, not _bitmap:
            // the shot is the stable snapshot, the live one may be mid-render.
            // Opened as a stream, NOT Save(path): the string overload swallows
            // I/O failures, which would turn an unwritable folder into silent
            // nothing instead of the error dialog below.
            using (var shot = new WriteableBitmap(
                new PixelSize(_shotW, _shotH), new Vector(96, 96),
                PixelFormats.Rgba8888, AlphaFormat.Opaque))
            {
                using (var fb = shot.Lock())
                {
                    var rowBytes = checked(_shotW * 4);
                    for (var y = 0; y < _shotH; y++)
                        System.Runtime.InteropServices.Marshal.Copy(
                            rgba,
                            y * rowBytes,
                            System.IntPtr.Add(fb.Address, y * fb.RowBytes),
                            rowBytes);
                }
                using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
                shot.Save(file);
            }
        }
        catch (Exception ex)
        {
            var loc = LocalizationService.Current;
            var errorTitle = loc?.Resolve(
                "module.plotter-gl.saveScreenshotErrorTitle", "Save screenshot")
                ?? "Save screenshot";
            await _fileDialogs.ShowErrorAsync(errorTitle, $"Save failed: {ex.Message}");
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
        catch (Exception ex)
        {
            // A preset re-frame failing is a later-operation failure, not an
            // initialisation one: _scene is not null, so the context
            // demonstrably started. Reporting RuntimeError here would blame the
            // user's driver for something the driver did not cause.
            ReportRenderFailure(PlotGlFailure.Other);
            LogRenderFailure("SNAP", PlotGlFailure.Other, "n/a", ex);
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
