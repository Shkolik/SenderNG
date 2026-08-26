using SenderNG.Core.Modules.Probing;
using Xunit;

namespace SenderNG.Core.Tests;

public class ProgramTransformTests
{
    [Fact]
    public void Rotate_90_degrees_swaps_XY()
    {
        var result = ProgramTransform.Rotate("G1 X10 Y0\nG0 X0 Y0", Math.PI / 2, 0, 0);

        Assert.Equal("G1 X0 Y10\nG0 X0 Y0", result);
    }

    [Fact]
    public void Rotate_preserves_non_motion_lines()
    {
        var result = ProgramTransform.Rotate("G21\nG1 X10 Y0\n; comment", Math.PI / 2, 0, 0);

        Assert.Equal("G21\nG1 X0 Y10\n; comment", result);
    }

    [Fact]
    public void ApplyHeightMap_injects_interpolated_Z_on_motion_lines()
    {
        var map = new HeightMap(10, 10, new Vec2(0, 0), new Vec2(20, 10));
        map.AddPoint(1, 0, 2);
        map.AddPoint(1, 1, 2);

        var result = ProgramTransform.ApplyHeightMap("G1 X10 Y5\nG0 X0 Y0\nG21", map);

        // X10 Y5 sits on the x=1 grid line with a height of 2.
        Assert.StartsWith("G1 X10 Y5 Z2", result);
        Assert.EndsWith("G21", result);
    }
}
