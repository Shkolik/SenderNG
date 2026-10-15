
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
using CNC.Core;
using Xunit;

namespace SenderNG.Core.Tests;

public class GCodeParserTests
{
    [Fact]
    public void Parse_G1_X10()
    {
        var b = GCodeParser.Parse("G1 X10 F200");
        Assert.Equal(1, b.Motion);
    }

    [Fact]
    public void Parse_G0_sets_motion_zero()
    {
        var b = GCodeParser.Parse("G0 X5 Y5");
        Assert.Equal(0, b.Motion);
    }

    [Fact]
    public void Parse_G1_F_and_X()
    {
        var b = GCodeParser.Parse("G1 X10 F200");
        Assert.Equal(10d, b.X);
        Assert.Equal(200d, b.F);
    }

    [Fact]
    public void Parse_G2_arc()
    {
        var b = GCodeParser.Parse("G2 X10 Y10 I5 J0");
        Assert.Equal(2, b.Motion);
        Assert.Equal(5d, b.I);
        Assert.Equal(0d, b.J);
    }

    [Fact]
    public void Parse_instance_ParseBlock_works()
    {
        var parser = new GCodeParser();
        string line = "G1 X10 F200";
        bool ok = parser.ParseBlock(ref line, false);
        Assert.True(ok);
        // tokens should contain linear motion
        Assert.NotEmpty(parser.Tokens);
    }

    [Fact]
    public void Parse_comment_returns_no_motion()
    {
        var b = GCodeParser.Parse("(comment)");
        Assert.Equal(-1, b.Motion); // or 0? check implementation returns -1 for no motion
    }

    [Theory]
    [InlineData("G54", "G54")]
    [InlineData("G55", "G55")]
    [InlineData("G59", "G59")]
    [InlineData("G59.3", "G59_3")]
    public void Coordinate_system_selection_emits_the_requested_command_without_controller_data(string block, string expected)
    {
        // Regression: with no controller data ($#) fetched, Machine.CoordSystem's
        // backing table is empty, its setter stores null and the getter returns 0.
        // The old emission formula (Commands.G54 + CoordSystem - 1) then degenerated
        // every selection to Commands.G53, crashing the emulator's GCAbsLinearMotion
        // cast when the program was opened (InvalidCastException on the UI thread).
        var program = GCodeProgram.Parse(block + "\n");
        var token = Assert.Single(program.Tokens);
        Assert.Equal(expected, token.Command.ToString());
        Assert.False(program.HasErrors);
    }

    [Fact]
    public void Freecad_program_with_coordinate_selection_survives_limits_computation()
    {
        // Stand-in for the exact crashing shape of a FreeCAD-exported .nc:
        // G17 G90 / G21 preamble followed by G54 before any motion.
        var program = GCodeProgram.Parse("G17 G90\nG21\nG54\nM3 S10000.0\nG0 X0.000 Y0.000 Z2.000\nG1 X15.107 Y11.888 Z0.225 F120.000\nM5\nG17 G90\nM2\n");
        Assert.False(program.HasErrors);
        var limits = GCodeLimitsCalculator.Compute(program.Tokens);
        Assert.NotNull(limits);
        Assert.DoesNotContain(program.Tokens, t => t is GCodeToken && t.Command == CNC.GCode.Commands.G53);
    }

    [Fact]
    public void Emulator_skips_degenerate_G53_token_instead_of_crashing()
    {
        // Defense in depth: a non-motion token with Command == G53 must not take
        // the app down through an InvalidCastException on file open.
        var emu = new GCodeEmulator(true);
        var actions = emu.Execute([new GCodeToken(CNC.GCode.Commands.G53, 1)]).ToList();
        // No throw (was InvalidCastException); the degenerate token is reported
        // like any other non-motion command instead of being simulated.
        Assert.Single(actions);
    }
}

/// <summary>
/// AxisFlags.All is the "every axis" mask the machine and emulator walk, so it
/// must cover U/V/W too (ioSender defines it as 0x1FF; this port had narrowed it
/// to 0x3F).
/// </summary>
[Collection("LegacyStatics")]
public class AxisFlagsAllTests
{
    [Fact]
    public void All_covers_all_nine_axes()
    {
        Assert.Equal(Enumerable.Range(0, 9), AxisFlags.All.ToIndices());
    }

    [Fact]
    public void A_tool_offset_copies_the_UVW_slots_too()
    {
        var previous = Machine.WorkParametersProvider;
        var seed = WorkParameters.CreateDefault(9, 1);
        seed.Tools.First(t => t.Id == 1).Offsets[6] = 5;   // U
        seed.Tools.First(t => t.Id == 1).Offsets[8] = 7;   // W

        try
        {
            Machine.WorkParametersProvider = () => seed;
            var machine = new Machine();
            machine.Reset();

            Assert.True(machine.SetToolOffset(new GCToolOffset(CNC.GCode.Commands.G43, 1, 1)));

            Assert.Equal(5, machine.GetToolOffset(6));
            Assert.Equal(7, machine.GetToolOffset(8));

            machine.CancelToolCompensation();
            Assert.Equal(0, machine.GetToolOffset(6));
            Assert.Equal(0, machine.GetToolOffset(8));
        }
        finally
        {
            Machine.WorkParametersProvider = previous;
        }
    }

    [Fact]
    public void G28_without_axis_words_moves_every_axis_including_UVW()
    {
        // G28 with no axis words means "all axes to the predefined position", so
        // the U/V/W slots must be part of the walk — the emulator falls back to
        // AxisFlags.All, and the absence of U/V/W there left U at its previous
        // value while claiming the move had happened.
        var previous = Machine.WorkParametersProvider;
        var seed = WorkParameters.CreateDefault(9);
        var g28 = new WorkCoordinateSystem("G28", 0, 9);
        g28.Values[6] = 3;   // U
        seed.CoordinateSystems.Add(g28);

        try
        {
            Machine.WorkParametersProvider = () => seed;

            var program = GCodeProgram.Parse("G0 U9\nG28\n");
            var emu = new GCodeEmulator(true);
            foreach (var _ in emu.Execute(program.Tokens.ToList()))
            {
            }

            Assert.Equal(3, emu.GetPosition(6));
        }
        finally
        {
            Machine.WorkParametersProvider = previous;
        }
    }
}
