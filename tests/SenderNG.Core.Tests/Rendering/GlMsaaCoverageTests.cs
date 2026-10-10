/*
 * GlMsaaCoverageTests.cs - checkpoint-6 evidence: MSAA changes toolpath line
 * pixels the same way it changes chrome line pixels
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

/// <summary>Checkpoint-6 evidence round (Step 5e.1/5e.2): the user report says
/// the MSAA selector changes chrome but not the toolpath. Structurally both
/// draw through the same fat-line program into the same <see cref="GlReadback"/>
/// FBO, so these tests settle it with pixels: the same fixed scene rendered
/// through one readback at <see cref="MsaaMode.None"/> and
/// <see cref="MsaaMode.X8"/> (sample count clamped by
/// <see cref="GlReadback"/> to the driver's <c>GL_MAX_SAMPLES</c>), then a
/// byte comparison that counts differing pixels, the max per-channel delta and
/// how many of those changes sit on partial-coverage (anti-aliased edge)
/// pixels. The runtime-switch test covers the per-tick
/// <c>SetMsaa</c> application wiring. Gated by <see cref="GlFactAttribute"/>;
/// the evidence lines (<c>[msaa-evidence]</c>) are the checkpoint numbers.</summary>
[Trait("Category", "GPU")]
public class GlMsaaCoverageTests
{
    private static readonly byte[] Background = [32, 35, 40, 255];
    private const int Size = 256;

    /// <summary>Coverage floors for the perceptual-branch assertion (Step 5e.2):
    /// both the 3.2-px toolpath and the 1.6-px chrome must show a real count of
    /// MSAA-changed edge pixels — MSAA reaches both. Sized from the observed
    /// llvmpipe evidence with margin, not from the exact driver count.</summary>
    private const int ToolpathEdgeFloor = 100;
    private const int ChromeEdgeFloor = 100;

    /// <summary>Step 5e.1 + decision gate: one diagonal toolpath segment (the
    /// real <see cref="GlCarriagePlaneLayer"/>, 3.2 palette width) rendered
    /// chrome-off through the same readback at None vs X8. The gate: toolpath
    /// edge pixels differ between the modes → MSAA does apply to the toolpath
    /// (perceptual finding); toolpath bytes identical → defect branch.</summary>
    [GlFact]
    public unsafe void Toolpath_edge_pixels_change_between_none_and_x8()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, Size, Size);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        scene.SetViewportSize(Size, Size);
        var layer = (GlCarriagePlaneLayer)scene.AddCarriagePlane();
        layer.AddSegment(
            new LineSegment(new Vector3(-60, -60, 0), new Vector3(60, 60, 0)), MotionType.Cut);
        scene.PumpCarriagePlanes();
        scene.SetViewPreset(ViewPreset.XY);
        // Chrome off: only the toolpath can light pixels.
        scene.SetView(new PlotterGlSettings
        {
            ShowOrigin = false,
            ShowEnvelope = false,
            ShowGrid = false,
        });

        var none = Capture(rb, scene);
        rb.SetMsaa(MsaaMode.X8);
        Assert.Equal(MsaaMode.X8, rb.Msaa);
        var x8 = Capture(rb, scene);
        var d = Diff(none, x8);

        Console.WriteLine(
            $"[msaa-evidence] toolpath none-vs-x8 samples={rb.Samples}: {d}");

        // Precondition: the scene really drew before we compare it.
        Assert.True(d.LitA > 100, $"precondition: toolpath must be visible, got {d}");
        // Step 5e.2 gate: toolpath edge pixels differ between None and X8.
        Assert.True(d.Changed > 0,
            "decision gate: toolpath bytes are identical between None and X8 " +
            $"— MSAA does not reach the toolpath (defect branch). Evidence: {d}");
        // Every changed pixel carries partial (anti-aliased) coverage in at
        // least one mode: the delta is the stroke's AA edge — never a change
        // inside flat background or solid line interior.
        Assert.Equal(d.Changed, d.EdgeChanged);
        // Perceptual branch coverage floor: a real count of changed edges.
        Assert.True(d.Changed >= ToolpathEdgeFloor,
            $"toolpath MSAA edge delta below the coverage floor: {d}");
    }

    /// <summary>The chrome side of the same comparison: the isometric grid's
    /// diagonal lines at the Nex 1.6 palette width, None vs X8 through the
    /// same readback. Supplies the chrome edge numbers the report quantifies
    /// against the toolpath.</summary>
    [GlFact]
    public unsafe void Chrome_edge_pixels_change_between_none_and_x8()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, Size, Size);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        scene.SetViewportSize(Size, Size);
        scene.SetViewPreset(ViewPreset.Isometric3D);
        scene.SetGrid(new BoundingBox(-50, -50, -1, 50, 50, 1), 10, 50);
        // Grid only: envelope and axes have no geometry here, the gates keep
        // the comparison on the 1.6-px chrome piece.
        scene.SetView(new PlotterGlSettings
        {
            ShowOrigin = false,
            ShowEnvelope = false,
            ShowGrid = true,
        });

        var none = Capture(rb, scene);
        rb.SetMsaa(MsaaMode.X8);
        Assert.Equal(MsaaMode.X8, rb.Msaa);
        var x8 = Capture(rb, scene);
        var d = Diff(none, x8);

        Console.WriteLine(
            $"[msaa-evidence] chrome none-vs-x8 samples={rb.Samples}: {d}");

        Assert.True(d.LitA > 100, $"precondition: chrome must be visible, got {d}");
        Assert.True(d.Changed > 0,
            $"chrome bytes identical between None and X8. Evidence: {d}");
        Assert.Equal(d.Changed, d.EdgeChanged);
        Assert.True(d.Changed >= ChromeEdgeFloor,
            $"chrome MSAA edge delta below the coverage floor: {d}");
    }

    /// <summary>The per-tick application wiring (Step 5e.2's runtime-switch
    /// assert): the SAME readback and the same diagonal toolpath scene through
    /// SetMsaa(None → X8 → None). X8 must change the frame's bytes versus the
    /// plain path (the selector reaches this exact scene), and back to None
    /// must land byte-identical on the original plain frame (no MSAA
    /// residue).</summary>
    [GlFact]
    public unsafe void Runtime_msaa_round_trip_changes_the_diagonal_scene_bytes()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, Size, Size);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        scene.SetViewportSize(Size, Size);
        var layer = (GlCarriagePlaneLayer)scene.AddCarriagePlane();
        layer.AddSegment(
            new LineSegment(new Vector3(-60, -60, 0), new Vector3(60, 60, 0)), MotionType.Cut);
        scene.PumpCarriagePlanes();
        scene.SetViewPreset(ViewPreset.XY);
        scene.SetView(new PlotterGlSettings
        {
            ShowOrigin = false,
            ShowEnvelope = false,
            ShowGrid = false,
        });

        var noneBefore = Capture(rb, scene);
        rb.SetMsaa(MsaaMode.X8);
        var samples = rb.Samples;
        var x8 = Capture(rb, scene);
        rb.SetMsaa(MsaaMode.None);
        var noneAfter = Capture(rb, scene);

        var d = Diff(noneBefore, x8);
        Console.WriteLine(
            $"[msaa-evidence] runtime round-trip none->x8->none samples={samples}: {d}");

        Assert.True(d.Changed > 0,
            "runtime switch: SetMsaa(None → X8) did not change the diagonal " +
            $"scene's bytes. Evidence: {d}");
        Assert.Equal(d.Changed, d.EdgeChanged);
        // None round-trips byte-identical: the plain path rebuilds clean.
        Assert.Equal(noneBefore, noneAfter);
    }

    /// <summary>One frame the way the view tick does it: clear →
    /// RenderFrame into the bound FBO → CopyFrame (resolve + glReadPixels).</summary>
    private static unsafe byte[] Capture(GlReadback rb, GlPlotScene scene)
    {
        rb.BeginFrame(Background);
        scene.RenderFrame(Size, Size);
        var px = new byte[Size * Size * 4];
        fixed (byte* p = px)
            Assert.True(rb.CopyFrame((nint)p, Size * 4));
        return px;
    }

    /// <summary>The checkpoint numbers for one None-vs-X8 frame pair:
    /// differing pixels, the largest per-channel delta, each mode's lit
    /// (non-background) footprint, and how many of the differing pixels are
    /// partial-coverage in at least one mode (the AA-edge changes).</summary>
    private static FrameDiff Diff(byte[] a, byte[] b)
    {
        var solids = SolidColors(a);
        var changed = 0;
        var edgeChanged = 0;
        var maxDelta = 0;
        var litA = 0;
        var litB = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            var bgA = IsBackground(a, i);
            var bgB = IsBackground(b, i);
            if (!bgA) litA++;
            if (!bgB) litB++;

            var delta = 0;
            for (var c = 0; c < 3; c++)
            {
                var d = Math.Abs(a[i + c] - b[i + c]);
                if (d > delta) delta = d;
            }
            if (delta == 0)
                continue;
            changed++;
            if (delta > maxDelta) maxDelta = delta;
            var partialA = !bgA && !solids.Contains(Pack(a, i));
            var partialB = !bgB && !solids.Contains(Pack(b, i));
            if (partialA || partialB)
                edgeChanged++;
        }
        return new FrameDiff(changed, maxDelta, litA, litB, edgeChanged);
    }

    /// <summary>The solid (fully-covered) line colors in a frame: every
    /// non-background color covering at least 2 % of the lit footprint — the
    /// line interiors. Anti-aliased edge blends are strictly between the
    /// background and a solid, spread thin over distinct values, so they never
    /// reach that share.</summary>
    private static HashSet<int> SolidColors(byte[] px)
    {
        var counts = new Dictionary<int, int>();
        var lit = 0;
        for (var i = 0; i < px.Length; i += 4)
        {
            if (IsBackground(px, i))
                continue;
            lit++;
            var key = Pack(px, i);
            counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
        }
        var floor = Math.Max(1, lit / 50);
        var solids = new HashSet<int>();
        foreach (var (key, n) in counts)
            if (n >= floor)
                solids.Add(key);
        return solids;
    }

    private static bool IsBackground(byte[] px, int i) =>
        px[i] == Background[0] && px[i + 1] == Background[1] && px[i + 2] == Background[2];

    private static int Pack(byte[] px, int i) =>
        (px[i] << 16) | (px[i + 1] << 8) | px[i + 2];

    private readonly record struct FrameDiff(
        int Changed, int MaxDelta, int LitA, int LitB, int EdgeChanged)
    {
        public override string ToString() =>
            $"changed={Changed} maxDelta={MaxDelta} litNone={LitA} litX8={LitB} " +
            $"edgeChanged={EdgeChanged}";
    }
}
