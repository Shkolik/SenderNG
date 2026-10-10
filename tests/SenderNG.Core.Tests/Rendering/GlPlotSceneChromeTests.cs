/*
 * GlPlotSceneChromeTests.cs - grid, axes and job envelope put non-background
 * pixels in the FBO, and the overlay toggles take them back out
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
using SenderNG.Modules.Plotter.GL.Rendering;
using SenderNG.Modules.Plotter.GL.Scene;
using SenderNG.Modules.Plotter.GL.Settings;

namespace SenderNG.Core.Tests;

/// <summary>The first visible chrome on the real pixel path: an FBO cleared to
/// the panel background, one <see cref="GlPlotScene.RenderFrame"/>, then a
/// readback that counts every pixel the grid/axes/envelope overwrote. Gated by
/// <see cref="GlFactAttribute"/> like the other GPU tests — on a machine with a
/// usable GL these assertions really run.</summary>
[Trait("Category", "GPU")]
public class GlPlotSceneChromeTests
{
    private static readonly byte[] Background = [32, 35, 40, 255];

    [GlFact]
    public unsafe void Grid_and_axes_draw_visible_pixels()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, 256, 256);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        scene.SetViewportSize(256, 256);
        scene.SetViewPreset(ViewPreset.Isometric3D);
        scene.SetGrid(new BoundingBox(-50, -50, -1, 50, 50, 1), 10, 50);
        scene.AddAxisArrows([(Vector3.Zero, "X"), (Vector3.Zero, "Y"), (Vector3.Zero, "Z")]);
        scene.SetJobEnvelope(new BoundingBox(0, 0, 0, 100, 100, 10));
        rb.BeginFrame(Background);
        scene.RenderFrame(256, 256);
        var px = new byte[256 * 256 * 4];
        fixed (byte* p = px)
            Assert.True(rb.CopyFrame((nint)p, 256 * 4));
        var lit = LitPixels(px);
        Assert.True(lit > 50, $"expected grid/axes/envelope pixels, got {lit}");
    }

    /// <summary>Checkpoint-5 item 3 (Nex parity): the chrome pieces draw with
    /// Nex's PER-PIECE thicknesses — grid 1.6, axis arrows 3.6, job envelope
    /// 1.6 (Nex <c>NexPlotScene.cs:265-267</c>) — not one shared width for
    /// every piece. Observed through the width seam the palette tests use: what
    /// each piece group actually pushed as <c>uLineWidthPx</c> this frame.</summary>
    [GlFact]
    public void Chrome_pieces_draw_at_their_nex_palette_widths()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, 256, 256);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        scene.SetViewportSize(256, 256);
        scene.SetViewPreset(ViewPreset.Isometric3D);
        scene.SetGrid(new BoundingBox(-50, -50, -1, 50, 50, 1), 10, 50);
        scene.AddAxisArrows([(Vector3.Zero, "X"), (Vector3.Zero, "Y"), (Vector3.Zero, "Z")]);
        scene.SetJobEnvelope(new BoundingBox(0, 0, 0, 100, 100, 10));

        rb.BeginFrame(Background);
        scene.RenderFrame(256, 256);

        var (grid, envelope, axes) = scene.ChromeWidthsPushed;
        Assert.Equal(1.6f, grid);     // Nex grid node thickness
        Assert.Equal(1.6f, envelope); // Nex job-envelope node thickness
        Assert.Equal(3.6f, axes);     // Nex axis-arrows node thickness
    }

    /// <summary>SetView is the runtime chrome gate the overlay bar drives: all
    /// three toggles off must leave an untouched background frame, no rebuild
    /// and no parked leftovers.</summary>
    [GlFact]
    public unsafe void SetView_toggles_take_the_chrome_away_live()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, 256, 256);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        scene.SetViewportSize(256, 256);
        scene.SetViewPreset(ViewPreset.Isometric3D);
        scene.SetGrid(new BoundingBox(-50, -50, -1, 50, 50, 1), 10, 50);
        scene.AddAxisArrows([(Vector3.Zero, "X"), (Vector3.Zero, "Y"), (Vector3.Zero, "Z")]);
        scene.SetJobEnvelope(new BoundingBox(0, 0, 0, 100, 100, 10));
        var px = new byte[256 * 256 * 4];

        rb.BeginFrame(Background);
        scene.RenderFrame(256, 256);
        fixed (byte* p = px)
            Assert.True(rb.CopyFrame((nint)p, 256 * 4));
        var lit = LitPixels(px);
        Assert.True(lit > 50, $"precondition: chrome must be visible first, got {lit}");

        scene.SetView(new PlotterGlSettings
        {
            ShowOrigin = false,
            ShowEnvelope = false,
            ShowGrid = false,
        });
        rb.BeginFrame(Background);
        scene.RenderFrame(256, 256);
        fixed (byte* p = px)
            Assert.True(rb.CopyFrame((nint)p, 256 * 4));
        Assert.Equal(0, LitPixels(px));
    }

    /// <summary>Checkpoint-4 item 1 (Nex <c>_contentFramed</c> parity): the
    /// constructor frames the camera once against empty content, so a model
    /// that lands afterwards must trigger exactly one reframe onto its bounds
    /// — the initial view centered like the snap button frames it, not skewed
    /// toward the grid defaults.</summary>
    [GlFact]
    public void First_content_arrival_frames_the_camera_onto_the_content()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var cam = scene.ActiveCamera;
        Assert.Equal(new Vector3(30f, 30f, -25f), cam.Target); // grid-default framing

        var layer = scene.AddCarriagePlane();
        layer.AddSegment(
            new LineSegment(new Vector3(100, 100, 0), new Vector3(140, 160, 0)), MotionType.Cut);
        scene.PumpCarriagePlanes();

        // The preset (still Isometric3D) re-applied against the content's
        // own bounds: center of the staged extents. Full framing, not just
        // the target — eye along the iso corner at radius * 2.2 and the
        // zoom basis untouched: mirrors ContentMetrics + SetViewPreset's
        // default branch, so a partial reframe that moved only Target must
        // fail here.
        Assert.Equal(new Vector3(120f, 130f, 0f), cam.Target);
        var center = new Vector3(120f, 130f, 0f); // the segment's midpoint
        var radius = MathF.Max(
            new Vector3(40f, 60f, 0f).Length() * 0.5f, 1f); // half the box diagonal
        Assert.Equal(
            center + Vector3.Normalize(new Vector3(-1f, -1f, 1f)) * (radius * 2.2f),
            cam.Position);
        Assert.Equal(100f, cam.Width);
    }

    /// <summary>The plan line "rebuild when bounds change": content staged
    /// far from the origin must move the grid and the job envelope onto it at
    /// the next pump. The FIRST arrival reframes once (Nex
    /// <c>_contentFramed</c>, checkpoint-4 item 1); every later rebuild is
    /// geometry-only — the camera never follows a bounds move again (that
    /// reframe was the checkpoint-3 zoom jump class).</summary>
    [GlFact]
    public void Chrome_follows_layer_bounds_and_only_the_first_arrival_frames()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);

        var layer = scene.AddCarriagePlane();
        layer.AddSegment(
            new LineSegment(new Vector3(100, 100, 0), new Vector3(140, 160, 0)), MotionType.Cut);
        scene.PumpCarriagePlanes();

        var cam = scene.ActiveCamera;
        var cameraAfterFirstFrame = (cam.Position, cam.Target, cam.Width);

        // Grid: the mill branch runs from machine (0,0) 20% past the model's
        // positive extent, at the content's own Z — not the ctor's -25 slab.
        var grid = scene.ChromeGridBounds;
        Assert.NotNull(grid);
        Assert.Equal(0f, grid.Value.MinX, 3);
        Assert.Equal(0f, grid.Value.MinY, 3);
        Assert.Equal(48f, grid.Value.MaxX, 3);   // span.X 40 * 1.2
        Assert.Equal(72f, grid.Value.MaxY, 3);   // span.Y 60 * 1.2
        Assert.Equal(-0.5f, grid.Value.MinZ, 3); // the content box's bottom

        // Envelope: the content box itself, wherever the content sits.
        var env = scene.ChromeEnvelope;
        Assert.NotNull(env);
        Assert.Equal(100f, env.Value.MinX, 3);
        Assert.Equal(140f, env.Value.MaxX, 3);
        Assert.Equal(160f, env.Value.MaxY, 3);
        Assert.Equal(-0.5f, env.Value.MinZ, 3);
        Assert.Equal(0.5f, env.Value.MaxZ, 3);

        // Content moves again: the chrome follows, the camera does not.
        layer.AddSegment(
            new LineSegment(new Vector3(200, 200, 0), new Vector3(240, 260, 0)), MotionType.Cut);
        scene.PumpCarriagePlanes();

        var movedGrid = scene.ChromeGridBounds;
        Assert.NotNull(movedGrid);
        Assert.Equal(168f, movedGrid.Value.MaxX, 3); // new span.X 140 * 1.2
        Assert.Equal(192f, movedGrid.Value.MaxY, 3); // new span.Y 160 * 1.2
        Assert.Equal(cameraAfterFirstFrame, (cam.Position, cam.Target, cam.Width));
    }

    /// <summary>Program reload to an empty run: the reload fence disposes the
    /// content and the chrome must fall back to the same grid-default bounds
    /// the ctor provisions (Nex's empty-chrome state).</summary>
    [GlFact]
    public void ResetBuild_returns_the_chrome_to_the_defaults()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var defaultGrid = scene.ChromeGridBounds;
        var defaultEnvelope = scene.ChromeEnvelope;

        var stale = scene.AddCarriagePlane();
        stale.AddSegment(
            new LineSegment(new Vector3(100, 100, 0), new Vector3(140, 160, 0)), MotionType.Cut);
        scene.PumpCarriagePlanes();
        Assert.NotEqual(defaultGrid, scene.ChromeGridBounds); // precondition: content moved it

        // The kinematic factory ends every build with CreateTool; the next
        // AddCarriagePlane runs the fence and starts the new run empty.
        scene.CreateTool(ToolShape.Cone);
        scene.AddCarriagePlane();
        scene.PumpCarriagePlanes();

        Assert.Equal(defaultGrid, scene.ChromeGridBounds);
        Assert.Equal(defaultEnvelope, scene.ChromeEnvelope);
    }

    /// <summary>Multi-plane (foam) runs: with two live carriage planes the
    /// grid switches to Nex's envelope-centered branch — pinned to the model's
    /// XY envelope like a work table — instead of the origin-corner table.</summary>
    [GlFact]
    public void Two_live_layers_pick_the_envelope_centered_grid()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);

        var top = scene.AddCarriagePlane();
        top.AddSegment(
            new LineSegment(new Vector3(100, 100, 0), new Vector3(140, 160, 0)), MotionType.Cut);
        var bottom = scene.AddCarriagePlane();
        bottom.AddSegment(
            new LineSegment(new Vector3(100, 100, 5), new Vector3(140, 160, 5)), MotionType.Cut);
        scene.PumpCarriagePlanes();

        var grid = scene.ChromeGridBounds;
        Assert.NotNull(grid);
        Assert.Equal(100f, grid.Value.MinX, 3);  // model min, not machine (0,0)
        Assert.Equal(140f, grid.Value.MaxX, 3);
        Assert.Equal(100f, grid.Value.MinY, 3);
        Assert.Equal(160f, grid.Value.MaxY, 3);
    }

    /// <summary>The rebuild latch must include the plane count: a second plane
    /// staged entirely inside the first plane's bounds does not move the
    /// aggregated (center, span) but still flips the grid to the multi-plane
    /// branch — without planeCount in the latch the chrome keeps the mill
    /// table for a 2-plane foam run.</summary>
    [GlFact]
    public void Second_plane_inside_the_first_planes_bounds_switches_the_grid_branch()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);

        var first = scene.AddCarriagePlane();
        first.AddSegment(
            new LineSegment(new Vector3(100, 100, 0), new Vector3(140, 160, 0)), MotionType.Cut);
        scene.PumpCarriagePlanes();

        var millGrid = scene.ChromeGridBounds;
        Assert.NotNull(millGrid);
        Assert.Equal(0f, millGrid.Value.MinX, 3); // precondition: one plane = mill table

        var second = scene.AddCarriagePlane();
        second.AddSegment(
            new LineSegment(new Vector3(110, 110, 0), new Vector3(120, 120, 0)), MotionType.Cut);
        scene.PumpCarriagePlanes();

        var grid = scene.ChromeGridBounds;
        Assert.NotNull(grid);
        Assert.Equal(100f, grid.Value.MinX, 3); // unchanged bounds, two planes: envelope-centered
        Assert.Equal(140f, grid.Value.MaxX, 3);
        Assert.Equal(100f, grid.Value.MinY, 3);
        Assert.Equal(160f, grid.Value.MaxY, 3);
    }

    /// <summary>Checkpoint-4 item 2: the step loop stops short of the far
    /// edge whenever (max − min) is not a multiple of the step, so the grid
    /// never closes its frame — there must still be a line at exactly
    /// <c>maxX</c> and one at exactly <c>maxY</c>.</summary>
    [Fact]
    public void Grid_far_edges_land_exactly_on_maxX_and_maxY_when_the_step_does_not()
    {
        // 10 % 3 and 7.5 % 3 are both non-zero: no step lands on either edge.
        var (minor, major) = GlPlotScene.BuildGridLines(
            new BoundingBox(0, 0, -1, 10, 7.5, 1), minor: 3, major: 6);
        var lines = minor.Concat(major).ToArray();

        Assert.True(VerticalLineAt(lines, 10f),
            "grid has no vertical line at exactly maxX (10)");
        Assert.True(HorizontalLineAt(lines, 7.5f),
            "grid has no horizontal line at exactly maxY (7.5)");
    }

    /// <summary>One line = 2 vertices × 7 floats (pos + color): verticals
    /// share X in both vertices, horizontals share Y.</summary>
    private static bool VerticalLineAt(float[] lines, float x)
    {
        for (var o = 0; o + 13 < lines.Length; o += 14)
            if (lines[o] == x && lines[o + 7] == x)
                return true;
        return false;
    }

    private static bool HorizontalLineAt(float[] lines, float y)
    {
        for (var o = 0; o + 13 < lines.Length; o += 14)
            if (lines[o + 1] == y && lines[o + 8] == y)
                return true;
        return false;
    }

    private static int LitPixels(byte[] px)
    {
        var lit = 0;
        for (var i = 0; i < px.Length; i += 4)
            if (px[i] != 32 || px[i + 1] != 35 || px[i + 2] != 40)
                lit++;
        return lit;
    }
}
