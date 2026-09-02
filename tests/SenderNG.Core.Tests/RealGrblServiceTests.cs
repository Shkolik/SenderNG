
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

using System.Threading.Channels;
using CNC.Core.Abstractions;
using CNC.Core.Services;

namespace SenderNG.Core.Tests;

/// <summary>
/// In-memory <see cref="IGrblTransport"/>: tests push inbound lines through
/// <see cref="Enqueue"/> and capture written lines via <see cref="Sent"/>. The
/// reader task drives <see cref="ReadLineAsync"/> off an unbounded channel.
/// </summary>
public sealed class FakeGrblTransport : IGrblTransport, IPortPresenceCheck
{
    private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>();
    private readonly List<string> _writtenLines = [];
    private readonly object _writtenGate = new();
    private bool _open;

    public bool IsOpen => _open;

    /// <summary>In-memory double: opening never resets a controller, so the service skips the boot-banner wait.</summary>
    public bool ResetsOnOpen => false;

    public bool AutoReply { get; set; } = true;

    /// <summary>When set, AutoReply answers $G with a [GC:...] dump naming this
    /// coordinate system (the connect-time active-system handshake).</summary>
    public string? GCReply { get; set; }

    /// <summary>When set, the next ReadLineAsync throws as a transport failure (not a cancel).</summary>
    public bool FailNextRead { get; set; }

    /// <summary>IPortPresenceCheck result: false simulates an unplugged controller.</summary>
    public bool PortPresent { get; set; } = true;

    bool IPortPresenceCheck.IsPortPresent() => PortPresent;

    /// <summary>Lines the service wrote; includes a marker for raw realtime bytes.</summary>
    public IReadOnlyList<string> Sent
    {
        get
        {
            lock (_writtenGate) return _writtenLines.ToArray();
        }
    }
    public List<string> WrittenRawBytes { get; } = [];

    public ConnectResetMode OpenedWithResetMode { get; private set; }

    public void Open(string target, int baudRate, ConnectResetMode resetMode = ConnectResetMode.None)
    {
        OpenedWithResetMode = resetMode;
        _open = true;
    }
    public void Close() => _open = false;

    public void Enqueue(string line) => _inbound.Writer.TryWrite(line);

    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        // Simulate the transport failing on its own (e.g. serial unplug / dropped
        // socket): the real transports convert fatal failures into an
        // OperationCanceledException whose token is NOT cancelled.
        if (FailNextRead)
            throw new OperationCanceledException(ct);

        // Return null on timeout-style empty windows by waiting briefly.
        if (await _inbound.Reader.WaitToReadAsync(ct) &&
            _inbound.Reader.TryRead(out var line))
        {
            return line;
        }
        return null;
    }

    public Task WriteLineAsync(string line)
    {
        lock (_writtenGate) _writtenLines.Add(line);
        // Auto-reply 'ok' unless the test controls the transcript. $I gets a
        // minimal classic-grbl reply so the connect handshake completes in one
        // attempt (the real boards answer $I with [VER:]/[OPT:]/... + ok; the
        // service keeps retrying until it collects bracket info lines).
        if (AutoReply)
        {
            if (line == "$I")
            {
                Enqueue("[VER:1.1f.20230820:]");
                Enqueue("[OPT:V,16,128]");
            }
            if (line == "$G" && GCReply is not null)
                Enqueue($"[GC:G0 {GCReply} G17 G21 G90 G94 M5 M9 T0 F0 S0]");
            Enqueue("ok");
        }
        return Task.CompletedTask;
    }

    public void WriteByte(byte b)
    {
        WrittenRawBytes.Add(((char)b).ToString());
    }

    public void Dispose() => Close();
}

public class RealGrblServiceTests
{
    [Fact]
    public async Task Connect_starts_and_state_emits_status_reports()
    {
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport);
        MachineState? latest = null;
        svc.State.Subscribe(s => latest = s);

        await svc.ConnectAsync("fake");
        Assert.True(svc.IsConnected);

        transport.Enqueue("<Run|WPos:10,20,30|FS:100,5000>");
        await WaitUntil(() => latest?.Status == "Run");

        Assert.Equal(10.0, latest!.Positions[0]);
        Assert.Equal(5000.0, latest.Speed);
    }

    [Fact]
    public async Task SendProgramLineAsync_returns_the_reply_without_suspending_the_poll()
    {
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport) { PollIntervalMs = 50 };
        await svc.ConnectAsync("fake");

        // Streaming send: returns the controller's raw reply, and the '?' poll
        // keeps running (status stays live during a run).
        var reply = await svc.SendProgramLineAsync("G0 X10");
        Assert.Equal("ok", reply);

        // The poller writes '?' as a realtime byte — the streaming send must
        // NOT suspend it (single sends don't either; the poll is the liveness
        // channel for the silence-based CommandTimeout).
        await WaitUntil(() => transport.WrittenRawBytes.Contains("?"), timeoutMs: 3000);
        Assert.NotNull(transport.Sent.LastOrDefault(l => l == "G0 X10"));
    }

    [Fact]
    public async Task Connect_learns_the_active_coordinate_system_from_G()
    {
        // Regression: the app must not assume G54 — the $G modal dump names the
        // ACTIVE system, and the DRO zero (G10 L20 P0) writes into exactly that.
        var transport = new FakeGrblTransport { GCReply = "G56" };
        using var svc = new RealGrblService(transport);

        await svc.ConnectAsync("fake");

        Assert.Contains("$G", transport.Sent);
        Assert.Equal("G56", svc.WorkParameters.ActiveCoordinateSystem);
    }

    [Fact]
    public async Task Activation_sent_by_the_app_is_remembered_across_work_parameter_refreshes()
    {
        // The reported bug: select G59, jog, zero all — the DRO zero's $#
        // pull-back republished a snapshot whose ActiveCoordinateSystem defaulted
        // to G54, yanking the Work Parameters selector back to G54 while the
        // controller ($G) was still on G59. The service must learn activations it
        // sends and carry them through the $# re-read.
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport);
        await svc.ConnectAsync("fake");

        await svc.SendAsync("G59"); // what the Work Parameters selector sends

        Assert.Equal("G59", svc.WorkParameters.ActiveCoordinateSystem);

        // The zero button's post-write $# pull-back must keep G59, not reset to G54.
        await svc.RefreshWorkParametersAsync();
        Assert.Equal("G59", svc.WorkParameters.ActiveCoordinateSystem);
    }

    [Fact]
    public async Task Rejected_activation_does_not_update_the_active_system()
    {
        // An alarm-state controller rejects G59 — the snapshot must stay on the
        // truth (G54), not claim a system the board never activated.
        var transport = new FakeGrblTransport { AutoReply = false };
        using var svc = new RealGrblService(transport);
        await svc.ConnectAsync("fake");

        var send = svc.SendAsync("G59");
        transport.Enqueue("error:22");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await send);

        Assert.Equal("G54", svc.WorkParameters.ActiveCoordinateSystem);
    }

    [Fact]
    public async Task Connect_falls_back_to_G54_when_modal_dump_is_missing()
    {
        var transport = new FakeGrblTransport(); // auto-replies ok, no [GC:...]
        using var svc = new RealGrblService(transport);

        await svc.ConnectAsync("fake");

        Assert.Equal("G54", svc.WorkParameters.ActiveCoordinateSystem);
    }

    [Fact]
    public async Task Status_report_WCS_field_marks_the_active_coordinate_system()
    {
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport);
        MachineState? latest = null;
        svc.State.Subscribe(s => latest = s);

        await svc.ConnectAsync("fake");
        transport.Enqueue("<Idle|MPos:0,0,0|WCS:g58>");
        await WaitUntil(() => latest?.ActiveCoordinateSystem == "G58");
    }

    [Fact]
    public async Task SendProgramLineAsync_returns_the_error_reply_verbatim()
    {
        var transport = new FakeGrblTransport { AutoReply = false };
        using var svc = new RealGrblService(transport);
        await svc.ConnectAsync("fake");

        // Script the transcript: enqueue the reply after the write lands.
        var pending = svc.SendProgramLineAsync("G0 X10");
        await Task.Delay(50);
        transport.Enqueue("error:22");

        var reply = await pending;
        Assert.Equal("error:22", reply); // streaming never throws on rejection — the streamer decides
    }

    [Fact]
    public async Task Completed_homing_marks_state_homed_and_alarm_clears_it()
    {
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport);
        MachineState? latest = null;
        svc.State.Subscribe(s => latest = s);

        await svc.ConnectAsync("fake");
        Assert.False(latest!.IsHomed);

        // $H completes when grbl replies "ok" (the board only answers after the
        // homing cycle finishes) — the service then marks the machine homed.
        await svc.SendAsync("$H");
        Assert.True(latest!.IsHomed);

        // Any alarm report means the machine needs (re-)homing again.
        transport.Enqueue("<Alarm|MPos:0,0,0>");
        await WaitUntil(() => latest?.IsHomed == false);
    }

    [Fact]
    public async Task Connect_queries_work_parameters_and_publishes_dump_to_raw_stream()
    {
        // ioSender's connect sequence runs $# (NGC work parameters) after $I so
        // the console shows the [G54:..]..[G92:..]/[TLO:..]/[PRB:..] dump. The
        // lines must reach the raw line stream (the console renders it), even
        // though the status parser ignores them.
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport);
        var raw = new List<string>();
        svc.ReceivedLines.Subscribe(raw.Add);

        await svc.ConnectAsync("fake");

        Assert.Contains("$#", transport.Sent);

        // The connect handshake auto-replies 'ok' for $# (no dump lines) in the
        // fake transport; push the actual work-parameter dump through and confirm
        // it lands on the raw stream for the console.
        transport.Enqueue("[G54:0.000,0.000,0.000]");
        transport.Enqueue("[G92:0.000,0.000,0.000]");
        // Wait for BOTH dump lines: the consumer publishes lines one at a time, so
        // waiting only on the first can race the second into the raw stream.
        await WaitUntil(() => raw.Contains("[G54:0.000,0.000,0.000]") &&
                             raw.Contains("[G92:0.000,0.000,0.000]"));
    }

    [Fact]
    public async Task Probe_report_fills_probe_position()
    {
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport);
        MachineState? latest = null;
        svc.State.Subscribe(s => latest = s);

        await svc.ConnectAsync("fake");
        transport.Enqueue("[PRB:1.5,2,3:1]");
        await WaitUntil(() => latest?.ProbeSuccess == true);

        Assert.True(latest!.ProbeSuccess);
        Assert.Equal(1.5, latest.ProbePosition![0]);
    }

    [Fact]
    public async Task Poller_sends_status_request_bytes()
    {
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport) { PollIntervalMs = 50 };

        await svc.ConnectAsync("fake");
        await Task.Delay(400);

        Assert.NotEmpty(transport.WrittenRawBytes);
        Assert.Contains(transport.WrittenRawBytes, b => b == "?");
    }

    [Fact]
    public async Task Poller_stays_active_while_command_is_in_flight()
    {
        // The '?' poll is the liveness channel for the silence-based
        // CommandTimeout: single sends ($H, MDI) keep poll reports flowing, so
        // a slow board that keeps answering status requests can never hit a
        // spurious timeout. Reports are routed to the state parser, never the
        // reply FIFO, so they can't be captured as command replies.
        var transport = new FakeGrblTransport();
        transport.AutoReply = false;
        using var svc = new RealGrblService(transport) { PollIntervalMs = 50 };
        await svc.ConnectAsync("fake");

        transport.WrittenRawBytes.Clear(); // discard the initial connect-time poll tick
        var sendTask = svc.SendAsync("G1 X10");
        await Task.Delay(250); // give several poll ticks a chance to write '?'
        transport.Enqueue("ok");
        await sendTask.WaitAsync(TimeSpan.FromSeconds(2));

        // While awaiting a reply polling must stay active — '?' bytes flow.
        Assert.Contains(transport.WrittenRawBytes, b => b == "?");
        Assert.Contains("G1 X10", transport.Sent);
    }

    [Fact]
    public async Task SendAsync_times_out_after_CommandTimeout_without_reply()
    {
        // Silence-based: no inbound line of ANY kind (no reports, no reply) for
        // the window means the controller is genuinely unresponsive — fail fast.
        var transport = new FakeGrblTransport { AutoReply = false };
        using var svc = new RealGrblService(transport)
        {
            CommandTimeout = TimeSpan.FromMilliseconds(100),
            PollIntervalMs = 50,
        };
        await svc.ConnectAsync("fake");

        await Assert.ThrowsAsync<TimeoutException>(async () => await svc.SendAsync("$H"));
    }

    [Fact]
    public async Task PendingSend_survives_feed_hold_well_past_CommandTimeout()
    {
        // A feed hold parks the controller for however long the operator needs:
        // timing out a held command is nonsense. The wait must survive the whole
        // Hold and complete once the controller resumes and replies.
        var transport = new FakeGrblTransport { AutoReply = false };
        using var svc = new RealGrblService(transport)
        {
            PollIntervalMs = 50,
            CommandTimeout = TimeSpan.FromMilliseconds(100),
        };
        await svc.ConnectAsync("fake");

        var sendTask = svc.SendAsync("G1 X10");
        await WaitUntil(() => transport.Sent.Contains("G1 X10"));

        // Controller goes into feed hold and stays there way past the window.
        transport.Enqueue("<Hold|MPos:10,0,0>");
        await Task.Delay(700);
        transport.Enqueue("<Hold|MPos:10,0,0>"); // still held

        // Operator resumes (cycle start): grbl finishes the line and replies.
        transport.Enqueue("<Run|MPos:10,0,0>");
        transport.Enqueue("ok");
        await sendTask.WaitAsync(TimeSpan.FromSeconds(2)); // must NOT timeout
    }

    [Fact]
    public async Task PendingSend_survives_long_command_while_reports_flow()
    {
        // A long operation with a live controller (homing a big slow machine):
        // status reports keep sliding the silence window, so a reply that takes
        // many windows to arrive is fine as long as the controller keeps talking.
        var transport = new FakeGrblTransport { AutoReply = false };
        using var svc = new RealGrblService(transport)
        {
            PollIntervalMs = 50,
            CommandTimeout = TimeSpan.FromMilliseconds(100),
        };
        await svc.ConnectAsync("fake");

        var sendTask = svc.SendAsync("$H");
        for (var i = 0; i < 12; i++)
        {
            await Task.Delay(70);
            transport.Enqueue($"<Run|MPos:{i},0,0>"); // grbl keeps reporting while working
        }
        transport.Enqueue("<Idle|MPos:0,0,0>");
        transport.Enqueue("ok");

        await sendTask.WaitAsync(TimeSpan.FromSeconds(3)); // must NOT timeout
    }

    [Fact]
    public async Task PendingSend_times_out_after_silence_even_though_reports_preceded()
    {
        // Reports slide the deadline but don't cancel it: once the controller
        // goes silent, one full window from the LAST line still fires.
        var transport = new FakeGrblTransport { AutoReply = false };
        using var svc = new RealGrblService(transport)
        {
            PollIntervalMs = 50,
            CommandTimeout = TimeSpan.FromMilliseconds(200),
        };
        await svc.ConnectAsync("fake");

        var sendTask = svc.SendAsync("$H");
        transport.Enqueue("<Run|MPos:1,0,0>");
        transport.Enqueue("<Run|MPos:2,0,0>");
        await Task.Delay(600); // ~3 windows of total silence after the last report

        await Assert.ThrowsAsync<TimeoutException>(async () => await sendTask);
    }

    [Fact]
    public async Task Streamed_run_survives_feed_hold_past_CommandTimeout_and_completes()
    {
        // The reported bug: pausing a program for longer than CommandTimeout
        // killed the run with "fail: timeout" because the in-flight line's reply
        // wait had a fixed deadline. A held controller replies only after
        // resume — the run must survive the whole hold and complete.
        var transport = new FakeGrblTransport { AutoReply = false };
        using var svc = new RealGrblService(transport)
        {
            PollIntervalMs = 50,
            CommandTimeout = TimeSpan.FromMilliseconds(100),
        };
        await svc.ConnectAsync("fake");

        var updates = new List<ProgramLineUpdate>();
        var program = GCodeProgram.Parse("G0 X1\nG0 X2\nG0 X3");
        var session = new GCodeProgramStreamer(svc).Start(program, new Progress<ProgramLineUpdate>(updates.Add));

        await WaitUntil(() => transport.Sent.Contains("G0 X1"));
        transport.Enqueue("ok");
        await WaitUntil(() => transport.Sent.Contains("G0 X2"));

        // Feed hold mid-line: stay held for 7x the CommandTimeout window.
        for (var i = 0; i < 7; i++)
        {
            transport.Enqueue("<Hold|MPos:10,10,0>");
            await Task.Delay(100);
        }

        // Resume (cycle start): line 2 finishes, the rest streams, run completes.
        transport.Enqueue("<Run|MPos:10,10,0>");
        transport.Enqueue("ok");
        await WaitUntil(() => transport.Sent.Contains("G0 X3"));
        transport.Enqueue("ok");

        var result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(result.IsSuccess, result.Error);
        // Progress callbacks are posted asynchronously and can land just after
        // Completion resolves — wait for all three instead of asserting instantly.
        await WaitUntil(() => updates.Count(u => u.Status == ProgramLineStatus.Ok) == 3);
    }

    [Fact]
    public async Task SendAsync_writes_line_and_returns_after_ok()
    {
        var transport = new FakeGrblTransport();
        transport.AutoReply = false;
        using var svc = new RealGrblService(transport);
        await svc.ConnectAsync("fake");

        var sendTask = svc.SendAsync("G1 X10");
        transport.Enqueue("ok");
        await sendTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains("G1 X10", transport.Sent);
    }

    [Fact]
    public async Task SendAsync_throws_when_controller_replies_with_error()
    {
        // A rejected command (e.g. G10 while locked/alarmed) replies "error:nn".
        // SendAsync must surface that as a failure so callers stop reporting
        // "Saved" for a write the controller refused.
        var transport = new FakeGrblTransport { AutoReply = false };
        using var svc = new RealGrblService(transport);
        await svc.ConnectAsync("fake");

        var sendTask = svc.SendAsync("G10 L2 P1 X5");
        transport.Enqueue("error:26");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sendTask.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains("error:26", ex.Message);
    }

    [Fact]
    public async Task GetSettingLines_collects_until_ok()
    {
        var transport = new FakeGrblTransport();
        transport.AutoReply = false;
        using var svc = new RealGrblService(transport);
        await svc.ConnectAsync("fake");

        var task = svc.GetSettingLinesAsync();
        transport.Enqueue("$110=800.00 (x mm/min)");
        transport.Enqueue("$111=1000.00 (y mm/min)");
        transport.Enqueue("ok");
        var lines = await task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public async Task Disconnect_stops_polling_and_closes_transport()
    {
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport) { PollIntervalMs = 50 };
        await svc.ConnectAsync("fake");

        svc.Disconnect();

        Assert.False(svc.IsConnected);
        Assert.False(transport.IsOpen);
    }

    [Fact]
    public async Task ReceivedLines_publish_raw_controller_output()
    {
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport);
        // Thread-safe capture: the reader publishes from a background thread while
        // this test thread asserts, and a plain List races on enumeration.
        var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
        svc.ReceivedLines.Subscribe(lines.Enqueue);

        await svc.ConnectAsync("fake");
        transport.Enqueue("<Idle|WPos:1,2,3>");
        transport.Enqueue("ok");
        transport.Enqueue("[PRB:1.5,2,3:1]");

        // Wait for the probe line specifically: connect-time $I auto-replies can
        // already fill the queue, so a plain count check would race ahead of the
        // lines this test enqueues.
        await WaitUntil(() => lines.Contains("[PRB:1.5,2,3:1]"));
        var snapshot = lines.ToArray();
        Assert.Contains("<Idle|WPos:1,2,3>", snapshot);
        Assert.Contains("ok", snapshot);
        Assert.Contains("[PRB:1.5,2,3:1]", snapshot);
    }

    [Fact]
    public async Task Realtime_write_goes_out_as_raw_byte()
    {
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport);
        await svc.ConnectAsync("fake");

        svc.SendRealtime(GrblRealtime.CoolantFloodToggle);

        Assert.Contains(transport.WrittenRawBytes, b => b == ((char)GrblRealtime.CoolantFloodToggle).ToString());
    }

    [Fact]
    public async Task Connect_runs_I_handshake_and_populates_capabilities()
    {
        var transport = new FakeGrblTransport { AutoReply = false };
        using var svc = new RealGrblService(transport);

        var connectTask = svc.ConnectAsync("fake");
        // Feed the full classic-grbl $I reply only once $I is actually written (on
        // real hardware the reply only ever comes after the command, so lines must
        // not arrive before it). A banner alone no longer completes the handshake.
        await WaitUntil(() => transport.Sent.Contains("$I"), 3000);
        transport.Enqueue("Grbl 1.1h ['$' for help]");
        transport.Enqueue("[VER:1.1h.20190825:]");
        transport.Enqueue("[OPT:V,15,128]");
        transport.Enqueue("ok");
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("$I", transport.Sent);
        Assert.True(await WaitUntilAsync(() => svc.Capabilities.IsKnown, 3000));
        Assert.False(svc.Capabilities.IsGrblHAL);
        Assert.Equal("1.1h", svc.Capabilities.Version);
        Assert.Equal(15, svc.Capabilities.PlanBufferSize);

        // The metadata scoping key carries the $I firmware fingerprint only (no
        // endpoint — moving the board to another port keeps its key; classic
        // grbl has no [AXS:] -> empty tail).
        Assert.Equal("grbl|1.1h.20190825|V,15,128|", svc.ControllerKey);
    }

    [Fact]
    public async Task Connect_retries_I_handshake_when_first_reply_is_banner_only()
    {
        // The DTR-at-open reset reboots Arduino-style boards; a $I sent while the
        // UART is still initializing is dropped, so only the boot banner arrives.
        // The handshake must retry until actual $I reply lines ([VER:]/[OPT:]) are
        // collected instead of settling for banner-only capabilities.
        var transport = new FakeGrblTransport { AutoReply = false };
        using var svc = new RealGrblService(transport);

        var connectTask = svc.ConnectAsync("fake");
        await WaitUntil(() => transport.Sent.Contains("$I"), 3000);
        // First $I: board still booting — only the boot banner, no $I reply.
        transport.Enqueue("Grbl 1.1h ['$' for help]");

        // Wait for the retry's second $I, then feed the real reply.
        await WaitUntil(() => transport.Sent.Count(l => l == "$I") >= 2, 6000);
        transport.Enqueue("[VER:1.1h.20190825:]");
        transport.Enqueue("[OPT:V,15,128]");
        transport.Enqueue("ok");
        await connectTask.WaitAsync(TimeSpan.FromSeconds(8));

        Assert.True(svc.Capabilities.IsKnown);
        Assert.Equal("1.1h", svc.Capabilities.Version);
        Assert.Equal(15, svc.Capabilities.PlanBufferSize);
        Assert.Equal(2, transport.Sent.Count(l => l == "$I"));
    }

    [Fact]
    public async Task Connect_applies_I_axis_count_to_state_and_grblinfo()
    {
        // A grblHAL $I handshake reporting 5 axes must grow the live DRO state
        // (MachineState.NumAxes -> DRO/signals rows) and the legacy GrblInfo
        // static that the parser/expressions read. Restore the global static so
        // the test can't leak into others.
        var savedAxes = CNC.Core.GrblInfo.NumAxes;
        var savedHAL = CNC.Core.GrblInfo.IsGrblHAL;
        try
        {
            var transport = new FakeGrblTransport { AutoReply = false };
            using var svc = new RealGrblService(transport);
            MachineState? latest = null;
            svc.State.Subscribe(s => latest = s);

            var connectTask = svc.ConnectAsync("fake");
            await WaitUntil(() => transport.Sent.Contains("$I"), 3000);
            transport.Enqueue("GrblHAL 1.2i ['$' for help]");
            transport.Enqueue("[VER:1.2i.20260521:]");
            transport.Enqueue("[OPT:V,15,128]");
            transport.Enqueue("[AXS:5:XYZAB]");
            transport.Enqueue("ok");
            await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(svc.Capabilities.IsKnown);
            Assert.Equal(5, svc.Capabilities.NumAxes);

            // Legacy global used by gcode parser / expressions must follow.
            Assert.Equal(5, CNC.Core.GrblInfo.NumAxes);
            Assert.True(CNC.Core.GrblInfo.IsGrblHAL);

            // Live state drives the DRO axis rows.
            Assert.True(await WaitUntilAsync(() => latest?.NumAxes == 5, 3000));
        }
        finally
        {
            CNC.Core.GrblInfo.NumAxes = savedAxes;
            CNC.Core.GrblInfo.IsGrblHAL = savedHAL;
        }
    }

    [Fact]
    public async Task Connect_forwards_serial_reset_mode_to_transport()
    {
        // The dialog's "On connect" mode (Toggle DTR) must reach the transport when
        // the port opens, so an Arduino bootloader gets its reset pulse.
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport) { SerialResetMode = ConnectResetMode.ToggleDtr };

        await svc.ConnectAsync("fake");

        Assert.Equal(ConnectResetMode.ToggleDtr, transport.OpenedWithResetMode);
    }

    [Fact]
    public async Task Reader_failure_flips_to_disconnected_and_notifies()
    {
        // The transport failing on its own (serial unplug, dropped TCP socket)
        // must flip the app to NOT CONNECTED and raise ConnectionChanged, instead
        // of silently ending the reader while IsConnected stays true.
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport);
        var changes = 0;
        svc.ConnectionChanged += (_, _) => changes++;

        await svc.ConnectAsync("fake");
        Assert.True(svc.IsConnected);

        transport.FailNextRead = true;
        transport.Enqueue("ok"); // wake the reader so it hits the failure

        Assert.True(await WaitUntilAsync(() => !svc.IsConnected, 3000));
        Assert.True(changes >= 2, "connect + disconnect should both raise ConnectionChanged");

        // Sending after a detected disconnect fails fast with a clear message.
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SendAsync("G1 X10"));
    }

    [Fact]
    public async Task Port_presence_watchdog_disconnects_when_port_disappears()
    {
        // A USB-serial unplug removes the COM port without necessarily throwing on
        // reads; the health watchdog must catch it and flip to disconnected.
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport) { HealthCheckIntervalMs = 100 };
        var changes = 0;
        svc.ConnectionChanged += (_, _) => changes++;

        await svc.ConnectAsync("fake");
        Assert.True(svc.IsConnected);

        transport.PortPresent = false; // cable pulled

        Assert.True(await WaitUntilAsync(() => !svc.IsConnected, 3000));
        Assert.True(changes >= 2);
    }

    [Fact]
    public async Task Reconnect_after_disconnect_restores_connection()
    {
        // The stale-state bug: after a disconnect (here: watchdog), ConnectAsync used
        // to no-op because IsConnected stayed true. Now it must reconnect cleanly.
        var transport = new FakeGrblTransport();
        using var svc = new RealGrblService(transport) { HealthCheckIntervalMs = 100 };

        await svc.ConnectAsync("fake");
        transport.PortPresent = false;
        Assert.True(await WaitUntilAsync(() => !svc.IsConnected, 3000));

        // Controller re-plugged: port is present again.
        transport.PortPresent = true;
        await svc.ConnectAsync("fake");
        Assert.True(svc.IsConnected);

        var sendTask = svc.SendAsync("G0 X1");
        await sendTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("G0 X1", transport.Sent);
    }

    [Fact]
    public async Task Disconnect_faults_in_flight_command_immediately()
    {
        // A command waiting for its reply (no ok yet) must not hang for the full
        // CommandTimeout when the controller disconnects — it fails fast.
        var transport = new FakeGrblTransport { AutoReply = false };
        using var svc = new RealGrblService(transport) { CommandTimeout = TimeSpan.FromMinutes(5) };
        await svc.ConnectAsync("fake");

        var sendTask = svc.SendAsync("$H"); // homing, reply never arrives
        await Task.Delay(100); // let the write + waiter registration settle

        svc.Disconnect();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sendTask.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains("disconnected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(10);
        }
        return false;
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(10);
        }
        Assert.True(condition(), "condition not met within timeout");
    }
}