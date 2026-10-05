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
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>
/// Scripted IGrblService for streamer tests: every sent line may be gated on a
/// per-line signal, the state is fully controllable (Hold/Alarm/Run), and
/// replies come from a script queue.
/// </summary>
public sealed class ScriptedGrblService : IGrblService
{
    private readonly BehaviorSubject<MachineState> _state;
    private readonly Queue<string> _replies = new();
    private readonly Queue<TaskCompletionSource> _gates = new();
    private readonly List<string> _sent = [];
    private readonly object _gate = new();

    public ScriptedGrblService(string initialStatus = "Idle")
    {
        _state = new BehaviorSubject<MachineState>(new MachineState { Status = initialStatus });
    }

    public bool IsConnected { get; set; } = true;
    public event EventHandler? ConnectionChanged { add { } remove { } }
    public IObservable<MachineState> State => _state;

    public IReadOnlyList<string> Sent { get { lock (_gate) return _sent.ToArray(); } }

    /// <summary>Reply the next send; defaults to "ok" when unset.</summary>
    public void Reply(string reply) { lock (_gate) _replies.Enqueue(reply); }

    /// <summary>Gate the next send: its reply (and completion) wait for SetResult.</summary>
    public TaskCompletionSource NextGate()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) _gates.Enqueue(tcs);
        return tcs;
    }

    public void SetState(string status, int lineNumber = 0) =>
        _state.OnNext(new MachineState { Status = status, LineNumber = lineNumber });

    public Task ConnectAsync(string port) { IsConnected = true; return Task.CompletedTask; }
    public void Disconnect() { IsConnected = false; }
    public Task SendAsync(string line) => Task.CompletedTask;

    public async Task<string> SendProgramLineAsync(string line, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource? gate = null;
        string reply = "ok";
        lock (_gate)
        {
            _sent.Add(line);
            if (_gates.Count > 0)
                gate = _gates.Dequeue();
            if (_replies.Count > 0)
                reply = _replies.Dequeue();
        }
        if (gate is not null)
            await gate.Task.WaitAsync(cancellationToken);
        return reply;
    }

    public void SendRealtime(byte code) { }
}

public class GCodeProgramStreamerTests
{
    private static GCodeProgram ProgramOf(params string[] lines) =>
        GCodeProgram.Parse(string.Join("\n", lines));

    private static async Task<List<ProgramLineUpdate>> RunAsync(GCodeProgram program, ScriptedGrblService svc)
    {
        var updates = new List<ProgramLineUpdate>();
        var session = new GCodeProgramStreamer(svc).Start(program, new Progress<ProgramLineUpdate>(updates.Add));
        var result = await session.Completion;
        Assert.True(result.IsSuccess, $"expected success, got {result.Outcome}: {result.Error}");
        Assert.NotNull(result);
        return updates;
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task Streams_every_line_in_order_with_sent_then_ok()
    {
        var svc = new ScriptedGrblService();
        var program = ProgramOf("G21", "G0 X10", "G1 X20 F100", "(comment)", "M5");

        var updates = await RunAsync(program, svc);

        Assert.Equal(new[] { "G21", "G0 X10", "G1 X20 F100", "(comment)", "M5" }, svc.Sent);

        // Each line reports Sent then Ok, in order.
        var sent = updates.Where(u => u.Status == ProgramLineStatus.Sent).Select(u => u.Sequence).ToArray();
        var ok = updates.Where(u => u.Status == ProgramLineStatus.Ok).Select(u => u.Sequence).ToArray();
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, sent);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, ok);
        Assert.All(updates, u => Assert.Null(u.Detail));
    }

    [Fact]
    public async Task Stops_streaming_at_the_program_end_and_skips_demarcation()
    {
        var svc = new ScriptedGrblService();
        var program = ProgramOf("%", "G21", "M2", "G0 X99");

        var updates = await RunAsync(program, svc);

        // The leading % is a file marker (not sent), M2 lands, then the run ends.
        Assert.Equal(new[] { "G21", "M2" }, svc.Sent);
        Assert.Equal(2, updates.Count(u => u.Status == ProgramLineStatus.Ok));
    }

    [Fact]
    public async Task Controller_error_marks_the_line_and_fails_the_run()
    {
        var svc = new ScriptedGrblService();
        svc.Reply("ok");
        svc.Reply("error:22");

        var program = ProgramOf("G21", "G0 X10", "G1 X20 F100");
        var updates = new List<ProgramLineUpdate>();
        var session = new GCodeProgramStreamer(svc).Start(program, new Progress<ProgramLineUpdate>(updates.Add));
        var result = await session.Completion;

        Assert.Equal(ProgramRunOutcome.Failed, result.Outcome);
        Assert.Equal("error:22", result.Error);
        Assert.Equal(2, result.LinesSent);

        var failed = Assert.Single(updates, u => u.Status == ProgramLineStatus.Error);
        Assert.Equal(2, failed.Sequence);
        Assert.Equal("error:22", failed.Detail);

        // Nothing past the failing line was sent.
        Assert.Equal(new[] { "G21", "G0 X10" }, svc.Sent);
    }

    [Fact]
    public async Task Hold_pauses_sending_until_the_state_returns_to_run()
    {
        var svc = new ScriptedGrblService("Run");
        var program = ProgramOf("G21", "G0 X10", "G1 X20 F100", "M5");

        var session = new GCodeProgramStreamer(svc).Start(program);
        var completion = session.Completion;

        // Let the first line through, then park the controller in Hold.
        await WaitForAsync(() => svc.Sent.Count >= 1);
        await Task.Delay(50);
        svc.SetState("Hold");

        await Task.Delay(150);
        var sentAtHold = svc.Sent.Count;

        // The streamer must not send anything while Held.
        await Task.Delay(150);
        Assert.Equal(sentAtHold, svc.Sent.Count);

        // Resume: the run continues to completion.
        svc.SetState("Run");
        var result = await completion;
        Assert.Equal(ProgramRunOutcome.Completed, result.Outcome);
        Assert.Equal(4, result.LinesOk);
    }

    [Fact]
    public async Task Alarm_aborts_the_run_and_cancels_the_inflight_line()
    {
        var svc = new ScriptedGrblService("Run");
        var pending = svc.NextGate(); // gate line 1 so we can alarm while it is in flight
        var program = ProgramOf("G0 X10", "G1 X20 F100", "M5");

        var updates = new List<ProgramLineUpdate>();
        var session = new GCodeProgramStreamer(svc).Start(program, new Progress<ProgramLineUpdate>(updates.Add));
        var completion = session.Completion;

        await Task.Delay(50);
        svc.SetState("Alarm");

        var result = await completion;
        Assert.Equal(ProgramRunOutcome.Failed, result.Outcome);
        Assert.StartsWith("Alarm", result.Error);
        Assert.Single(updates, u => u.Status == ProgramLineStatus.Sent); // in-flight never resolved
        _ = pending;
    }

    [Fact]
    public async Task Stop_ends_the_run_without_sending_further_lines()
    {
        var svc = new ScriptedGrblService();
        var pending = svc.NextGate(); // gate line 1 so the stop lands mid-flight
        var program = ProgramOf("G21", "G0 X10", "G1 X20 F100");

        var session = new GCodeProgramStreamer(svc).Start(program);
        var completion = session.Completion;

        await WaitForAsync(() => svc.Sent.Count >= 1);
        session.Stop();

        var result = await completion;
        Assert.Equal(ProgramRunOutcome.Stopped, result.Outcome);
        Assert.Equal(1, result.LinesSent);
        _ = pending;
    }

    [Fact]
    public async Task Disconnect_fails_the_run_with_a_clear_message()
    {
        var svc = new ScriptedGrblService();
        svc.IsConnected = false;

        var result = await new GCodeProgramStreamer(svc).Start(ProgramOf("G21")).Completion;

        Assert.Equal(ProgramRunOutcome.Failed, result.Outcome);
        Assert.Equal("Not connected.", result.Error);
    }

    [Fact]
    public async Task Program_with_parse_errors_fails_without_sending_anything()
    {
        var svc = new ScriptedGrblService();
        var program = ProgramOf("G21", "G1 X20", "M5"); // G1 without F is invalid

        var result = await new GCodeProgramStreamer(svc).Start(program).Completion;

        Assert.Equal(ProgramRunOutcome.Failed, result.Outcome);
        Assert.Equal(0, result.LinesSent);
        Assert.Empty(svc.Sent); // zero bytes reached the controller
        Assert.Contains("Line 2", result.Error);
    }

    [Fact]
    public async Task Runs_to_completion_against_the_demo_service_and_receives_ok_replies()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "sng-streamer-" + Guid.NewGuid().ToString("N")))
            .Load();
        var svc = new DemoGrblService(settings);
        await svc.ConnectAsync("demo");

        var replies = new List<string>();
        using var sub = svc.ReceivedLines.Subscribe(replies.Add);

        var program = ProgramOf("G21", "G0 X10 Y10", "G1 X20 Y20 F100", "M5");
        var session = new GCodeProgramStreamer(svc).Start(program);
        var result = await session.Completion;

        Assert.Equal(ProgramRunOutcome.Completed, result.Outcome);
        Assert.Equal(4, result.LinesOk);
        // The demo answered every sent line with "ok" on the raw stream.
        await WaitForAsync(() => replies.Count >= 4);
        Assert.Equal(4, replies.Count(r => r == "ok"));
    }

    [Fact]
    public async Task Demo_rejects_invalid_lines_and_the_run_fails()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "sng-streamer-" + Guid.NewGuid().ToString("N")))
            .Load();
        var svc = new DemoGrblService(settings);
        await svc.ConnectAsync("demo");

        var program = ProgramOf("G21", "R5", "G0 X1"); // R first word is not valid G-code
        var session = new GCodeProgramStreamer(svc).Start(program);
        var result = await session.Completion;

        Assert.Equal(ProgramRunOutcome.Failed, result.Outcome);
        Assert.Equal("error:22", result.Error);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("condition not met in time");
            await Task.Delay(20);
        }
    }
}