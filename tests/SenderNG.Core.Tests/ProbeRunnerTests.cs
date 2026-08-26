using CNC.Core.Services;
using SenderNG.Core.Modules.Probing;
using Xunit;

namespace SenderNG.Core.Tests;

public class ProbeRunnerTests
{
    private static async Task<DemoGrblService> ConnectDemoAsync()
    {
        var demo = new DemoGrblService(new AppSettings());
        await demo.ConnectAsync("demo");
        return demo;
    }

    [Fact]
    public async Task Execute_captures_probe_position_after_G38_move()
    {
        var demo = await ConnectDemoAsync();
        var runner = new ProbeRunner(demo);

        var result = await runner.ExecuteAsync(["G91F100", "G38.3F100Z-10"]);

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
            ["G91F100", "G38.3F100Z-10", "!G0Z0.5", "G38.3F25Z-2"]);

        var pos = Assert.Single(result.Positions);
        Assert.Equal(-11.5, pos[2], 3);
    }

    [Fact]
    public async Task Message_lines_are_reported_but_not_sent()
    {
        var demo = await ConnectDemoAsync();
        var runner = new ProbeRunner(demo);

        var result = await runner.ExecuteAsync(["#Probing point 1 of 4", "G38.3F100Z-5"]);

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

        var result = await runner.ExecuteAsync(program.Lines);

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

        var result = await runner.ExecuteAsync(program.Lines);

        Assert.True(result.Success);
        Assert.Equal(2, result.Positions.Count); // X probe then Y probe
        Assert.True(result.Positions[0][2] == 2d); // probing depth
    }
}
