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

using System.Text.RegularExpressions;
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

    // ------------------------------------------------------------------
    // M2 — ArcsToLines
    // ------------------------------------------------------------------

    private static string ArcsToLines(string text, double tolerance = 0.1d)
    {
        var program = GCodeProgram.Parse(text);
        Assert.False(program.HasErrors, string.Join("; ", program.Errors));
        var lines = GCodeTransformers.ArcsToLines(program.Tokens, tolerance, program.Decimals);
        var result = string.Join("\n", lines);

        // The engine gate: transformed output must re-parse cleanly.
        var reparsed = GCodeProgram.Parse(result);
        Assert.False(reparsed.HasErrors, string.Join("; ", reparsed.Errors));
        return result;
    }

    private static double[] Words(string line, params char[] letters)
    {
        var values = new double[letters.Length];
        for (var i = 0; i < letters.Length; i++)
        {
            var match = Regex.Match(line, $"{letters[i]}(-?\\d+(?:\\.\\d+)?)");
            values[i] = match.Success
                ? double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                : double.NaN;
        }
        return values;
    }

    private static string[] G1Lines(string result) =>
        result.Split('\n')
            .Where(l => l.StartsWith("G1X") || l.StartsWith("G1Y") || l.StartsWith("G1Z") || l == "G1")
            .ToArray();

    [Fact]
    public void ArcsToLines_semicircle_emits_g1_chain_from_start_to_end()
    {
        // G2 (CW) from (10,0) about center (5,0) to (0,0) — lower semicircle.
        var result = ArcsToLines("G17\nG90\nG0 X10 Y0\nG2 X0 Y0 I-5 J0 F100\n");
        var g1 = G1Lines(result);

        Assert.True(g1.Length >= 3, result);
        Assert.Equal("G1X10Y0Z0", g1[0]); // chain starts at the true arc start
        Assert.Equal("G1X0Y0Z0", g1[^1]); // chain ends at the arc endpoint
        foreach (var line in g1.Skip(1).Take(g1.Length - 2))
            Assert.True(Words(line, 'Y')[0] < 0d, "semicircle must dip below the X axis: " + line);
    }

    [Fact]
    public void ArcsToLines_full_circle_returns_to_start()
    {
        var result = ArcsToLines("G0 X10 Y0\nG2 X10 Y0 I-5 J0 F100\n");
        var g1 = G1Lines(result);

        Assert.True(g1.Length > 12, result); // ~16 segments at 0.1 mm chord error
        Assert.Equal("G1X10Y0Z0", g1[^1]);
    }

    [Fact]
    public void ArcsToLines_radius_mode_arc_uses_center_from_r()
    {
        var result = ArcsToLines("G0 X10 Y0\nG2 X0 Y0 R5 F100\n");
        var g1 = G1Lines(result);

        Assert.True(g1.Length >= 3, result);
        Assert.Equal("G1X0Y0Z0", g1[^1]);
    }

    [Fact]
    public void ArcsToLines_tighter_tolerance_yields_more_lines()
    {
        var coarse = ArcsToLines("G0 X10 Y0\nG2 X0 Y0 I-5 J0 F100\n", tolerance: 1.0d);
        var fine = ArcsToLines("G0 X10 Y0\nG2 X0 Y0 I-5 J0 F100\n", tolerance: 0.01d);

        Assert.True(G1Lines(fine).Length > G1Lines(coarse).Length,
            $"fine {G1Lines(fine).Length} vs coarse {G1Lines(coarse).Length}");
    }

    [Fact]
    public void ArcsToLines_keeps_feed_before_first_segment()
    {
        // F300 sits AFTER the arc words on the same block — it must still be
        // emitted ahead of the chain so the first G1 inherits the arc's feed.
        var result = ArcsToLines("G0 X10 Y0\nG2 X0 Y0 I-5 J0 F300\n");
        var lines = result.Split('\n');

        Assert.Equal("(Arcs to lines transform applied)", lines[0]);
        Assert.Equal("G0X10Y0", lines[1]);
        Assert.Equal("F300", lines[2]); // hoisted ahead of the chain
        Assert.True(Array.IndexOf(lines, "F300") < Array.FindIndex(lines, l => l.StartsWith("G1X")),
            "feed must precede the first G1 segment");
        Assert.Contains("(Arc to lines start: G2X0Y0I-5J0)", lines);
    }

    [Fact]
    public void ArcsToLines_g91_program_emits_deltas_that_sum_to_travel()
    {
        var result = ArcsToLines("G91\nG0 X10 Y0\nG2 X-10 Y0 I-5 J0 F100\n");
        var g1 = G1Lines(result);

        Assert.True(g1.Length >= 3, result);

        double sumX = 0, sumY = 0;
        foreach (var line in g1)
        {
            var xy = Words(line, 'X', 'Y');
            if (!double.IsNaN(xy[0])) sumX += xy[0];
            if (!double.IsNaN(xy[1])) sumY += xy[1];
        }

        Assert.True(Math.Abs(sumX - -10d) < 0.001d, $"X sum {sumX}");
        Assert.True(Math.Abs(sumY - 0d) < 0.001d, $"Y sum {sumY}");
        Assert.Contains("G91", result.Split('\n')); // distance mode preserved
    }

    [Fact]
    public void ArcsToLines_spline_emits_chain_ending_at_spline_end()
    {
        var result = ArcsToLines("G5 X10 Y0 I5 J0 P5 Q0 F100\n");
        var g1 = G1Lines(result);

        Assert.True(g1.Length >= 3, result);
        Assert.Equal("G1X10Y0Z0", g1[^1]);
        Assert.Contains("(Spline to lines start: G5X10Y0I5J0P5Q0)", result.Split('\n'));
        Assert.Contains("(Spline to lines end)", result.Split('\n'));
    }

    [Fact]
    public void ArcsToLines_limits_preserved_within_tolerance()
    {
        var text = "G0 X0 Y0\nG1 X20 Y0 F100\nG2 X20 Y20 I0 J10\nG1 X0 Y20\nG2 X0 Y0 I0 J-10\n";
        var before = GCodeLimitsCalculator.Compute(text)!;
        var after = GCodeLimitsCalculator.Compute(ArcsToLines(text))!;

        foreach (var axis in new[] { 'X', 'Y' })
        {
            var b = before.First(l => l.Letter == axis);
            var a = after.First(l => l.Letter == axis);
            Assert.True(Math.Abs(a.Min - b.Min) < 0.2d, $"{axis} min {a.Min} vs {b.Min}");
            Assert.True(Math.Abs(a.Max - b.Max) < 0.2d, $"{axis} max {a.Max} vs {b.Max}");
        }
    }

    [Fact]
    public void ArcsToLines_passes_through_other_tokens_unchanged()
    {
        var result = ArcsToLines(
            "G21\nG90\nM3 S1000\nG53 G0 X5 Y5\nG0 X1 Y1\nG1 X2 Y2 F100\nG2 X0 Y2 I-0.5 J0\n");
        var lines = result.Split('\n');

        Assert.Contains("G21", lines);
        Assert.Contains("G90", lines);
        Assert.Contains(lines, l => l.Contains("S1000") && l.Contains("M3")); // same-block words stay merged
        Assert.Contains("G53G0X5Y5", lines);
        Assert.Contains("G0X1Y1", lines);
        Assert.Contains("F100G1X2Y2", lines); // F and G1 keep sharing their original block
    }

    [Fact]
    public void ArcsToLines_does_not_mutate_the_source_tokens()
    {
        var program = GCodeProgram.Parse("G0 X10 Y0\nG2 X0 Y0 I-5 J0\nG5 X10 Y0 I5 J0 P5 Q0\n");
        var before = GCodeParser.TokensToGCode([.. program.Tokens]);

        GCodeTransformers.ArcsToLines(program.Tokens, 0.1d, program.Decimals);

        Assert.Equal(before, GCodeParser.TokensToGCode([.. program.Tokens]));
    }
}
