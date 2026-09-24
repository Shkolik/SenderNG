
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
using SenderNG.Core.Modules.Probing;
using Xunit;

namespace SenderNG.Modules.Probing.Tests;

public class ProbeRunnerTests
{
    private static async Task<DemoGrblService> ConnectDemoAsync()
    {
        var demo = new DemoGrblService(new MachineProfile());
        await demo.ConnectAsync("demo");
        return demo;
    }

    [Fact]
    public async Task Execute_captures_probe_position_after_G38_move()
    {
        var demo = await ConnectDemoAsync();
        var runner = new ProbeRunner(demo);

        var result = await runner.ExecuteAsync(["G91F100", "G38.3F100Z-10"], TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var pos = Assert.Single(result.Positions);
        Assert.Equal(-10, pos[2], 3);
    }

    [Fact]
    public async Task Retract_marker_drops_search_probe_position()
    {
        var demo = await ConnectDemoAsync();
        var runner = new ProbeRunner(demo);

        // Fast probe (captured), retract (! drops it), slow probe (captured).
        var result = await runner.ExecuteAsync(
            ["G91F100", "G38.3F100Z-10", "!G0Z0.5", "G38.3F25Z-2"], TestContext.Current.CancellationToken);

        var pos = Assert.Single(result.Positions);
        Assert.Equal(-11.5, pos[2], 3);
    }

    [Fact]
    public async Task Message_lines_are_reported_but_not_sent()
    {
        var demo = await ConnectDemoAsync();
        var runner = new ProbeRunner(demo);

        var result = await runner.ExecuteAsync(["#Probing point 1 of 4", "G38.3F100Z-5"], TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("Probing point 1 of 4", result.LastMessage);
        Assert.Single(result.Positions);
    }

    [Fact]
    public async Task ToolLength_program_ends_at_expected_touch()
    {
        var demo = await ConnectDemoAsync();
        var runner = new ProbeRunner(demo);
        var program = new ProbeProgramBuilder(ProbingProfile.CreateDefault())
            .BuildToolLength(probeFixture: false);

        var result = await runner.ExecuteAsync(program.Lines, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var pos = Assert.Single(result.Positions);
        Assert.Equal(-11.5, pos[2], 3); // fast probe dropped, slow latch probe lands deeper
    }

    [Fact]
    public async Task EdgeFinder_corner_captures_two_probe_positions()
    {
        var demo = await ConnectDemoAsync();
        var runner = new ProbeRunner(demo);
        var program = new ProbeProgramBuilder(ProbingProfile.CreateDefault())
            .BuildEdgeFinder(Edge.A, new Pos(10, 20, 5), internalFinder: false);

        var result = await runner.ExecuteAsync(program.Lines, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(2, result.Positions.Count); // X probe then Y probe
        Assert.True(result.Positions[0][2] == 2d); // probing depth
    }
}
