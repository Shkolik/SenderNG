using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class GCodeLimitsCalculatorTests
{
    [Fact]
    public void Compute_null_or_empty_returns_null()
    {
        Assert.Null(GCodeLimitsCalculator.Compute(null));
        Assert.Null(GCodeLimitsCalculator.Compute(""));
        Assert.Null(GCodeLimitsCalculator.Compute("; comment only"));
    }

    [Fact]
    public void Compute_linear_moves_bounding_box()
    {
        var text = "G1 F100 X10 Y20 Z5\r\nG1 X-5 Y2 Z12\r\n";
        var limits = GCodeLimitsCalculator.Compute(text);

        Assert.NotNull(limits);
        Assert.Equal(3, limits.Count);
        Assert.Equal(('X', -5.0, 10.0), (limits[0].Letter, limits[0].Min, limits[0].Max));
        Assert.Equal(('Y', 2.0, 20.0), (limits[1].Letter, limits[1].Min, limits[1].Max));
        Assert.Equal(('Z', 5.0, 12.0), (limits[2].Letter, limits[2].Min, limits[2].Max));
    }

    [Fact]
    public void Compute_end_points_only_like_ioSender()
    {
        // Bounding box accumulates move END points (ioSender semantics), not segment starts.
        var limits = GCodeLimitsCalculator.Compute("G1 F100 X10 Y10 Z10");

        Assert.NotNull(limits);
        Assert.Equal(10.0, limits[0].Min);
        Assert.Equal(10.0, limits[0].Max);
    }

    [Fact]
    public void Compute_crlf_cr_lf_foam_style_file_ok()
    {
        var lines = new[] { ";*** FOAM BLOCK ***", "M03 S500.00", "G01 X9.40 Y150.00 Z9.40 A150.00 F420.0", "G01 X462.60 Y0.40 Z462.60 A0.40 F420.0", "M05" };
        var limits = GCodeLimitsCalculator.Compute(string.Join("\r\r\n", lines) + "\r\r\n");

        Assert.NotNull(limits);
        Assert.Equal(9.4, limits[0].Min);
        Assert.Equal(462.6, limits[0].Max);
        Assert.Equal(0.4, limits[1].Min);
    }
}
