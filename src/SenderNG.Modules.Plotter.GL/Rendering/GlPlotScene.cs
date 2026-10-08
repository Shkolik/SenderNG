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
/// Task 1 placeholder: every member is a no-op and the factory methods hand out
/// inert layers, so the module's whole call surface (view input handlers, the
/// kinematic factory, the view model's run-state pushes) runs end to end with
/// nothing drawn — the empty viewport the scaffold expects. Task 4 replaces the
/// body with the real GL renderer; this contract and the view-side extras
/// (<c>SetView</c>/<c>PumpCarriagePlanes</c>/<c>PumpToolWire</c>/<c>ZoomPercent</c>)
/// stay as they are here.
/// </para></summary>
internal sealed class GlPlotScene : IPlotScene
{
    public GlPlotScene(GlContext context)
    {
        Context = context;
    }

    /// <summary>The context this scene renders on. Scene and context are one
    /// lifetime: GlPlotScene holds GL objects created on it, so the view
    /// disposes the scene together with the context (FreeRenderResources) and
    /// rebuilds both on re-attach.</summary>
    internal GlContext Context { get; }

    /// <summary>Last viewport size handed in by the view (physical px). Stored
    /// because the renderer is what actually consumes it — Task 4 reads it when
    /// sizing the GL viewport.</summary>
    internal int ViewportWidth { get; private set; }

    internal int ViewportHeight { get; private set; }

    /// <summary>Percent readout above the zoom cluster; always the fitted 100%
    /// while nothing is drawn, so the label stays hidden.</summary>
    public float ZoomPercent => 100f;

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

    public void SetViewPreset(ViewPreset preset)
    {
    }

    public void Pan(PanDelta delta)
    {
    }

    public void ZoomAround(Vector2 screenPoint, double factor)
    {
    }

    public void SetViewportSize(int wPx, int hPx)
    {
        ViewportWidth = wPx;
        ViewportHeight = hPx;
    }

    public void PanByScreenDelta(Vector2 screenDeltaPx)
    {
    }

    public void RotateByScreenDelta(Vector2 screenDeltaPx)
    {
    }

    public void BeginOrbit(Vector2 screenPointPx)
    {
    }

    public void EndOrbit()
    {
    }

    public void Reframe()
    {
    }

    public void AddAxisArrows(IReadOnlyList<(Vector3 Pos, string Label)> arrows)
    {
    }

    public void SetGrid(BoundingBox bounds, double minor, double major)
    {
    }

    public void SetJobEnvelope(BoundingBox box)
    {
    }

    public ICarriagePlaneLayer AddCarriagePlane() => new NoopLayer();

    public ILoftSurface AddLoftSurface() => new NoopLoft();

    public IPlotTool CreateTool(ToolShape shape) => new NoopTool();

    /// <summary>Draw this frame into the readback's FBO — already bound,
    /// viewport-sized and cleared by <see cref="GlReadback.BeginFrame"/> before
    /// this is called. Task 2: nothing to draw yet, so the solid clear IS the
    /// frame; Task 4+ adds the camera-driven chrome draws. <paramref name="rtW"/>
    /// / <paramref name="rtH"/> are the render-target pixels (supersample
    /// factor applied), the size the draws must cover.</summary>
    public void RenderFrame(uint rtW, uint rtH)
    {
    }

    public void Dispose()
    {
        // Task 1: nothing GPU-side exists yet. Task 4 frees the GL objects.
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
