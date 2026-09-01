
/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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
using SenderNG.Core.Settings;

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
    private readonly BehaviorSubject<WorkParameters> _workParameters;
    private readonly Subject<string> _receivedLines = new();
    private readonly IGrblTransport _transport;
    private readonly object _gate = new();
    private readonly Queue<IMessageWaiter> _waiters = new();

    private CancellationTokenSource? _cts;
    private string? _connectionTarget;
    private Task? _consumerTask;
    private Timer? _pollTimer;
    private Timer? _healthTimer;
    private volatile bool _pollerSuspended;
    private int _disconnecting;
    private int _pollIntervalMs = 200;
    private int _healthCheckIntervalMs = 1000;
    private TimeSpan _commandTimeout = TimeSpan.FromSeconds(60);
    // Liveness timestamp: any line the consumer reads (status report or reply)
    // slides the CommandTimeout window, so a controller that keeps talking is
    // never timed out (homing a big machine, feed hold). Written on the consumer
    // thread, read by AwaitWaiterAsync — Volatile access keeps both cheap.
    private long _lastLineReceivedTicks;
    private static readonly TimeSpan LivenessTick = TimeSpan.FromMilliseconds(200);

    public bool IsConnected { get; private set; }

    /// <summary>Identity of the current connection (serial port or network
    /// endpoint) — readable endpoint for status text. Kept from the last connect
    /// so a reconnect to the same port reports the same endpoint.</summary>
    public string ConnectionId => _connectionTarget ?? "unknown";

    /// <summary>
    /// Metadata scoping key: the <c>$I</c> firmware fingerprint (firmware,
    /// version.build, raw <c>[OPT:]</c>/<c>[AXS:]</c>) with NO endpoint — moving the
    /// machine to another COM port keeps its names, and two byte-identical
    /// boards (clones) intentionally share one set. "unknown" while the
    /// handshake is incomplete or failed (no tool table is shown then anyway).
    /// </summary>
    public string ControllerKey => Capabilities.ControllerKey;

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

    /// <summary>
    /// Read-only snapshot of the controller work parameters; replaced (never
    /// mutated in place) and published on <see cref="WorkParametersChanges"/>.
    /// </summary>
    public WorkParameters WorkParameters => _workParameters.Value;

    /// <summary>Emits a fresh work-parameters snapshot on connect and whenever a
    /// bracket report (<c>[G54:..]</c>, <c>[TLO:..]</c>, tool/probe) arrives.</summary>
    public IObservable<WorkParameters> WorkParametersChanges => _workParameters;

    public RealGrblService(IGrblTransport? transport = null)
    {
        _transport = transport ?? new SerialGrblTransport();
        _subject = new BehaviorSubject<MachineState>(new MachineState());
        _workParameters = new BehaviorSubject<WorkParameters>(WorkParameters.CreateDefault(3));
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
    /// The SILENCE window for a pending command: how long <see cref="SendAsync"/>
    /// waits for a reply when the controller stops talking entirely. It is NOT a
    /// deadline from the write — any inbound line (a status report, a
    /// work-parameter dump line) slides the window to now+<see cref="CommandTimeout"/>,
    /// and while the machine is paused (Hold feed hold / Door safety door) the
    /// deadline is disabled outright. So long-running commands like <c>$H</c>
    /// (homing a large slow machine can take many minutes) or a feed hold of any
    /// length can never hit a spurious timeout; a genuinely silent controller
    /// (hung, lost command) still fails after one window. The await is fully
    /// async (no UI blocking); disconnects cancel it immediately via the token.
    /// Floored at two status-poll intervals so a short configured window can't
    /// race the poller.
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
        _connectionTarget = port;
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

        // ioSender's connect sequence follows $I with a $# (NGC work parameters)
        // exchange — the [G54:..]..[G92:..]/[TLO:..]/[PRB:..] dump that shows in
        // the console. Best-effort and non-fatal, exactly like the $I handshake:
        // the lines are published on the raw stream so the console shows them;
        // the parser ignores them (no MachineState fields map to work offsets yet).
        try
        {
            // Work-parameter lines are parsed below after capabilities are applied.
        }
        catch (OperationCanceledException)
        {
            // Disconnected mid-handshake; reconnect retries from scratch.
        }
        catch
        {
            // Non-fatal: $# is a console nicety, not required to connect.
        }

        try
        {
            var parameters = WorkParameters.Parse(await GetWorkParameterLinesAsync(port),
                Capabilities.NumAxes, Capabilities.NumTools);
            parameters.ActiveCoordinateSystem = "G54";
            _workParameters.OnNext(parameters);
        }
        catch
        {
            _workParameters.OnNext(
                WorkParameters.CreateDefault(Capabilities.NumAxes, Capabilities.NumTools));
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
            // WPos/MPos arrays are sized to hold all reported positions, and seed
            // the signals the controller reports as available (ioSender's
            // OptionalSignals) so the signals panel gates its optional LEDs.
            lock (_gate)
            {
                if (_subject.Value.NumAxes != Capabilities.NumAxes)
                {
                    _subject.OnNext(new MachineState(Capabilities.NumAxes)
                    {
                        OptionalSignals = Capabilities.OptionalSignals,
                    });
                }
                else if (_subject.Value.OptionalSignals != Capabilities.OptionalSignals)
                {
                    _subject.Value.OptionalSignals = Capabilities.OptionalSignals;
                    _subject.OnNext(CloneOf(_subject.Value));
                }
            }
        }
        catch
        {
            // Non-fatal: capabilities remain available on the property either way.
        }
    }

    /// <summary>
    /// Suspend the '?' poller while a multi-line metadata exchange ($$/$I/$#/
    /// $ES/$EG/$SED) is in flight (ioSender PollGrbl.Suspend/Resume semantics).
    /// Single sends ($H, MDI) do NOT suspend: their status reports are the
    /// liveness channel the silence-based CommandTimeout slides on, and the
    /// consumer loop already routes reports away from the reply FIFO. Resumed
    /// even on failure via a finally guard.
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
    /// Wait for <paramref name="waiter"/> to complete. The deadline is
    /// silence-based: ANY inbound line (a status report included) slides it to
    /// now+<paramref name="timeout"/>, and while the machine is paused
    /// (Hold/Door) the deadline is disabled entirely — a controller that is
    /// alive and talking (homing a large machine, parked on a feed hold) is
    /// never timed out, while a genuinely silent one (no line of any kind for
    /// the window) still fails fast. On timeout/cancel the waiter is removed
    /// from the FIFO queue so a hung exchange can't poison the reader and
    /// starve every later command (ioSender would reset the port here).
    /// </summary>
    private async Task AwaitWaiterAsync(ReplyWaiter waiter, TimeSpan timeout, CancellationToken ct)
    {
        // Never race the poller: if a configured silence window is shorter than
        // the poll period, reports can't arrive in time to slide it. Floor the
        // window at two poll intervals.
        var window = timeout.TotalMilliseconds >= 2 * _pollIntervalMs
            ? timeout
            : TimeSpan.FromMilliseconds(2 * _pollIntervalMs);
        var deadline = DateTime.UtcNow.Add(window);
        try
        {
            while (true)
            {
                if (waiter.Task.IsCompleted)
                {
                    await waiter.Task; // propagate faults, if any
                    return;
                }

                if (IsPausedState())
                {
                    // Feed hold / safety door park the controller: grbl replies
                    // only after the operator resumes, and a pause is
                    // operator-paced by definition. Just wait for the reply (or
                    // cancellation); the deadline logic resumes once the state
                    // leaves Hold/Door (this loop re-checks every tick).
                    await Task.WhenAny(waiter.Task, Task.Delay(LivenessTick, ct));
                    continue;
                }

                var lastLineTicks = Volatile.Read(ref _lastLineReceivedTicks);
                if (lastLineTicks > 0)
                {
                    var slid = new DateTime(lastLineTicks, DateTimeKind.Utc).Add(window);
                    if (slid > deadline)
                        deadline = slid;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    throw new TimeoutException(
                        "Timed out waiting for the controller reply (no line received within the CommandTimeout window).");

                var delay = remaining < LivenessTick ? remaining : LivenessTick;
                await Task.WhenAny(waiter.Task, Task.Delay(delay, ct));
            }
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
        catch (OperationCanceledException)
        {
            // The CALLER's token cancelled the exchange (e.g. the program streamer
            // aborted on alarm/stop), while the connection is still up. Drop the
            // waiter like the timeout path does, so a reply that may never arrive
            // (grbl flushes its buffer into Alarm) can't poison the FIFO and starve
            // later commands (e.g. $X after an aborted run).
            lock (_gate)
            {
                if (_waiters.TryPeek(out var head) && ReferenceEquals(head, waiter))
                    _waiters.Dequeue();
                else
                    waiter.TryComplete();
            }
            throw;
        }
    }

    /// <summary>Hold (feed hold) and Door (safety door open) physically park the
    /// controller: grbl stops processing commands and won't reply until the
    /// pause ends. A timeout while paused is nonsense — the operator paces it.</summary>
    private bool IsPausedState()
    {
        var status = _subject.Value.Status;
        return status.StartsWith("Hold", StringComparison.OrdinalIgnoreCase) ||
               status.StartsWith("Door", StringComparison.OrdinalIgnoreCase);
    }

    private readonly struct PollScope : IDisposable
    {
        private readonly RealGrblService _svc;
        public PollScope(RealGrblService svc) => _svc = svc;
        public void Dispose() => _svc.ResumePolling();
    }

    public Task SendAsync(string line)
    {
        if (_cts is null)
            throw new InvalidOperationException("Not connected.");
        // The poller stays active: grbl answers '?' as a realtime command even
        // while executing $H or a long MDI move, and those status reports are
        // what keep the silence-based CommandTimeout from firing spuriously.
        return SendCoreAsync(line, _cts.Token, throwOnError: true);
    }

    /// <summary>
    /// Streaming send: like <see cref="SendAsync"/>, keeps the '?' poll running
    /// so the DRO/state stay live during a file run (single sends keep it too).
    /// Safe because the consumer loop parses status reports and continues BEFORE
    /// serving the reply FIFO — poll replies can never be captured as command
    /// replies. Returns the controller's raw reply ("ok" / "error:NN") — the
    /// streamer marks the failing line with the exact text and aborts. Throws on
    /// disconnect. The caller's token (linked with the service's) aborts the
    /// wait immediately on alarm/stop, self-healing the FIFO.
    /// </summary>
    public async Task<string> SendProgramLineAsync(string line, CancellationToken cancellationToken = default)
    {
        if (_cts is null)
            throw new InvalidOperationException("Not connected.");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        var reply = await SendCoreAsync(line, linked.Token, throwOnError: false);
        // ioSender re-requests the work parameters on error, so the offsets
        // editor reflects the authoritative board state after a rejected line.
        if (IsErrorReply(reply))
            _ = RefreshWorkParametersAfterErrorAsync();
        return reply;
    }

    private async Task<string> SendCoreAsync(string line, CancellationToken cancellationToken, bool throwOnError)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected.");

        // Register the waiter *before* writing so we can't lose a fast reply
        // that lands between the write and the enqueue. The consumer loop
        // serves waiters FIFO, matching grbl's strictly serial reply stream.
        string? reply = null;
        var waiter = new ReplyWaiter
        {
            // Every exchange answers exactly one line; capture it so a rejected
            // command ("error:nn") is surfaced instead of silently succeeding.
            OnLine = line => { reply = line; return true; },
        };
        lock (_gate) _waiters.Enqueue(waiter);
        await _transport.WriteLineAsync(line);
        await AwaitWaiterAsync(waiter, _commandTimeout, cancellationToken);

        if (throwOnError && IsErrorReply(reply))
        {
            // ioSender re-requests the work parameters when the controller signals
            // an error (GrblError -> GrblWorkParameters.Get), so the offsets editor
            // reflects the authoritative board state (e.g. a rejected G10) instead
            // of keeping a stale snapshot. Best-effort and fire-and-forget.
            _ = RefreshWorkParametersAfterErrorAsync();
            throw new InvalidOperationException($"Controller rejected '{line}': {reply}");
        }

        if (line.Trim().Equals("$H", StringComparison.OrdinalIgnoreCase))
        {
            lock (_gate)
            {
                if (!_subject.Value.IsHomed)
                {
                    _subject.Value.IsHomed = true;
                    _subject.OnNext(CloneOf(_subject.Value));
                }
            }
        }

        return reply ?? string.Empty;
    }

    private Task RefreshWorkParametersAfterErrorAsync() =>
        SafeReadWorkParametersAsync();

    private async Task SafeReadWorkParametersAsync()
    {
        try
        {
            await ReadWorkParametersAsync();
        }
        catch
        {
            // Non-fatal: a failed re-read just keeps the last published snapshot.
        }
    }

    /// <summary>True when a command replied with grbl/grblHAL's "error" token.</summary>
    private static bool IsErrorReply(string? line) =>
        line?.Equals("error", StringComparison.OrdinalIgnoreCase) == true ||
        line?.StartsWith("error:", StringComparison.OrdinalIgnoreCase) == true;

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
    /// $ES dump: grblHAL setting metadata ([SETTING:...] lines) — ENUMS boards only.
    /// Classic grbl replies "error" to the unknown command (or nothing), so this
    /// returns empty and the caller falls back to the embedded catalog.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetSettingDetailLinesAsync()
    {
        var lines = new List<string>();
        try
        {
            // On classic grbl an unsupported $ command draws an "error:NN" reply. Treat
            // any error line as the terminal line: ExchangeAsync drops the terminal
            // line, so the metadata list stays empty and the loader falls back to the
            // embedded catalog (classic settings are mapped onto the grblHAL tree).
            await ExchangeAsync("$ES", lines, IsOkOrError, TimeSpan.FromSeconds(1.5));
        }
        catch (TimeoutException)
        {
            // Classic grbl may stay silent to unknown $ commands; keep what we read.
        }
        return lines;
    }

    /// <summary>
    /// $EG dump: grblHAL settings group tree ([SETTINGGROUP:...] lines) — ENUMS boards only.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetSettingGroupLinesAsync()
    {
        var lines = new List<string>();
        try
        {
            // See GetSettingDetailLinesAsync — classic's "error:NN" must terminate and
            // be dropped so the loader uses the catalog grouping rather than a bogus
            // group tree (which previously pushed every setting into "Ungrouped").
            await ExchangeAsync("$EG", lines, IsOkOrError, TimeSpan.FromSeconds(1.5));
        }
        catch (TimeoutException)
        {
            // Classic grbl may stay silent to unknown $ commands; keep what we read.
        }
        return lines;
    }

    /// <summary>
    /// $SED=&lt;id&gt;: long description for one setting (grblHAL SED boards only).
    /// The firmware replies "[SETTINGDESCR:id|text]" then "ok" — or just "ok" when
    /// the setting has no description (returns null then).
    /// </summary>
    public async Task<string?> GetSettingDescriptionAsync(int id)
    {
        var lines = new List<string>();
        try
        {
            // Treat classic's "error:NN" (unsupported command) as terminal so the
            // 1.5s exchange returns promptly instead of waiting out the timeout.
            await ExchangeAsync($"$SED={id}", lines, IsOkOrError, TimeSpan.FromSeconds(1.5));
        }
        catch (TimeoutException)
        {
            // Classic grbl may stay silent to unknown $ commands; keep what we read.
        }
        return lines.Select(GrblSettingsParser.ParseDescriptionLine).FirstOrDefault(static d => d.HasValue)?.Description;
    }

    /// <summary>
    /// Terminal-line predicate for grblHAL-only metadata commands ($ES/$EG/$SED).
    /// <c>ok</c> ends a successful reply and <c>error</c>/<c>error:NN</c> ends an
    /// unsupported one — classic grbl uses the latter for commands it doesn't know,
    /// and because <see cref="ExchangeAsync"/> drops the terminal line, an error
    /// reply leaves the collected list empty (loader falls back to the catalog).
    /// </summary>
    private static bool IsOkOrError(string line) =>
        line == "ok" || line == "error" || line.StartsWith("error:", StringComparison.OrdinalIgnoreCase);

    /// <summary>One-shot $# re-read so the UI can refresh from the board (ioSender
    /// re-requests on view activation and on controller error).</summary>
    public Task RefreshWorkParametersAsync() => ReadWorkParametersAsync();

    /// <summary>Reads the controller's current work-coordinate table using $# and
    /// republishes the refreshed snapshot.</summary>
    public async Task<WorkParameters> ReadWorkParametersAsync()
    {
        var lines = await GetWorkParameterLinesAsync(string.Empty);
        var parameters = WorkParameters.Parse(lines, Capabilities.NumAxes, Capabilities.NumTools);
        _workParameters.OnNext(parameters);
        return parameters;
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
    /// <c>$#</c> work-parameter exchange: collect the [G54:..]..[PRB:..] dump
    /// until the trailing "ok"/"error" or a short deadline (classic grbl replies
    /// without "ok"). The lines are published to the raw stream by the consumer
    /// loop before this collector sees them, so the console renders the dump
    /// exactly as ioSender does at connect.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetWorkParameterLinesAsync(string port)
    {
        var lines = new List<string>();
        try
        {
            await ExchangeAsync("$#", lines, l => l is "ok" or "error", TimeSpan.FromSeconds(1.5));
        }
        catch (TimeoutException)
        {
            // Classic grbl $# has no "ok"; keep what we read.
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
        var cts = _cts ?? throw new InvalidOperationException("Command exchange requires a started service");
        await AwaitWaiterAsync(waiter, timeout, cts.Token);
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

            // Liveness heartbeat for the silence-based CommandTimeout: every
            // inbound line (status report or reply) slides pending waits' deadline.
            Volatile.Write(ref _lastLineReceivedTicks, DateTime.UtcNow.Ticks);

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

            // Fold bracket work-parameter reports ([G54:..], [TLO:..], [TLR:..],
            // tool table) into the live snapshot so the offsets editor and the
            // work-parameters widget update without a manual refresh. The line also
            // falls through to the waiter below so a $# dump still collects it.
            ApplyLiveWorkParameter(line);

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

    /// <summary>
    /// Applies a bracketed work-parameter report to a clone of the live snapshot
    /// and republishes it when the line changed something. Runs on the consumer
    /// thread alongside status parsing; the UI is updated by subscribers posting
    /// back to the dispatcher.
    /// </summary>
    private void ApplyLiveWorkParameter(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line[0] != '[')
            return;
        var clone = _workParameters.Value.Clone();
        if (!clone.ApplyLine(line))
            return;
        lock (_gate)
            _workParameters.OnNext(clone);
    }

    /// <summary>Deep-copy a <see cref="MachineState"/> so each emitted snapshot is isolated.</summary>
    private static MachineState CloneOf(MachineState src)
    {
        var copy = new MachineState(src.NumAxes)
        {
            Status = src.Status,
            Signals = src.Signals,
            OptionalSignals = src.OptionalSignals,
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
            IsHomed = src.IsHomed,
        };
        Array.Copy(src.Positions, copy.Positions, Math.Min(src.Positions.Length, copy.Positions.Length));
        copy.MachinePositions = src.MachinePositions is null
            ? null
            : (double[])src.MachinePositions.Clone();
        copy.WorkOffsets = src.WorkOffsets is null
            ? null
            : (double[])src.WorkOffsets.Clone();
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