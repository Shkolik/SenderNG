using SenderNG.Core.Modules.Probing;
using Xunit;

namespace SenderNG.Core.Tests;

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
}
