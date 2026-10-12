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
 * Â· Redistributions of source code must retain the above copyright notice, this
 * list of conditions and the following disclaimer.
 *
 * Â· Redistributions in binary form must reproduce the above copyright notice, this
 * list of conditions and the following disclaimer in the documentation and/or
 * other materials provided with the distribution.
 *
 * Â· Neither the name of the copyright holder nor the names of its contributors may
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
        // Point (20,0) pivots around (10,0): a 90Â° swing lands on (10,10).
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

        // Feed word joins the motion on its original (same) line â€” canonical order.
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
        // deltas accumulate â€” X always rotates to the negative Y direction first.
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
    // M2 â€” ArcsToLines
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
        // G2 (CW) from (10,0) about center (5,0) to (0,0) â€” lower semicircle.
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

    // The chord-tolerance count divides by sqrt(a * (2r - a)), which is only real
    // while a < 2r. Both failure modes below are reachable at the default 0.1
    // tolerance with no user input, from a small-radius arc.

    [Theory]
    [InlineData(0.04d)]   // a*(2r-a) < 0 -> sqrt NaN -> (int)NaN == 0
    [InlineData(0.02d)]
    [InlineData(0.01d)]
    public void ArcsToLines_arc_smaller_than_half_the_tolerance_is_still_tessellated(double r)
    {
        // Pre-fix this collapsed to a single "G1X0Y0Z0" - a straight chord
        // across the arc, silently cutting the wrong geometry.
        var g1 = G1Lines(ArcsToLines($"G0 X{r} Y0\nG2 X0 Y0 I{-r / 2} J0 F100\n"));

        Assert.True(g1.Length > 1, $"r={r} produced {g1.Length} line(s): {string.Join(" ", g1)}");
    }

    [Fact]
    public async Task Arcs_to_lines_arc_exactly_half_the_tolerance_terminates()
    {
        // a*(2r-a) == 0 exactly: pre-fix the divisor was 0, the count came out
        // Infinity, and (int)Infinity is int.MaxValue - the sampler loop then ran
        // ~2.1e9 times appending points and wedged the process. Only a timeout
        // check is safe here; running it pre-fix does not return. WaitAsync
        // rather than Wait/Result so the overrun surfaces as a TimeoutException
        // and the analyzer does not flag a blocking call in a test (xUnit1031).
        var task = Task.Run(() => ArcsToLines("G0 X0.05 Y0\nG2 X0 Y0 I-0.025 J0 F100\n"));

        var result = await task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(G1Lines(result).Length > 1);
    }

    [Theory]
    [InlineData(0.1d)]
    [InlineData(0.3d)]
    [InlineData(0.5d)]
    [InlineData(0.9d)]
    public void ArcsToLines_a_coarser_typed_tolerance_does_not_swallow_small_arcs(double tolerance)
    {
        // Raising the tolerance is a reasonable thing to type for speed, and a
        // 0.2-radius arc survives a 0.5 tolerance only if the count is guarded.
        var g1 = G1Lines(ArcsToLines("G0 X0.2 Y0\nG2 X0 Y0 I-0.1 J0 F100\n", tolerance));

        Assert.True(g1.Length > 1, $"tol={tolerance} produced {g1.Length} line(s): {string.Join(" ", g1)}");
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(0d)]
    [InlineData(0.0001d)]
    public void ArcsToLines_a_tolerance_below_the_floor_is_clamped_not_mishandled(double tolerance)
    {
        // ArcsToLines already floors the tolerance at 0.001 (GCodeTransformers.cs:92),
        // so a nonsense value cannot reach the sampler's sqrt. Pinned here because
        // that floor is the only thing standing between a typed-in 0 and the
        // degenerate divisor, and it is easy to lose.
        var g1 = G1Lines(ArcsToLines("G0 X0.2 Y0\nG2 X0 Y0 I-0.1 J0 F100\n", tolerance));

        Assert.True(g1.Length > 1, $"tol={tolerance} produced {g1.Length} line(s): {string.Join(" ", g1)}");
    }

    // The spline side of the same parameter. Pre-fix it rewrote the caller's
    // arcResolution on every pass of the sampling loop, which is where the
    // "mutated inside the loop" defect lived; the resolved value must be the one
    // the whole run uses.

    [Theory]
    [InlineData(1.5d)]
    [InlineData(20d)]
    public void ArcsToLines_spline_large_tolerance_matches_the_zero_002_fallback(double tolerance)
    {
        // A value above 1 has no points-per-circle reading for a spline, so it
        // must land on the same output as passing the 0.002 fallback directly.
        Assert.Equal(G1Lines(ArcsToLines(SplineProgram, 0.002d)),
                     G1Lines(ArcsToLines(SplineProgram, tolerance)));
    }

    [Fact]
    public void ArcsToLines_spline_respects_a_tolerance_within_range()
    {
        // Below 1 the value is a real parameter step, so it must still bite:
        // a finer tolerance has to yield at least as many points as a coarser one.
        var coarse = G1Lines(ArcsToLines(SplineProgram, 0.5d));
        var fine = G1Lines(ArcsToLines(SplineProgram, 0.1d));

        Assert.True(fine.Length >= coarse.Length,
            $"fine {fine.Length} vs coarse {coarse.Length}");
    }

    private const string SplineProgram = "G0 X0 Y0\nG5 X10 Y0 I2 J5 P8 Q5 F100\n";

    [Fact]
    public void ArcsToLines_keeps_feed_before_first_segment()
    {
        // F300 sits AFTER the arc words on the same block â€” it must still be
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

    private static string Compress(string text, GCodeCommentMode mode = GCodeCommentMode.Keep)
    {
        var program = GCodeProgram.Parse(text);
        Assert.False(program.HasErrors, string.Join("; ", program.Errors));
        var lines = GCodeTransformers.Compress(program.Tokens, mode);
        return string.Join("\n", lines);
    }

    [Fact]
    public void Compress_merges_repeated_modal_words_and_preserves_motion()
    {
        const string input =
            "G21 G90\n" +
            "G1 X10 Y10 F100\n" +
            "G1 X20 Y10\n" +
            "G1 X20 Y20\n" +
            "M30\n";

        var result = Compress(input);
        var lines = result.Split('\n');

        // Modal G1 command words vanish after the first motion of a run (the
        // controller's modal state carries them); word tokens share the block
        // before the motion (F100G1X10Y10 â€” engine serialization order). The
        // serializer's compress drops only words equal to the run's FIRST
        // motion, so X20/Y20 survive here (upstream semantics).
        Assert.Contains("F100G1X10Y10", lines);
        Assert.Contains("X20Y10", lines);
        Assert.Contains("X20Y20", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("G1X20", StringComparison.Ordinal));

        // The motion is unchanged: limits before and after are identical, and
        // the output re-parses clean through the engine gate.
        var before = GCodeLimitsCalculator.Compute(GCodeProgram.Parse(input).Tokens);
        var after = GCodeLimitsCalculator.Compute(GCodeProgram.Parse(result).Tokens);
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].Min, after[i].Min, 3);
            Assert.Equal(before[i].Max, after[i].Max, 3);
        }
        Assert.False(GCodeProgram.Parse(result).HasErrors);
    }

    [Fact]
    public void Compress_keeps_comments_by_default_and_stripAll_drops_them()
    {
        const string input = "(header)\nG21\n; remark\nG1 X0 Y0 F100\n(operation 1)\nM30\n";

        var kept = Compress(input, GCodeCommentMode.Keep);
        Assert.Contains("(header)", kept.Split('\n'));
        Assert.Contains("(operation 1)", kept.Split('\n'));

        var stripped = Compress(input, GCodeCommentMode.StripAll);
        var strippedLines = stripped.Split('\n');
        Assert.DoesNotContain(strippedLines, l => l.StartsWith("(", StringComparison.Ordinal));
        Assert.False(GCodeProgram.Parse(stripped).HasErrors);
    }

    [Fact]
    public void Compress_drops_semicolon_remark_lines_like_any_token_transform()
    {
        // ';' lines never tokenize (the parser returns them as non-sendable
        // lines), so they cannot survive ANY token-level transform â€” verify the
        // inherent behavior is at least consistent between modes.
        const string input = "; remark\nG1 X0 Y0 F100\n";

        Assert.DoesNotContain(";", Compress(input, GCodeCommentMode.Keep));
        Assert.DoesNotContain(";", Compress(input, GCodeCommentMode.StripAll));
    }

    [Fact]
    public void Compress_does_not_mutate_the_source_tokens()
    {
        var program = GCodeProgram.Parse("G0 X10 Y20\nG1 X10 Y30 F100\nG2 X20 Y30 I5 J0\nG81 X5 Y5 R2 Z-1 F100\n");
        var before = GCodeParser.TokensToGCode([.. program.Tokens]);

        GCodeTransformers.Compress(program.Tokens, GCodeCommentMode.Keep);

        // Compress mode mutates the motion tokens it merges â€” the transform must
        // clone them so the caller's stream stays byte-identical.
        Assert.Equal(before, GCodeParser.TokensToGCode([.. program.Tokens]));
    }
}

