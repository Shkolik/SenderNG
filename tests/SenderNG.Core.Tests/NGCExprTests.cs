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

// Machine.WorkParametersProvider is a process-global mutable static, and every
// test class that builds a MainViewModel overwrites it from its constructor
// (MainViewModel.cs:271) without joining the LegacyStatics collection. These
// tests therefore seed the work-offset rows directly instead of going through
// that provider, so they cannot lose a race with a parallel class.
public class NGCExprTests
{
    /// <summary>
    /// Mirrors the translation Machine.Reset() applies to the snapshot's
    /// coordinate systems, without the process-global indirection.
    /// </summary>
    private sealed class SeededMachine : Machine
    {
        internal void Seed(WorkParameters seed)
        {
            foreach (WorkCoordinateSystem cs in seed.CoordinateSystems)
            {
                var row = new CoordinateSystem(cs.Code, string.Empty);
                int n = Math.Min(cs.Values.Length, row.Values.Length);
                for (int i = 0; i < n; i++)
                    row.Values[i] = cs.Values[i];
                coordinateSystems.Add(row);
            }
        }

        // SelectedTool has a protected setter, so a test picks the active tool
        // the way Reset() does.
        internal void Select(Tool tool) => SelectedTool = tool;
    }

    /// <summary>
    /// The coordinate-system offset read-only parameters (#5221..#5390) come in
    /// ten-id blocks, one per work-offset row, and the axis is the last digit:
    /// #5221-#5230 is G54 (X..), #5241-#5250 is G55, and so on in steps of 20.
    /// Each block's first id must resolve; a reversed range silently reads 0
    /// instead of failing, which is a wrong number, not a diagnostic.
    /// </summary>
    private static NGCExpr Seeded(out WorkParameters seed)
    {
        seed = WorkParameters.CreateDefault(3);

        // Distinct per (row, axis) so a test can tell the addressed cell apart
        // from every other one: row index * 10 + axis index.
        for (int row = 0; row < seed.CoordinateSystems.Count; row++)
            for (int axis = 0; axis < 3; axis++)
                seed.CoordinateSystems[row].Values[axis] = row * 10d + axis;

        var machine = new SeededMachine();
        machine.Seed(seed);

        return new NGCExpr(machine);
    }

    [Fact]
    public void Coordinate_offset_block_5321_reads_the_G59_row()
    {
        var expr = Seeded(out var seed);

        // Row 5 == G59, whose X is 5 * 10 + 0.
        Assert.Equal(50d, seed.CoordinateSystems[5].Values[0]);
        Assert.Equal(50d, expr.GetNumberedParameter(5321));

        // ... and the rest of the block addresses the same row, one id per axis.
        Assert.Equal(51d, expr.GetNumberedParameter(5322));
        Assert.Equal(52d, expr.GetNumberedParameter(5323));
    }

    [Fact]
    public void Every_coordinate_offset_block_reads_its_own_row()
    {
        var expr = Seeded(out var seed);

        for (int row = 0; row < seed.CoordinateSystems.Count; row++)
        {
            int block = 5221 + row * 20;

            for (int axis = 0; axis < 3; axis++)
            {
                int id = block + axis;
                Assert.True(expr.GetNumberedParameter(id, out double value), $"#{id} did not resolve");
                Assert.Equal(seed.CoordinateSystems[row].Values[axis], value);
            }
        }
    }

    [Fact]
    public void Gaps_between_offset_blocks_are_not_resolved()
    {
        var expr = Seeded(out _);

        // #5231-#5240 (and every other inter-block gap) addresses no row, so it
        // must read 0 rather than aliasing a neighbouring block.
        Assert.Equal(0d, expr.GetNumberedParameter(5231));
        Assert.Equal(0d, expr.GetNumberedParameter(5240));
    }

    // #<selected_tool> parses the controller's tool code. Every ToolTableEntry
    // carries an int Id, so through the normal load path the code is always a
    // bare integer — but Tool.Code is a plain settable string and a macro
    // expression can read it at any time. A code the parse cannot read must
    // report "no tool" (-1), not throw out of the expression evaluator.

    [Fact]
    public void Selected_tool_reads_the_number_of_a_numeric_tool_code()
    {
        var machine = new SeededMachine();
        machine.Select(new Tool("7"));

        Assert.Equal(7d, new NGCExpr(machine).GetNamedParameter(NGCExpr.NamedParam.selected_tool));
    }

    [Fact]
    public void Selected_tool_is_minus_one_when_no_tool_is_selected()
    {
        Assert.Equal(-1d, new NGCExpr(new SeededMachine()).GetNamedParameter(NGCExpr.NamedParam.selected_tool));
    }

    [Fact]
    public void Selected_tool_reports_minus_one_for_a_code_that_is_not_a_number()
    {
        var machine = new SeededMachine();
        machine.Select(new Tool("1.5mm"));   // grblHAL-style named tool

        Assert.Equal(-1d, new NGCExpr(machine).GetNamedParameter(NGCExpr.NamedParam.selected_tool));
    }

    [Fact]
    public void Selected_tool_reports_minus_one_for_an_empty_code()
    {
        var machine = new SeededMachine();
        machine.Select(new Tool(string.Empty));

        Assert.Equal(-1d, new NGCExpr(machine).GetNamedParameter(NGCExpr.NamedParam.selected_tool));
    }
}
