/*
 * EmulatorWorkParametersBridgeTests.cs
 *
 * The emulator (GCodeEmulator) and expression engine (NGCExpr) start from the
 * controller's WorkParameters snapshot through Machine.WorkParametersProvider
 * instead of the stubbed legacy statics. These tests pin the bridge: seeded
 * coordinate systems/G28/G30/G92 rows, G10 writes, probe expressions and the
 * tool table must all be honored, and a null provider must preserve the old
 * empty-table behavior.
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


using CNC.Core;
using CNC.Core.Services;

namespace SenderNG.Core.Tests;

// Machine.WorkParametersProvider is a process-global mutable static that the
// LegacyStatics classes write; all such tests opt into the same non-parallel
// collection so readers and writers never race.
[Collection("LegacyStatics")]
public class EmulatorWorkParametersBridgeTests
{
    // Machine.WorkParametersProvider is a process-global static that parallel
    // test classes also write, so every test manages it with try/finally
    // instead of relying on ambient state. The emulator copies its seed at
    // construction, so assertions on the returned instance are safe after the
    // provider has been restored.
    private static GCodeEmulator Run(string gcode, WorkParameters? seed)
    {
        try
        {
            Machine.WorkParametersProvider = () => seed;

            var program = GCodeProgram.Parse(gcode);
            var emu = new GCodeEmulator(true);
            foreach (var _ in emu.Execute(program.Tokens.ToList()))
            {
            }
            return emu;
        }
        finally
        {
            Machine.WorkParametersProvider = null;
        }
    }

    private static WorkParameters Seeded()
    {
        return WorkParameters.CreateDefault(3);
    }

    [Fact]
    public void Seeded_active_system_and_G10_P0_write_use_the_snapshot_table()
    {
        var wp = Seeded();
        wp.ActiveCoordinateSystem = "G55";
        wp.CoordinateSystems.First(c => c.Code == "G55").Values[0] = 10;

        var emu = Run("G10 L2 P0 X1", wp);

        // G55 selected from the snapshot (Id 2: G54=1, G55=2) and its row got the write.
        Assert.Equal(2, emu.CoordSystem);
        Assert.Equal(1, emu.GetCoordSystem(1).Values[0]); // index 1 == G55
    }

    [Fact]
    public void G10_L2_Pn_writes_into_the_seeded_system()
    {
        var emu = Run("G10 L2 P2 X20", Seeded());

        Assert.Equal(20, emu.GetCoordSystem(1).Values[0]); // P2 == G55
    }

    [Fact]
    public void G28_moves_to_the_seeded_predefined_position()
    {
        var wp = Seeded();
        wp.CoordinateSystems.Add(new WorkCoordinateSystem("G28", 0, 3));
        wp.CoordinateSystems.Last().Values[0] = 100;

        var emu = Run("G0 X5\nG28", wp);

        Assert.Equal(100, emu.GetPosition(0));
    }

    [Fact]
    public void G92_reads_and_resets_the_seeded_G92_row()
    {
        var wp = Seeded();
        wp.CoordinateSystems.Add(new WorkCoordinateSystem("G92", 0, 3));
        wp.CoordinateSystems.Last().Values[0] = 3;

        var emu = Run("G92 X5", wp);
        Assert.Equal(5, emu.GetG92Offset(0));

        var viaG10 = Run("G10 L2 P10 X7", wp);
        Assert.Equal(7, viaG10.GetG92Offset(0)); // P10 addresses the G92 row

        var reset = Run("G92 X5\nG92.1", wp);
        Assert.Equal(0, reset.GetG92Offset(0));
    }

    [Fact]
    public void PRB_expressions_read_the_seeded_probe_result()
    {
        var wp = Seeded();
        wp.ProbePosition = [1.0, 2.0, 3.0];
        wp.ProbeSuccess = true;

        var emu = Run("G0 X[#5061] Y[#5062] Z[#5070]", wp);

        Assert.Equal(1.0, emu.GetPosition(0));
        Assert.Equal(2.0, emu.GetPosition(1));
        Assert.Equal(1.0, emu.GetPosition(2)); // #5070 == probe result (success)
    }

    [Fact]
    public void Seeded_tool_table_makes_G43_functional()
    {
        var wp = Seeded();
        var tool = new ToolTableEntry(1, 3);
        tool.Offsets[2] = 5;
        wp.Tools.Add(tool);

        var emu = Run("T1 G43 H1", wp);

        Assert.Equal("1", emu.SelectedTool?.Code);
        Assert.Equal(5, emu.GetToolOffset(2));
    }

    [Fact]
    public void Null_provider_preserves_the_empty_table_behavior()
    {
        var plain = Run("G55\nG0 X5\nG28", null);
        Assert.Equal(0, plain.CoordSystem);      // no active system
        Assert.Equal(5, plain.GetPosition(0));   // moves still execute (offsets 0)
        Assert.Equal(0, plain.GetG92Offset(0));  // no G92 row

        var probe = Run("G0 X[#5061]", null);
        // Unresolvable expression: the engine reports a bad parameter, the word is
        // dropped, and the move never executes (pre-bridge behavior).
        Assert.Equal(0, probe.GetPosition(0));

        var tooled = Run("T1 G43 H1", null);
        Assert.Equal(0, tooled.GetToolOffset(2)); // no tools
    }
}
