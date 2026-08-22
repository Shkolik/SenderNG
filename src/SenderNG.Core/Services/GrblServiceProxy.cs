using System.Reactive.Subjects;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

/// <summary>
/// Stable IGrblService singleton that forwards to a swappable inner service.
/// Consumers subscribe once and keep receiving state across demo/real swaps;
/// the proxy re-subscribes its relay subject whenever <see cref="Inner"/> changes.
/// </summary>
public class GrblServiceProxy : IGrblService
{
    private IGrblService _inner;
    private IDisposable? _subscription;
    private readonly Subject<MachineState> _subject = new();

    public GrblServiceProxy(IGrblService inner) =>
        _inner = SetInner(inner);

    public IGrblService Inner
    {
        get => _inner;
        set => _inner = SetInner(value);
    }

    public bool IsConnected => _inner.IsConnected;

    // The relay subject, not the inner subject, so existing subscriptions
    // survive an inner swap.
    public IObservable<MachineState> State => _subject;

    public Task ConnectAsync(string port) => _inner.ConnectAsync(port);

    public void Disconnect() => _inner.Disconnect();

    public Task SendAsync(string line) => _inner.SendAsync(line);

    private IGrblService SetInner(IGrblService value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _subscription?.Dispose();
        _subscription = value.State.Subscribe(_subject.OnNext);
        return value;
    }
}
