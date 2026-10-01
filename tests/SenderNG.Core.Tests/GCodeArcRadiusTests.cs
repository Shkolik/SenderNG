/*

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

A� Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

A� Redistributions in binary form must reproduce the above copyright notice,
this list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

A� Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using CNC.Core.Services;
using CNC.GCode;
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>
/// Radius-mode arcs (<c>G2</c>/<c>G3 … R…</c>) derive their centre from
/// <c>h_x2_div_d = 4R² - x² - y²</c>. When the requested radius is too small for the chord — or the
/// start and end coincide — that discriminant goes negative, or the division by
/// <c>sqrt(x²+y²)</c> blows up, and the centre comes out NaN. The parser used to only
/// <c>Console.Write</c> the condition, a stream no Avalonia host displays, and carried on. The
/// observable damage is not a NaN reaching the machine: it is that the arc is silently replaced by
/// its chord, so a toolpath the user asked to flatten comes out geometrically wrong with nothing
/// but an invisible console line to say so.
/// </summary>
public class GCodeArcRadiusTests
{
    /// <summary>Chord 10 with R1: 4·1² − 10² = −96. The classic impossible arc.</summary>
    private const string ImpossibleArc = "G1 F100 X0 Y0\nG2 X10 Y0 R1\n";

    [Fact]
    public void Impossible_radius_arc_is_rejected_rather_than_flattened_to_its_chord()
    {
        // Observed before the fix: ArcsToLines emitted "(Arc to lines start: G2X10Y0R1)"
        // followed by a single "G1X10Y0Z0" — the arc's bounding geometry was gone
        // and only the console carried the reason.
        var program = GCodeProgram.Parse(ImpossibleArc);

        var ex = Assert.Throws<GCodeException>(() =>
            GCodeTransformers.ArcsToLines(program.Tokens, 0.1d, program.Decimals));

        Assert.Contains("radius", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Computing_limits_for_an_impossible_arc_surfaces_the_error()
    {
        var ex = Assert.Throws<GCodeException>(() => GCodeLimitsCalculator.Compute(ImpossibleArc));

        Assert.Contains("radius", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_counter_clockwise_impossible_arc_is_rejected_too()
    {
        var program = GCodeProgram.Parse("G1 F100 X0 Y0\nG3 X10 Y0 R1\n");

        Assert.Throws<GCodeException>(() =>
            GCodeTransformers.ArcsToLines(program.Tokens, 0.1d, program.Decimals));
    }

    [Fact]
    public void A_helical_impossible_arc_is_rejected_too()
    {
        var program = GCodeProgram.Parse("G1 F100 X0 Y0 Z0\nG2 X10 Y0 Z5 R1\n");

        Assert.Throws<GCodeException>(() =>
            GCodeTransformers.ArcsToLines(program.Tokens, 0.1d, program.Decimals));
    }

    [Fact]
    public void Zero_length_radius_arc_is_rejected_too()
    {
        // start == end makes x == y == 0, so h_x2_div_d = 4R² is positive and the
        // discriminant check alone passes; the offsets then become 0·Infinity =
        // NaN. A finite-centre check is the only thing that catches this one.
        var program = GCodeProgram.Parse("G1 F100 X5 Y5\nG2 X5 Y5 R5\n");

        var ex = Assert.Throws<GCodeException>(() =>
            GCodeTransformers.ArcsToLines(program.Tokens, 0.1d, program.Decimals));

        Assert.Contains("radius", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_valid_semicircular_arc_is_still_accepted()
    {
        // Chord 10 with R5 puts the discriminant exactly on zero. This is the
        // boundary the fix must not cross: over-rejecting it would refuse
        // ordinary half-circles.
        var program = GCodeProgram.Parse("G1 F100 X0 Y0\nG2 X10 Y0 R5\n");

        Assert.False(program.HasErrors);
        Assert.All(program.Entries, e => Assert.True(e.IsSendable));
    }

    [Fact]
    public void A_valid_semicircular_arc_still_reports_its_extent()
    {
        var limits = GCodeLimitsCalculator.Compute("G1 F100 X0 Y0\nG2 X10 Y0 R5\n");

        Assert.NotNull(limits);
        Assert.Equal(10.0, limits[0].Max);
        Assert.Equal(5.0, limits[1].Max);   // the arc bulges to y = R
    }

    [Fact]
    public void A_valid_arc_larger_than_its_chord_is_accepted()
    {
        // R well above chord/2 is a shallow arc, still a positive discriminant.
        var program = GCodeProgram.Parse("G1 F100 X0 Y0\nG2 X10 Y0 R20\n");

        Assert.False(program.HasErrors);
    }

    [Fact]
    public void A_valid_arc_is_still_flattened_to_real_arc_segments()
    {
        // The counterpart to the chord regression above: a valid arc must still
        // produce several segments, not a single straight move.
        var program = GCodeProgram.Parse("G1 F100 X0 Y0\nG2 X10 Y0 R5\n");
        var lines = GCodeTransformers.ArcsToLines(program.Tokens, 0.1d, program.Decimals);

        Assert.True(lines.Count(l => l.Contains('X') && l.StartsWith("G1")) > 1);
    }
}
