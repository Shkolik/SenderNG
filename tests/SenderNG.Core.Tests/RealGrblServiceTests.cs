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
    public bool AutoReply { get; set; } = true;

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

    public void Open(string target, int baudRate) => _open = true;
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
        // Auto-reply 'ok' unless the test controls the transcript.
        if (AutoReply)
            Enqueue("ok");
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
    public async Task Poller_does_not_fire_while_command_is_in_flight()
    {
        var transport = new FakeGrblTransport();
        transport.AutoReply = false;
        using var svc = new RealGrblService(transport) { PollIntervalMs = 50 };
        await svc.ConnectAsync("fake");

        transport.WrittenRawBytes.Clear(); // discard the initial connect-time poll tick
        var sendTask = svc.SendAsync("G1 X10");
        await Task.Delay(250); // give several poll ticks a chance to write '?'
        transport.Enqueue("ok");
        await sendTask.WaitAsync(TimeSpan.FromSeconds(2));

        // While awaiting a reply the poller must be suspended, so no '?' bytes.
        Assert.DoesNotContain(transport.WrittenRawBytes, b => b == "?");
    }

    [Fact]
    public async Task SendAsync_times_out_after_CommandTimeout_without_reply()
    {
        // Long-running blocking commands like $H (homing) may take tens of
        // seconds; the timeout is configurable and only fires when the reply
        // genuinely never arrives.
        var transport = new FakeGrblTransport { AutoReply = false };
        using var svc = new RealGrblService(transport) { CommandTimeout = TimeSpan.FromMilliseconds(100) };
        await svc.ConnectAsync("fake");

        await Assert.ThrowsAsync<TimeoutException>(async () => await svc.SendAsync("$H"));
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
        // Feed the $I reply only once $I is actually written (on real hardware the
        // reply only ever comes after the command, so lines must not arrive before it).
        await WaitUntil(() => transport.Sent.Contains("$I"), 3000);
        transport.Enqueue("Grbl 1.1h ['$' for help]");
        transport.Enqueue("ok");
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("$I", transport.Sent);
        Assert.True(await WaitUntilAsync(() => svc.Capabilities.IsKnown, 3000));
        Assert.False(svc.Capabilities.IsGrblHAL);
        Assert.Equal("1.1h", svc.Capabilities.Version);
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