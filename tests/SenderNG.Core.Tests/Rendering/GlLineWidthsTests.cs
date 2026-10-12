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
/// (drain → trial → drain). The probe stays on as evidence for the
/// due-diligence doc; since checkpoint 4 the draws themselves use palette
/// widths through the expansion shader, never driver acceptance.</summary>
[Trait("Category", "GPU")]
public class GlLineWidthsTests
{
    private readonly ITestOutputHelper _output;

    public GlLineWidthsTests(ITestOutputHelper output) => _output = output;

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

    /// <summary>The wiring after checkpoint 4: the probe still measures what
    /// this driver accepts (evidence for the due-diligence doc), but the
    /// widths the layers draw with are the palette values — rendering no
    /// longer depends on driver <c>glLineWidth</c> acceptance (fat-line
    /// geometry renders them as quads). The scene's chrome palette is
    /// per-piece (checkpoint-5 item 3) and pinned end-to-end by
    /// <c>Chrome_pieces_draw_at_their_palette_widths</c>.</summary>
    [GlFact]
    public void Layers_draw_the_palette_widths_not_the_accepted_set()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);

        var accepted = scene.AcceptedLineSizes;
        _output.WriteLine(
            $"scene accepted line widths: [{string.Join(", ", accepted)}]");

        // The probe remains honest evidence for this context...
        Assert.Contains(GlLineWidths.Fallback, accepted);
        // ...but the draw widths are the palette, independent of acceptance:
        var layer = (GlCarriagePlaneLayer)scene.AddCarriagePlane();
        Assert.Equal(GlCarriagePlaneLayer.BaseLineThickness, layer.BaseLineWidth);
        Assert.Equal(GlCarriagePlaneLayer.ExecutedLineThickness, layer.BaseLineWidth);
    }
}
