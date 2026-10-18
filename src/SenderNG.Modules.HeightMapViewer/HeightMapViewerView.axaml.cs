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

using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
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

    // Clear color painted every tick: light stays the historically frozen
    // near-white {250, 250, 250} (the frame is opaque — alpha 255); dark
    // follows the Canvas.Bg token via RefreshCanvasBackground, mirroring the
    // plotter GL view's pattern (GlCanvasBackground lives in Plotter.GL,
    // which this module must not reference).
    private byte[] _backgroundRgba = [250, 250, 250, 255];

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
    // Navigation viewport in panel px — the zoom cluster's center-zoom target.
    private uint _wPx;
    private uint _hPx;
    // Latest displayed frame (RGBA, native readback size) for "Save screenshot".
    private byte[]? _shotRgba;
    private int _shotW;
    private int _shotH;

    private bool _panActive;
    private bool _orbitActive;
    private Vector2 _lastPanPx;
    private Vector2 _lastOrbitPx;

    // The locale listener is owned by the view and (un)subscribed symmetrically
    // with attach/detach. The host builds a NEW view per dialog open while
    // LocalizationService.Current is an app-lifetime singleton, so a listener
    // left subscribed would root every abandoned view.
    private LocalizationService? _localization;
    private EventHandler? _localeChanged;

    public HeightMapViewerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _vm = DataContext as HeightMapViewerViewModel;
            EnsureScene();
        };
        AttachedToVisualTree += (_, _) => OnAttachedToVisualTree();
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        // Fresh view per dialog open, so a constructor subscription dies with
        // the dialog — no detach bookkeeping needed.
        ActualThemeVariantChanged += (_, _) => RefreshCanvasBackground();
        ApplyLocalizedTexts();
    }

    /// <summary>The GL clear color. Light stays the historically frozen near-white
    /// canvas value; dark uses the Canvas.Bg token color with opaque alpha.</summary>
    public static byte[] BackgroundRgbaFor(bool dark, Color canvasBg) =>
        dark ? [canvasBg.R, canvasBg.G, canvasBg.B, 255] : [250, 250, 250, 255];

    /// <summary>Resolve the header, reset-button and fallback strings in the
    /// current locale. Reading them in code-behind (not binding) mirrors the
    /// plotter: re-running on <see cref="LocalizationService.Changed"/> keeps a
    /// live language switch working.</summary>
    private void ApplyLocalizedTexts()
    {
        var loc = LocalizationService.Current;
        FallbackTitle.Text = loc?.Resolve(
            "module.heightmap-viewer.renderUnavailable", "3D view unavailable")
            ?? "3D view unavailable";
        FallbackHint.Text = GenericHint();
        ToolTip.SetTip(SaveScreenshotButton, loc?.Resolve(
            "module.heightmap-viewer.saveScreenshot", "Save screenshot (PNG)")
            ?? "Save screenshot (PNG)");
        ToolTip.SetTip(ReframeButton, loc?.Resolve(
            "module.heightmap-viewer.reset", "Reset view")
            ?? "Reset view");
        ToolTip.SetTip(ZoomInButton, loc?.Resolve(
            "module.heightmap-viewer.zoomIn", "Zoom in") ?? "Zoom in");
        ToolTip.SetTip(ZoomOutButton, loc?.Resolve(
            "module.heightmap-viewer.zoomOut", "Zoom out") ?? "Zoom out");
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

    private void OnAttachedToVisualTree()
    {
        SubscribeLocale();
        RefreshCanvasBackground();
        EnsureScene();
    }

    /// <summary>Re-resolve the clear color from the Canvas.Bg token for the
    /// current theme variant. Runs on attach (which resolves the variant) and
    /// on every ActualThemeVariantChanged. TryFindResource walks the styling
    /// chain — TryGetResource only checks this element's own resources and
    /// misses the app-level theme dictionaries.</summary>
    private void RefreshCanvasBackground()
    {
        var fallback = Colors.White;
        var canvasBg = fallback;
        if (this.TryFindResource("Sender.Canvas.Bg", ActualThemeVariant, out var value))
            canvasBg = value switch
            {
                ISolidColorBrush brush => brush.Color,
                Color color => color,
                _ => fallback,
            };
        _backgroundRgba = BackgroundRgbaFor(ActualThemeVariant == ThemeVariant.Dark, canvasBg);
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeLocale();
        DisableTick();
        FreeRenderResources();
    }

    /// <summary>Idempotently listen for locale changes while the view is
    /// attached. Re-attach re-subscribes; a one-shot dialog is torn down by
    /// <see cref="UnsubscribeLocale"/> on detach.</summary>
    private void SubscribeLocale()
    {
        if (_localeChanged is not null)
            return;
        var loc = LocalizationService.Current;
        if (loc is null)
            return;
        _localeChanged = (_, _) => ApplyLocalizedTexts();
        _localization = loc;
        loc.Changed += _localeChanged;
    }

    /// <summary>Idempotently drop the locale listener so the singleton does not
    /// root this view after the dialog closes.</summary>
    private void UnsubscribeLocale()
    {
        if (_localeChanged is null)
            return;
        var loc = _localization;
        if (loc is not null)
            loc.Changed -= _localeChanged;
        _localization = null;
        _localeChanged = null;
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
        var r = RenderSizing.Resolve(pw, ph, _vm?.Settings?.Supersample ?? true);

        // Navigation viewport stays in on-screen (panel) px: cursor px and the
        // pan/zoom math divide by these, so they must equal what the user points
        // at, not the supersampled render target.
        scene.SetViewportSize((int)r.PanelW, (int)r.PanelH);
        _wPx = r.PanelW;
        _hPx = r.PanelH;
        scene.SetLayer(_vm?.Layer);
        scene.SetShowOrigin(_vm?.Settings?.ShowOrigin ?? true);

        if (!SetupReadback(r))
            return;

        try
        {
            _gl.MakeCurrent();
            _readback!.BeginFrame(_backgroundRgba);
            scene.RenderFrame(_readback.Width, _readback.Height);
            // CopyFrame's glReadPixels synchronizes with the draws above.
            using (var fb = _bitmap!.Lock())
            {
                _readback.CopyFrame(fb.Address, fb.RowBytes);
                CaptureScreenshot(fb, r.RtW, r.RtH);
            }
            GpuImage.InvalidateVisual();
            UpdateZoomLabel(scene);
        }
        catch (Exception ex)
        {
            ReportRenderFailure(PlotGlFailures.Classify(ex, GlFailureCause.Runtime), ex);
        }
    }

    /// <summary>Create (or resize) the readback target and its bitmap for this
    /// tick's render size, and apply the configured MSAA mode. A failure is
    /// logged and retried next tick — usually transient host-memory pressure,
    /// not a verdict.</summary>
    private bool SetupReadback(in RenderSize r)
    {
        try
        {
            // MSAA rides the same tick as the render size, but on its own axis
            // (sample count vs supersample scale). The first create takes the
            // configured mode straight to the ctor — no default-then-rebuild
            // double allocation on the first tick.
            var msaa = _vm?.Settings?.MsaaMode ?? MsaaMode.None;
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
                    System.Diagnostics.Trace.WriteLine(
                        $"[HeightMapViewer] MSAA {msaa} unavailable, falling back to None: {ex.Message}");
                    _readback.SetMsaa(MsaaMode.None);
                }
            }

            if (created)
            {
                _bitmap = new WriteableBitmap(
                    new PixelSize((int)r.RtW, (int)r.RtH),
                    new Avalonia.Vector(96, 96),
                    PixelFormats.Rgba8888,
                    AlphaFormat.Opaque);
                GpuImage.Source = _bitmap;
            }

            _rtW = r.RtW;
            _rtH = r.RtH;
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

    private void OnReframe(object? sender, RoutedEventArgs e) => _scene?.Reframe();

    private void OnZoomIn(object? sender, RoutedEventArgs e) => CenterZoom(1.25);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => CenterZoom(1.0 / 1.25);

    private void CenterZoom(double factor)
    {
        var scene = _scene;
        if (scene is null)
            return;
        scene.ZoomAround(new Vector2(_wPx * 0.5f, _hPx * 0.5f), factor);
    }

    /// <summary>Percent readout above the zoom cluster — hidden while the view is
    /// effectively at the fitted 100%.</summary>
    private void UpdateZoomLabel(HeightMapScene scene)
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

    /// <summary>Keep a copy of the displayed frame so the save button can export
    /// the current view at its native readback size.</summary>
    private void CaptureScreenshot(ILockedFramebuffer fb, uint w, uint h)
    {
        var len = checked((int)w * (int)h * 4);
        if (_shotRgba is null || _shotRgba.Length != len)
        {
            _shotRgba = new byte[len];
            _shotW = (int)w;
            _shotH = (int)h;
        }
        Marshal.Copy(fb.Address, _shotRgba, 0, len);
    }

    /// <summary>Write the latest readback frame (native size, pixel-aligned) to a
    /// PNG through the standard save dialog. Cancelled dialog is silent; a write
    /// failure surfaces a modal error.</summary>
    private async void OnSaveScreenshot(object? sender, RoutedEventArgs e)
    {
        var dialogs = _vm?.FileDialogs;
        if (dialogs is null)
            return;
        var rgba = _shotRgba;
        if (rgba is null || _shotW <= 0 || _shotH <= 0)
            return;
        try
        {
            var loc = LocalizationService.Current;
            var saveTitle = loc?.Resolve(
                "module.heightmap-viewer.saveScreenshot", "Save screenshot (PNG)")
                ?? "Save screenshot (PNG)";
            var suggested = $"SenderNG_heightmap_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{_shotW}x{_shotH}.png";
            var path = await dialogs.SaveFileAsync(saveTitle, suggested, "PNG image|*.png");
            if (path is null)
                return;
            using (var shot = new WriteableBitmap(
                new PixelSize(_shotW, _shotH), new Avalonia.Vector(96, 96),
                PixelFormats.Rgba8888, AlphaFormat.Opaque))
            {
                using (var fb = shot.Lock())
                {
                    var rowBytes = checked(_shotW * 4);
                    for (var y = 0; y < _shotH; y++)
                        Marshal.Copy(rgba, y * rowBytes,
                            IntPtr.Add(fb.Address, y * fb.RowBytes), rowBytes);
                }
                using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
                shot.Save(file);
            }
        }
        catch (Exception ex)
        {
            var loc = LocalizationService.Current;
            var errorTitle = loc?.Resolve(
                "module.heightmap-viewer.saveScreenshotErrorTitle", "Save screenshot")
                ?? "Save screenshot";
            await dialogs.ShowErrorAsync(errorTitle, $"Save failed: {ex.Message}");
        }
    }

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
