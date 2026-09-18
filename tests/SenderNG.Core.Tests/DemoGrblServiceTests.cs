
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

using CNC.Core.Abstractions;
using CNC.Core.Services;
using System.Reactive.Linq;
using Xunit;

namespace SenderNG.Core.Tests;

public class DemoGrblServiceTests
{
    private static MachineProfile SettingsFor(int demoAxes) => new() { DemoAxes = demoAxes };

    // Each test needs a FRESH settings store: the demo persists settings to disk,
    // so the shared default file (and earlier runs) would change MaxTravel and
    // make travel-threshold behavior non-deterministic.
    private static string TempSettingsFile() =>
        Path.Combine(Path.GetTempPath(), "sng-demo-jog-" + Guid.NewGuid().ToString("N") + ".txt");

    // The demo reuses ONE mutable MachineState instance, mutated in place under
    // its lock on every emission/poll tick — reading the object after the fact
    // can observe a concurrent tick's update. Snapshot inside a State listener
    // instead: the listener runs on the emitting thread's lock, so each JogSnap
    // is a consistent pair, and writing the reference is atomic (readers use
    // Volatile.Read).
    private sealed class JogSnap
    {
        private JogSnap(double workX, double machineX)
        {
            WorkX = workX;
            MachineX = machineX;
        }

        public double WorkX { get; }
        public double MachineX { get; }

        public static JogSnap From(MachineState s) => new(
            s.Positions.Length > 0 ? s.Positions[0] : 0,
            s.MachinePositions is { Length: > 0 } mp ? mp[0] : s.Positions.Length > 0 ? s.Positions[0] : 0);
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
        var svc = new DemoGrblService(new MachineProfile());
        Assert.False(svc.IsConnected);
    }

    [Fact]
    public async Task Disconnect_resets_connection()
    {
        var svc = new DemoGrblService(new MachineProfile());
        await svc.ConnectAsync("demo");
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

    [Fact]
    public async Task Continuous_jog_sets_jog_status_and_does_not_teleport()
    {
        var file = TempSettingsFile();
        try
        {
            using var svc = new DemoGrblService(SettingsFor(3), file, pollIntervalMs: 10);

            // Snapshot the live state from inside a listener: the demo emits under
            // its lock, so the captured pair is consistent and the reference write
            // is atomic. Re-reading the mutated-in-place state object afterwards can
            // observe a concurrent poll tick (position slips by 0.1/tick).
            JogSnap? snap = null;
            using (svc.State.Subscribe(s => snap = JogSnap.From(s)))
            {
                await svc.ConnectAsync("demo");
                await svc.SendAsync("$J=G91 X-200 F600"); // X travel is 200 → continuous
            }

            var st = await svc.State.FirstAsync();
            Assert.Equal("Jog", st.Status);
            var latest = Volatile.Read(ref snap);
            Assert.NotNull(latest);
            // No teleport: the send itself moves nothing. Motion before/after is
            // poll progress only (≈0.1 mm per 10 ms at 600 mm/min), never the
            // 200 mm travel.
            Assert.True(Math.Abs(latest!.WorkX) < 2, $"expected no teleport, got {latest.WorkX}");
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task Continuous_jog_advances_positions_across_polls()
    {
        var file = TempSettingsFile();
        try
        {
            using var svc = new DemoGrblService(SettingsFor(3), file, pollIntervalMs: 10);
            JogSnap? snap = null;
            using var sub = svc.State.Subscribe(s => snap = JogSnap.From(s));

            await svc.ConnectAsync("demo");
            await svc.SendAsync("$J=G91 X200 F600"); // travel-length jog → continuous

            // Wait for a few poll ticks of advance, then freeze the simulator and
            // assert on the captured pair. A fixed wall-clock delay is not
            // deterministic under full-suite load: a starved test thread can
            // overshoot by hundreds of ms and rack up >2 mm of poll progress
            // (each 10 ms tick = 0.1 mm at 600 mm/min). Waiting for a small
            // advance bounds the captured distance inside the (0, 2) window
            // regardless of scheduling load.
            JogSnap? advanced = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var candidate = Volatile.Read(ref snap);
                if (candidate is not null && candidate.WorkX >= 0.2) // ≥ 2 ticks
                {
                    advanced = candidate;
                    break;
                }
                await Task.Delay(5);
            }
            svc.SendRealtime(GrblRealtime.JogCancel); // stop the simulator's motion

            Assert.NotNull(advanced);
            Assert.True(advanced!.WorkX > 0, $"expected a positive advance, got {advanced.WorkX}");
            Assert.True(advanced.WorkX < 2, $"expected under 2 mm, got {advanced.WorkX}");
            Assert.Equal(advanced.WorkX, advanced.MachineX); // work == machine: no offset
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task Jog_cancel_realtime_stops_continuous_jog()
    {
        var file = TempSettingsFile();
        try
        {
            using var svc = new DemoGrblService(SettingsFor(3), file, pollIntervalMs: 10);
            await svc.ConnectAsync("demo");
            await svc.SendAsync("$J=G91 X200 F600");

            // Wait (bounded) until the simulator has actually advanced: a fixed
            // delay can race a starved poll timer under full-suite load and find
            // the jog still at rest.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((await svc.State.FirstAsync()).Positions[0] <= 0 && DateTime.UtcNow < deadline)
                await Task.Delay(5);

            var before = await svc.State.FirstAsync();
            Assert.Equal("Jog", before.Status);
            Assert.True(before.Positions[0] > 0); // the jog is actually advancing

            svc.SendRealtime(GrblRealtime.JogCancel);

            // JogCancel is synchronous and freezes the position the instant it
            // returns; record THEN so no poll tick can sneak between recording
            // and the stop.
            var st = await svc.State.FirstAsync();
            Assert.Equal("Idle", st.Status);
            var recorded = st.Positions[0];

            await Task.Delay(60);
            var after = await svc.State.FirstAsync();
            Assert.Equal(recorded, after.Positions[0]); // polling no longer advances
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task Click_jog_still_applies_immediately()
    {
        var file = TempSettingsFile();
        try
        {
            using var svc = new DemoGrblService(SettingsFor(3), file, pollIntervalMs: 10);
            await svc.ConnectAsync("demo");

            await svc.SendAsync("$J=G91 X1 F1000"); // 1 mm is far below the 200 mm travel threshold

            var st = await svc.State.FirstAsync();
            Assert.Equal("Idle", st.Status);
            Assert.Equal(1, st.Positions[0]);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task Demo_reports_max_travel_from_settings()
    {
        var file = TempSettingsFile();
        try
        {
            using var svc = new DemoGrblService(SettingsFor(3), file);

            Assert.Equal([200, 200, 100], svc.Capabilities.MaxTravel);

            await svc.ConnectAsync("demo");
            await svc.SendAsync("$130=350");
            Assert.Equal(350, svc.Capabilities.MaxTravel[0]);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }
}

public class DemoGrblServiceG92Tests
{
    private static MachineProfile SettingsFor(int demoAxes) => new() { DemoAxes = demoAxes };

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

    [Fact]
    public async Task G10_L20_P0_zeroes_the_active_offset_and_persists_it()
    {
        // Regression: the DRO zero (G10 L20 P0) must store the offset into the
        // ACTIVE coordinate system — the Work Parameters table then shows it.
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");
        await svc.SendAsync("G1 X10 Y20 Z30");
        await svc.SendAsync("G58"); // activate G58, like selecting it from Work Parameters

        await svc.SendAsync("G90 G10 L20 P0 X0 Y0 Z0");

        var st = await svc.State.FirstAsync();
        Assert.Equal(0, st.Positions[0]);
        Assert.Equal(0, st.Positions[1]);
        Assert.Equal(0, st.Positions[2]);

        var g58 = Assert.Single(svc.WorkParameters.CoordinateSystems, x => x.Code == "G58");
        Assert.Equal(10, g58.Values[0]); // machine position absorbed into the offset
        Assert.Equal(20, g58.Values[1]);
        Assert.Equal(30, g58.Values[2]);

        // Systems other than the active one stay untouched.
        var g54 = Assert.Single(svc.WorkParameters.CoordinateSystems, x => x.Code == "G54");
        Assert.All(g54.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public async Task G10_L20_Pn_zeros_a_numbered_system_without_activating_it()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");
        await svc.SendAsync("G1 X10");

        await svc.SendAsync("G10 L20 P5 X0"); // P5 = G58 (G54=1 … G59.3=9)

        var g58 = Assert.Single(svc.WorkParameters.CoordinateSystems, x => x.Code == "G58");
        Assert.Equal(10, g58.Values[0]);
        Assert.Equal("G54", svc.WorkParameters.ActiveCoordinateSystem); // selection unchanged
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
            var svc = new DemoGrblService(new MachineProfile(), file);
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
        var svc = new DemoGrblService(new MachineProfile());
        await svc.ConnectAsync("demo");

        await svc.SendAsync("$13=1");
        var lines = await svc.GetSettingLinesAsync();

        Assert.Contains("$13=1", lines);
        Assert.DoesNotContain("$13=0", lines);
    }

    [Fact]
    public async Task Jog_and_motion_unaffected_by_setting_prefix()
    {
        var svc = new DemoGrblService(new MachineProfile());
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
            var svc = new DemoGrblService(new MachineProfile(), file);
            await svc.ConnectAsync("demo");
            await svc.SendAsync("$13=1");
            await svc.SendAsync("$110=800.000");

            Assert.True(File.Exists(file));

            var reloaded = new DemoGrblService(new MachineProfile(), file);
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
        var demo = new DemoGrblService(new MachineProfile());
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
    private static MachineProfile SettingsFor(int demoAxes) => new() { DemoAxes = demoAxes };

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
    private static MachineProfile SettingsFor(int demoAxes) => new() { DemoAxes = demoAxes };

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

public class DemoGrblServiceStatusCommandTests
{
    private static MachineProfile SettingsFor(int demoAxes) => new() { DemoAxes = demoAxes };

    [Fact]
    public async Task Send_unlock_and_home_commands_are_accepted()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");

        await svc.SendAsync("$X");
        await svc.SendAsync("$H");

        var st = await svc.State.FirstAsync();
        Assert.Equal("Idle", st.Status);
        Assert.Equal(3, st.NumAxes);
    }
}
