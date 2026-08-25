using CNC.Core.Abstractions;
using CNC.Core.Services;
using System.Reactive.Linq;
using Xunit;

namespace SenderNG.Core.Tests;

public class DemoGrblServiceTests
{
    private static AppSettings SettingsFor(int demoAxes)
    {
        var s = new AppSettings();
        s.Current.DemoAxes = demoAxes;
        return s;
    }

    [Fact]
    public async Task Demo_connects_and_returns_state()
    {
        var svc = new DemoGrblService(SettingsFor(4));
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
        var svc = new DemoGrblService(SettingsFor(3));
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
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");
        await svc.SendAsync("G0 X10 Y5 Z2");
        var st = await svc.State.FirstAsync();
        Assert.Equal(10, st.Positions[0]);
        Assert.Equal(5, st.Positions[1]);
        Assert.Equal(2, st.Positions[2]);
    }

    [Fact]
    public async Task Jog_command_applies_incremental_delta()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");
        await svc.SendAsync("G0 X10 Y5 Z2");

        await svc.SendAsync("$J=G91 G0 X1 Y-2 F1000");

        var st = await svc.State.FirstAsync();
        Assert.Equal(11, st.Positions[0]);
        Assert.Equal(3, st.Positions[1]);
        Assert.Equal(2, st.Positions[2]);
    }

    [Fact]
    public async Task Jog_without_explicit_mode_uses_modal_distance_mode()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");

        // G91 sets incremental mode modally; subsequent plain move is relative.
        await svc.SendAsync("G91");
        await svc.SendAsync("G0 X10");

        var st = await svc.State.FirstAsync();
        Assert.Equal(10, st.Positions[0]);

        await svc.SendAsync("G90");
        await svc.SendAsync("G0 X5");

        st = await svc.State.FirstAsync();
        Assert.Equal(5, st.Positions[0]);
    }

    [Fact]
    public async Task Jog_supports_A_and_B_axes_when_present()
    {
        var svc = new DemoGrblService(SettingsFor(5));
        await svc.ConnectAsync("demo");

        await svc.SendAsync("$J=G91 G0 A1 B-1 F500");

        var st = await svc.State.FirstAsync();
        Assert.Equal(1, st.Positions[3]);
        Assert.Equal(-1, st.Positions[4]);
    }

    [Fact]
    public async Task Jog_ignores_axes_beyond_NumAxes()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");

        await svc.SendAsync("$J=G91 G0 X1 A5 B7 F500");

        var st = await svc.State.FirstAsync();
        Assert.Equal(3, st.NumAxes);
        Assert.Equal(1, st.Positions[0]);
        Assert.Equal(0, st.Positions[1]);
        Assert.Equal(0, st.Positions[2]);
    }

    [Fact]
    public async Task Absolute_G1_move_still_sets_absolute_positions()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");

        await svc.SendAsync("G1 X4 Y4 F200");

        var st = await svc.State.FirstAsync();
        Assert.Equal(4, st.Positions[0]);
        Assert.Equal(4, st.Positions[1]);
    }
}

public class DemoGrblServiceG92Tests
{
    private static AppSettings SettingsFor(int demoAxes)
    {
        var s = new AppSettings();
        s.Current.DemoAxes = demoAxes;
        return s;
    }

    [Fact]
    public async Task G92_zeroes_listed_axes_only()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");
        await svc.SendAsync("G1 X10 Y20 Z30");

        await svc.SendAsync("G92 X0 Z0");

        var st = await svc.State.FirstAsync();
        Assert.Equal(0, st.Positions[0]);
        Assert.Equal(20, st.Positions[1]);
        Assert.Equal(0, st.Positions[2]);
    }

    [Fact]
    public async Task G92_sets_axis_to_given_value()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");
        await svc.SendAsync("G1 X50");

        await svc.SendAsync("G92 X5.5");

        var st = await svc.State.FirstAsync();
        Assert.Equal(5.5, st.Positions[0]);
    }
}

public class DemoGrblServiceSettingsTests
{
    [Fact]
    public async Task GetSettingLinesAsync_returns_defaults()
    {
        // Explicit empty temp dir: must not read the real app's persisted demo settings.
        var file = Path.Combine(Path.GetTempPath(), "sng-test-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var svc = new DemoGrblService(new AppSettings(), file);
            var lines = await svc.GetSettingLinesAsync();
            Assert.Contains("$0=10", lines);
            Assert.Contains("$13=0", lines);
            Assert.Contains("$110=500.000", lines);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task Send_setting_assignment_updates_store()
    {
        var svc = new DemoGrblService(new AppSettings());
        await svc.ConnectAsync("demo");

        await svc.SendAsync("$13=1");
        var lines = await svc.GetSettingLinesAsync();

        Assert.Contains("$13=1", lines);
        Assert.DoesNotContain("$13=0", lines);
    }

    [Fact]
    public async Task Jog_and_motion_unaffected_by_setting_prefix()
    {
        var svc = new DemoGrblService(new AppSettings());
        await svc.ConnectAsync("demo");

        await svc.SendAsync("$J=G91 X5");
        await svc.SendAsync("G1 X10 Y20 Z30");

        var st = await svc.State.FirstAsync();
        Assert.Equal(15, st.Positions[0]);
    }
}

public class DemoGrblServiceSettingsFileTests
{
    [Fact]
    public async Task Settings_persist_to_file_and_reload_in_new_instance()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sng-test-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "grbl-settings-demo.txt");
        try
        {
            var svc = new DemoGrblService(new AppSettings(), file);
            await svc.ConnectAsync("demo");
            await svc.SendAsync("$13=1");
            await svc.SendAsync("$110=800.000");

            Assert.True(File.Exists(file));

            var reloaded = new DemoGrblService(new AppSettings(), file);
            var lines = await reloaded.GetSettingLinesAsync();
            Assert.Contains("$13=1", lines);
            Assert.Contains("$110=800.000", lines);
            Assert.Contains("$0=10", lines); // default still present
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}

public class GrblSettingsProxyRoutingTests
{
    [Fact]
    public async Task Proxy_routes_settings_to_active_inner_service()
    {
        var demo = new DemoGrblService(new AppSettings());
        IGrblSettingsProvider proxy = new GrblServiceProxy(demo);

        var lines = await proxy.GetSettingLinesAsync();
        Assert.Contains("$0=10", lines);
    }

    [Fact]
    public async Task Proxy_throws_clear_error_when_inner_has_no_settings()
    {
        var bare = new BareGrblService();
        IGrblSettingsProvider proxy = new GrblServiceProxy(bare);

        await Assert.ThrowsAsync<NotSupportedException>(() => proxy.GetSettingLinesAsync());
    }

    private class BareGrblService : IGrblService
    {
        public bool IsConnected => false;
        public IObservable<MachineState> State => Observable.Empty<MachineState>();
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }
}

public class DemoGrblServiceRealtimeCycleTests
{
    private static AppSettings SettingsFor(int demoAxes)
    {
        var s = new AppSettings();
        s.Current.DemoAxes = demoAxes;
        return s;
    }

    [Fact]
    public async Task Feed_hold_realtime_sets_status_hold()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");

        svc.SendRealtime(GrblRealtime.FeedHold);

        var st = await svc.State.FirstAsync();
        Assert.Equal("Hold", st.Status);
    }

    [Fact]
    public async Task Cycle_start_realtime_resumes_from_hold()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");
        svc.SendRealtime(GrblRealtime.FeedHold);

        svc.SendRealtime(GrblRealtime.CycleStart);

        var st = await svc.State.FirstAsync();
        Assert.Equal("Run", st.Status);
    }

    [Fact]
    public async Task Cycle_start_realtime_starts_run_from_idle()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");

        // ~ with a loaded program starts the job: Idle -> Run (grbl semantics).
        svc.SendRealtime(GrblRealtime.CycleStart);

        var st = await svc.State.FirstAsync();
        Assert.Equal("Run", st.Status);
    }

    [Fact]
    public async Task Reset_realtime_clears_positions_and_returns_to_idle()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");
        await svc.SendAsync("G0 X10 Y20 Z30");

        svc.SendRealtime(GrblRealtime.Reset);

        var st = await svc.State.FirstAsync();
        Assert.Equal("Idle", st.Status);
        Assert.Equal([0, 0, 0], st.Positions);
    }
}

public class DemoGrblServicePollingTests
{
    private static AppSettings SettingsFor(int demoAxes)
    {
        var s = new AppSettings();
        s.Current.DemoAxes = demoAxes;
        return s;
    }

    [Fact]
    public async Task Poll_loop_reemits_state_while_connected()
    {
        var svc = new DemoGrblService(SettingsFor(3), pollIntervalMs: 10);
        var emissions = 0;
        using var sub = svc.State.Subscribe(_ => Interlocked.Increment(ref emissions));

        await svc.ConnectAsync("demo");

        // BehaviorSubject replay gives one immediate emission; polling adds more.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (Volatile.Read(ref emissions) < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        svc.Disconnect();
        Assert.True(Volatile.Read(ref emissions) >= 3, $"expected polled emissions, got {emissions}");
    }

    [Fact]
    public async Task Poll_loop_stops_after_disconnect()
    {
        var svc = new DemoGrblService(SettingsFor(3), pollIntervalMs: 10);
        var emissions = 0;
        using var sub = svc.State.Subscribe(_ => Interlocked.Increment(ref emissions));

        await svc.ConnectAsync("demo");

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (Volatile.Read(ref emissions) < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        svc.Disconnect();
        var after = Volatile.Read(ref emissions);
        await Task.Delay(80); // several poll intervals must not emit
        Assert.Equal(after, Volatile.Read(ref emissions));
    }
}
