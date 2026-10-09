/*
 * GlLineWidthsTests.cs - the line-width acceptance probe and its picker
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

using SenderNG.Modules.Plotter.GL.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SenderNG.Core.Tests;

/// <summary>Checkpoint 3's line-width evidence: this driver reports
/// <c>ALIASED_LINE_WIDTH_RANGE = [1,255]</c> and still refuses every wider
/// line, so the reported range proves nothing and the widths have to be tried
/// (drain → trial → drain). The probe's accepted set is the only thing the
/// chrome and the layers are allowed to draw with — and the accepted set is
/// what the due-diligence doc has to record.</summary>
[Trait("Category", "GPU")]
public class GlLineWidthsTests
{
    private readonly ITestOutputHelper _output;

    public GlLineWidthsTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Pick_takes_the_largest_accepted_width_that_fits()
    {
        Assert.Equal(1.5f, GlLineWidths.Pick([1f, 1.5f, 2f, 3f, 3.2f], 1.5f));
        Assert.Equal(1f, GlLineWidths.Pick([1f, 1.5f, 2f], 1.2f));
        // Nex's 3.2 palette request: nearest accepted width that does not
        // exceed it (3.2 itself when the driver takes it).
        Assert.Equal(3f, GlLineWidths.Pick([1f, 1.5f, 2f, 3f], 3.2f));
        Assert.Equal(3.2f, GlLineWidths.Pick([1f, 1.5f, 2f, 3f, 3.2f], 3.2f));
    }

    [Fact]
    public void Pick_falls_back_to_the_smallest_accepted_then_to_one()
    {
        // A driver that only takes wider lines still gets its smallest offer.
        Assert.Equal(2f, GlLineWidths.Pick([2f, 3f], 1f));
        // Nothing accepted at all: the one width every driver takes.
        Assert.Equal(GlLineWidths.Fallback, GlLineWidths.Pick([], 1.5f));
    }

    /// <summary>The evidence step itself, headless: log what this driver
    /// really accepts so the checkpoint record can carry it.</summary>
    [GlFact]
    public void Probe_reports_the_accepted_set_for_this_driver()
    {
        using var ctx = GlContext.Create();
        ctx.MakeCurrent();

        var accepted = GlLineWidths.Probe(ctx.Api);
        _output.WriteLine(
            $"line widths accepted: [{string.Join(", ", accepted)}] " +
            $"of candidates [{string.Join(", ", GlLineWidths.Candidates)}] " +
            $"| {ctx.Renderer} {ctx.Version}");

        Assert.Contains(GlLineWidths.Fallback, accepted); // universally legal
        Assert.All(accepted, w => Assert.Contains(w, GlLineWidths.Candidates));
        Assert.Equal(accepted.OrderBy(w => w).ToArray(), accepted); // ascending
    }

    /// <summary>The wiring: the scene probes once, and both the chrome width
    /// and the layer's toolpath width come out of that accepted set — never
    /// out of the reported range.</summary>
    [GlFact]
    public void Scene_and_layers_draw_only_accepted_widths()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);

        var accepted = scene.AcceptedLineSizes;
        _output.WriteLine(
            $"scene accepted line widths: [{string.Join(", ", accepted)}] " +
            $"| chrome {scene.ChromeLineWidth}");

        Assert.Contains(GlLineWidths.Fallback, accepted);
        Assert.Contains(scene.ChromeLineWidth, accepted);
        Assert.True(scene.ChromeLineWidth <= 1.5f,
            $"chrome width {scene.ChromeLineWidth} exceeds the plan's 1.5");

        var layer = (GlCarriagePlaneLayer)scene.AddCarriagePlane();
        Assert.Contains(layer.BaseLineWidth, accepted);
        Assert.True(layer.BaseLineWidth <= GlCarriagePlaneLayer.BaseLineThickness,
            $"layer width {layer.BaseLineWidth} exceeds Nex's {GlCarriagePlaneLayer.BaseLineThickness}");
    }
}
