
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

using SenderNG.Core.Modules.Probing;
using Xunit;

namespace SenderNG.Core.Tests;

public class ProbeProgramBuilderTests
{
    private static ProbeProgramBuilder Make() =>
        new(ProbingProfile.CreateDefault());

    private static readonly Pos Start = new(10, 20, 5);

    [Fact]
    public void ToolLength_probes_Z_with_latch_pass()
    {
        var p = Make().BuildToolLength(probeFixture: false);

        Assert.Equal(
        [
            "G91F100",
            "G38.3F100Z-10",
            "!G0Z0.5",
            "G38.3F25Z-2",
        ], p.Lines);
    }

    [Fact]
    public void ToolLength_fixture_rapids_to_fixture_origin_first()
    {
        var p = Make().BuildToolLength(probeFixture: true, homeZ: 0, fixtureZ: 0);

        Assert.Equal(
        [
            "G91F100",
            "G53G0Z0",
            "G53G0X0Y0",
            "G53G0Z0",
            "G38.3F100Z-10",
            "!G0Z0.5",
            "G38.3F25Z-2",
        ], p.Lines);
    }

    [Fact]
    public void EdgeFinder_external_corner_A_probes_X_then_Y()
    {
        var p = Make().BuildEdgeFinder(Edge.A, Start, internalFinder: false);

        Assert.Equal(
        [
            "G91F100",
            "G53G0Y25",
            "G53G0X4",
            "G53G0Z2",
            "G38.3F100X10",
            "!G0X-0.5",
            "G38.3F25X2",
            "G53G0X4",
            "G53G0Z5",
            "G53G0X10",
            "G53G0X15Y20",
            "G53G0Y14",
            "G53G0Z2",
            "G38.3F100Y10",
            "!G0Y-0.5",
            "G38.3F25Y2",
            "G53G0Y14",
            "G53G0Z5",
        ], p.Lines);
    }

    [Fact]
    public void EdgeFinder_internal_corner_A_probes_negative_directions()
    {
        var p = Make().BuildEdgeFinder(Edge.A, Start, internalFinder: true);

        Assert.Equal(
        [
            "G91F100",
            "G53G0X15Y25",
            "G53G0Z2",
            "G38.3F100X-10",
            "!G0X0.5",
            "G38.3F25X-2",
            "G53G0X15",
            "G53G0X15Y25",
            "G38.3F100Y-10",
            "!G0Y0.5",
            "G38.3F25Y-2",
            "G53G0Y25",
            "G53G0Z5",
        ], p.Lines);
    }

    [Fact]
    public void EdgeFinder_external_edge_AB_probes_Y_only()
    {
        var p = Make().BuildEdgeFinder(Edge.AB, Start, internalFinder: false);

        Assert.Equal(
        [
            "G91F100",
            "G53G0Y14",
            "G53G0Z2",
            "G38.3F100Y10",
            "!G0Y-0.5",
            "G38.3F25Y2",
            "G53G0Y14",
            "G53G0Z5",
        ], p.Lines);
    }

    [Fact]
    public void CenterFinder_inside_probes_both_axes_from_midpoint()
    {
        var p = Make().BuildCenterFinder(Center.Inside, new Pos(0, 0, 0), sizeX: 40, sizeY: 30);

        Assert.Equal(
        [
            "G91F100",
            "G53G0Z-3",
            "G53G0X-14",
            "G38.3F100X-10",
            "!G0X0.5",
            "G38.3F25X-2",
            "G53G0X14",
            "G38.3F100X10",
            "!G0X-0.5",
            "G38.3F25X2",
            "G53G0X0",
            "G53G0Y-9",
            "G38.3F100Y-10",
            "!G0Y0.5",
            "G38.3F25Y-2",
            "G53G0Y9",
            "G38.3F100Y10",
            "!G0Y-0.5",
            "G38.3F25Y2",
            "G53G0Y0",
            "G53G0Z0",
        ], p.Lines);
    }

    [Fact]
    public void CenterFinder_outside_probes_outward()
    {
        var p = Make().BuildCenterFinder(Center.Outside, new Pos(0, 0, 0), sizeX: 40, sizeY: 30);

        var lines = p.Lines;
        Assert.Equal("G91F100", lines[0]);
        Assert.Equal("G53G0X-26", lines[1]);   // -(sizeX/2 + clearance)
        Assert.Equal("G53G0Z-3", lines[2]);
        Assert.Contains("G38.3F100X10", lines);  // probe +X
        Assert.Contains("G38.3F100X-10", lines); // probe -X
        Assert.Equal("G53G0Z0", lines[^1]);
    }

    [Fact]
    public void Rotation_AB_probes_Y_at_two_X_positions()
    {
        var p = Make().BuildRotation(Edge.AB, Start);

        Assert.Equal(
        [
            "G91F100",
            "G53G0Y14",
            "G53G0Z2",
            "G38.3F100Y10",
            "!G0Y-0.5",
            "G38.3F25Y2",
            "G53G0Y14",
            "G53G0X15",
            "G38.3F100Y10",
            "!G0Y-0.5",
            "G38.3F25Y2",
            "G53G0Y14",
            "G53G0Z5",
            "G53G0X10Y20",
        ], p.Lines);
    }

    [Fact]
    public void Rotation_AB_with_determineX0Y0_adds_a_left_edge_probe()
    {
        var p = Make().BuildRotation(Edge.AB, Start, determineX0Y0: true);

        // The two AB probes are unchanged; a third probe follows on the left
        // edge: approach -X at (start.X - clearance, start.Y + Offset), probe +X.
        Assert.Equal(
        [
            "G91F100",
            "G53G0Y14",
            "G53G0Z2",
            "G38.3F100Y10",
            "!G0Y-0.5",
            "G38.3F25Y2",
            "G53G0Y14",
            "G53G0X15",
            "G38.3F100Y10",
            "!G0Y-0.5",
            "G38.3F25Y2",
            "G53G0Y14",
            "G53G0Z5",
            "G53G0X10Y20",
            "G53G0X4Y25",
            "G53G0Z2",
            "G38.3F100X10",
            "!G0X-0.5",
            "G38.3F25X2",
            "G53G0X4",
            "G53G0Z5",
            "G53G0X10Y20",
        ], p.Lines);
    }

    [Fact]
    public void HeightMap_probes_grid_in_snake_path()
    {
        var map = new HeightMap(10, 10, new Vec2(0, 0), new Vec2(20, 10));
        Assert.Equal(3, map.SizeX);
        Assert.Equal(2, map.SizeY);

        var p = Make().BuildHeightMap(map, new Pos(0, 0, 5), addPause: false);

        Assert.Equal(
        [
            "G91F100",
            "#Probing point 1 of 6",
            "G38.3F100Z-10",
            "!G0Z0.5",
            "G38.3F25Z-2",
            "G53G0Z5",
            "G0Y10",
            "#Probing point 2 of 6",
            "G38.3F100Z-10",
            "!G0Z0.5",
            "G38.3F25Z-2",
            "G53G0Z5",
            "G0X10",
            "#Probing point 3 of 6",
            "G38.3F100Z-10",
            "!G0Z0.5",
            "G38.3F25Z-2",
            "G53G0Z5",
            "G0Y-10",
            "#Probing point 4 of 6",
            "G38.3F100Z-10",
            "!G0Z0.5",
            "G38.3F25Z-2",
            "G53G0Z5",
            "G0X10",
            "#Probing point 5 of 6",
            "G38.3F100Z-10",
            "!G0Z0.5",
            "G38.3F25Z-2",
            "G53G0Z5",
            "G0Y10",
            "#Probing point 6 of 6",
            "G38.3F100Z-10",
            "!G0Z0.5",
            "G38.3F25Z-2",
            "G53G0Z5",
        ], p.Lines);
    }
}
