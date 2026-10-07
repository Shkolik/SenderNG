
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

using System.Reactive.Subjects;
using CNC.Core.Services;
using SenderNG.Modules.Probing.Core;
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

    // The demo applies a move synchronously inside SendAsync, so its Run/Idle
    // reports are already over by the time the caller looks. A real controller
    // accepts the line and returns while the axis is still travelling, and only
    // reports Run then Idle on later polls. AsyncGrblService models that, which is
    // the only way to reach the stale-replay race at all.

    [Fact]
    public async Task Probe_position_comes_from_the_post_move_report_not_a_stale_Idle()
    {
        var grbl = new AsyncGrblService();
        var runner = new ProbeRunner(grbl);

        var result = await runner.ExecuteAsync(["G38.3F100Z-10"], TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var pos = Assert.Single(result.Positions);
        Assert.Equal(-10, pos[2], 3);
    }

    [Fact]
    public async Task The_next_line_is_not_sent_until_the_machine_reports_Idle_again()
    {
        var grbl = new AsyncGrblService();
        var runner = new ProbeRunner(grbl);

        await runner.ExecuteAsync(["G38.3F100Z-10", "G0Z5"], TestContext.Current.CancellationToken);

        // The retract must be sent after the probe move came back to Idle, never
        // while the axis is still travelling towards the touch point.
        var probe = grbl.Events.IndexOf("idle");
        var retract = grbl.Events.IndexOf("send:G0Z5");
        Assert.True(probe >= 0, grbl.Log);
        Assert.True(retract > probe, $"retract sent at {retract}, first Idle at {probe}: {grbl.Log}");
    }

    [Fact]
    public async Task A_line_that_does_not_move_an_axis_still_completes()
    {
        // Requiring an observed Run (as the stepper wizard's gate does) would make
        // this line wait out the 120s move timeout and fail the run.
        var grbl = new AsyncGrblService();
        var runner = new ProbeRunner(grbl);

        var result = await runner.ExecuteAsync(["G91F100", "G38.3F100Z-10"], TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Single(result.Positions);
    }

    private sealed class AsyncGrblService : CNC.Core.Abstractions.IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = new(new MachineState());
        private readonly List<string> _events = [];
        private readonly object _gate = new();

        public List<string> Events { get { lock (_gate) return [.. _events]; } }
        public string Log => string.Join(" | ", Events);

        public IObservable<MachineState> State => _subject;
        public bool IsConnected => true;

        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) { }

        public Task SendAsync(string line)
        {
            Record("send:" + line);
            // Fire and forget: the controller has accepted the line but has not
            // started moving yet, so the caller's await completes with the machine
            // still reporting the previous Idle.
            _ = Task.Run(async () =>
            {
                await Task.Delay(15);
                var s = _subject.Value;
                s.Status = "Run";
                _subject.OnNext(s);
                Record("run");

                await Task.Delay(15);
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(line.ToUpperInvariant(), @"([XYZ])(-?[\d.]+)"))
                {
                    s.Positions["XYZ".IndexOf(m.Groups[1].Value[0])] =
                        double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                }
                if (line.Contains("G38", StringComparison.OrdinalIgnoreCase))
                {
                    s.ProbeSuccess = true;
                    s.ProbePosition = (double[])s.Positions.Clone();
                }
                s.Status = "Idle";
                _subject.OnNext(s);
                Record("idle");
            });
            return Task.CompletedTask;
        }

        private void Record(string e)
        {
            lock (_gate) _events.Add(e);
        }
    }
}
