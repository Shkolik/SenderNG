using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class GrblServiceProxyTests
{
    private sealed class FakeGrbl(string name) : IGrblService
    {
        public BehaviorSubject<MachineState> Subject { get; } = new(new MachineState(3));
        public Subject<string> Lines { get; } = new();
        public List<string> Sent { get; } = [];
        public bool IsConnected { get; private set; }
        public IObservable<MachineState> State => Subject;
        public IObservable<string> ReceivedLines => Lines;
        public Task ConnectAsync(string port) { IsConnected = true; return Task.CompletedTask; }
        public void Disconnect() => IsConnected = false;
        public void SendRealtime(byte code) => Sent.Add($"RT:{code:X2}");

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
    public void Subscriber_sees_inner_initial_state_on_construction()
    {
        var inner = new FakeGrbl("demo"); // BehaviorSubject seeded with MachineState(3)
        var proxy = new GrblServiceProxy(inner);

        var states = new List<string>();
        using var sub = proxy.State.Subscribe(s => states.Add(s.Status));

        // Regression: widgets stayed empty on startup because the proxy's State
        // did not replay the inner service's current machine state.
        Assert.Equal(["Idle"], states);
    }

    [Fact]
    public void ReceivedLines_forward_from_inner_and_survive_swap()
    {
        var demo = new FakeGrbl("demo");
        var real = new FakeGrbl("real");
        var proxy = new GrblServiceProxy(demo);

        var lines = new List<string>();
        using var sub = proxy.ReceivedLines.Subscribe(lines.Add);

        demo.Lines.OnNext("demo line");
        proxy.Inner = real;
        real.Lines.OnNext("real line");

        // The console subscriber keeps receiving raw lines across inner swaps.
        Assert.Equal(["demo line", "real line"], lines);
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

        // The subscriber sees the inner's state immediately on attach, then
        // live values; the swap replays the new inner's current state too.
        Assert.Equal(["Idle", "Demo", "Idle", "Real"], states);
    }
}
