/*
 * GlMsaaHealerTests.cs - checkpoint-7 evidence: the MSAA re-arm healer draws
 * on the ShowOrigin frame and paints zero pixels
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

/// <summary>Task 5f (checkpoint-7): on Intel the frame's only triangle pass
/// (the axis cones) disables multisample coverage for every later draw until
/// the next framebuffer clear, and a <c>GL_LINES</c> draw re-arms it — so
/// <see cref="GlPlotScene"/> draws one degenerate off-screen line right after
/// the cones. Mesa never shows the defect, so the hardware proof is the
/// controller's checkpoint; what these tests guard is everything headless can
/// prove: the heal path fires exactly when the cone pass does (the
/// <see cref="GlPlotScene.MsaaHealerDrawn"/> latch, negative-checked on the
/// frame that must not draw it), and it is pixel-neutral — the same frame
/// bytes and the same non-background pixel count with and without it, i.e.
/// the 5e coverage numbers cannot move because the healer contributes none.
/// Gated by <see cref="GlFactAttribute"/>.</summary>
[Trait("Category", "GPU")]
public class GlMsaaHealerTests
{
    private static readonly byte[] Background = [32, 35, 40, 255];
    private const int Size = 256;

    /// <summary>One real diagonal toolpath segment, two frames of the SAME
    /// scene differing only by the ShowOrigin gate — with the axis shafts and
    /// cone heads stripped (empty <c>AddAxisArrows</c>), the heal segment is
    /// then the only draw inside that gate, so the pair differs by exactly
    /// the heal path. The latch must stay down on the first frame and trip on
    /// the second; the two buffers must be byte-identical with equal
    /// non-background counts (the healer adds zero pixels).</summary>
    [GlFact]
    public unsafe void Healer_trips_on_the_show_origin_frame_and_adds_zero_pixels()
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
        // Strip shafts and cone heads: inside ShowOrigin the healer line is
        // then the block's only draw (empty meshes skip, DrawAxisCones
        // early-outs), which is what makes the two frames comparable.
        scene.AddAxisArrows([]);
        scene.SetViewPreset(ViewPreset.XY);
        scene.SetView(new PlotterGlSettings
        {
            ShowOrigin = false,
            ShowEnvelope = false,
            ShowGrid = false,
        });
        var withoutHeal = Capture(rb, scene);
        Assert.False(scene.MsaaHealerDrawn,
            "the heal path must not run while ShowOrigin is off");

        scene.SetView(new PlotterGlSettings
        {
            ShowOrigin = true,
            ShowEnvelope = false,
            ShowGrid = false,
        });
        var withHeal = Capture(rb, scene);
        Assert.True(scene.MsaaHealerDrawn,
            "the heal segment must draw after the cone pass when ShowOrigin is on");

        var litA = LitPixels(withoutHeal);
        var litB = LitPixels(withHeal);
        Console.WriteLine(
            $"[msaa-healer] lit without={litA} with={litB} bytesEqual=" +
            $"{withoutHeal.AsSpan().SequenceEqual(withHeal)}");

        // Precondition: the frame really drew content, so the equality below
        // is not two empty buffers agreeing on nothing.
        Assert.True(litA > 100, $"precondition: toolpath must be visible, got {litA} lit");
        // The heal path is pixel-neutral: same non-background count, same bytes.
        Assert.Equal(litA, litB);
        Assert.Equal(withoutHeal, withHeal);
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

    /// <summary>The non-background (lit) pixel count of a captured frame —
    /// the coverage number the healer must not move.</summary>
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
}
