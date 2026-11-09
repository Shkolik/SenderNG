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

public class StatusReportParserTests
{
    [Fact]
    public void Parses_the_executing_line_number_from_Ln_field()
    {
        var state = new MachineState(3);
        Assert.True(StatusReportParser.Parse("<Run|WPos:1,2,3|Ln:42>", state));

        Assert.Equal("Run", state.Status);
        Assert.Equal(42, state.LineNumber);
    }

    [Fact]
    public void Line_number_stays_zero_when_field_is_absent()
    {
        var state = new MachineState(3);
        StatusReportParser.Parse("<Idle|WPos:0,0,0>", state);

        Assert.Equal(0, state.LineNumber);
    }

    [Fact]
    public void Ports_field_position_is_preserved()
    {
        var state = new MachineState(3);
        Assert.True(StatusReportParser.Parse("<Hold|MPos:0,0,0|WPos:1,2,3|Ln:12|FS:0,0>", state));

        Assert.Equal("Hold", state.Status);
        Assert.Equal(12, state.LineNumber);
        Assert.Equal(1, state.Positions[0]);
    }

    // grbl/grblHAL report exactly ONE position field per status report ($10 bit 0
    // picks MPos or WPos), so the two read-outs must be derived from whichever one
    // the controller sends plus WCO. Machine position is the reported field here.
    [Fact]
    public void Machine_position_report_derives_work_position_from_the_reported_offset()
    {
        var state = new MachineState(4);
        Assert.True(StatusReportParser.Parse("<Idle|MPos:10,-10,10,0|WCO:-870,-300,0,0>", state));

        Assert.Equal([10, -10, 10, 0], state.MachinePositions!);
        Assert.Equal([880, 290, 10, 0], state.Positions);
    }

    // The mirror case: a controller configured for work position never sends an
    // MPos: field, and the MPos read-out used to stay frozen on the seeded zeros.
    [Fact]
    public void Work_position_report_derives_machine_position_from_the_reported_offset()
    {
        var state = new MachineState(4);
        Assert.True(StatusReportParser.Parse("<Idle|WPos:880,290,10,0|WCO:-870,-300,0,0>", state));

        Assert.Equal([880, 290, 10, 0], state.Positions);
        Assert.Equal([10, -10, 10, 0], state.MachinePositions!);
    }

    // Offsets are reported periodically, not in every report (grblHAL's WCO
    // counter), so the last known WCO has to be carried across reports.
    [Fact]
    public void Work_position_report_reuses_the_last_known_offset()
    {
        var state = new MachineState(3);
        Assert.True(StatusReportParser.Parse("<Idle|WPos:880,0,0|WCO:-870,0,0>", state));
        Assert.True(StatusReportParser.Parse("<Jog|WPos:885,0,0>", state));

        Assert.Equal(885, state.Positions[0]);
        Assert.Equal(15, state.MachinePositions![0]);
    }

    // A controller that reports neither offset nor machine position (WCO disabled)
    // has no offset as far as the sender knows: the read-outs agree, exactly like
    // the MPos-only fallback derives work = machine.
    [Fact]
    public void Work_position_report_without_any_offset_leaves_the_readouts_equal()
    {
        var state = new MachineState(3);
        Assert.True(StatusReportParser.Parse("<Idle|WPos:12,34,56>", state));

        Assert.Equal([12, 34, 56], state.Positions);
        Assert.Equal([12, 34, 56], state.MachinePositions!);
    }
}
