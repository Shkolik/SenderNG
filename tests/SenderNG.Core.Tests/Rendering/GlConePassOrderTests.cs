/*
 * GlConePassOrderTests.cs - checkpoint-8 order guard: the cone pass must be
 * the frame's last draw (Intel MSAA) + the 5e coverage check, unchanged
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

/// <summary>Task 5g (checkpoint-8): on Intel any in-frame GL_TRIANGLES draw
/// drops multisample coverage for every later draw until the next framebuffer
/// clear, so <see cref="GlPlotScene"/> must run its cone pass (the frame's
/// only triangle pass) AFTER the line passes. Mesa never shows the defect —
/// both orders render alike there — so the guard is structural: the
/// <c>LayersDrawnBeforeCones</c> one-shot latch is reset per
/// <see cref="GlPlotScene.RenderFrame"/> and fed only by the actual draw
/// methods (DrawLayers raises the observation, DrawAxisCones latches it), so
/// it reads true after a frame with content and false the moment anyone moves
/// the cones back ahead of the layers (the checkpoint-7 order). The second
/// test re-runs the 5e toolpath None-vs-X8 comparison on the same scene: the
/// reorder must not move Mesa's coverage numbers. Gated by
/// <see cref="GlFactAttribute"/>.</summary>
[Trait("Category", "GPU")]
public class GlConePassOrderTests
{
    private static readonly byte[] Background = [32, 35, 40, 255];
    private const int Size = 256;

    /// <summary>The 5e coverage floor for the toolpath's MSAA edge delta —
    /// the same threshold <c>GlMsaaCoverageTests</c> gates on, kept here so
    /// the reorder cannot quietly drain it.</summary>
    private const int ToolpathEdgeFloor = 100;

    /// <summary>The order guard: one scene driven through the full frame
    /// sequence — fresh scene (latch down by construction), a chrome-only
    /// frame whose layer pass early-returns (cones run, latch must stay
    /// down), then a frame with real toolpath content (latch must read true).
    /// The two negative phases are what keep the property from being
    /// tautological; move the cone pass back before <c>DrawLayers</c> and the
    /// final assert goes red.</summary>
    [GlFact]
    public unsafe void Cone_pass_runs_after_the_layer_pass_on_a_frame_with_content()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, Size, Size);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        scene.SetViewportSize(Size, Size);
        scene.SetViewPreset(ViewPreset.XY);

        Assert.False(scene.LayersDrawnBeforeCones,
            "the per-frame order latch must start each frame down — a " +
            "born-true property would guard nothing");

        // Chrome-only frame: DrawLayers early-returns (no layers yet) while
        // the cone pass still runs — layers-drew-before-cones cannot be true.
        Capture(rb, scene);
        Assert.False(scene.LayersDrawnBeforeCones,
            "no layer pass ran this frame, so the latch must not read true");

        var layer = (GlCarriagePlaneLayer)scene.AddCarriagePlane();
        layer.AddSegment(
            new LineSegment(new Vector3(-60, -60, 0), new Vector3(60, 60, 0)), MotionType.Cut);
        scene.PumpCarriagePlanes();

        var px = Capture(rb, scene);
        Assert.True(scene.LayersDrawnBeforeCones,
            "the cone pass must run after the layer pass — if this fails, " +
            "DrawAxisCones moved back ahead of DrawLayers (checkpoint-7's " +
            "order), which poisons the toolpath's MSAA coverage on Intel");

        // Precondition: the frame really drew content, so the latch above
        // did not observe two empty passes agreeing on nothing.
        var lit = LitPixels(px);
        Assert.True(lit > 100, $"precondition: toolpath must be visible, got {lit} lit");
        Console.WriteLine(
            $"[cone-order] layers-before-cones={scene.LayersDrawnBeforeCones} lit={lit}");
    }

    /// <summary>The 5e coverage check, kept through the reorder: the same
    /// chrome-off diagonal toolpath scene through the same readback at
    /// <see cref="MsaaMode.None"/> vs <see cref="MsaaMode.X8"/>. Mesa renders
    /// both cone orders alike, so the 5e numbers must hold — the toolpath's
    /// edge pixels change between the modes (MSAA reaches the stroke), every
    /// changed pixel is a partial-coverage (AA) edge, and the count clears
    /// the 5e floor.</summary>
    [GlFact]
    public unsafe void Toolpath_msaa_coverage_numbers_hold_after_the_cone_reorder()
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
            $"[msaa-order-guard] toolpath none-vs-x8 samples={rb.Samples}: {d}");

        // Precondition: the scene really drew before we compare it.
        Assert.True(d.LitA > 100, $"precondition: toolpath must be visible, got {d}");
        // The 5e decision gate: toolpath edge pixels differ between None and X8.
        Assert.True(d.Changed > 0,
            "decision gate: toolpath bytes are identical between None and X8 " +
            $"— MSAA does not reach the toolpath (defect branch). Evidence: {d}");
        // 99 % floor, not equality — cross-driver residue, see the note on
        // the GlMsaaCoverageTests toolpath gate: the delta is the stroke's
        // AA edge.
        Assert.True(d.EdgeChanged * 100 >= d.Changed * 99,
            $"changed pixels must be AA-edge changes within the 99 % floor: {d}");
        // The 5e perceptual coverage floor: a real count of changed edges.
        Assert.True(d.Changed >= ToolpathEdgeFloor,
            $"toolpath MSAA edge delta below the coverage floor: {d}");
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

    /// <summary>The non-background (lit) pixel count of a captured frame.</summary>
    private static int LitPixels(byte[] px)
    {
        var lit = 0;
        for (var i = 0; i < px.Length; i += 4)
        {
            if (px[i] == Background[0] && px[i + 1] == Background[1]
                && px[i + 2] == Background[2])
                continue;
            lit++;
        }
        return lit;
    }

    /// <summary>The checkpoint numbers for one None-vs-X8 frame pair —
    /// verbatim from <c>GlMsaaCoverageTests</c> (5e), so the evidence line
    /// reads directly against the recorded 5e numbers.</summary>
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
            // Same edge-like clause as GlMsaaCoverageTests.Diff: a mode's
            // sample grid can fully cover a pixel whose centre sample the
            // other mode missed — the stroke boundary moving by sub-pixel
            // sampling, not an interior change.
            if (partialA || partialB || bgA != bgB)
                edgeChanged++;
        }
        return new FrameDiff(changed, maxDelta, litA, litB, edgeChanged);
    }

    /// <summary>The solid (fully-covered) line colors in a frame — verbatim
    /// from <c>GlMsaaCoverageTests</c> (5e).</summary>
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
