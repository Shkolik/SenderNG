/*
 * GlPlotScene.cs - contract-driven plot scene for the GL renderer
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
using SenderNG.Modules.Plotter.GL.Scene;
using SenderNG.Modules.Plotter.GL.Settings;

namespace SenderNG.Modules.Plotter.GL.Rendering;

/// <summary>The plot scene the view drives through <see cref="IPlotScene"/>.
/// <para>
/// Navigation is live: two <see cref="GlCamera"/> modes (ortho/perspective),
/// Nex's preset/pan/zoom/orbit wiring retyped onto <see cref="CameraGestures"/>,
/// and the view/projection cached per <see cref="RenderFrame"/> for cursor-ray
/// placement. The viewport still renders only the background clear — the
/// content draws are Task 4; this contract and the view-side extras
/// (<c>SetView</c>/<c>PumpCarriagePlanes</c>/<c>PumpToolWire</c>/<c>ZoomPercent</c>)
/// stay as they are here.
/// </para></summary>
internal sealed class GlPlotScene : IPlotScene
{
    private const float OrbitSensitivity = 0.005f;

    private readonly GlCamera _ortho = new();
    private readonly GlCamera _persp = new();
    private GlCamera _active = null!;
    private bool _perspectiveActive;
    private ViewPreset _lastPreset = ViewPreset.Isometric3D;
    private float _orthoZoom = 1f;
    private float _navW = 1f;
    private float _navH = 1f;
    private float _fitOrthoWidth = 1f;
    private float _fitDist = 1f;
    private Vector3? _orbitPivot;

    /// <summary>Last frame's render truth for cursor-ray placement (the inverse
    /// view-projection the GPU actually drew with). <c>_hasInvViewProj</c> goes
    /// true after the first <see cref="RenderFrame"/>.</summary>
    private Matrix4x4 _invViewProj;
    private bool _hasInvViewProj;

    public GlPlotScene(GlContext context)
    {
        Context = context;
        SetViewPreset(ViewPreset.Isometric3D);
    }

    /// <summary>The context this scene renders on. Scene and context are one
    /// lifetime: GlPlotScene holds GL objects created on it, so the view
    /// disposes the scene together with the context (FreeRenderResources) and
    /// rebuilds both on re-attach.</summary>
    internal GlContext Context { get; }

    /// <summary>Percent readout above the zoom cluster — the fit-seeded
    /// zoom (100 = fitted), hidden at 100% by the view.</summary>
    public float ZoomPercent
    {
        get
        {
            if (_perspectiveActive == false)
            {
                if (_ortho.Width <= 0f)
                    return 100f;
                return 100f * _fitOrthoWidth / _ortho.Width;
            }
            var dist = (_persp.Target - _persp.Position).Length();
            if (dist <= 0f)
                return 100f;
            return 100f * _fitDist / dist;
        }
    }

    /// <summary>Push the overlay view toggles (runtime-only, no rebuild).</summary>
    public void SetView(PlotterGlSettings settings)
    {
    }

    /// <summary>Swap-pump the carriage planes into fresh static geometry
    /// (called at the apply cadence, not every tick).</summary>
    public void PumpCarriagePlanes()
    {
    }

    /// <summary>Swap-pump the tool connector wire and loft surface.</summary>
    public void PumpToolWire()
    {
    }

    /// <inheritdoc/>
    public void SetViewPreset(ViewPreset preset)
    {
        _lastPreset = preset;
        _orbitPivot = null;
        var (center, radius, span) = ContentMetrics();
        // One zoom basis for every orthographic projection: the model's
        // bounding-box diagonal. Per-plane spans made a long thin board
        // huge in YZ (max(Y,Z) is small) and tiny-consistent ones awkward;
        // a common world-units-per-pixel keeps all projections at the same
        // apparent scale, and is the tracked base for the wheel zoom.
        _orthoZoom = MathF.Max(span.Length() * 1.1f, 1f);
        switch (preset)
        {
            case ViewPreset.XY:
                _ortho.Width = _orthoZoom;
                _ortho.Position = center + Vector3.UnitZ * (radius * 2f);
                _ortho.Target = center;
                _ortho.Up = Vector3.UnitY;
                _active = _ortho;
                _perspectiveActive = false;
                _fitOrthoWidth = _orthoZoom;
                break;
            case ViewPreset.XZ:
                _ortho.Width = _orthoZoom;
                _ortho.Position = center - Vector3.UnitY * (radius * 2f);
                _ortho.Target = center;
                _ortho.Up = Vector3.UnitZ;
                _active = _ortho;
                _perspectiveActive = false;
                _fitOrthoWidth = _orthoZoom;
                break;
            case ViewPreset.YZ:
                _ortho.Width = _orthoZoom;
                _ortho.Position = center + Vector3.UnitX * (radius * 2f);
                _ortho.Target = center;
                _ortho.Up = Vector3.UnitZ;
                _active = _ortho;
                _perspectiveActive = false;
                _fitOrthoWidth = _orthoZoom;
                break;
            default:
                // Skia's iso projector exactly: X -> right/down, Y -> left/down,
                // Z -> straight up. The camera offset must be -normalize(1,1,-1)
                // (front-upper corner); +normalize(1,1,-1) looks from underneath.
                _persp.Position = center
                    + Vector3.Normalize(new Vector3(-1f, -1f, 1f)) * (radius * 2.2f);
                _persp.Target = center;
                _persp.Up = Vector3.Normalize(new Vector3(0.5f, 0.5f, 1f));
                _active = _persp;
                _perspectiveActive = true;
                _fitDist = radius * 2.2f;
                break;
        }
    }

    /// <inheritdoc/>
    public void Pan(PanDelta delta)
    {
        var v = -delta.WorldDelta;
        _persp.Position += v;
        _persp.Target += v;
        _ortho.Position += v;
        _ortho.Target += v;
    }

    /// <inheritdoc/>
    public void ZoomAround(Vector2 screenPoint, double factor)
    {
        if (factor <= 0)
            return;
        CameraGestures.AnchorZoom(_active, screenPoint, factor, _navW, _navH);
    }

    /// <inheritdoc/>
    public void SetViewportSize(int wPx, int hPx)
    {
        // Navigation viewport stays in on-screen (panel) px: cursor px and the
        // pivot/pan/zoom NDC all divide by these, so they must equal what the
        // user points at, not the supersampled render target.
        _navW = Math.Max(1, wPx);
        _navH = Math.Max(1, hPx);
    }

    /// <inheritdoc/>
    public void PanByScreenDelta(Vector2 screenDeltaPx)
    {
        var v = CameraGestures.ScreenPanDelta(_active, screenDeltaPx, _navW, _navH);
        Pan(new PanDelta(v));
    }

    /// <inheritdoc/>
    public void RotateByScreenDelta(Vector2 screenDeltaPx)
    {
        if (_perspectiveActive == false)
            return;
        if (_orbitPivot is not Vector3 origin)
            return;
        CameraGestures.RotateAroundPoint(
            _persp, origin, screenDeltaPx, OrbitSensitivity, invertY: true, useWorldUp: false);
    }

    /// <inheritdoc/>
    public void BeginOrbit(Vector2 screenPointPx)
    {
        if (_perspectiveActive == false)
        {
            _orbitPivot = null;
            return;
        }
        var cam = _persp;
        var dist = (cam.Target - cam.Position).Length();
        if (dist < 0.01f)
        {
            _orbitPivot = cam.Target;
            return;
        }
        if (_hasInvViewProj)
        {
            _orbitPivot = CameraGestures.CursorRayPlanePivot(
                _invViewProj,
                screenPointPx,
                _navW,
                _navH,
                1f,
                cam.Target,
                Vector3.Normalize(cam.Target - cam.Position));
        }
        else
        {
            // Pre-first-frame fallback: no render snapshot yet, so the pivot is
            // the taut safely-degenerate target. The panel has no visible frame
            // before the first tick anyway.
            _orbitPivot = cam.Target;
        }
    }

    /// <inheritdoc/>
    public void EndOrbit()
    {
        _orbitPivot = null;
    }

    /// <inheritdoc/>
    public void Reframe() => SetViewPreset(_lastPreset);

    /// <inheritdoc/>
    public void AddAxisArrows(IReadOnlyList<(Vector3 Pos, string Label)> arrows)
    {
    }

    /// <inheritdoc/>
    public void SetGrid(BoundingBox bounds, double minor, double major)
    {
    }

    /// <inheritdoc/>
    public void SetJobEnvelope(BoundingBox box)
    {
    }

    public ICarriagePlaneLayer AddCarriagePlane() => new NoopLayer();

    public ILoftSurface AddLoftSurface() => new NoopLoft();

    public IPlotTool CreateTool(ToolShape shape) => new NoopTool();

    /// <summary>Bounding metrics of the live content. No content exists before
    /// Task 4, so this is Nex's not-found envelope (min/max ±25) — the default
    /// box the presets frame until real geometry lands.</summary>
    private static (Vector3 Center, float Radius, Vector3 Span) ContentMetrics()
    {
        var min = new Vector3(-25f);
        var max = new Vector3(25f);
        var center = (min + max) * 0.5f;
        var span = max - min;
        var radius = MathF.Max(span.Length() * 0.5f, 1f);
        return (center, radius, span);
    }

    /// <summary>Inverse view-projection cached by the last
    /// <see cref="RenderFrame"/> — the matrices the GPU actually drew with, in
    /// panel-pixel NDC for the gesture unprojections.</summary>
    internal Matrix4x4 CurrentInvViewProj() => _invViewProj;

    /// <summary>Draw this frame into the readback's FBO — already bound,
    /// viewport-sized and cleared by <see cref="GlReadback.BeginFrame"/> before
    /// this is called. Recomputes the active camera's view/projection for the
    /// render-target aspect and caches the inverse for cursor-ray placement;
    /// the content draws themselves come in Task 4. <paramref name="rtW"/>
    /// / <paramref name="rtH"/> are the render-target pixels (supersample
    /// factor applied), the size the draws must cover.</summary>
    public void RenderFrame(uint rtW, uint rtH)
    {
        var view = _active.View();
        var proj = _active.Projection(rtW, rtH);
        _hasInvViewProj = Matrix4x4.Invert(view * proj, out _invViewProj);
    }

    public void Dispose()
    {
        // Task 4 frees the GL objects; navigation state is managed memory only.
    }

    private sealed class NoopLayer : ICarriagePlaneLayer
    {
        public void AddSegment(LineSegment seg, MotionType type)
        {
        }

        public void SetSegmentRunState(int segmentIndex, RunState state)
        {
        }

        public void RebuildExecuted()
        {
        }
    }

    private sealed class NoopLoft : ILoftSurface
    {
        public void SetWirePlane(Vector3[] polygon)
        {
        }

        public void SetLoftSheet(IReadOnlyList<Vector3> sideA, IReadOnlyList<Vector3> sideB)
        {
        }
    }

    private sealed class NoopTool : IPlotTool
    {
        public ToolShape Shape { get; set; }

        public void UpdatePosition(Endpoint endpoints)
        {
        }

        public void Dispose()
        {
        }
    }
}
