/*
 * GCodeWrapTests.cs - engine tests for GCodeTransformers.AxisRemap (rotary wrap/unwrap)
 */

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

using CNC.Core.Services; // GCodeProgram lives in CNC.Core.Services, not CNC.GCode
using CNC.GCode;
using Xunit;

namespace SenderNG.Core.Tests;

public class GCodeWrapTests
{
    // A diameter of 360/π makes the cylinder exactly 1 mm per degree, so scale
    // and every feed conversion in these tests are exact and readable.
    private const double OneMmPerDegree = 360d / System.Math.PI;

    private static string Wrap(string text, int fromAxis = 0, int toAxis = 3,
        double diameter = OneMmPerDegree, double zShift = 0d)
        => Remap(text, fromAxis, toAxis, diameter, toAxisIsRotary: true, zShift);

    private static string Unwrap(string text, int fromAxis = 3, int toAxis = 0,
        double diameter = OneMmPerDegree, double zShift = 0d)
        => Remap(text, fromAxis, toAxis, diameter, toAxisIsRotary: false, zShift);

    private static string Remap(string text, int fromAxis, int toAxis, double diameter,
        bool toAxisIsRotary, double zShift)
    {
        var program = GCodeProgram.Parse(text);
        Assert.False(program.HasErrors, string.Join("; ", program.Errors));
        var mmPerDegree = diameter * System.Math.PI / 360d;
        var lines = GCodeTransformers.AxisRemap(program.Tokens, fromAxis, toAxis,
            mmPerDegree, toAxisIsRotary, program.Decimals, zShift, "Wrap applied");
        return string.Join("\n", lines);
    }

    [Fact]
    public void Wrap_maps_the_source_axis_onto_the_rotary_axis()
    {
        var result = Wrap("G0 X10\n");
        Assert.Equal(["(Wrap applied)", "G0A10"], result.Split('\n'));
    }

    [Fact]
    public void Wrap_maps_y_onto_b()
    {
        var result = Wrap("G0 Y10\n", fromAxis: 1, toAxis: 4);
        Assert.Equal(["(Wrap applied)", "G0B10"], result.Split('\n'));
    }

    [Fact]
    public void Unwrap_maps_the_rotary_axis_onto_the_linear_axis()
    {
        var result = Unwrap("G0 A10\n");
        Assert.Equal(["(Wrap applied)", "G0X10"], result.Split('\n'));
    }

    [Fact]
    public void Wrap_leaves_motions_without_the_source_axis_untouched()
    {
        var result = Wrap("G0 Y5\n"); // from X, so the Y-only move passes through
        Assert.Equal(["(Wrap applied)", "G0Y5"], result.Split('\n'));
    }

    [Fact]
    public void Wrap_keeps_other_axes_and_orders_words_by_axis()
    {
        var result = Wrap("G0 X10 Y5\n");
        Assert.Equal(["(Wrap applied)", "G0Y5A10"], result.Split('\n'));
    }

    [Fact]
    public void Wrap_shifts_z_by_the_center_offset()
    {
        var result = Wrap("G90\nG0 Z0\n", zShift: 25d);
        Assert.Equal(["(Wrap applied)", "G90", "G0Z25"], result.Split('\n'));
    }

    [Fact]
    public void Wrap_scales_incremental_deltas()
    {
        var result = Wrap("G91\nG0 X10\n");
        Assert.Equal(["(Wrap applied)", "G91", "G0A10"], result.Split('\n'));
    }

    [Fact]
    public void Wrap_leaves_g53_machine_coordinate_moves_untouched()
    {
        var result = Wrap("G53 G0 X10 Y0\n");
        Assert.Equal(["(Wrap applied)", "G53G0X10Y0"], result.Split('\n'));
    }

    [Fact]
    public void Wrap_does_not_shift_z_on_a_g53_machine_coordinate_move()
    {
        // A G53 line that also spells its motion splits into the passthrough G53
        // token plus a plain motion token carrying the axis words — in either word
        // order. The center shift must not touch the machine coordinates.
        var result = Wrap("G53 G0 Z0\nG0 G53 Z0\n", zShift: 25d);
        Assert.Equal(["(Wrap applied)", "G53G0Z0", "G53G0Z0"], result.Split('\n'));
    }

    [Fact]
    public void Wrap_refuses_arcs()
    {
        var program = GCodeProgram.Parse("G2 X10 Y0 I5 J0\n");
        Assert.Throws<InvalidOperationException>(() =>
            GCodeTransformers.AxisRemap(program.Tokens, 0, 3, 1d, true, program.Decimals));
    }

    [Fact]
    public void Wrap_does_not_mutate_the_source_tokens()
    {
        var program = GCodeProgram.Parse("G0 X10 Y0\nG0 X0 Y10\n");
        var before = GCodeParser.TokensToGCode([.. program.Tokens]);

        GCodeTransformers.AxisRemap(program.Tokens, 0, 3, 1d, true, program.Decimals);

        Assert.Equal(before, GCodeParser.TokensToGCode([.. program.Tokens]));
    }

    [Fact]
    public void Wrap_sets_units_per_minute_feed_for_a_pure_rotary_move()
    {
        // X-only move → A-only move; D=360/π keeps the feed numerically unchanged.
        var result = Wrap("G21\nG1 X10 F100\n");
        Assert.Equal(["(Wrap applied)", "G21", "F100G1A10"], result.Split('\n'));
    }

    [Fact]
    public void Wrap_sets_inverse_time_feed_for_a_mixed_rotary_linear_move()
    {
        // X becomes A (6); the remaining linear path is Y = 8mm — the remapped
        // axis is excluded (upstream parity); inverse-time F = 100/8 = 12.5.
        var result = Wrap("G1 X6 Y8 F100\n");
        Assert.Equal(["(Wrap applied)", "G93F12.5G1Y8A6"], result.Split('\n'));
    }

    [Fact]
    public void Unwrap_sets_units_per_minute_feed_for_a_pure_linear_move()
    {
        var result = Unwrap("G1 A10 F100\n");
        Assert.Equal(["(Wrap applied)", "F100G1X10"], result.Split('\n'));
    }

    [Fact]
    public void Unwrap_sets_inverse_time_feed_for_a_mixed_move()
    {
        // A→X (10 deg = 10mm) + Y 10mm → path (the non-target part) = 10mm.
        var result = Unwrap("G1 A10 Y10 F100\n");
        Assert.Equal(["(Wrap applied)", "G93F10G1X10Y10"], result.Split('\n'));
    }

    [Fact]
    public void Feed_mode_returns_to_units_per_minute_after_a_mixed_move()
    {
        var result = Wrap("G1 X6 Y8 F100\nG1 Z-1 F50\n");
        Assert.Equal(
            ["(Wrap applied)", "G93F12.5G1Y8A6", "G94F50G1Z-1"],
            result.Split('\n'));
    }

    [Fact]
    public void A_repeated_feed_is_not_re_emitted()
    {
        var result = Wrap("G1 X10 F100\nG1 X20\n");
        Assert.Equal(["(Wrap applied)", "F100G1A10", "G1A20"], result.Split('\n'));
    }

    [Fact]
    public void A_standalone_feed_word_keeps_its_own_block()
    {
        var result = Wrap("F100\nG1 X10\n");
        Assert.Equal(["(Wrap applied)", "F100", "G1A10"], result.Split('\n'));
    }

    [Fact]
    public void Wrap_scales_by_the_diameter_and_restores_the_program_feed_after_a_rotary_move()
    {
        // D = 100mm → 0.8727 mm/deg → 1/0.8727 = 1.1459 deg/mm. X10 becomes
        // A11.459 fed at 114.6 deg/min (= 100 mm/min of surface); the following
        // Y-only move must go back to the program's 100 mm/min, not inherit 114.6
        // (an unrestored converted F runs that move ~15% fast).
        var result = Wrap("G1 X10 F100\nG1 Y5\n", diameter: 100d);
        Assert.Equal(["(Wrap applied)", "F114.6G1A11.459", "F100G1Y5"], result.Split('\n'));
    }

    [Fact]
    public void Unwrap_scales_by_the_diameter_for_a_pure_linear_move()
    {
        // D = 100mm → A10 is 8.727mm of surface; the pure linear move keeps G94
        // with F = 100 × 0.8727 = 87.3 mm/min.
        var result = Unwrap("G1 A10 F100\n", diameter: 100d);
        Assert.Equal(["(Wrap applied)", "F87.3G1X8.727"], result.Split('\n'));
    }

    [Fact]
    public void Unwrap_inverse_time_feed_is_feed_over_dist_not_scaled()
    {
        // A→X (8.727mm) + Y 10mm: dist is the REMAINING linear path (Y = 10mm)
        // and the formula is F = feed/dist — no mmPerDegree factor.
        var result = Unwrap("G1 A10 Y10 F100\n", diameter: 100d);
        Assert.Equal(["(Wrap applied)", "G93F10G1X8.727Y10"], result.Split('\n'));
    }

    [Fact]
    public void Wrap_uses_units_per_minute_when_a_mixed_move_has_no_linear_travel()
    {
        // Line 2 repeats Z5, so there is no Z travel and dist == 0: the move must
        // fall back to G94 with the converted feed, never to G93 with F0.
        var result = Wrap("G1 X10 Z5 F100\nG1 X20 Z5\n");
        Assert.Equal(
            ["(Wrap applied)", "G93F20G1Z5A10", "G94F100G1Z5A20"],
            result.Split('\n'));
    }

    [Fact]
    public void Wrap_restores_units_per_minute_and_the_program_feed_on_a_rapid()
    {
        // A G0 carries no feed semantics, but it must not leave inverse time (and
        // its F) in effect for the next G1: ensure G94 and restore F = feed.
        var result = Wrap("G1 X6 Y8 F100\nG0 X20\n");
        Assert.Equal(
            ["(Wrap applied)", "G93F12.5G1Y8A6", "G94F100G0A20"],
            result.Split('\n'));
    }

    [Fact]
    public void Wrap_does_not_let_a_g53_block_pollute_the_next_moves_feed()
    {
        // The G53 move is in MACHINE coordinates: it must not become the "previous
        // position" of the next move, or the inverse-time feed is computed from a
        // bogus distance (100/|8-100| = 1.1 instead of 100/8 = 12.5).
        var result = Wrap("G53 G0 Y100\nG1 X6 Y8 F100\n");
        Assert.Equal(
            ["(Wrap applied)", "G53G0Y100", "G93F12.5G1Y8A6"],
            result.Split('\n'));
    }

    [Fact]
    public void Wrap_does_not_count_the_center_shift_as_travel()
    {
        // D = 100mm with Z0 at center shifts every Z by +50. At Z0 that shift is a
        // coordinate translation, not motion, so the move is a pure rotary one:
        // G94 with the converted feed, not G93 fed from a phantom 50mm of travel.
        var result = Wrap("G1 X10 Z0 F100\n", diameter: 100d, zShift: 50d);
        Assert.Equal(["(Wrap applied)", "F114.6G1Z50A11.459"], result.Split('\n'));
    }

    [Fact]
    public void Wrap_preserves_the_duration_of_a_source_inverse_time_pure_move()
    {
        // A G93 F is a duration (1/min), not a speed: the source move takes 1/5 min,
        // so the converted units/min feed must be |deltaTarget| x F = 10 deg * 5.
        var result = Wrap("G93\nG1 X10 F5\n");
        Assert.Equal(["(Wrap applied)", "G93", "G94F50G1A10"], result.Split('\n'));
    }

    [Fact]
    public void Wrap_keeps_a_source_inverse_time_feed_verbatim_on_a_mixed_move()
    {
        // The move is already expressed in inverse time and the geometry is only
        // re-expressed, so its duration (and therefore its F) is unchanged.
        var result = Wrap("G93\nG1 X6 Y8 F10\n");
        Assert.Equal(["(Wrap applied)", "G93", "F10G1Y8A6"], result.Split('\n'));
    }

    [Fact]
    public void Wrap_refuses_g95_units_per_revolution_feeds()
    {
        // The feed model is distance-based; G95 would silently produce a wrong F.
        var program = GCodeProgram.Parse("G95\nG1 X10 F0.5\n");
        Assert.Throws<InvalidOperationException>(() =>
            GCodeTransformers.AxisRemap(program.Tokens, 0, 3, 1d, true, program.Decimals));
    }
}
