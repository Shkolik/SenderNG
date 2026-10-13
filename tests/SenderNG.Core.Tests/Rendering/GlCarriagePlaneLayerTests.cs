/*
 * GlCarriagePlaneLayerTests.cs - toolpath staging, run-state overlay and the
 * colour palette on the real GL path
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

using SenderNG.Modules.Common.Render;
using System.Numerics;
using Silk.NET.OpenGL;
using SenderNG.Modules.Plotter.GL.Rendering;
using SenderNG.Modules.Plotter.GL.Scene;
using SenderNG.Modules.Plotter.GL.Settings;

namespace SenderNG.Core.Tests;

/// <summary>The Task 5 layer: segments stage on CPU, run-state transitions
/// drive the executed overlay idempotently, and the palette is a
/// <see cref="Vector4"/> colour table. Gated by
/// <see cref="GlFactAttribute"/> like the other GPU tests.</summary>
[Trait("Category", "GPU")]
public class GlCarriagePlaneLayerTests
{
    [GlFact]
    public void AddSegment_appends_two_vertices_and_tracks_bounds()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var layer = scene.AddCarriagePlane();
        layer.AddSegment(new LineSegment(new Vector3(0, 0, 0), new Vector3(10, 0, 0)), MotionType.Cut);
        layer.AddSegment(new LineSegment(new Vector3(10, 0, 0), new Vector3(10, 10, 0)), MotionType.Rapid);
        Assert.True(((GlCarriagePlaneLayer)layer).TryGetBounds(out var b));
        Assert.Equal(0f, b.MinX); Assert.Equal(10f, b.MaxX); Assert.Equal(10f, b.MaxY);
    }

    [GlFact]
    public void RunState_transitions_change_overlay_only_on_change()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var layer = (GlCarriagePlaneLayer)scene.AddCarriagePlane();
        layer.AddSegment(new LineSegment(Vector3.Zero, Vector3.UnitX), MotionType.Cut);
        layer.SetSegmentRunState(0, RunState.Executed);
        layer.SetSegmentRunState(0, RunState.Executed);   // idempotent — no duplicate overlay entry
        Assert.Equal(2, layer.ExecutedVerticesCount);
        layer.SetSegmentRunState(0, RunState.Pending);    // rewind → full overlay rebuild
        layer.RebuildExecuted();                          // rare O(n) path stays valid
        Assert.Equal(0, layer.ExecutedVerticesCount);
        Assert.True(layer.PendingMutations > 0);         // RebuildGate was marked, not GPU-touched
    }

    [GlFact]
    public void Cut_and_rapid_segments_use_distinct_colors()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var layer = (GlCarriagePlaneLayer)scene.AddCarriagePlane();
        layer.AddSegment(new LineSegment(Vector3.Zero, Vector3.UnitX), MotionType.Cut);
        layer.AddSegment(new LineSegment(Vector3.UnitX, Vector3.UnitY), MotionType.Rapid);

        var cut = layer.ColorAt(0);
        var rapid = layer.ColorAt(1);
        Assert.NotEqual(cut, rapid);

        // Color-table assertion style: component-wise equality against the
        // palette constants.
        Assert.Equal(GlCarriagePlaneLayer.CutColor, cut);
        Assert.Equal(0.5f, cut.X);
        Assert.Equal(0.5f, cut.Y);
        Assert.Equal(0.5f, cut.Z);
        Assert.Equal(1f, cut.W);

        Assert.Equal(GlCarriagePlaneLayer.RapidColor, rapid);
        Assert.Equal(0.9f, rapid.X);
        Assert.Equal(0.12f, rapid.Y);
        Assert.Equal(0.12f, rapid.Z);
        Assert.Equal(1f, rapid.W);
    }

    /// <summary>Headless replacement for the waived GUI check (Step 5.4): the
    /// pumped toolpath really draws — chrome toggled off so only the layer can
    /// light pixels — and the spike's one-shot GL error probe (in
    /// <see cref="GlPlotScene.RenderFrame"/>, Debug only) ran clean over the
    /// frame that drew it.</summary>
    [GlFact]
    public unsafe void Pump_and_RenderFrame_draw_the_toolpath_without_gl_errors()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, 256, 256);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        scene.SetViewportSize(256, 256);
        var layer = (GlCarriagePlaneLayer)scene.AddCarriagePlane();
        layer.AddSegment(new LineSegment(new Vector3(-40, 0, 0), new Vector3(40, 0, 0)), MotionType.Cut);
        layer.AddSegment(new LineSegment(new Vector3(0, -40, 0), new Vector3(0, 40, 0)), MotionType.Rapid);
        scene.PumpCarriagePlanes();
        scene.SetViewPreset(ViewPreset.XY);
        // Chrome off: only the toolpath can light pixels.
        scene.SetView(new PlotterGlSettings
        {
            ShowOrigin = false,
            ShowEnvelope = false,
            ShowGrid = false,
        });

        rb.BeginFrame(Background);
        scene.RenderFrame(256, 256);
        var px = new byte[256 * 256 * 4];
        fixed (byte* p = px)
            Assert.True(rb.CopyFrame((nint)p, 256 * 4));
        var lit = LitPixels(px);
        Assert.True(lit > 50, $"expected toolpath pixels, got {lit}");

        Assert.True(scene.GlErrorProbeRan, "the one-shot probe should fire on the first content frame");
        Assert.Equal(GLEnum.NoError, scene.GlErrorProbeResult);
    }

    private static readonly byte[] Background = [32, 35, 40, 255];

    private static int LitPixels(byte[] px)
    {
        var lit = 0;
        for (var i = 0; i < px.Length; i += 4)
            if (px[i] != 32 || px[i + 1] != 35 || px[i + 2] != 40)
                lit++;
        return lit;
    }
}
