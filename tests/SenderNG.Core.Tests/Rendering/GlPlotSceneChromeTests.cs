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

using SenderNG.Modules.Common.Render;
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
        scene.AddAxisArrows(
        [
            (Vector3.Zero, Vector3.UnitX, "X"),
            (Vector3.Zero, Vector3.UnitY, "Y"),
            (Vector3.Zero, Vector3.UnitZ, "Z"),
        ]);
        scene.SetJobEnvelope(new BoundingBox(0, 0, 0, 100, 100, 10));
        rb.BeginFrame(Background);
        scene.RenderFrame(256, 256);
        var px = new byte[256 * 256 * 4];
        fixed (byte* p = px)
            Assert.True(rb.CopyFrame((nint)p, 256 * 4));
        var lit = LitPixels(px);
        Assert.True(lit > 50, $"expected grid/axes/envelope pixels, got {lit}");
    }

    /// <summary>Checkpoint-5 item 3: the chrome pieces draw with PER-PIECE
    /// thicknesses — grid 1.6, axis arrows 3.6, job envelope 1.6 — not one
    /// shared width for every piece. Observed through the width seam the
    /// palette tests use: what each piece group actually pushed as
    /// <c>uLineWidthPx</c> this frame.</summary>
    [GlFact]
    public void Chrome_pieces_draw_at_their_palette_widths()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, 256, 256);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        scene.SetViewportSize(256, 256);
        scene.SetViewPreset(ViewPreset.Isometric3D);
        scene.SetGrid(new BoundingBox(-50, -50, -1, 50, 50, 1), 10, 50);
        scene.AddAxisArrows(
        [
            (Vector3.Zero, Vector3.UnitX, "X"),
            (Vector3.Zero, Vector3.UnitY, "Y"),
            (Vector3.Zero, Vector3.UnitZ, "Z"),
        ]);
        scene.SetJobEnvelope(new BoundingBox(0, 0, 0, 100, 100, 10));

        rb.BeginFrame(Background);
        scene.RenderFrame(256, 256);

        var (grid, envelope, axes) = scene.ChromeWidthsPushed;
        Assert.Equal(1.6f, grid);     // grid node thickness
        Assert.Equal(1.6f, envelope); // job-envelope node thickness
        Assert.Equal(3.6f, axes);     // axis-arrows node thickness
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
        scene.AddAxisArrows(
        [
            (Vector3.Zero, Vector3.UnitX, "X"),
            (Vector3.Zero, Vector3.UnitY, "Y"),
            (Vector3.Zero, Vector3.UnitZ, "Z"),
        ]);
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

    /// <summary>Checkpoint-4 item 1 (the <c>_contentFramed</c> latch): the
    /// constructor frames the camera once against empty content, so a model
    /// that lands afterwards must trigger exactly one reframe onto its bounds
    /// — the initial view is the empty framing (center origin), so the
    /// first arrival lands on the content's own bounds.</summary>
    [GlFact]
    public void First_content_arrival_frames_the_camera_onto_the_content()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var cam = scene.ActiveCamera;
        Assert.Equal(new Vector3(0f), cam.Target); // empty-state framing: ±25 fallback, origin center

        var layer = scene.AddCarriagePlane();
        layer.AddSegment(
            new LineSegment(new Vector3(100, 100, 0), new Vector3(140, 160, 0)), MotionType.Cut);
        scene.PumpCarriagePlanes();

        // The preset (still Isometric3D) re-applied against the content's
        // own bounds: center of the staged extents. Full framing, not just
        // the target — eye along the iso corner at radius * 2.2 and the
        // zoom basis untouched: mirrors ContentMetrics + SetViewPreset's
        // default branch, so a partial reframe that moved only Target must
        // fail here. The eye stays in the negative X/Y, positive Z octant;
        // the grid is moved to the far side instead (FarSideGridSign), which
        // is what keeps the axis orientation of this view unchanged.
        Assert.Equal(new Vector3(120f, 130f, 0f), cam.Target);
        var center = new Vector3(120f, 130f, 0f); // the segment's midpoint
        var radius = MathF.Max(
            new Vector3(40f, 60f, 0f).Length() * 0.5f, 1f); // half the box diagonal
        Assert.Equal(
            center + Vector3.Normalize(new Vector3(-1f, -1f, 1f)) * (radius * 2.2f),
            cam.Position);
        Assert.Equal(Vector3.Normalize(new Vector3(0.5f, 0.5f, 1f)), cam.Up);
        Assert.Equal(100f, cam.Width);
    }

    /// <summary>The plan line "rebuild when bounds change": content staged
    /// far from the origin must move the grid and the job envelope onto it at
    /// the next pump. The FIRST arrival reframes once (the
    /// <c>_contentFramed</c> latch, checkpoint-4 item 1); every later rebuild is
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
    /// content and the chrome must fall back to the empty-state reference grid
    /// the ctor provisions (<c>BuildEmptyChrome</c>).</summary>
    [GlFact]
    public void ResetBuild_returns_the_chrome_to_the_defaults()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var defaultGrid = scene.ChromeGridBounds;
        var defaultEnvelope = scene.ChromeEnvelope;
        Assert.NotNull(defaultGrid);                       // ctor provisions the empty chrome
        Assert.Equal(-50f, defaultGrid.Value.MinX, 3);
        Assert.Equal(-50f, defaultGrid.Value.MinY, 3);
        Assert.Equal(0f, defaultGrid.Value.MinZ, 3);
        Assert.Equal(50f, defaultGrid.Value.MaxX, 3);
        Assert.Equal(50f, defaultGrid.Value.MaxY, 3);
        Assert.Equal(0f, defaultGrid.Value.MaxZ, 3);
        Assert.Null(defaultEnvelope);                      // no envelope without a program

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
        Assert.Null(scene.ChromeEnvelope);
        Assert.Equal((2d, 10d), scene.ChromeGridSteps);
    }

    /// <summary>The no-program state: a fixed
    /// −50..+50 reference grid at Z=0 (10×10 major cells of 10, minor 2),
    /// no job envelope, and the camera framed from the ±25 fallback so the
    /// view centers on the origin with the default fit.</summary>
    [GlFact]
    public void Empty_state_chrome_is_the_reference_grid()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);

        var grid = scene.ChromeGridBounds;
        Assert.NotNull(grid);
        Assert.Equal(-50f, grid.Value.MinX, 3);
        Assert.Equal(-50f, grid.Value.MinY, 3);
        Assert.Equal(0f, grid.Value.MinZ, 3);
        Assert.Equal(50f, grid.Value.MaxX, 3);
        Assert.Equal(50f, grid.Value.MaxY, 3);
        Assert.Equal(0f, grid.Value.MaxZ, 3);
        Assert.Equal((2d, 10d), scene.ChromeGridSteps);
        Assert.Null(scene.ChromeEnvelope);

        var cam = scene.ActiveCamera;
        Assert.Equal(new Vector3(0f), cam.Target);
        Assert.Equal(100f, scene.ZoomPercent, 3); // fresh scene sits at its fit
    }

    /// <summary>The latch must discriminate the empty state from content whose
    /// aggregated bbox happens to equal the ±25 fallback: after a reset the
    /// empty chrome is latched with the live plane count already at N, so a
    /// first arrival whose bounds are exactly −25..+25 collides with that
    /// latch on (center, span, PlaneCount) — without HasContent in the latch
    /// the rebuild early-returns and the empty chrome (no envelope) sticks.</summary>
    [GlFact]
    public void First_content_equal_to_the_fallback_box_still_rebuilds_after_reset()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var defaultGrid = scene.ChromeGridBounds;

        // Reset while a layer is already live: the pump after the reload
        // fence records the empty-state latch with PlaneCount = 1 — the
        // count the collision needs (same sequence as the reset test).
        var stale = scene.AddCarriagePlane();
        stale.AddSegment(
            new LineSegment(new Vector3(100, 100, 0), new Vector3(140, 160, 0)), MotionType.Cut);
        scene.PumpCarriagePlanes();
        scene.CreateTool(ToolShape.Cone);
        var fresh = scene.AddCarriagePlane();
        scene.PumpCarriagePlanes();
        Assert.Null(scene.ChromeEnvelope); // precondition: empty chrome latched

        // First content whose bbox is exactly the ±25 fallback: (center,
        // span, PlaneCount) all collide with the empty latch — only
        // HasContent tells the states apart. The camera reframe is not
        // observable here (fallback and content share the same framing), so
        // the envelope's return from null is the rebuild's signature.
        fresh.AddSegment(
            new LineSegment(new Vector3(-25f), new Vector3(25f)), MotionType.Cut);
        scene.PumpCarriagePlanes();

        Assert.NotNull(scene.ChromeEnvelope);
        Assert.NotEqual(defaultGrid, scene.ChromeGridBounds); // content-state grid, not the reference one
    }

    /// <summary>Multi-plane (foam) runs: with two live carriage planes the
    /// grid switches to the envelope-centered branch — pinned to the model's
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

    /// <summary>The grid plane's normal axis and sign: a machine whose grid
    /// falls back to the world-YZ plane draws it past the content's X-min —
    /// and past its X-max when the driving axis is inverted.</summary>
    [Fact]
    public void Grid_lines_offset_along_the_normal_axis_by_sign()
    {
        var bounds = new BoundingBox(-20, -30, -10, 20, 30, 10);

        var (minorLow, majorLow) = GlPlotScene.BuildGridLines(
            bounds, minor: 10, major: 20, normalAxis: 0, normalSign: 1);
        var low = minorLow.Concat(majorLow).ToArray();
        Assert.True(low.Length > 0);
        Assert.All(VertexOffsets(low), o => Assert.Equal(-20.5f, low[o]));

        var (minorHigh, majorHigh) = GlPlotScene.BuildGridLines(
            bounds, minor: 10, major: 20, normalAxis: 0, normalSign: -1);
        var high = minorHigh.Concat(majorHigh).ToArray();
        Assert.True(high.Length > 0);
        Assert.All(VertexOffsets(high), o => Assert.Equal(20.5f, high[o]));
    }

    /// <summary>The grid spans the two world axes around its normal — the
    /// YZ-plane grid covers the bounds' Y and Z extents, not X's.</summary>
    [Fact]
    public void Grid_lines_span_the_two_world_axes_around_the_normal()
    {
        var bounds = new BoundingBox(-20, -30, -10, 20, 30, 10);
        var (minor, major) = GlPlotScene.BuildGridLines(
            bounds, minor: 10, major: 20, normalAxis: 0, normalSign: 1);
        var lines = minor.Concat(major).ToArray();

        var ys = VertexOffsets(lines).Select(o => lines[o + 1]).ToArray();
        var zs = VertexOffsets(lines).Select(o => lines[o + 2]).ToArray();
        Assert.Equal(-30f, ys.Min(), 3);
        Assert.Equal(30f, ys.Max(), 3);
        Assert.Equal(-10f, zs.Min(), 3);
        Assert.Equal(10f, zs.Max(), 3);
    }

    /// <summary>AutoGridBounds in a non-default plane: the origin-corner and
    /// envelope-centered rules move onto the span axes, and the normal axis
    /// carries the caller's content end.</summary>
    [Fact]
    public void Auto_grid_bounds_follow_the_normal_axis()
    {
        var center = new Vector3(50f, 60f, 5f);
        var span = new Vector3(40f, 60f, 10f);

        var mill = GlPlotScene.AutoGridBounds(center, span, planeCount: 1, normalAxis: 0, endOnNormal: -0.5f);
        Assert.Equal(-0.5f, mill.MinX, 3);
        Assert.Equal(-0.5f, mill.MaxX, 3);
        Assert.Equal(0f, mill.MinY, 3);   // origin-corner table on the span axes
        Assert.Equal(72f, mill.MaxY, 3);  // span.Y 60 * 1.2
        Assert.Equal(0f, mill.MinZ, 3);
        Assert.Equal(12f, mill.MaxZ, 3);  // span.Z 10 * 1.2

        var foam = GlPlotScene.AutoGridBounds(center, span, planeCount: 2, normalAxis: 0, endOnNormal: 3f);
        Assert.Equal(3f, foam.MinX, 3);
        Assert.Equal(3f, foam.MaxX, 3);
        Assert.Equal(30f, foam.MinY, 3);  // envelope-centered: 60 ± 30
        Assert.Equal(90f, foam.MaxY, 3);
        Assert.Equal(0f, foam.MinZ, 3);   // 5 ± 5
        Assert.Equal(10f, foam.MaxZ, 3);
    }

    /// <summary>SetMachineChrome re-provisions the chrome on the next pump —
    /// the metrics themselves did not move, so this is the latch clear. The
    /// empty-state reference grid reorients into the machine's plane.</summary>
    [GlFact]
    public void Set_machine_chrome_reorients_the_empty_grid()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);

        scene.SetMachineChrome(
        [
            (Vector3.Zero, Vector3.UnitX, "?"),
            (Vector3.Zero, Vector3.UnitY, "X"),
            (Vector3.Zero, Vector3.UnitZ, "Y"),
        ], gridNormalAxis: 0, gridNormalSign: 1);
        scene.PumpCarriagePlanes();

        var grid = scene.ChromeGridBounds;
        Assert.NotNull(grid);
        Assert.Equal(0f, grid.Value.MinX, 3);     // the normal axis stays at the origin
        Assert.Equal(0f, grid.Value.MaxX, 3);
        Assert.Equal(-50f, grid.Value.MinY, 3);   // the field reorients onto Y × Z
        Assert.Equal(50f, grid.Value.MaxY, 3);
        Assert.Equal(-50f, grid.Value.MinZ, 3);
        Assert.Equal(50f, grid.Value.MaxZ, 3);
    }

    /// <summary>The orbit-pivot marker lifecycle: the translucent amber
    /// sphere is up exactly while an orbit gesture is open (RMB), a preset
    /// switch cancels it, an ortho preset refuses the gesture, and the radius
    /// tracks max(0.5, contentRadius * 0.05) from the framing metrics.</summary>
    [GlFact]
    public void Orbit_pivot_sphere_tracks_the_gesture_and_sizes_to_the_content()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);

        // Fresh scene: no gesture, no marker; the ctor preset seeded the radius
        // from the ±25 fallback (radius 43.301 → 2.165).
        Assert.False(scene.OrbitSphereVisible);
        var expectedRadius = MathF.Max(
            0.5f, MathF.Max(new Vector3(50f).Length() * 0.5f, 1f) * 0.05f);
        Assert.Equal(expectedRadius, scene.OrbitSphereRadius, 4);

        scene.BeginOrbit(new Vector2(10, 10));
        Assert.True(scene.OrbitSphereVisible); // RMB down: marker at the pivot

        scene.EndOrbit();
        Assert.False(scene.OrbitSphereVisible); // release: marker gone

        scene.BeginOrbit(new Vector2(10, 10));
        Assert.True(scene.OrbitSphereVisible);
        scene.SetViewPreset(ViewPreset.XY); // preset switch cancels the marker
        Assert.False(scene.OrbitSphereVisible);

        // Ortho is rotation-locked: BeginOrbit must refuse and keep it hidden.
        scene.BeginOrbit(new Vector2(10, 10));
        Assert.False(scene.OrbitSphereVisible);
    }

    /// <summary>The marker on the pixel path: chrome toggles off, so the only
    /// thing that can light the frame during an orbit is the sphere — and the
    /// frame must go dark again the moment the gesture ends.</summary>
    [GlFact]
    public unsafe void Orbit_pivot_sphere_lights_pixels_during_the_gesture()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, 256, 256);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        scene.SetViewportSize(256, 256);
        scene.SetViewPreset(ViewPreset.Isometric3D);
        scene.SetView(new PlotterGlSettings
        {
            ShowOrigin = false,
            ShowEnvelope = false,
            ShowGrid = false,
        });
        var px = new byte[256 * 256 * 4];

        rb.BeginFrame(Background);
        scene.RenderFrame(256, 256);
        fixed (byte* p = px)
            Assert.True(rb.CopyFrame((nint)p, 256 * 4));
        Assert.Equal(0, LitPixels(px)); // precondition: chrome off, no gesture

        scene.BeginOrbit(new Vector2(128, 128));
        rb.BeginFrame(Background);
        scene.RenderFrame(256, 256);
        fixed (byte* p = px)
            Assert.True(rb.CopyFrame((nint)p, 256 * 4));
        var lit = LitPixels(px);
        Assert.True(lit > 30, $"expected orbit-pivot sphere pixels, got {lit}");

        scene.EndOrbit();
        rb.BeginFrame(Background);
        scene.RenderFrame(256, 256);
        fixed (byte* p = px)
            Assert.True(rb.CopyFrame((nint)p, 256 * 4));
        Assert.Equal(0, LitPixels(px)); // marker leaves with the gesture
    }

    /// <summary>Offsets of the interleaved line vertices (pos 3 + color 4)
    /// inside a line buffer.</summary>
    private static IEnumerable<int> VertexOffsets(float[] lines)
    {
        for (var o = 0; o + 6 < lines.Length; o += 7)
            yield return o;
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
