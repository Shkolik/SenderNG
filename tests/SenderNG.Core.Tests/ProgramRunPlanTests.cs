/*
 * ProgramRunPlanTests.cs - entry-range plans for partial program sessions
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

using CNC.Core.Services;
using Xunit;

namespace SenderNG.Core.Tests;

public class ProgramRunPlanTests
{
    private static GCodeProgram ProgramOf(params string[] lines) =>
        GCodeProgram.Parse(string.Join("\n", lines));

    [Fact]
    public void Whole_keeps_every_entry_in_program_order()
    {
        var program = ProgramOf("G21", "G0 X10", "M5");

        var plan = ProgramRunPlan.Whole(program);

        Assert.Equal(program.Entries, plan.Entries);
    }

    [Fact]
    public void FromLine_keeps_the_row_and_everything_after_it()
    {
        var program = ProgramOf("G21", "G0 X10", "G1 X20 F100", "M5");

        var plan = ProgramRunPlan.FromLine(program, 2);

        Assert.Equal(["G1 X20 F100", "M5"], plan.Entries.Select(e => e.Text));
        // Sequences are the program's own, so progress maps back to grid rows.
        Assert.Equal([3, 4], plan.Entries.Select(e => e.Sequence));
    }

    [Fact]
    public void FromLine_zero_is_the_whole_program()
    {
        var program = ProgramOf("G21", "M5");

        var plan = ProgramRunPlan.FromLine(program, 0);

        Assert.Equal(program.Entries, plan.Entries);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void FromLine_out_of_range_yields_an_empty_plan(int row)
    {
        var program = ProgramOf("G21", "M5");

        var plan = ProgramRunPlan.FromLine(program, row);

        Assert.Empty(plan.Entries);
    }

    [Fact]
    public void Selection_sorts_dedupes_and_preserves_file_order()
    {
        var program = ProgramOf("G21", "G0 X10", "G1 X20 F100", "M5");

        var plan = ProgramRunPlan.Selection(program, [3, 1, 3, 2]);

        Assert.Equal(["G0 X10", "G1 X20 F100", "M5"], plan.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Selection_drops_out_of_range_rows()
    {
        var program = ProgramOf("G21", "M5");

        var plan = ProgramRunPlan.Selection(program, [-3, 0, 99]);

        Assert.Equal(["G21"], plan.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Selection_keeps_non_sendable_entries_so_program_markers_still_apply()
    {
        var program = ProgramOf("%", "G21", "; note", "M30");

        // A selection that includes the closing marker/comment rows keeps them in
        // the plan: the streamer skips them, and the M30 still ends the session.
        var plan = ProgramRunPlan.Selection(program, [1, 2, 3]);

        Assert.Equal(["G21", "; note", "M30"], plan.Entries.Select(e => e.Text));
        Assert.Equal(2, plan.Entries.Count(e => e.IsSendable));
    }

    [Fact]
    public void Empty_selection_yields_an_empty_plan()
    {
        var program = ProgramOf("G21");

        Assert.Empty(ProgramRunPlan.Selection(program, []).Entries);
        Assert.Empty(ProgramRunPlan.Selection(program, [-1]).Entries);
    }
}
