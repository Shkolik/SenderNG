
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

using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class GCodeLimitsCalculatorTests
{
    [Fact]
    public void Compute_null_or_empty_returns_null()
    {
        Assert.Null(GCodeLimitsCalculator.Compute((string?)null));
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

    [Fact]
    public void Compute_reports_every_axis_present_not_only_xyz()
    {
        // Foam file moves X/Y/Z/A — A limits must be reported alongside XYZ.
        var text = "G1 F100 X10 Y20 Z5 A15\r\nG1 X-5 Y2 Z12 A-3\r\n";
        var limits = GCodeLimitsCalculator.Compute(text);

        Assert.NotNull(limits);
        Assert.Equal(4, limits.Count);
        Assert.Equal(('X', -5.0, 10.0), (limits[0].Letter, limits[0].Min, limits[0].Max));
        Assert.Equal(('Y', 2.0, 20.0), (limits[1].Letter, limits[1].Min, limits[1].Max));
        Assert.Equal(('Z', 5.0, 12.0), (limits[2].Letter, limits[2].Min, limits[2].Max));
        Assert.Equal(('A', -3.0, 15.0), (limits[3].Letter, limits[3].Min, limits[3].Max));
    }

    [Fact]
    public void Compute_omits_axes_the_program_never_moves()
    {
        var text = "G1 F100 X10 Y10\r\n";
        var limits = GCodeLimitsCalculator.Compute(text);

        Assert.NotNull(limits);
        Assert.Equal(2, limits.Count);
        Assert.Equal('X', limits[0].Letter);
        Assert.Equal('Y', limits[1].Letter);
    }
}
