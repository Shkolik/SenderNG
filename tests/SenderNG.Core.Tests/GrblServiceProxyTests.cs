using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class GrblServiceProxyTests
{
    private sealed class FakeGrbl(string name) : IGrblService
    {
        public BehaviorSubject<MachineState> Subject { get; } = new(new MachineState(3));
        public List<string> Sent { get; } = [];
        public bool IsConnected { get; private set; }
        public IObservable<MachineState> State => Subject;
        public Task ConnectAsync(string port) { IsConnected = true; return Task.CompletedTask; }
        public void Disconnect() => IsConnected = false;
        public Task SendAsync(string line)
        {
            Sent.Add(line);
            return Task.CompletedTask;
        }
        public override string ToString() => name;
    }

    [Fact]
    public void Proxy_forwards_calls_to_inner_service()
    {
        var inner = new FakeGrbl("a");
        var proxy = new GrblServiceProxy(inner);

        Assert.Same(inner, proxy.Inner);
        Assert.False(proxy.IsConnected);
        proxy.ConnectAsync("COM3").Wait();
        Assert.True(proxy.IsConnected);

        proxy.SendAsync("G0 X1").Wait();
        Assert.Equal(["G0 X1"], inner.Sent);

        proxy.Disconnect();
        Assert.False(proxy.IsConnected);
    }

    [Fact]
    public void Subscribers_keep_receiving_state_after_inner_swap()
    {
        var demo = new FakeGrbl("demo");
        var real = new FakeGrbl("real");
        var proxy = new GrblServiceProxy(demo);

        var states = new List<string>();
        using var sub = proxy.State.Subscribe(s => states.Add(s.Status));

        demo.Subject.OnNext(new MachineState(3) { Status = "Demo" });
        proxy.Inner = real;
        real.Subject.OnNext(new MachineState(3) { Status = "Real" });

        // The swap itself replays the new inner's current (default) state,
        // then live values flow through.
        Assert.Equal(["Demo", "Idle", "Real"], states);
    }
}
