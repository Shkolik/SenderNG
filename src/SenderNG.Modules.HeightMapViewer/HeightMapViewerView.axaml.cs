/*
 * HeightMapViewerView.axaml.cs - Avalonia host for the height-map GL surface
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

using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SenderNG.Core.Localization;
using SenderNG.Modules.Common.Render;

namespace SenderNG.Modules.HeightMapViewer;

/// <summary>
/// Hosts the height-map GL scene. Mirrors the plotter's GL view: context and
/// scene are one per-attach lifetime (a GL context renders on the thread that
/// owns it, so creation is synchronous on the UI thread; detach disposes both
/// together and re-attach rebuilds both). Each ~33ms tick makes the context
/// current, clears the readback FBO, draws the scene, and copies the frame into
/// a <see cref="WriteableBitmap"/> shown by <c>GpuImage</c>. The view model is
/// polled for its latest layer, which rebuilds the GPU mesh when it changes.
/// </summary>
public partial class HeightMapViewerView : UserControl
{
    private const double TickIntervalMs = 33.0;

    /// <summary>Near-white clear ({250, 250, 250} after the opaque readback).</summary>
    private static readonly byte[] BackgroundRgba = [250, 250, 250, 255];

    private HeightMapViewerViewModel? _vm;
    private GlContext? _gl;
    private HeightMapScene? _scene;
    private GlReadback? _readback;
    private WriteableBitmap? _bitmap;
    private DispatcherTimer? _tick;
    private bool _tickSubscribed;
    // Why GL creation failed, remembered for the view's lifetime so a failed
    // context is never re-attempted.
    private PlotGlFailure? _glVerdict;
    private uint _rtW;
    private uint _rtH;

    private bool _panActive;
    private bool _orbitActive;
    private Vector2 _lastPanPx;
    private Vector2 _lastOrbitPx;

    public HeightMapViewerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _vm = DataContext as HeightMapViewerViewModel;
            EnsureScene();
        };
        AttachedToVisualTree += (_, _) => EnsureScene();
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        ApplyLocalizedTexts();
        var loc = LocalizationService.Current;
        if (loc is not null)
            loc.Changed += (_, _) => ApplyLocalizedTexts();
    }

    /// <summary>Resolve the header, reset-button and fallback strings in the
    /// current locale. Reading them in code-behind (not binding) mirrors the
    /// plotter: re-running on <see cref="LocalizationService.Changed"/> keeps a
    /// live language switch working.</summary>
    private void ApplyLocalizedTexts()
    {
        var loc = LocalizationService.Current;
        HeaderText.Text = loc?.Resolve("module.heightmap-viewer.title", "Height map 3D")
            ?? "Height map 3D";
        ResetViewButton.Content = loc?.Resolve("module.heightmap-viewer.reset", "Reset view")
            ?? "Reset view";
        FallbackTitle.Text = loc?.Resolve(
            "module.heightmap-viewer.renderUnavailable", "3D view unavailable")
            ?? "3D view unavailable";
        FallbackHint.Text = GenericHint();
    }

    private static string GenericHint()
        => LocalizationService.Current?.Resolve(
            "module.heightmap-viewer.renderUnavailableHint", "No OpenGL context could be created.")
            ?? "No OpenGL context could be created.";

    /// <summary>Create the GL context and scene synchronously on the UI thread
    /// (context affinity), then start the tick. Re-attach re-runs this after the
    /// detach teardown, so every attach gets a scene bound to a fresh context —
    /// GL objects never outlive the context that created them.</summary>
    private void EnsureScene()
    {
        if (_scene is not null || _glVerdict is not null)
            return;
        GlContext? ctx = null;
        HeightMapScene? scene = null;
        try
        {
            ctx = GlContext.Create();
            scene = new HeightMapScene(ctx);
        }
        catch (Exception ex)
        {
            scene?.Dispose();
            ctx?.Dispose();
            ReportRenderFailure(PlotGlFailures.Classify(ex, GlFailureCause.ContextCreate), ex);
            return;
        }
        _gl = ctx;
        _scene = scene;
        EnableTick();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        DisableTick();
        FreeRenderResources();
    }

    private void ReportRenderFailure(PlotGlFailure failure, Exception ex)
    {
        _glVerdict = failure;
        System.Diagnostics.Trace.WriteLine(
            $"[HeightMapViewer] GL failed={failure} {ex.GetType().Name}: {ex.Message}");
        DisableTick();
        FreeRenderResources();
        ScenePanel.IsVisible = false;
        FallbackHint.Text = GenericHint();
        FallbackPanel.IsVisible = true;
    }

    private void FreeRenderResources()
    {
        // Order matters: each step needs the next one alive — the FBO and the
        // scene's GL objects only delete through a live context.
        _readback?.Dispose();
        _readback = null;
        _scene?.Dispose();
        _scene = null;
        _gl?.Dispose();
        _gl = null;
        _bitmap = null;
        GpuImage.Source = null;
    }

    private void EnableTick()
    {
        if (_gl is null)
            return;
        if (_tick is null)
        {
            _tick = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(TickIntervalMs),
            };
        }
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
        if (scene is null || _gl is null)
            return;

        var tl = TopLevel.GetTopLevel(this);
        var scale = tl?.RenderScaling ?? 1.0;
        var bounds = GpuImage.Bounds;
        var client = tl?.ClientSize ?? default(Size);
        var wDip = bounds.Width > 0 ? bounds.Width : client.Width;
        var hDip = bounds.Height > 0 ? bounds.Height : client.Height;
        if (wDip < 8 || hDip < 8)
            return;
        var pw = (uint)Math.Max(1, Math.Round(wDip * scale));
        var ph = (uint)Math.Max(1, Math.Round(hDip * scale));

        scene.SetViewportSize((int)pw, (int)ph);
        scene.SetLayer(_vm?.Layer);

        if (!SetupReadback(pw, ph))
            return;

        try
        {
            _gl.MakeCurrent();
            _readback!.BeginFrame(BackgroundRgba);
            scene.RenderFrame(_readback.Width, _readback.Height);
            // CopyFrame's glReadPixels synchronizes with the draws above.
            using (var fb = _bitmap!.Lock())
                _readback.CopyFrame(fb.Address, fb.RowBytes);
            GpuImage.InvalidateVisual();
        }
        catch (Exception ex)
        {
            ReportRenderFailure(PlotGlFailures.Classify(ex, GlFailureCause.Runtime), ex);
        }
    }

    /// <summary>Create (or resize) the readback target and its bitmap for this
    /// tick's pixel size. A failure is logged and retried next tick — usually
    /// transient host-memory pressure, not a verdict.</summary>
    private bool SetupReadback(uint w, uint h)
    {
        try
        {
            var created = false;
            if (_readback is null)
            {
                _readback = new GlReadback(_gl!, w, h);
                created = true;
            }
            else if (w != _rtW || h != _rtH)
            {
                _readback.Resize(w, h);
                created = true;
            }

            if (created)
            {
                _bitmap = new WriteableBitmap(
                    new PixelSize((int)w, (int)h),
                    new Avalonia.Vector(96, 96),
                    PixelFormats.Rgba8888,
                    AlphaFormat.Opaque);
                GpuImage.Source = _bitmap;
            }

            _rtW = w;
            _rtH = h;
            return true;
        }
        catch (Exception ex)
        {
            _readback?.Dispose();
            _readback = null;
            System.Diagnostics.Trace.WriteLine($"[HeightMapViewer] readback failed: {ex.Message}");
            return false;
        }
    }

    private float PxScale() => (float)(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0);

    private static Vector2 ToPx(Point dip, float scale) => new((float)(dip.X * scale), (float)(dip.Y * scale));

    private void OnResetView(object? sender, RoutedEventArgs e)
        => _scene?.SetViewPreset(ViewPreset.Isometric3D);

    private void OnPlotInputPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var scene = _scene;
        if (scene is null)
            return;
        var px = ToPx(e.GetPosition(PlotInput), PxScale());
        var kind = e.GetCurrentPoint(PlotInput).Properties.PointerUpdateKind;
        if (kind == PointerUpdateKind.LeftButtonPressed)
        {
            _panActive = true;
            _lastPanPx = px;
            e.Pointer.Capture(PlotInput);
        }
        else if (kind == PointerUpdateKind.RightButtonPressed)
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
}
