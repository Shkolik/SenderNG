using System.Reactive.Subjects;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

/// <summary>
/// Stable IGrblService singleton that forwards to a swappable inner service.
/// Consumers subscribe once and keep receiving state across demo/real swaps;
/// the proxy re-subscribes its relay subject whenever <see cref="Inner"/> changes.
/// </summary>
public class GrblServiceProxy : IGrblService, IGrblSettingsProvider
{
    private IGrblService _inner;
    private IDisposable? _subscription;
    private IDisposable? _linesSubscription;
    private EventHandler? _innerConnectionHandler;
    private EventHandler? _connectionChanged;
    private readonly BehaviorSubject<MachineState> _subject = new(new MachineState());
    // Bounded replay so the console realizes late (center-panel tabs are built
    // lazily by Avalonia from a deferred enumeration) still sees the connect
    // burst — the $I identity/options lines and recent status — instead of an
    // empty log. The demo service publishes no raw lines, so the buffer is empty
    // in demo mode (the console stays silent as intended).
    private readonly ReplaySubject<string> _receivedLines = new(bufferSize: 50);

    public GrblServiceProxy(IGrblService inner) =>
        _inner = SetInner(inner);

    /// <summary>
    /// Forwards the inner service's connection events and also fires on every
    /// inner swap (the new inner may already be connected). Subscribers survive
    /// demo/real/network transitions, like the state relay.
    /// </summary>
    public event EventHandler? ConnectionChanged
    {
        add => _connectionChanged += value;
        remove => _connectionChanged -= value;
    }

    public IGrblService Inner
    {
        get => _inner;
        set => _inner = SetInner(value);
    }

    public bool IsConnected => _inner.IsConnected;

    public ControllerCapabilities Capabilities => _inner.Capabilities;

    // BehaviorSubject: replays the current machine state, so subscribers that
    // attach after construction (widgets, MainViewModel) immediately see the
    // inner service's latest state instead of waiting for the next emission.
    // The relay subscription in SetInner feeds the inner's replayed value
    // through here on attach and on every demo/real swap.
    public IObservable<MachineState> State => _subject;

    // Raw controller output is relayed the same way as state, so console
    // subscribers keep receiving lines across demo/real/network inner swaps.
    public IObservable<string> ReceivedLines => _receivedLines;

    public Task ConnectAsync(string port) => _inner.ConnectAsync(port);

    public void Disconnect() => _inner.Disconnect();

    public Task SendAsync(string line) => _inner.SendAsync(line);

    public void SendRealtime(byte code) => _inner.SendRealtime(code);

    public Task<IReadOnlyList<string>> GetSettingLinesAsync() =>
        _inner is IGrblSettingsProvider provider
            ? provider.GetSettingLinesAsync()
            : throw new NotSupportedException("The active machine does not support settings.");

    private IGrblService SetInner(IGrblService value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _subscription?.Dispose();
        _subscription = value.State.Subscribe(_subject.OnNext);
        _linesSubscription?.Dispose();
        _linesSubscription = value.ReceivedLines.Subscribe(_receivedLines.OnNext);

        if (_innerConnectionHandler is not null)
            _inner.ConnectionChanged -= _innerConnectionHandler;
        _innerConnectionHandler = (_, _) => _connectionChanged?.Invoke(this, EventArgs.Empty);
        value.ConnectionChanged += _innerConnectionHandler;

        // The new inner may already be connected/disconnected; tell subscribers
        // so e.g. the state indicator updates without waiting for a state event.
        _connectionChanged?.Invoke(this, EventArgs.Empty);
        return value;
    }
}
