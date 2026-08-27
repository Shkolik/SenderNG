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
    private Timer? _healthTimer;
    private volatile bool _pollerSuspended;
    private int _disconnecting;
    private int _pollIntervalMs = 200;
    private int _healthCheckIntervalMs = 1000;
    private TimeSpan _commandTimeout = TimeSpan.FromSeconds(60);

    public bool IsConnected { get; private set; }

    public event EventHandler? ConnectionChanged;

    /// <summary>Every complete line read from the transport, before status parsing (the console renders these).</summary>
    public IObservable<string> ReceivedLines => _receivedLines;

    public IObservable<MachineState> State => _subject;

    /// <summary>Serial baud rate used on connect. Default 115200, tunable per profile.</summary>
    public int BaudRate { get; set; } = DefaultBaudRate;

    /// <summary>
    /// Serial "On connect" reset action (ioSender Port-dialog dropdown): Toggle DTR
    /// pulses the DTR line low after the port opens to reset an 8-bit Arduino
    /// bootloader; Toggle RTS does the same for ESP32-style boards. Applied by the
    /// serial transport when <see cref="ConnectAsync"/> opens the port.
    /// </summary>
    public ConnectResetMode SerialResetMode { get; set; }

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
    /// How often the transport-health watchdog runs (≥100 ms). While connected it
    /// asks the transport whether the port/socket is still present and flips to
    /// NOT CONNECTED when it isn't — catching unplugs that don't surface as a
    /// read/write exception (e.g. a removed USB-serial COM port).
    /// </summary>
    public int HealthCheckIntervalMs
    {
        get => _healthCheckIntervalMs;
        set => _healthCheckIntervalMs = Math.Max(100, value);
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
        {
            // A previous session may have left a stale "connected" flag when a
            // disconnect didn't surface as an exception. If the transport reports
            // the connection is actually gone, reset first so we reconnect fresh
            // instead of silently no-op'ing.
            if (_transport is IPortPresenceCheck present && !present.IsPortPresent())
                HandleDisconnect();
            else
                return;
        }

        _transport.Open(port, BaudRate, SerialResetMode);
        IsConnected = true;
        ConnectionChanged?.Invoke(this, EventArgs.Empty);

        _cts = new CancellationTokenSource();
        _consumerTask = Task.Run(() => ConsumerLoopAsync(_cts.Token));

        // The DTR-at-open reset reboots Arduino-style boards through their AVR
        // bootloader; writing ANY bytes during the bootloader window can stall the
        // controller (Optiboot consumes them as programming protocol and never
        // starts grbl — verified on a Mega 2560: early '?'/$I writes left it
        // silent until a fresh reset). So hold ALL writes until the boot banner
        // appears (or a bounded timeout for controllers that don't reset — e.g.
        // an already-running board or TCP, which never prints a banner here).
        if (_transport.ResetsOnOpen)
            await WaitForBootBannerAsync();

        await StartPolling(port);
        StartHealthWatch();

        // Identify the controller so UI can adapt to its capabilities. Non-fatal:
        // a controller that ignores $I (or answers slowly) still connects and polls.
        // Retry: a freshly booted board (right after a re-plug or an On-connect DTR
        // reset) can drop the first $I while its UART/boot banner settles — that left
        // the DRO at the default 3 axes until a later reconnect. Each attempt is a
        // fresh exchange; the loop stops as soon as the board identifies itself. The
        // window is generous (~20s worst case) because a DTR pulse hard-resets the
        // board, which can take several seconds to boot and answer.
        try
        {
            for (var attempt = 0; attempt < 10 && !_cts.IsCancellationRequested; attempt++)
            {
                var infoLines = await GetInfoLinesAsync(port);
                Capabilities = ControllerInfoParser.ParseInfo(infoLines);
                // A boot banner alone ("Grbl 1.1h ['$' for help]") is not a complete
                // handshake: the DTR-at-open reset reboots Arduino-style boards, and
                // a $I sent while the UART is still initializing is dropped. The
                // banner makes ParseInfo report Known, so keep retrying until we
                // actually collect $I reply lines ([VER:]/[OPT:]/[AXS:]/firmware).
                // A boot [MSG:...] line doesn't count either — it precedes the reply.
                if (Capabilities.IsKnown &&
                    infoLines.Any(l => l.StartsWith('[') && !l.StartsWith("[MSG:")))
                    break;
                await Task.Delay(500, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Disconnected mid-handshake; reconnect will retry from scratch.
            Capabilities = ControllerCapabilities.Unknown;
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

    /// <summary>
    /// Wait (bounded) for the controller's boot banner on the raw-line stream,
    /// e.g. "Grbl 1.1h ['$' for help]" or "grblHAL 1.1f ['$' or '$HELP' for help]".
    /// The banner proves grbl is up and its UART is accepting commands; writing
    /// before it appears can be dropped (boot race) or worse, consumed by an AVR
    /// bootloader. Boards that don't reset on open (already booted, TCP) print
    /// nothing and just pay the bounded wait.
    /// </summary>
    private async Task WaitForBootBannerAsync()
    {
        try
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var sub = _receivedLines.Subscribe(line =>
            {
                if (line.StartsWith("Grbl", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("grblHAL", StringComparison.OrdinalIgnoreCase))
                    tcs.TrySetResult();
            });
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), _cts!.Token);
        }
        catch (TimeoutException)
        {
            // No banner — the controller didn't reset (already running) or prints
            // nothing; proceed with the normal handshake.
        }
        catch (OperationCanceledException)
        {
            // Disconnected while waiting.
        }
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
    /// Watchdog that asks the transport whether the connection is still physically
    /// present while we're connected. Catches unplugs that reads/writes never
    /// surface as exceptions (e.g. a removed USB-serial COM port on Windows).
    /// </summary>
    private void StartHealthWatch()
    {
        _healthTimer?.Dispose();
        _healthTimer = new Timer(_ => CheckHealth(), null, _healthCheckIntervalMs, _healthCheckIntervalMs);
    }

    private void CheckHealth()
    {
        if (!IsConnected || _cts is null)
            return;
        if (_transport is IPortPresenceCheck present && !present.IsPortPresent())
            HandleDisconnect();
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
            // A failed status write can be a transient adapter hiccup; only treat
            // it as a disconnect when the transport confirms the connection is gone.
            if (_transport is IPortPresenceCheck present && !present.IsPortPresent())
                HandleDisconnect();
        }
    }

    /// <summary>
    /// Spontaneous transport failure (reader hit an error, the poll watchdog found
    /// the port/socket gone, or ConnectAsync detected a stale dead session).
    /// Flips the app to NOT CONNECTED and tears the session down; safe to call from
    /// the reader or timer threads, and idempotent via the <see cref="IsConnected"/> guard.
    /// </summary>
    private void HandleDisconnect()
    {
        if (!IsConnected)
            return;
        Disconnect();
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
        catch (Exception ex) when (!IsConnected)
        {
            // The controller disconnected (spontaneously or via Disconnect) while the
            // command was in flight. Whether the waiter was faulted first or the token
            // cancellation won the race, surface a clear message instead of a bare
            // TaskCanceledException. IsConnected is set false at the very start of
            // Disconnect, so this branch is deterministic.
            if (ex is InvalidOperationException)
                throw;
            throw new InvalidOperationException("Controller disconnected.", ex);
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
                return; // deliberate disconnect (cts cancelled by Disconnect)
            }
            catch (OperationCanceledException)
            {
                // Transport signalled closure on its own — a dropped TCP socket or
                // a failed serial read after unplug. That's a disconnect: flip the
                // app state and tear down instead of silently ending the reader.
                HandleDisconnect();
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
        // Idempotent against concurrent calls — HandleDisconnect can race with a
        // user-initiated Disconnect, and a double teardown would dispose _cts twice.
        if (Interlocked.Exchange(ref _disconnecting, 1) == 1)
            return;
        try
        {
            // Flip the flag first so in-flight AwaitWaiterAsync calls (which guard on
            // IsConnected) deterministically report "Controller disconnected."
            IsConnected = false;
            _pollerSuspended = true;
            _pollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _pollTimer?.Dispose();
            _pollTimer = null;

            _healthTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _healthTimer?.Dispose();
            _healthTimer = null;

            // Fault any in-flight command waiters so SendAsync/ExchangeAsync fail fast
            // with a clear message instead of hanging until the command timeout.
            // Fault BEFORE cancelling the token so the awaiting WaitAsync deterministically
            // throws the waiter's exception rather than racing with a TaskCanceledException.
            lock (_gate)
            {
                while (_waiters.Count > 0)
                    _waiters.Dequeue().TrySetException(
                        new InvalidOperationException("Controller disconnected."));
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

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
        finally
        {
            Interlocked.Exchange(ref _disconnecting, 0);
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

        /// <summary>Fault the wait (used on disconnect so in-flight commands fail fast).</summary>
        bool TrySetException(Exception exception);
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

        public bool TrySetException(Exception exception)
        {
            try
            {
                return _tcs.TrySetException(exception);
            }
            catch
            {
                return false;
            }
        }
    }
}