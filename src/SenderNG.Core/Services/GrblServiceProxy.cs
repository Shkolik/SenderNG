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
    private readonly BehaviorSubject<MachineState> _subject = new(new MachineState());

    public GrblServiceProxy(IGrblService inner) =>
        _inner = SetInner(inner);

    public IGrblService Inner
    {
        get => _inner;
        set => _inner = SetInner(value);
    }

    public bool IsConnected => _inner.IsConnected;

    // BehaviorSubject: replays the current machine state, so subscribers that
    // attach after construction (widgets, MainViewModel) immediately see the
    // inner service's latest state instead of waiting for the next emission.
    // The relay subscription in SetInner feeds the inner's replayed value
    // through here on attach and on every demo/real swap.
    public IObservable<MachineState> State => _subject;

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
        return value;
    }
}
