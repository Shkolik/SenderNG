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

    private static int LitPixels(byte[] px)
    {
        var lit = 0;
        for (var i = 0; i < px.Length; i += 4)
            if (px[i] != 32 || px[i + 1] != 35 || px[i + 2] != 40)
                lit++;
        return lit;
    }
}
