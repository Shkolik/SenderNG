/*
 * ToolpathBuilderTests.cs
 *
 * ToolpathBuilder flattens the emulator's walk over a parsed token stream into
 * per-axis ToolpathSegments (program/work frame) — the geometry source for the
 * plotter plugin. These tests pin: linear/rapid distinction, arc + helix
 * flattening (off-plane axes interpolate), G91 deltas, per-axis slicing for the
 * foam machine shape (second carriage on Z/A), frame agreement with the limits
 * calculator (the plot must agree with the program view's LimitRows), and the
 * line-number mapping the run-frontier overlay keys on.
 */

using CNC.Core;
using CNC.Core.Services;
using CNC.GCode;

namespace SenderNG.Core.Tests;

// Machine.WorkParametersProvider is a process-global mutable static: the frame
// test writes it, so the class joins the non-parallel collection shared with
// the other provider-writer tests.
[Collection("LegacyStatics")]
public class ToolpathBuilderTests
{
    private static IReadOnlyList<ToolpathSegment> Build(string gcode)
    {
        return ToolpathBuilder.Build(GCodeProgram.Parse(gcode).Tokens);
    }

    private static bool Same(double a, double b, double eps = 1e-6) => Math.Abs(a - b) < eps;

    [Fact]
    public void Empty_input_produces_no_segments()
    {
        Assert.Empty(ToolpathBuilder.Build(GCodeProgram.Parse("; comment only\n").Tokens));
        Assert.Empty(ToolpathBuilder.Build([]));
    }

    [Fact]
    public void Linear_moves_become_feed_segments_with_line_numbers()
    {
        var segments = Build("G1 X10 Y0 F100\nG1 Y10");

        Assert.Equal(2, segments.Count);
        Assert.All(segments, s => Assert.False(s.Rapid));

        Assert.Equal(0, segments[0].From[0]);
        Assert.Equal(10, segments[0].To[0]);
        Assert.Equal(0, segments[0].To[1]);
        Assert.Equal(1u, segments[0].LineNumber);

        Assert.Equal(10, segments[1].From[0]);
        Assert.Equal(10, segments[1].To[0]);
        Assert.Equal(10, segments[1].To[1]);
        Assert.Equal(2u, segments[1].LineNumber);
    }

    [Fact]
    public void G0_is_marked_rapid()
    {
        var segments = Build("G0 X5\nG1 X10 F100");

        Assert.Equal(2, segments.Count);
        Assert.True(segments[0].Rapid);
        Assert.False(segments[1].Rapid);
    }

    [Fact]
    public void Modal_and_zero_length_yields_are_dropped()
    {
        // F and M words yield zero-length actions from the emulator; only the
        // actual motion may appear.
        var segments = Build("G1 X10 F100\nM3 S1000\nG1 X10");

        var segment = Assert.Single(segments);
        Assert.Equal(1u, segment.LineNumber);
    }

    [Fact]
    public void Arc_flattens_into_a_chord_chain_ending_on_the_target()
    {
        // Full circle would test sweep special-casing; a quarter arc pins the
        // geometry: R10 from (0,0) to (10,10) around center (10,0), CCW.
        var segments = Build("G3 X10 Y10 I10 J0 F100");

        Assert.True(segments.Count > 2, "an arc must flatten to multiple chords");
        Assert.All(segments, s => Assert.False(s.Rapid));

        // Chains connect and the last chord lands on the commanded end point.
        for (var i = 1; i < segments.Count; i++)
        {
            Assert.Equal(segments[i - 1].To[0], segments[i].From[0]);
            Assert.Equal(segments[i - 1].To[1], segments[i].From[1]);
        }
        Assert.Equal(10, segments[^1].To[0]);
        Assert.Equal(10, segments[^1].To[1]);

        // Every chord vertex sits on the R10 circle around (10,0).
        foreach (var s in segments)
        {
            var dx = s.To[0] - 10;
            var dy = s.To[1] - 0;
            Assert.True(Same(Math.Sqrt(dx * dx + dy * dy), 10, 1e-3), $"vertex ({s.To[0]},{s.To[1]}) off circle");
        }
    }

    [Fact]
    public void Helical_arc_interpolates_the_off_plane_axis()
    {
        // Quarter arc XY with Z rising 0 -> 5: Z must interpolate across the chords.
        var segments = Build("G3 X10 Y10 Z5 I10 J0 F100");

        Assert.True(segments.Count > 2);
        Assert.True(Same(segments[0].From[2], 0));
        Assert.True(Same(segments[^1].To[2], 5));
        // Monotone rise across the chain.
        for (var i = 1; i < segments.Count; i++)
            Assert.True(segments[i].To[2] >= segments[i - 1].To[2] - 1e-9);
    }

    [Fact]
    public void G91_deltas_accumulate_like_the_controller_would()
    {
        // Feed is required on G1 (the parser rejects feed-less G1, error 22
        // semantics) — the first block carries it, modal afterwards.
        var segments = Build("G91\nG1 X5 F100\nG1 X5");

        Assert.Equal(2, segments.Count);
        Assert.Equal(0, segments[0].From[0]);
        Assert.Equal(5, segments[0].To[0]);
        Assert.Equal(5, segments[1].From[0]);
        Assert.Equal(10, segments[1].To[0]);
    }

    [Fact]
    public void Foam_machine_slices_second_carriage_from_Z_A_axes()
    {
        // A foam-style block: tower 1 rides X/Y, tower 2 rides Z/A. The foam
        // plane indices are 0,1 (plane 0) and 2,3 (plane 1) — the plotter reads
        // those from MachineType.Planes and slices the per-axis segment arrays.
        var segments = Build("G1 X10 Y2 Z8 A2 F100");

        var segment = Assert.Single(segments);
        // Tower 1 plane slice (indices 0,1): X 0->10, Y 0->2.
        Assert.Equal(0, segment.From[0]);
        Assert.Equal(10, segment.To[0]);
        Assert.Equal(0, segment.From[1]);
        Assert.Equal(2, segment.To[1]);
        // Tower 2 plane slice (indices 2,3): Z 0->8, A 0->2.
        Assert.Equal(0, segment.From[2]);
        Assert.Equal(8, segment.To[2]);
        Assert.Equal(0, segment.From[3]);
        Assert.Equal(2, segment.To[3]);
    }

    [Fact]
    public void PlaneAxisIndices_match_the_foam_carriage_decomposition()
    {
        var foam = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        Assert.Equal([0, 1], foam.PlaneAxisIndices(0));
        Assert.Equal([2, 3], foam.PlaneAxisIndices(1));

        var mill = MachineTypeCatalog.Resolve(MachineTypes.Mill);
        Assert.Equal([0, 1, 2], mill.PlaneAxisIndices(0));
    }

    [Fact]
    public void Frame_agrees_with_the_limits_calculator_on_a_G54_program()
    {
        // The frame check from the plan: regular moves carry the program
        // coordinates AS WRITTEN (a seeded G54 offset must NOT shift them —
        // the drawn path stays in the program/work frame, which is the frame
        // MachineState.Positions (WPos) reports, so the live glyph lands on
        // the path), G53 machine moves fold into that frame, and the path's
        // end extremes agree with the program view's LimitRows (which record
        // end positions only — the path additionally contains the initial
        // approach rapid from the assumed origin, so it may extend further).
        var savedAxes = CNC.Core.GrblInfo.NumAxes;
        try
        {
            Machine.WorkParametersProvider = () =>
            {
                var wp = WorkParameters.CreateDefault(3);
                wp.CoordinateSystems.First(c => c.Code == "G54").Values[0] = 100;
                wp.CoordinateSystems.First(c => c.Code == "G54").Values[1] = 50;
                return wp;
            };

            // Program frame preserved despite the active G54 offset: the final
            // point is the program's (15,15) — not offset-shifted (115,65) — and
            // the approach rapid lands exactly on the commanded (5,5).
            var segments = Build("G0 X5 Y5\nG1 X15 Y5 F100\nG1 X15 Y15");
            Assert.Equal(15, segments[^1].To[0]);
            Assert.Equal(15, segments[^1].To[1]);
            Assert.Equal(5, segments[0].To[0]);
            Assert.Equal(5, segments[0].To[1]);

            // G53 folds into the program frame: machine X0 with offset 100 → −100.
            // Modal motion form (G0 set by the previous block): the parser emits
            // one GCAbsLinearMotion carrying the axis words and the emulator
            // subtracts the active offsets. (The explicit-word form "G53 G0 X0"
            // loses the fold in this engine — the parser then also emits a plain
            // program-frame G0; the builder matches the engine, and the limits
            // calculator agrees, so plots stay consistent.) Untouched axes carry
            // their program-frame value over: Y stays 5.
            var g53 = Build("G0 X5 Y5\nG53 X0");
            var last = g53[^1];
            Assert.True(Same(last.To[0], -100), $"G53 X0 expected at −100 in program frame, got {last.To[0]}");
            Assert.True(Same(last.To[1], 5), $"G53 fold leaves Y untouched: expected 5, got {last.To[1]}");

            // End-extremes agreement with the limits rows (same program as above).
            var limits = GCodeLimitsCalculator.Compute("G0 X5 Y5\nG1 X15 Y5 F100\nG1 X15 Y15");
            Assert.NotNull(limits);
            var x = limits.First(l => l.Letter == 'X');
            var y = limits.First(l => l.Letter == 'Y');

            var maxX = segments.Max(s => Math.Max(s.From[0], s.To[0]));
            var maxY = segments.Max(s => Math.Max(s.From[1], s.To[1]));
            var minX = segments.Min(s => Math.Min(s.From[0], s.To[0]));

            Assert.Equal(x.Max, maxX);
            Assert.Equal(y.Max, maxY);
            // The path covers everything the limits report (and the approach).
            Assert.True(minX <= x.Min + 1e-9);
        }
        finally
        {
            Machine.WorkParametersProvider = null;
        }
    }

    [Fact]
    public void Tokens_are_never_mutated_by_the_build()
    {
        var program = GCodeProgram.Parse("G3 X10 Y10 I10 J0 F100");
        var before = program.Tokens.Select(t => t.ToString()).ToList();

        ToolpathBuilder.Build(program.Tokens);

        var after = program.Tokens.Select(t => t.ToString()).ToList();
        Assert.Equal(before, after);
    }
}
