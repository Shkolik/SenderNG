using System.Reactive.Subjects;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

/// <summary>
/// Serial grbl / grblHAL service. A dedicated background reader owns the port:
/// status reports (<c>&lt;...&gt;</c>, <c>[PRB:...]</c>) are parsed straight into
/// <see cref="MachineState"/> and emitted continuously, while line-command replies
/// (<c>ok</c>, <c>$N=...</c>) are handed to the awaiting command. A timer polls the
/// controller with the realtime '?' byte so live DRO/MPos update without user action
/// (ioSender <c>PollGrbl</c> semantics). Connect is non-blocking and the class is
/// <see cref="IDisposable"/> so the reader/poller never leak.
///
/// Transport (<see cref="IGrblTransport"/>) is injected for testability; the default
/// is <see cref="SerialGrblTransport"/> over <c>System.IO.Ports</c>.
/// </summary>
public class RealGrblService : IGrblService, IGrblSettingsProvider, IDisposable
{
    private const int DefaultBaudRate = 115200;
    private readonly BehaviorSubject<MachineState> _subject;
    private readonly Subject<string> _receivedLines = new();
    private readonly IGrblTransport _transport;
    private readonly object _gate = new();
    private readonly Queue<IMessageWaiter> _waiters = new();

    private CancellationTokenSource? _cts;
    private Task? _consumerTask;
    private Timer? _pollTimer;
    private volatile bool _pollerSuspended;
    private int _pollIntervalMs = 200;
    private TimeSpan _commandTimeout = TimeSpan.FromSeconds(60);

    public bool IsConnected { get; private set; }

    public event EventHandler? ConnectionChanged;

    /// <summary>Every complete line read from the transport, before status parsing (the console renders these).</summary>
    public IObservable<string> ReceivedLines => _receivedLines;

    public IObservable<MachineState> State => _subject;

    /// <summary>Serial baud rate used on connect. Default 115200, tunable per profile.</summary>
    public int BaudRate { get; set; } = DefaultBaudRate;

    /// <summary>Capabilities from the connect-time <c>$I</c> handshake; Unknown until populated.</summary>
    public ControllerCapabilities Capabilities { get; private set; } = ControllerCapabilities.Unknown;

    public RealGrblService(IGrblTransport? transport = null)
    {
        _transport = transport ?? new SerialGrblTransport();
        _subject = new BehaviorSubject<MachineState>(new MachineState());
    }

    /// <summary>Status-poll interval in ms (≥50). Tuned by tests; shown for visibility.</summary>
    public int PollIntervalMs
    {
        get => _pollIntervalMs;
        set => _pollIntervalMs = Math.Max(50, value);
    }

    /// <summary>
    /// How long <see cref="SendAsync"/> waits for the command's "ok"/"error"
    /// reply. grbl is a blocking controller: it answers only after the command
    /// finishes, and long-running commands like <c>$H</c> (homing) or a slow MDI
    /// move can take tens of seconds — a short timeout turns a normal cycle into
    /// a spurious TimeoutException. The await is fully async (no UI blocking);
    /// disconnects cancel it immediately via the token.
    /// </summary>
    public TimeSpan CommandTimeout
    {
        get => _commandTimeout;
        set => _commandTimeout = value > TimeSpan.Zero ? value : TimeSpan.FromSeconds(60);
    }

    public async Task ConnectAsync(string port)
    {
        if (IsConnected)
            return;

        _transport.Open(port, BaudRate);
        IsConnected = true;
        ConnectionChanged?.Invoke(this, EventArgs.Empty);

        _cts = new CancellationTokenSource();
        _consumerTask = Task.Run(() => ConsumerLoopAsync(_cts.Token));

        await StartPolling(port);

        // Identify the controller so UI can adapt to its capabilities. Non-fatal:
        // a controller that ignores $I (or answers slowly) still connects and polls.
        try
        {
            Capabilities = ControllerInfoParser.ParseInfo(await GetInfoLinesAsync(port));
        }
        catch
        {
            Capabilities = ControllerCapabilities.Unknown;
        }

        // Push the discovered axis count into the legacy GrblInfo static (the
        // DRO, gcode parser and expressions read it) and into the live machine
        // state so the DRO/signals grow to the board's real axes.
        ApplyCapabilities();
    }

    private async Task StartPolling(string port)
    {
        // Brief handshake wait: let the controller settle, then begin the ? poll.
        try
        {
            await Task.Delay(100, _cts!.Token);
        }
        catch (OperationCanceledException) { }

        if (_cts is null || _cts.IsCancellationRequested)
            return;

        _pollTimer = new Timer(_ => PollTick(), null, 0, _pollIntervalMs);
    }

    /// <summary>
    /// Reflect the connect-time <c>$I</c> capabilities into the legacy
    /// <see cref="GrblInfo"/> static (axis count, grblHAL flag) and into the live
    /// <see cref="MachineState"/> so the DRO and signals show the board's real
    /// axes. Runs best-effort after the handshake; classic grbl that omits $I
    /// keeps the defaults.
    /// </summary>
    private void ApplyCapabilities()
    {
        if (!Capabilities.IsKnown || Capabilities.NumAxes <= 0)
            return;

        try
        {
            // Legacy static is authoritative for parsing/expressions; only update
            // when the handshake actually identified the board.
            GrblInfo.IsGrblHAL = Capabilities.IsGrblHAL;
            GrblInfo.NumAxes = Capabilities.NumAxes;

            // Grow the live state so users immediately see the extra axes and
            // WPos/MPos arrays are sized to hold all reported positions.
            lock (_gate)
            {
                if (_subject.Value.NumAxes != Capabilities.NumAxes)
                    _subject.OnNext(new MachineState(Capabilities.NumAxes));
            }
        }
        catch
        {
            // Non-fatal: capabilities remain available on the property either way.
        }
    }

    private void PollTick()
    {
        if (_pollerSuspended || !IsConnected || _cts?.IsCancellationRequested == true)
            return;
        try
        {
            _transport.WriteByte(0x3F); // '?' - realtime status request
        }
        catch
        {
            // Adapter hiccup; the next tick retries.
        }
    }

    /// <summary>
    /// Suspend the '?' poller while a line-command reply is in flight so $$/$I/probe
    /// flows aren't interleaved with status requests (ioSender PollGrbl.Suspend/Resume
    /// semantics). Resumed even on failure via a finally guard.
    /// </summary>
    private PollScope SuspendPolling()
    {
        _pollerSuspended = true;
        return new PollScope(this);
    }

    private void ResumePolling()
    {
        _pollerSuspended = false;
    }

    /// <summary>
    /// Wait for <paramref name="waiter"/> to complete. On timeout the waiter is
    /// removed from the FIFO queue so a hung command can't poison the reader and
    /// starve every later exchange (ioSender would reset the port here). Marks the
    /// waiting done so the consumer loop stops trying to serve it.
    /// </summary>
    private async Task AwaitWaiterAsync(ReplyWaiter waiter, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await waiter.Task.WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            lock (_gate)
            {
                // The head waiter is the one we're awaiting (commands are served FIFO
                // and never concurrent); drop it so subsequent commands get served.
                if (_waiters.TryPeek(out var head) && ReferenceEquals(head, waiter))
                    _waiters.Dequeue();
                else
                    waiter.TryComplete(); // a later waiter owned the head; just cancel this task
            }
            throw;
        }
    }

    private readonly struct PollScope : IDisposable
    {
        private readonly RealGrblService _svc;
        public PollScope(RealGrblService svc) => _svc = svc;
        public void Dispose() => _svc.ResumePolling();
    }

    public async Task SendAsync(string line)
    {
        if (!IsConnected || _cts is null)
            throw new InvalidOperationException("Not connected.");

        // Suspend the '?' poll so its status replies don't interleave with this
        // command's payload. Register the waiter *before* writing so we can't lose a
        // fast reply that lands between the write and the enqueue. The consumer loop
        // serves waiters FIFO, matching grbl's strictly serial reply stream.
        using var poll = SuspendPolling();
        var waiter = new ReplyWaiter();
        lock (_gate) _waiters.Enqueue(waiter);
        await _transport.WriteLineAsync(line);
        await AwaitWaiterAsync(waiter, _commandTimeout, _cts.Token);
    }

    public void SendRealtime(byte code)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected.");
        _transport.WriteByte(code);
    }

    /// <summary>
    /// $$ report: collect "$N=value" lines until the trailing "ok". Consumption is
    /// driven off the shared reader, so status reports arriving mid-dump don't corrupt it.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetSettingLinesAsync()
    {
        var lines = new List<string>();
        await ExchangeAsync("$$", lines, l => l is "ok" or "error");
        return lines;
    }

    /// <summary>
    /// <c>$I</c> handshake: collect identity/options lines until the trailing "ok"
    /// or a short deadline (classic grbl may not send "ok"). Doesn't throw on
    /// timeout — returns whatever arrived so the handshake is never fatal.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetInfoLinesAsync(string port)
    {
        var lines = new List<string>();
        try
        {
            await ExchangeAsync("$I", lines, l => l is "ok" or "error", TimeSpan.FromSeconds(1.5));
        }
        catch (TimeoutException)
        {
            // Classic grbl $I has no "ok"; keep what we read.
        }
        return lines;
    }

    /// <summary>
    /// Write a command and run the reader-handled reply through <paramref name="onLine"/>
    /// until <paramref name="isLast"/> reports a terminal line. Status reports are
    /// parsed into the live state and never reach the caller.
    /// </summary>
    private async Task ExchangeAsync(string command, ICollection<string> onLine, Func<string, bool> isLast)
        => await ExchangeAsync(command, onLine, isLast, TimeSpan.FromSeconds(5));

    private async Task ExchangeAsync(string command, ICollection<string> onLine, Func<string, bool> isLast, TimeSpan timeout)
    {
        var waiter = new ReplyWaiter
        {
            OnLine = line =>
            {
                // Don't add the terminal line itself (e.g. the trailing 'ok' after
                // a $$ dump) to the collected output.
                var last = isLast(line);
                if (!last)
                    onLine.Add(line);
                return last;
            },
        };
        using var poll = SuspendPolling();
        lock (_gate) _waiters.Enqueue(waiter);
        await _transport.WriteLineAsync(command);
        await AwaitWaiterAsync(waiter, timeout, _cts.Token);
    }

    private async Task ConsumerLoopAsync(CancellationToken ct)
    {
        // The consumer owns the port's read side unconditionally so the poller's
        // status reports are drained even when no command is pending. gbl processes
        // commands serially and answers in order, so the head waiter always matches
        // the currently-active command; status reports are parsed regardless.
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await _transport.ReadLineAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // Transport signalled closure (e.g. a dropped TCP connection);
                // end the reader cleanly instead of faulting the background task.
                return;
            }

            if (line is null)
                continue; // adapter timeout; keep reading

            // Raw line first: the console shows actual controller output (status
            // reports, ok/error replies, $I/$ settings dumps) unfiltered by parsing.
            _receivedLines.OnNext(line);

            // Parse into a fresh snapshot so observers hold a stable instance that
            // later polls can't mutate out from under them (BehaviorSubject + in-place
            // mutation would let a held reference change after emission).
            var state = CloneOf(_subject.Value);
            if (StatusReportParser.Parse(line, state))
            {
                _subject.OnNext(state);
                continue;
            }

            // A command reply: feed it to the head waiter (FIFO). Drop stray
            // replies that arrive with no pending command.
            IMessageWaiter? head;
            lock (_gate) { _waiters.TryPeek(out head); }
            if (head is null)
                continue;

            if (head.HandleLine(line))
            {
                head.TryComplete();
                lock (_gate) _waiters.Dequeue();
            }
        }
    }

    /// <summary>Deep-copy a <see cref="MachineState"/> so each emitted snapshot is isolated.</summary>
    private static MachineState CloneOf(MachineState src)
    {
        var copy = new MachineState(src.NumAxes)
        {
            Status = src.Status,
            Signals = src.Signals,
            Flood = src.Flood,
            Mist = src.Mist,
            IsToolChanging = src.IsToolChanging,
            SpindleState = src.SpindleState,
            Feed = src.Feed,
            Speed = src.Speed,
            FeedOvr = src.FeedOvr,
            RapidOvr = src.RapidOvr,
            SpindleOvr = src.SpindleOvr,
            ProbeSuccess = src.ProbeSuccess,
        };
        Array.Copy(src.Positions, copy.Positions, Math.Min(src.Positions.Length, copy.Positions.Length));
        copy.MachinePositions = src.MachinePositions is null
            ? null
            : (double[])src.MachinePositions.Clone();
        copy.ProbePosition = src.ProbePosition is null
            ? null
            : (double[])src.ProbePosition.Clone();
        return copy;
    }

    public void Disconnect()
    {
        _pollerSuspended = true;
        _pollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _pollTimer?.Dispose();
        _pollTimer = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        IsConnected = false;
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            _transport.Close();
        }
        catch
        {
            // Best effort on close.
        }
    }

    public void Dispose()
    {
        Disconnect();
        _transport.Dispose();
    }

    /// <summary>Waiters awaiting a command reply, processed by the consumer loop.</summary>
    private interface IMessageWaiter
    {
        /// <summary>Feed a non-status line; return true when the reply is complete.</summary>
        bool HandleLine(string line);

        /// <summary>Mark the wait complete (success or cancelled).</summary>
        bool TryComplete();
    }

    private sealed class ReplyWaiter : IMessageWaiter
    {
        private readonly TaskCompletionSource _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<string, bool>? OnLine { get; init; }

        public Task Task => _tcs.Task;

        public bool HandleLine(string line) => OnLine?.Invoke(line) ?? true;

        public bool TryComplete()
        {
            try
            {
                return _tcs.TrySetResult();
            }
            catch
            {
                return false;
            }
        }
    }
}