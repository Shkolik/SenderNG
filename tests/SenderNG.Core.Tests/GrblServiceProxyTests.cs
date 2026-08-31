
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
    public void Late_console_subscriber_sees_recent_connected_lines()
    {
        // Regression: the connect burst ($I identity/options, first status lines)
        // used to be lost because ReceivedLines was a non-replaying Subject and
        // Avalonia realizes the center-panel ConsoleView lazily. The proxy now
        // replays recent lines so a late-attaching console still shows them.
        var inner = new FakeGrbl("real");
        var proxy = new GrblServiceProxy(inner);

        inner.Lines.OnNext("GrblHAL 1.2i");
        inner.Lines.OnNext("[OPT:V,15,128]");

        var late = new List<string>();
        using var sub = proxy.ReceivedLines.Subscribe(late.Add);

        Assert.Contains("GrblHAL 1.2i", late);
        Assert.Contains("[OPT:V,15,128]", late);
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
