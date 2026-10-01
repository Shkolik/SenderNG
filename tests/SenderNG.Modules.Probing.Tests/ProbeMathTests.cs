
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

namespace SenderNG.Modules.Probing.Tests;

public class ProbeMathTests
{
    [Fact]
    public void EdgePoint_external_corner_A_compensates_radius_on_both_axes()
    {
        // X probe touched at (23.5, 25, 2), Y probe at (15, 33.5, 2); radius 1.
        var pos = ProbeMath.ComputeEdgePoint(Edge.A,
            [new Pos(23.5, 25, 2), new Pos(15, 33.5, 2)], 2d, internalFinder: false);

        Assert.Equal(24.5, pos.X);
        Assert.Equal(34.5, pos.Y);
        Assert.Equal(2, pos.Z);
    }

    [Fact]
    public void EdgePoint_internal_corner_A_compensates_inward()
    {
        var pos = ProbeMath.ComputeEdgePoint(Edge.A,
            [new Pos(-24, -25, 2), new Pos(-15, -34.5, 2)], 2d, internalFinder: true);

        Assert.Equal(-25, pos.X);
        Assert.Equal(-35.5, pos.Y);
    }

    [Fact]
    public void EdgePoint_edge_probes_single_axis()
    {
        var pos = ProbeMath.ComputeEdgePoint(Edge.AB,
            [new Pos(10, 33.5, 2)], 2d, internalFinder: false);

        Assert.Equal(10, pos.X);       // X not probed: start X retained
        Assert.Equal(34.5, pos.Y);     // Y probed -: contact = touch - radius
    }

    [Fact]
    public void Center_inside_computes_midpoint_and_adds_diameter()
    {
        var probes = new[]
        {
            new Pos(-24, 0, 2),  // X- touch
            new Pos(24, 0, 2),   // X+ touch
            new Pos(0, -19, 2),  // Y- touch
            new Pos(0, 19, 2),   // Y+ touch
        };

        var (center, xd, yd) = ProbeMath.ComputeCenter(Center.Inside, new Pos(0, 0, 5), probes, 2d, 40, 30);

        Assert.Equal(0, center.X);
        Assert.Equal(0, center.Y);
        Assert.Equal(50, xd);   // |24 - (-24)| + 2
        Assert.Equal(40, yd);   // |19 - (-19)| + 2
    }

    [Fact]
    public void Center_outside_subtracts_diameter()
    {
        var probes = new[]
        {
            new Pos(-26, 0, 2),
            new Pos(26, 0, 2),
            new Pos(0, -21, 2),
            new Pos(0, 21, 2),
        };

        var (center, xd, yd) = ProbeMath.ComputeCenter(Center.Outside, new Pos(0, 0, 5), probes, 2d, 40, 30);

        Assert.Equal(0, center.X);
        Assert.Equal(0, center.Y);
        Assert.Equal(50, xd);   // |26 - (-26)| - 2
        Assert.Equal(40, yd);   // |21 - (-21)| - 2
    }

    [Fact]
    public void Center_XInside_computes_midpoint_on_the_X_pair_only()
    {
        var probes = new[]
        {
            new Pos(-24, 0, 2),  // X- touch
            new Pos(24, 0, 2),   // X+ touch
        };

        var (center, xd, yd) = ProbeMath.ComputeCenter(Center.XInside, new Pos(0, 0, 5), probes, 2d, 40, 30);

        Assert.Equal(0, center.X);
        Assert.Equal(0, center.Y);   // Y pair never probed
        Assert.Equal(50, xd);        // |24 - (-24)| + 2
        Assert.Equal(0, yd);
    }

    [Fact]
    public void Center_XInside_probes_X_even_when_only_sizeY_is_given()
    {
        // Regression: the legacy modes gate the X pair on sizeY (ioSender's
        // FindMode); the axis-specific modes must follow their own axis.
        var probes = new[] { new Pos(-24, 0, 2), new Pos(24, 0, 2) };

        var (center, xd, _) = ProbeMath.ComputeCenter(Center.XInside, new Pos(0, 0, 5), probes, 2d, sizeX: 0, sizeY: 40);

        Assert.Equal(0, center.X);
        Assert.Equal(50, xd);
    }

    [Fact]
    public void Center_YOutside_computes_midpoint_on_the_Y_pair_only()
    {
        var probes = new[]
        {
            new Pos(0, -21, 2),  // Y- touch
            new Pos(0, 21, 2),   // Y+ touch
        };

        var (center, xd, yd) = ProbeMath.ComputeCenter(Center.YOutside, new Pos(0, 0, 5), probes, 2d, 40, 30);

        Assert.Equal(0, center.X);
        Assert.Equal(0, center.Y);
        Assert.Equal(0, xd);
        Assert.Equal(40, yd);        // |21 - (-21)| - 2
    }

    [Fact]
    public void RotationAngle_flat_edge_is_zero_and_vertical_edge_inverts()
    {
        var flat = ProbeMath.ComputeRotationAngle(Edge.AB, new Pos(10, 20, 2), new Pos(20, 20, 2));
        Assert.Equal(0, flat);

        var sloped = ProbeMath.ComputeRotationAngle(Edge.AD, new Pos(10, 20, 2), new Pos(20, 21, 2));
        Assert.True(Math.Abs(sloped + 10d) < 0.01); // 1/10 slope inverted -> -10
    }

    [Fact]
    public void OffsetCommands_match_grbl_syntax()
    {
        var pos = new Pos(24.5, 34.5, 2);
        Assert.Equal("G10L2P1X24.5Y34.5Z2", ProbeMath.OffsetCommand(CoordinateMode.G10, CoordinateSystem.G54, pos, "XYZ"));
        Assert.Equal("G92X24.5Y34.5", ProbeMath.OffsetCommand(CoordinateMode.G92, CoordinateSystem.G54, pos, "XY"));
        Assert.Equal(string.Empty, ProbeMath.OffsetCommand(CoordinateMode.Measure, CoordinateSystem.G54, pos, "XY"));
    }

    [Fact]
    public void ToolLengthTableCommand_emits_g10_l1_with_probed_z()
    {
        Assert.Equal("G10L1P2Z-11.5", ProbeMath.ToolLengthTableCommand(2, -11.5));
        Assert.Equal("G10L1P0Z0", ProbeMath.ToolLengthTableCommand(0, 0));
        Assert.Equal("G10L1P7Z1.25", ProbeMath.ToolLengthTableCommand(7, 1.25));
    }

    [Fact]
    public void ComputeCorner_finds_the_corner_of_a_square_workpiece()
    {
        // Bottom edge at y = 50, left edge at x = 25; probe radius 0.5 so the
        // raw touches sit one radius outside the edges (approach +Y / +X).
        var corner = ProbeMath.ComputeCorner(
            new Pos(0, 49.5, 0), new Pos(100, 49.5, 0), new Pos(24.5, 60, 0),
            probedSlope: 0d, probeOffsetX: 0d, probeOffsetY: 0d, probeRadius: 0.5d, offset: 30d);

        Assert.Equal(25d, corner.X, 6);
        Assert.Equal(50d, corner.Y, 6);
    }

    [Fact]
    public void ComputeCorner_intersects_a_sloped_bottom_edge_exactly()
    {
        // Bottom edge y = 50 + 0.1x through (0,50)/(100,60); left edge through
        // (25,65) perpendicular to it. Closed-form intersection:
        //   t = 12.5/30.3 -> (26.2376..., 52.6237...)
        var corner = ProbeMath.ComputeCorner(
            new Pos(0, 50, 0), new Pos(100, 60, 0), new Pos(25, 65, 0),
            probedSlope: 0.1d, probeOffsetX: 0d, probeOffsetY: 0d, probeRadius: 0d, offset: 30d);

        Assert.Equal(26.2376237623762376d, corner.X, 9);
        Assert.Equal(52.6237623762376237d, corner.Y, 9);
        // And the corner really sits on the probed bottom-edge line.
        Assert.Equal(50d + 0.1d * corner.X, corner.Y, 9);
    }

    [Fact]
    public void ComputeCorner_applies_probe_offsets_and_radius_in_approach_direction()
    {
        // Touch positions include the spindle-to-tip offsets: the corner
        // shifts by exactly ProbeOffsetX/Y plus the radius.
        var corner = ProbeMath.ComputeCorner(
            new Pos(0, 49, 0), new Pos(100, 49, 0), new Pos(24, 60, 0),
            probedSlope: 0d, probeOffsetX: 1d, probeOffsetY: 0.5d, probeRadius: 0.5d, offset: 30d);

        // p1/p2.Y + 0.5 + 0.5 = 50; p3.X + 1 + 0.5 = 25.5
        Assert.Equal(25.5d, corner.X, 6);
        Assert.Equal(50d, corner.Y, 6);
    }

    // Regression: ToDegrees is handed a value that ComputeRotationRadians has
    // already run through Atan, so applying Atan again read every tilt too
    // small (45 deg reported as 38.15, 30 deg as 27.61) in the "Probed angle"
    // readout at ProbeViewModel.cs:1294.
    [Theory]
    [InlineData(45d)]
    [InlineData(30d)]
    [InlineData(10d)]
    [InlineData(1.5d)]
    [InlineData(-22.5d)]
    [InlineData(0d)]
    public void ToDegrees_converts_radians_to_degrees(double degrees)
    {
        Assert.Equal(degrees, ProbeMath.ToDegrees(degrees * Math.PI / 180d), 3);
    }

    [Fact]
    public void Probed_angle_readout_matches_the_slope_that_was_probed()
    {
        // The real call site: ComputeRotationRadians(Edge.AB, ...) of a
        // 10-unit rise over a 10-unit run is 45 degrees.
        var radians = ProbeMath.ComputeRotationRadians(Edge.AB, new Pos(10, 20, 2), new Pos(20, 30, 2));

        Assert.Equal(45d, ProbeMath.ToDegrees(radians), 3);
    }

    [Fact]
    public void Probed_angle_readout_is_accurate_across_the_small_tilts_probing_measures()
    {
        // 1-3 degrees is the realistic probing band, where the double Atan was
        // small but still a real reading error. The readout must match the true
        // tilt to floating-point precision, not merely to plausible-looking
        // three decimal places (the old code was 6e-4 out at 2 degrees).
        foreach (var trueDegrees in new[] { 1d, 1.5d, 2d, 2.5d, 3d })
        {
            var slope = Math.Tan(trueDegrees * Math.PI / 180d);
            // 100 units of run with 100*tan(theta) of rise, so the AB slope is
            // exactly tan(theta) and the true tilt is trueDegrees.
            var radians = ProbeMath.ComputeRotationRadians(Edge.AB, new Pos(0, 0, 0), new Pos(100, 100 * slope, 0));

            Assert.Equal(trueDegrees, ProbeMath.ToDegrees(radians), 9);
        }
    }
}
