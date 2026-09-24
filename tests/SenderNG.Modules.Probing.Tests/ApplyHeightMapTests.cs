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
using CNC.GCode;
using SenderNG.Core.Modules.Probing;
using Xunit;

namespace SenderNG.Modules.Probing.Tests;

public class ApplyHeightMapTests
{
    private static string HeightMap(string text, Func<double, double, double> surface, double segmentLength = 5)
    {
        var program = GCodeProgram.Parse(text);
        Assert.False(program.HasErrors, string.Join("; ", program.Errors));
        var lines = GCodeTransformers.ApplyHeightMap(program.Tokens, surface, segmentLength, program.Decimals);
        return string.Join("\n", lines);
    }

    [Fact]
    public void ApplyHeightMap_corrects_short_moves_in_one_segment()
    {
        // Move shorter than the grid spacing: corrected once at the endpoint;
        // the block's feed is hoisted ahead of the segment.
        var result = HeightMap("G1 X3 Y0 F100", (_, _) => 2);

        Assert.Equal(
            [
                "(Height map applied)",
                "F100",
                "G1X3Y0Z2",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void ApplyHeightMap_splits_feed_moves_at_grid_spacing()
    {
        // 10 mm move, 5 mm grid: two segments, each endpoint corrected; every
        // segment re-states its Z (upstream parity).
        var result = HeightMap("G1 X10 Y0 F100", (_, _) => 2);

        Assert.Equal(
            [
                "(Height map applied)",
                "F100",
                "G1X5Y0Z2",
                "G1X10Y0Z2",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void ApplyHeightMap_corrects_rapids_once_at_the_endpoint()
    {
        // Rapids are not split (upstream parity) — one corrected endpoint.
        var result = HeightMap("G0 X10 Y0", (_, _) => 2);

        Assert.Equal(
            [
                "(Height map applied)",
                "G0X10Y0Z2",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void ApplyHeightMap_replaces_the_existing_Z_word()
    {
        // The line-based transform APPENDED a second Z word (invalid on a
        // controller); the token path rebuilds the word properly, and the Z
        // interpolates along the move before the surface correction is added.
        var result = HeightMap("G1 X4 Z5 F100", (_, _) => 2);

        Assert.Equal(
            [
                "(Height map applied)",
                "F100",
                "G1X2Z4.5",
                "G1X4Z7",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void ApplyHeightMap_emits_deltas_for_G91_programs()
    {
        // G91 output is deltas (upstream emits absolutes — broken on a
        // controller); they accumulate to the same total travel.
        var result = HeightMap("G91\nG1 X10 Y0 F100", (_, _) => 2);

        Assert.Equal(
            [
                "(Height map applied)",
                "G91",
                "F100",
                "G1X5Z2",
                "G1X5Z2",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void ApplyHeightMap_splits_arcs_into_sub_arcs_with_recomputed_IJ()
    {
        // Quarter circle (10,0) -> (0,10) about center (0,0) (I=-10, J=0): the
        // sub-arcs stay G3, the first I/J = center - sub-arc start, the last
        // segment lands exactly on the arc end, and every segment carries the
        // surface Z. The positioning rapid is corrected once (not split).
        var lines = HeightMap("G0 X10 Y0\nG3 X0 Y10 I-10 J0 F100", (_, _) => 2, segmentLength: 5).Split('\n');

        Assert.Equal("(Height map applied)", lines[0]);
        Assert.Equal("G0X10Y0Z2", lines[1]);
        var arcs = lines.Where(l => l.StartsWith("G3")).ToList();
        Assert.True(arcs.Count >= 2, string.Join(" | ", lines));
        Assert.Contains("I-10", arcs[0]);
        Assert.Contains("J0", arcs[0]);
        Assert.Contains("Z2", arcs[0]);
        // The last sub-arc lands exactly on the arc end; its Z word is diffed
        // away because the surface is flat (upstream last-segment semantics).
        Assert.Contains("G3X0Y10", arcs[^1]);
    }

    [Fact]
    public void ApplyHeightMap_keeps_radius_mode_arcs_with_their_R()
    {
        var result = HeightMap("G3 X0 Y10 R10 F100", (_, _) => 2, segmentLength: 0);

        Assert.Equal(
            [
                "(Height map applied)",
                "F100",
                "G3X0Y10Z2R10",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void ApplyHeightMap_refuses_arcs_outside_the_XY_plane()
    {
        var program = GCodeProgram.Parse("G18\nG2 X10 Y5 R5 F100");
        Assert.False(program.HasErrors);

        Assert.Throws<InvalidOperationException>(() =>
            GCodeTransformers.ApplyHeightMap(program.Tokens, (_, _) => 2, 5, program.Decimals));
    }

    [Fact]
    public void ApplyHeightMap_passes_non_motion_tokens_through()
    {
        var result = HeightMap("G21\nM3 S1000\nG1 X3 Y0 F100", (_, _) => 2);

        Assert.Equal(
            [
                "(Height map applied)",
                "G21",
                "S1000M3",
                "F100",
                "G1X3Y0Z2",
            ],
            result.Split('\n'));
    }

    [Fact]
    public void ApplyHeightMap_output_reparses_through_the_engine_gate()
    {
        var result = HeightMap("G1 X10 Y0 F100\nG2 X20 Y0 I5 J0 F100", (x, y) => Math.Sin(x / 10d));

        Assert.False(GCodeProgram.Parse(result).HasErrors);
    }

    [Fact]
    public void ApplyHeightMap_does_not_mutate_the_source_tokens()
    {
        var program = GCodeProgram.Parse("G0 X10 Y20\nG1 X10 Y30 F100\nG2 X20 Y30 I5 J0\n");
        var before = GCodeParser.TokensToGCode([.. program.Tokens]);

        GCodeTransformers.ApplyHeightMap(program.Tokens, (_, _) => 2, 5, program.Decimals);

        Assert.Equal(before, GCodeParser.TokensToGCode([.. program.Tokens]));
    }

    // --------------------------------------------------- rotation angle chain

    [Fact]
    public void Rotation_chain_rotates_the_program_parallel_to_the_probed_edge()
    {
        // 15° misalignment — large enough that upstream's slope-as-radians
        // approximation is measurably wrong (rotating by 0.2679 instead of
        // atan(0.2679)) and the Atan fix provably right.
        var theta = 15d * Math.PI / 180d;
        var p1 = new Pos(50, 20, 0);
        var p2 = new Pos(100, 20 + 50 * Math.Tan(theta), 0);

        var slope = ProbeMath.ComputeRotationAngle(Edge.AB, p1, p2);
        Assert.Equal(Math.Tan(theta), slope, 6);
        var angle = ProbeMath.ComputeRotationRadians(Edge.AB, p1, p2);
        Assert.Equal(theta, angle, 6);

        // The workpiece's top edge, drawn axis-aligned in the program.
        var program = GCodeProgram.Parse("G21\nG1 X50 Y20 F100\nG1 X100 Y20 F100");
        var lines = GCodeTransformers.Rotate(program.Tokens, angle, 0, 0, program.Decimals);
        var rotated = GCodeProgram.Parse(string.Join("\n", lines));
        Assert.False(rotated.HasErrors, string.Join("; ", rotated.Errors));

        // Invariant: after apply the program edge is parallel to the probed
        // edge (slope equal within rounding) — the cut follows the workpiece.
        var moves = rotated.Tokens.OfType<GCLinearMotion>()
            .Where(m => m.AxisFlags.HasFlag(AxisFlags.X) && m.AxisFlags.HasFlag(AxisFlags.Y)).ToList();
        Assert.Equal(2, moves.Count);
        var d = new double[] { moves[1].Values[0] - moves[0].Values[0], moves[1].Values[1] - moves[0].Values[1] };
        Assert.Equal(slope, d[1] / d[0], 3);
    }

    [Fact]
    public void Rotation_chain_handles_vertical_edges_via_the_inverse_slope()
    {
        // Left edge (AD): ComputeRotationAngle returns -1/raw-slope; the chain
        // must still rotate the program's vertical edge parallel to the probe.
        var theta = 15d * Math.PI / 180d;
        var p1 = new Pos(100, 50, 0);
        var p2 = new Pos(100 + 50 * Math.Tan(theta), 0, 0);

        var raw = ProbeMath.ComputeRotationAngle(Edge.AD, p1, p2);
        Assert.Equal(Math.Tan(theta), raw, 6);
        var angle = ProbeMath.ComputeRotationRadians(Edge.AD, p1, p2);
        Assert.Equal(theta, angle, 6);

        var program = GCodeProgram.Parse("G1 X100 Y50 F100\nG1 X100 Y0 F100");
        var lines = GCodeTransformers.Rotate(program.Tokens, angle, 0, 0, program.Decimals);
        var rotated = GCodeProgram.Parse(string.Join("\n", lines));
        Assert.False(rotated.HasErrors, string.Join("; ", rotated.Errors));

        var moves = rotated.Tokens.OfType<GCLinearMotion>()
            .Where(m => m.AxisFlags.HasFlag(AxisFlags.X) && m.AxisFlags.HasFlag(AxisFlags.Y)).ToList();
        Assert.Equal(2, moves.Count);
        var d = new double[] { moves[1].Values[0] - moves[0].Values[0], moves[1].Values[1] - moves[0].Values[1] };
        var rawSlope = (p2.Y - p1.Y) / (p2.X - p1.X);
        Assert.Equal(rawSlope, d[1] / d[0], 2);
    }
}