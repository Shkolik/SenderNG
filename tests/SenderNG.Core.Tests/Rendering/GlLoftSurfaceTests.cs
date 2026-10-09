/*
 * GlLoftSurfaceTests.cs - Task 7 loft sheet: BuildSheet quad-strip semantics
 * ported from Nex's NexLoftSurface
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

/// <summary>Task 7: <see cref="GlLoftSurface"/> ports
/// <c>NexLoftSurface.BuildSheet</c> (lines 140-185) verbatim — one quad per
/// consecutive pair with 4 unshared vertices and 6 indices each, the two
/// degenerate-pair skip rules, the count-mismatch throw, and the empty-sheet
/// cases — and <see cref="GlPlotScene"/> draws that sheet only under the
/// <c>ShowLoft</c> gate, after the cone pass, blended over what is behind it.
/// Gated by <see cref="GlFactAttribute"/> like the other GPU
/// tests.</summary>
[Trait("Category", "GPU")]
public class GlLoftSurfaceTests
{
    private static readonly byte[] Background = [32, 35, 40, 255];
    private const int Size = 256;

    [GlFact]
    public void SetLoftSheet_builds_a_quad_strip_between_the_rows()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var loft = (GlLoftSurface)scene.AddLoftSurface();
        var a = Enumerable.Range(0, 8).Select(i => new Vector3(i, 0, 0)).ToArray();
        var b = Enumerable.Range(0, 8).Select(i => new Vector3(i, 0, 5)).ToArray();
        loft.SetLoftSheet(a, b);
        Assert.Equal(7 * 4, loft.VertexCount);   // 7 quads x 4 verts (indexed)
        Assert.Equal(7 * 6, loft.IndexCount);    // 7 quads x 6 indices
        Assert.True(loft.HasGeometry);
    }

    [GlFact]
    public void SetLoftSheet_rejects_mismatched_sides()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var loft = (GlLoftSurface)scene.AddLoftSurface();
        var a = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0) };
        var b = new[] { new Vector3(0, 5, 0) };
        Assert.Throws<ArgumentException>(() => loft.SetLoftSheet(a, b));
    }

    [GlFact]
    public void Fewer_than_two_points_yields_no_geometry()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var loft = (GlLoftSurface)scene.AddLoftSurface();
        loft.SetLoftSheet([], []);
        Assert.False(loft.HasGeometry);
        loft.SetLoftSheet([new Vector3(1, 2, 3)], [new Vector3(4, 5, 6)]);
        Assert.False(loft.HasGeometry);
    }

    [GlFact]
    public void Degenerate_pairs_are_skipped()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var loft = (GlLoftSurface)scene.AddLoftSurface();
        // Skip rule 1 (Nex BuildSheet): both sides stationary across the pair.
        loft.SetLoftSheet(
            [new Vector3(0, 0, 0), new Vector3(0, 0, 0)],
            [new Vector3(5, 0, 0), new Vector3(5, 0, 0)]);
        Assert.False(loft.HasGeometry);
        // Skip rule 2: sideB coincides with sideA.
        var same = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(2, 0, 0) };
        loft.SetLoftSheet(same, same);
        Assert.False(loft.HasGeometry);
    }

    /// <summary>The <c>ShowLoft</c> runtime gate: the sheet's one-shot
    /// observation latch (the established order-guard pattern) stays down on a
    /// frame drawn with the toggle off and reads true on the next frame with
    /// it on — geometry unchanged in between, so the difference is the gate
    /// alone.</summary>
    [GlFact]
    public void DrawLoft_runs_only_when_ShowLoft_is_on()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, Size, Size);
        using var scene = new GlPlotScene(ctx);
        var loft = (GlLoftSurface)scene.AddLoftSurface();
        // Worldwide sheet at z=50 (above everything in the default XY view):
        var a = Enumerable.Range(0, 5).Select(i => new Vector3(i * 500f - 1000f, -1000f, 50f)).ToArray();
        var b = Enumerable.Range(0, 5).Select(i => new Vector3(i * 500f - 1000f, 1000f, 50f)).ToArray();
        loft.SetLoftSheet(a, b);
        scene.SetView(new PlotterGlSettings { ShowLoft = false });
        Capture(rb, scene);
        Assert.False(scene.LoftDrawnThisFrame);
        scene.SetView(new PlotterGlSettings { ShowLoft = true });
        Capture(rb, scene);
        Assert.True(scene.LoftDrawnThisFrame);
    }

    /// <summary>The checkpoint-8/9 ordering rule extended to the frame's
    /// blend pass: the loft draws only after the cone pass ran, so the sheet
    /// lands after every line pass (5g rule) with only triangle draws
    /// following it.</summary>
    [GlFact]
    public void DrawLoft_runs_after_the_cone_pass()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, Size, Size);
        using var scene = new GlPlotScene(ctx);
        var loft = (GlLoftSurface)scene.AddLoftSurface();
        var a = Enumerable.Range(0, 5).Select(i => new Vector3(i * 500f - 1000f, -1000f, 50f)).ToArray();
        var b = Enumerable.Range(0, 5).Select(i => new Vector3(i * 500f - 1000f, 1000f, 50f)).ToArray();
        loft.SetLoftSheet(a, b);
        scene.SetView(new PlotterGlSettings { ShowOrigin = true, ShowLoft = true });
        Capture(rb, scene);
        Assert.True(scene.LoftDrawnAfterConePass);
    }

    /// <summary>The alpha proof: the sheet draws at 0.35 opacity over the
    /// same scene without it, and the center pixel lands exactly on the
    /// expected <c>0.35 * src + 0.65 * dst</c> blend result. This is the test
    /// that catches <c>MeshFragment</c> regressing back to a hardcoded 1.0
    /// alpha (the geometry half above cannot see that).</summary>
    [GlFact]
    public void Sheet_blends_over_what_is_behind_it()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, Size, Size);
        using var scene = new GlPlotScene(ctx);
        var loft = (GlLoftSurface)scene.AddLoftSurface();
        var a = Enumerable.Range(0, 5).Select(i => new Vector3(i * 500f - 1000f, -1000f, 50f)).ToArray();
        var b = Enumerable.Range(0, 5).Select(i => new Vector3(i * 500f - 1000f, 1000f, 50f)).ToArray();
        loft.SetLoftSheet(a, b);
        // The default preset is Isometric3D, and under that framing the
        // camera sits BELOW z=50 — the sheet then draws zero pixels and there
        // is nothing to blend. Snap to XY (the view this test's math
        // assumes, per the brief): the camera looks down from above z=50 and
        // the sheet covers the center pixel.
        scene.SetViewPreset(ViewPreset.XY);

        const int cx = 128, cy = 128;                    // 256x256 center pixel
        var i0 = (cy * 256 + cx) * 4;

        scene.SetView(new PlotterGlSettings { ShowLoft = false });
        var dst = Capture(rb, scene);                    // what is behind the sheet

        scene.SetView(new PlotterGlSettings { ShowLoft = true });
        var outPx = Capture(rb, scene);                  // sheet blended over it

        // Flat normal of this quad is (0,0,-1) (SafeNormal of b0-a0 x b1-a0),
        // so vShade = max(dot((0,0,-1), normalize(LightDirection)), 0) * 0.7 + 0.3
        // with LightDirection = (0.4, 0.8, 0.5) (GlPlotScene.cs:71) -> 0.3.
        var src = new[] { 0.42f * 0.3f * 255f, 0.42f * 0.3f * 255f, 0.54f * 0.3f * 255f };
        for (var c = 0; c < 3; c++)
        {
            var expected = 0.35f * src[c] + 0.65f * dst[i0 + c];
            Assert.InRange(outPx[i0 + c], expected - 3, expected + 3);
        }
    }

    /// <summary>One frame the way the view tick does it: clear →
    /// RenderFrame into the bound FBO → CopyFrame (resolve + glReadPixels) —
    /// the <c>GlConePassOrderTests.Capture</c> helper, per-file copy by
    /// convention.</summary>
    private static unsafe byte[] Capture(GlReadback rb, GlPlotScene scene)
    {
        rb.BeginFrame(Background);
        scene.RenderFrame(Size, Size);
        var px = new byte[Size * Size * 4];
        fixed (byte* p = px)
            Assert.True(rb.CopyFrame((nint)p, Size * 4));
        return px;
    }
}
