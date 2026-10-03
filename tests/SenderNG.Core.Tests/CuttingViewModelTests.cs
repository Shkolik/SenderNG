
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

using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Plugins;

namespace SenderNG.Core.Tests;

public class CuttingViewModelTests
{
    private sealed class RecordingGrbl(List<string> sent) : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = new(new MachineState(3));
        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) => sent.Add($"RT:{code:X2}");

        public Task SendAsync(string line)
        {
            sent.Add(line);
            return Task.CompletedTask;
        }

        public void Push(MachineState state) => _subject.OnNext(state);
    }

    [Fact]
    public void Overrides_start_at_100_percent()
    {
        var vm = new CuttingViewModel(new RecordingGrbl([]));

        Assert.Equal(100, vm.FeedOvr);
        Assert.Equal(100, vm.RapidOvr);
        Assert.Equal(100, vm.SpindleOvr);
    }

    [Fact]
    public void Feed_override_sends_realtime_bytes()
    {
        var sent = new List<string>();
        var vm = new CuttingViewModel(new RecordingGrbl(sent));

        vm.FeedCoarsePlusCommand.Execute(null);
        vm.FeedFineMinusCommand.Execute(null);
        vm.FeedResetCommand.Execute(null);

        Assert.Equal(["RT:91", "RT:94", "RT:90"], sent);
    }

    [Fact]
    public void Rapid_override_sends_realtime_bytes()
    {
        var sent = new List<string>();
        var vm = new CuttingViewModel(new RecordingGrbl(sent));

        vm.RapidHalfCommand.Execute(null);
        vm.RapidQuarterCommand.Execute(null);
        vm.RapidFullCommand.Execute(null);

        Assert.Equal(["RT:96", "RT:97", "RT:95"], sent);
    }

    [Fact]
    public void Speed_override_sends_realtime_bytes()
    {
        var sent = new List<string>();
        var vm = new CuttingViewModel(new RecordingGrbl(sent));

        vm.SpeedCoarseMinusCommand.Execute(null);
        vm.SpeedFinePlusCommand.Execute(null);
        vm.SpeedResetCommand.Execute(null);

        Assert.Equal(["RT:9B", "RT:9C", "RT:99"], sent);
    }

    [Fact]
    public void State_subscription_mirrors_live_values_and_overrides()
    {
        var grbl = new RecordingGrbl([]);
        var vm = new CuttingViewModel(grbl);

        grbl.Push(new MachineState(3)
        {
            Feed = 630,
            Speed = 12000,
            FeedOvr = 150,
            RapidOvr = 50,
            SpindleOvr = 90,
        });

        Assert.Equal(630, vm.Feed);
        Assert.Equal(10800, vm.Speed);
        Assert.Equal(10800, vm.SpindleRpm);
        Assert.Equal(150, vm.FeedOvr);
        Assert.Equal(50, vm.RapidOvr);
        Assert.Equal(90, vm.SpindleOvr);
        Assert.False(vm.IsRapidFull);
        Assert.True(vm.IsRapidHalf);
        Assert.False(vm.IsRapidQuarter);
    }

    [Fact]
    public void Feed_reads_zero_while_held_and_restores_on_resume()
    {
        var grbl = new RecordingGrbl([]);
        var vm = new CuttingViewModel(grbl);

        // Running: live feed mirrors the report (grblHAL reports F word × override).
        grbl.Push(new MachineState(3) { Status = "Run", Feed = 105, Speed = 8000 });
        Assert.Equal(105, vm.Feed);

        // Feed hold: the machine is stopped, but the report still carries the
        // programmed feed — the widget must show 0 while held (Hold and Hold:0/1).
        grbl.Push(new MachineState(3) { Status = "Hold", Feed = 105, Speed = 8000 });
        Assert.Equal(0, vm.Feed);
        grbl.Push(new MachineState(3) { Status = "Hold:1", Feed = 105, Speed = 8000 });
        Assert.Equal(0, vm.Feed);

        // Speed is untouched by a feed hold — a real spindle keeps spinning.
        Assert.Equal(8000, vm.Speed);

        // Resume: live feed comes back.
        grbl.Push(new MachineState(3) { Status = "Run", Feed = 105, Speed = 8000 });
        Assert.Equal(105, vm.Feed);
        Assert.Equal(8000, vm.Speed);
    }

    [Fact]
    public void Speed_and_spindle_input_include_speed_override()
    {
        var sent = new List<string>();
        var grbl = new RecordingGrbl(sent);
        var vm = new CuttingViewModel(grbl);

        grbl.Push(new MachineState(3) { Speed = 1000, SpindleOvr = 100 });
        Assert.Equal(1000, vm.Speed);
        Assert.Equal(1000, vm.SpindleRpm);

        grbl.Push(new MachineState(3) { Speed = 1000, SpindleOvr = 110 });
        Assert.Equal(1100, vm.Speed);
        Assert.Equal(1100, vm.SpindleRpm);

        grbl.Push(new MachineState(3) { Speed = 1000, SpindleOvr = 100 });
        Assert.Equal(1000, vm.Speed);
        Assert.Equal(1000, vm.SpindleRpm);

        grbl.Push(new MachineState(3) { Speed = 0, SpindleOvr = 100 });
        Assert.Equal(0, vm.Speed);
        Assert.Equal(1000, vm.SpindleRpm);

        vm.SpindleCwCommand.Execute(null);
        Assert.Equal(["M3 S1000"], sent);
    }
}

public class DemoGrblServiceCuttingTests
{
    [Fact]
    public async Task Demo_simulates_overrides_and_live_values()
    {
        var svc = new DemoGrblService(new MachineProfile());
        await svc.ConnectAsync("demo");

        await svc.SendAsync("M3 S12000 F630");
        var st = await svc.State.FirstAsync();
        Assert.Equal(12000, st.Speed);
        Assert.Equal(630, st.Feed);

        svc.SendRealtime(GrblRealtime.FeedOvrCoarsePlus);
        st = await svc.State.FirstAsync();
        Assert.Equal(110, st.FeedOvr);

        svc.SendRealtime(GrblRealtime.RapidOvrMedium);
        st = await svc.State.FirstAsync();
        Assert.Equal(50, st.RapidOvr);

        svc.SendRealtime(GrblRealtime.SpindleStopToggle);
        st = await svc.State.FirstAsync();
        Assert.Equal(0, st.Speed);

        svc.SendRealtime(GrblRealtime.SpindleStopToggle);
        st = await svc.State.FirstAsync();
        Assert.Equal(12000, st.Speed);
    }
}

public class CuttingViewModelSpindleTests
{
    private sealed class RecordingGrbl(List<string> sent, Exception? sendFault = null) : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = new(new MachineState(3));
        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) => sent.Add($"RT:{code:X2}");

        public Task SendAsync(string line)
        {
            sent.Add(line);
            return sendFault is null ? Task.CompletedTask : Task.FromException(sendFault);
        }

        public void Push(MachineState state) => _subject.OnNext(state);
    }

    [Fact]
    public void Spindle_rpm_defaults_to_1000()
    {
        var vm = new CuttingViewModel(new RecordingGrbl([]));

        Assert.Equal(1000, vm.SpindleRpm);
    }

    [Fact]
    public void Cw_command_sends_unoverridden_rpm_when_override_is_active()
    {
        var sent = new List<string>();
        var grbl = new RecordingGrbl(sent);
        var vm = new CuttingViewModel(grbl);

        grbl.Push(new MachineState(3) { SpindleOvr = 110 });
        vm.SpindleRpm = 1100;
        vm.SpindleCwCommand.Execute(null);

        Assert.Equal(["M3 S1000"], sent);
    }

    [Fact]
    public void Cw_command_sends_m3_with_rpm()
    {
        var sent = new List<string>();
        var vm = new CuttingViewModel(new RecordingGrbl(sent));
        vm.SpindleRpm = 1500;

        vm.SpindleCwCommand.Execute(null);

        Assert.Equal(["M3 S1500"], sent);
    }

    [Fact]
    public void Ccw_command_sends_m4_with_rpm()
    {
        var sent = new List<string>();
        var vm = new CuttingViewModel(new RecordingGrbl(sent));
        vm.SpindleRpm = 2500;

        vm.SpindleCcwCommand.Execute(null);

        Assert.Equal(["M4 S2500"], sent);
    }

    [Fact]
    public void Direction_commands_and_input_disabled_while_spindle_running()
    {
        var grbl = new RecordingGrbl([]);
        var vm = new CuttingViewModel(grbl);

        Assert.True(vm.SpindleCwCommand.CanExecute(null));
        Assert.True(vm.SpindleCcwCommand.CanExecute(null));

        grbl.Push(new MachineState(3) { Speed = 12000 });

        Assert.True(vm.IsSpindleOn);
        Assert.False(vm.SpindleCwCommand.CanExecute(null));
        Assert.False(vm.SpindleCcwCommand.CanExecute(null));

        grbl.Push(new MachineState(3) { Speed = 0 });

        Assert.False(vm.IsSpindleOn);
        Assert.True(vm.SpindleCwCommand.CanExecute(null));
        Assert.True(vm.SpindleCcwCommand.CanExecute(null));
    }

    [Fact]
    public void Off_command_sends_m5()
    {
        var sent = new List<string>();
        var vm = new CuttingViewModel(new RecordingGrbl(sent));

        vm.SpindleOffCommand.Execute(null);

        Assert.Equal(["M5"], sent);
    }

    [Fact]
    public void Spindle_rpm_tracks_last_speed_with_1000_fallback()
    {
        var grbl = new RecordingGrbl([]);
        var vm = new CuttingViewModel(grbl);

        // Fallback before any S word is seen.
        Assert.Equal(1000, vm.SpindleRpm);

        grbl.Push(new MachineState(3) { Speed = 12000 });
        Assert.Equal(12000, vm.SpindleRpm);

        // Spindle stopped: keep the last commanded speed, don't drop to 0.
        grbl.Push(new MachineState(3) { Speed = 0 });
        Assert.Equal(12000, vm.SpindleRpm);

        grbl.Push(new MachineState(3) { Speed = 8000 });
        Assert.Equal(8000, vm.SpindleRpm);
    }

    [Fact]
    public void Spindle_start_reports_failure_and_never_sent_when_the_send_faults()
    {
        var sent = new List<string>();
        var vm = new CuttingViewModel(
            new RecordingGrbl(sent, new IOException("transport write failed")));

        vm.SpindleCwCommand.Execute(null);

        Assert.NotNull(vm.Status);
        Assert.Contains("transport write failed", vm.Status!);
        Assert.DoesNotContain("sent", vm.Status);
        Assert.Single(sent);
        Assert.StartsWith("M3", sent[0]);
    }

    [Fact]
    public void Spindle_off_reports_failure_when_the_send_faults()
    {
        var sent = new List<string>();
        var vm = new CuttingViewModel(
            new RecordingGrbl(sent, new IOException("transport write failed")));

        vm.SpindleOffCommand.Execute(null);

        Assert.NotNull(vm.Status);
        Assert.Contains("transport write failed", vm.Status!);
        Assert.DoesNotContain("sent", vm.Status);
    }
}
