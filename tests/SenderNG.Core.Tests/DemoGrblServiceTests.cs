using CNC.Core.Abstractions;
using CNC.Core.Services;
using System.Reactive.Linq;
using Xunit;

namespace SenderNG.Core.Tests;

public class DemoGrblServiceTests
{
    [Fact]
    public async Task Demo_connects_and_returns_state()
    {
        var svc = new DemoGrblService(new AppSettings { DemoAxes = 4 });
        await svc.ConnectAsync("demo");
        Assert.True(svc.IsConnected);
        var st = await svc.State.FirstAsync();
        Assert.Equal(4, st.NumAxes);
    }

    [Fact]
    public void Not_connected_before_connect()
    {
        var svc = new DemoGrblService(new AppSettings());
        Assert.False(svc.IsConnected);
    }

    [Fact]
    public void Disconnect_resets_connection()
    {
        var svc = new DemoGrblService(new AppSettings());
        svc.ConnectAsync("demo").Wait();
        svc.Disconnect();
        Assert.False(svc.IsConnected);
    }

    [Fact]
    public async Task Send_status_query_pushes_state()
    {
        var svc = new DemoGrblService(new AppSettings { DemoAxes = 3 });
        await svc.ConnectAsync("demo");
        MachineState? received = null;
        using var sub = svc.State.Subscribe(s => received = s);
        await svc.SendAsync("?");
        Assert.NotNull(received);
        Assert.Equal(3, received.NumAxes);
        Assert.NotEmpty(received.Status);
    }

    [Fact]
    public async Task Send_motion_line_updates_positions()
    {
        var svc = new DemoGrblService(new AppSettings { DemoAxes = 3 });
        await svc.ConnectAsync("demo");
        await svc.SendAsync("G0 X10 Y5 Z2");
        var st = await svc.State.FirstAsync();
        Assert.Equal(10, st.Positions[0]);
        Assert.Equal(5, st.Positions[1]);
        Assert.Equal(2, st.Positions[2]);
    }
}
