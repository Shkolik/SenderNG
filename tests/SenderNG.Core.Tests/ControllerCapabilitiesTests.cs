using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class ControllerInfoParserTests
{
    [Fact]
    public void Classic_grbl_identity_sets_version()
    {
        var caps = ControllerInfoParser.ParseInfo(["Grbl 1.1h ['$' for help]"]);
        Assert.True(caps.IsKnown);
        Assert.False(caps.IsGrblHAL);
        Assert.Equal("1.1h", caps.Version);
        // classic grbl 1.1 has no M7 signal -> mist hidden
        Assert.False(caps.CoolantMist);
        Assert.True(caps.CoolantFlood);
        Assert.Equal(3, caps.NumAxes);
    }

    [Fact]
    public void GrblHAL_ver_line_sets_version_build_and_firmware()
    {
        var caps = ControllerInfoParser.ParseInfo(["[VER:1.1f.20230820:grblHAL]"]);
        Assert.True(caps.IsKnown);
        Assert.True(caps.IsGrblHAL);
        Assert.Equal("1.1f", caps.Version);
        Assert.Equal(20230820, caps.Build);
        Assert.Equal("grblHAL", caps.Firmware);
        // grblHAL supports mist
        Assert.True(caps.CoolantMist);
    }

    [Fact]
    public void Real_grblHAL_reply_sets_axes_from_axs_and_tools_from_opt()
    {
        // Captured from a real grbl 1.2i board (COM6): axes come from [AXS:], the
        // 4th [OPT:] field is the tool count, not axes.
        var caps = ControllerInfoParser.ParseInfo(
            ["[VER:1.2i.20260521:]", "[AXS:5:XYZAB]", "[OPT:VNMGH,35,255,48]"]);
        Assert.True(caps.IsKnown);
        Assert.True(caps.IsGrblHAL);
        Assert.Equal("1.2i", caps.Version);
        Assert.Equal(20260521, caps.Build);
        Assert.Equal(5, caps.NumAxes);
        Assert.Equal(35, caps.PlanBufferSize);
        Assert.Equal(255, caps.SerialBufferSize);
        Assert.Equal(48, caps.NumTools);
        Assert.True(caps.CoolantMist); // grblHAL
        Assert.True(caps.HomingEnabled); // 'H' option present
    }

    [Fact]
    public void Opt_line_sets_buffers_and_tools_but_not_axes()
    {
        // Without an [AXS:] line, OPT's numbers are plan/serial/tools — axes stay 3.
        var caps = ControllerInfoParser.ParseInfo(["[OPT:Z+FRPNM-SV,64,256,8]"]);
        Assert.True(caps.IsKnown);
        Assert.Equal(64, caps.PlanBufferSize);
        Assert.Equal(256, caps.SerialBufferSize);
        Assert.Equal(8, caps.NumTools);
        Assert.Equal(3, caps.NumAxes);
        Assert.True(caps.CoolantMist); // 'M' present
        Assert.True(caps.CoolantFlood);
    }

    [Fact]
    public void Full_set_combining_ver_axs_opt_is_processed()
    {
        var caps = ControllerInfoParser.ParseInfo(
            ["[VER:1.2i.20210101:grblHAL]", "[AXS:5:XYZAB]", "[OPT:Z+FRPNM-SV,128,256,4]"]);
        Assert.True(caps.IsKnown);
        Assert.True(caps.IsGrblHAL);
        Assert.Equal("1.2i", caps.Version);
        Assert.Equal(5, caps.NumAxes);
        Assert.Equal(4, caps.NumTools);
    }

    [Fact]
    public void Unrecognized_lines_returns_unknown()
    {
        var caps = ControllerInfoParser.ParseInfo(["garbage", "", "$$..."]);
        Assert.False(caps.IsKnown);
    }
}

public class ControllerCapabilitiesDemoAndProxyTests
{
    [Fact]
    public void DemoGrblService_reports_full_capabilities()
    {
        using var demo = new DemoGrblService(new AppSettings());
        Assert.True(demo.Capabilities.IsKnown);
        Assert.True(demo.Capabilities.CoolantMist);
        Assert.True(demo.Capabilities.CoolantFlood);
    }

    [Fact]
    public void GrblServiceProxy_forwards_capabilities_from_inner()
    {
        var inner = new DemoGrblService(new AppSettings());
        var proxy = new GrblServiceProxy(inner);
        Assert.Same(inner.Capabilities, proxy.Capabilities);
    }

    [Fact]
    public void Default_interface_member_is_unknown()
    {
        // A minimal double that doesn't override Capabilities falls back to Unknown.
        // (Default interface members are only reachable through the interface type.)
        var fake = new BareFake();
        var iface = (CNC.Core.Abstractions.IGrblService)fake;
        Assert.False(iface.Capabilities.IsKnown);
    }

    private sealed class BareFake : CNC.Core.Abstractions.IGrblService
    {
        public bool IsConnected => false;
        public System.IObservable<MachineState> State { get; } =
            new System.Reactive.Subjects.BehaviorSubject<MachineState>(new MachineState(3));
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }
}