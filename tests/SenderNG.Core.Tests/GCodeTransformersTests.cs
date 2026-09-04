/*
 * GCodeTransformersTests.cs
 *
 * Copyright (c) 2026 Andrew Shkolik
 *
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without modification,
 * are permitted provided that the following conditions are met:
 *
 * · Redistributions of source code must retain the above copyright notice, this
 * list of conditions and the following disclaimer.
 *
 * · Redistributions in binary form must reproduce the above copyright notice, this
 * list of conditions and the following disclaimer in the documentation and/or
 * other materials provided with the distribution.
 *
 * · Neither the name of the copyright holder nor the names of its contributors may
 * be used to endorse or promote products derived from this software without
 * specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
 * ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
 * ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *
 */

using CNC.Core.Services;
using CNC.GCode;
using Xunit;

namespace SenderNG.Core.Tests;

public class GCodeTransformersTests
{
    private static string Rotate(string text, double degrees, double ox = 0, double oy = 0)
    {
        var program = GCodeProgram.Parse(text);
        Assert.False(program.HasErrors, string.Join("; ", program.Errors));
        var lines = GCodeTransformers.Rotate(program.Tokens,
            degrees * Math.PI / 180d, ox, oy, program.Decimals);
        return string.Join("\n", lines);
    }

    [Fact]
    public void Rotate_90_degrees_about_origin_maps_x_to_y()
    {
        var result = Rotate("G21\nG0 X10 Y0\nG0 X0 Y10\n", 90);

        Assert.Equal(
            [
                "(90 degree rotation applied)",
                "G21",
                "G0X0Y10",
                "G0X-10Y0",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void Rotate_about_arbitrary_origin_keeps_origin_point_fixed()
    {
        // Point (20,0) pivots around (10,0): a 90° swing lands on (10,10).
        var result = Rotate("G0 X20 Y0\n", 90, ox: 10, oy: 0);

        Assert.Equal(
            [
                "(90 degree rotation applied)",
                "G0X10Y10",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void Rotate_180_degrees_about_origin_flips_signs()
    {
        var result = Rotate("G1 X10 Y-20 F100\n", 180);

        // Feed word joins the motion on its original (same) line — canonical order.
        Assert.Equal(
            [
                "(180 degree rotation applied)",
                "F100G1X-10Y20",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void Rotate_incremental_program_rotates_deltas_and_accumulates()
    {
        // ioSender semantics: G91 deltas rotate about the origin and the rotated
        // deltas accumulate — X always rotates to the negative Y direction first.
        var result = Rotate("G21\nG91\nG0 Y5\nG0 X3\n", 90);

        Assert.Equal(
            [
                "(90 degree rotation applied)",
                "G21",
                "G91",
                "G0X-5Y0",
                "G0X0Y-2",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void Rotate_arc_rotates_endpoint_and_ijk_center_words()
    {
        var result = Rotate("G17\nG90\nG2 X10 Y0 I5 J0\n", 90);

        Assert.Equal(
            [
                "(90 degree rotation applied)",
                "G17",
                "G90",
                "G2X0Y10I0J5",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void Rotate_refuses_radius_mode_arcs_like_ioSender()
    {
        var program = GCodeProgram.Parse("G2 X10 Y0 R5\n");
        Assert.Throws<InvalidOperationException>(() =>
            GCodeTransformers.Rotate(program.Tokens, Math.PI / 2d, 0, 0, program.Decimals));
    }

    [Fact]
    public void Rotate_refuses_arcs_outside_the_xy_plane_like_ioSender()
    {
        var program = GCodeProgram.Parse("G18\nG2 X10 Z0 I5 K0\n");
        Assert.Throws<InvalidOperationException>(() =>
            GCodeTransformers.Rotate(program.Tokens, Math.PI / 2d, 0, 0, program.Decimals));
    }

    [Fact]
    public void Rotate_leaves_g53_machine_coordinate_moves_untouched()
    {
        var result = Rotate("G53 G0 X10 Y0\nG1 X5 Y5 F100\n", 90);

        Assert.Equal(
            [
                "(90 degree rotation applied)",
                "G53G0X10Y0",
                "F100G1X-5Y5",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void Rotate_spline_rotates_endpoint_and_i_j_p_q_control_points()
    {
        // G5 cubic spline: endpoint + I/J/P/Q control points rotate (control
        // offsets swing about the origin, ioSender parity).
        var result = Rotate("G5 X10 Y0 I5 J0 P5 Q0\n", 90);

        Assert.Equal(
            [
                "(90 degree rotation applied)",
                "G5X0Y10I0J5P0Q5",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void Rotate_canned_drill_rotates_hole_position()
    {
        var result = Rotate("G90\nG81 X10 Y0 Z-1 R1 F100\n", 90);

        Assert.Equal(
            [
                "(90 degree rotation applied)",
                "G90",
                "F100G81X0Y10Z-1R1",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void Rotate_uses_file_precision_when_rounding()
    {
        var program = GCodeProgram.Parse("G0 X10.1234 Y0\n");
        Assert.Equal(4, program.Decimals);

        var result = string.Join("\n",
            GCodeTransformers.Rotate(program.Tokens, Math.PI / 2d, 0, 0, program.Decimals));

        Assert.Contains("G0X0Y10.1234", result);
    }

    [Fact]
    public void Rotate_does_not_mutate_the_source_tokens()
    {
        var program = GCodeProgram.Parse("G0 X10 Y0\nG2 X5 Y0 I2 J0\n");
        var before = GCodeParser.TokensToGCode([.. program.Tokens]);

        GCodeTransformers.Rotate(program.Tokens, Math.PI / 2d, 0, 0, program.Decimals);

        Assert.Equal(before, GCodeParser.TokensToGCode([.. program.Tokens]));
    }
}
