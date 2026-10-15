
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

using System.Reactive.Linq;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Fixed;
using SenderNG.Modules.Plugins;
using Xunit;

namespace SenderNG.Core.Tests;

public class MachineStateTests
{
    [Fact]
    public void Parameterless_ctor_allocates_machine_positions()
    {
        var state = new MachineState();
        Assert.NotNull(state.MachinePositions);
        Assert.Equal(state.Positions.Length, state.MachinePositions.Length);
    }

    [Fact]
    public void NumAxes_ctor_allocates_both_position_arrays()
    {
        var state = new MachineState(4);
        Assert.Equal(4, state.Positions.Length);
        Assert.Equal(4, state.MachinePositions!.Length);
    }
}

public class DemoGrblServiceMachinePositionTests
{
    private static MachineProfile SettingsFor(int demoAxes) => new() { DemoAxes = demoAxes };

    [Fact]
    public async Task Absolute_move_tracks_machine_position_with_zero_offset()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");

        await svc.SendAsync("G0 X10 Y5 Z2");

        var st = await svc.State.FirstAsync();
        Assert.Equal(10, st.Positions[0]);
        Assert.Equal(10, st.MachinePositions![0]);
    }

    [Fact]
    public async Task G92_shifts_work_offset_but_keeps_machine_position()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");
        await svc.SendAsync("G0 X50 Y50");

        await svc.SendAsync("G92 X0 Y0");

        var st = await svc.State.FirstAsync();
        Assert.Equal(0, st.Positions[0]);
        Assert.Equal(50, st.MachinePositions![0]);
    }

    [Fact]
    public async Task Machine_position_follows_work_plus_offset_after_G92()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");
        await svc.SendAsync("G0 X50");
        await svc.SendAsync("G92 X10"); // offset = 50 - 10 = 40

        await svc.SendAsync("G0 X20");

        var st = await svc.State.FirstAsync();
        Assert.Equal(20, st.Positions[0]);
        Assert.Equal(60, st.MachinePositions![0]);
    }

    [Fact]
    public async Task G53_moves_in_absolute_machine_coordinates()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");
        await svc.SendAsync("G0 X50");
        await svc.SendAsync("G92 X10"); // offset 40

        await svc.SendAsync("G53 G0 X5");

        var st = await svc.State.FirstAsync();
        Assert.Equal(5, st.MachinePositions![0]);
        Assert.Equal(-35, st.Positions[0]); // work = machine - offset
    }

    [Fact]
    public async Task Homing_zeroes_machine_position_work_stays_shifted()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");
        await svc.SendAsync("G0 X50");
        await svc.SendAsync("G92 X10"); // offset 40

        await svc.SendAsync("$H");

        var st = await svc.State.FirstAsync();
        Assert.Equal(0, st.MachinePositions![0]);
        Assert.Equal(-40, st.Positions[0]);
    }

    [Fact]
    public async Task Reset_zeroes_machine_and_work_positions()
    {
        var svc = new DemoGrblService(SettingsFor(3));
        await svc.ConnectAsync("demo");
        await svc.SendAsync("G0 X10 Y20 Z30");

        svc.SendRealtime(GrblRealtime.Reset);

        var st = await svc.State.FirstAsync();
        Assert.Equal([0, 0, 0], st.Positions);
        Assert.Equal([0, 0, 0], st.MachinePositions!);
    }
}

public class MposViewModelTests
{
    private sealed class BehaviorSubjectGrbl : IGrblService
    {
        private readonly System.Reactive.Subjects.BehaviorSubject<MachineState> _subject;
        public BehaviorSubjectGrbl(System.Reactive.Subjects.BehaviorSubject<MachineState> subject) => _subject = subject;
        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    [Fact]
    public void State_push_shows_machine_positions_per_axis()
    {
        var subject = new System.Reactive.Subjects.BehaviorSubject<MachineState>(new MachineState(3));
        var vm = new MposViewModel(new BehaviorSubjectGrbl(subject));

        subject.OnNext(new MachineState
        {
            NumAxes = 3,
            Positions = [1, 2, 3],
            MachinePositions = [10, 20, 30],
        });

        Assert.Equal(3, vm.Axes.Count);
        Assert.Equal("X", vm.Axes[0].Letter);
        Assert.Equal(10, vm.Axes[0].Position);
        Assert.Equal(20, vm.Axes[1].Position);
        Assert.Equal(30, vm.Axes[2].Position);
    }

    [Fact]
    public void Missing_machine_positions_renders_zeros_without_error()
    {
        var subject = new System.Reactive.Subjects.BehaviorSubject<MachineState>(new MachineState(3));
        var vm = new MposViewModel(new BehaviorSubjectGrbl(subject));

        subject.OnNext(new MachineState { NumAxes = 3, Positions = [1, 2, 3] });

        Assert.Equal(3, vm.Axes.Count);
        Assert.Equal(0, vm.Axes[0].Position);
    }

    [Fact]
    public void NumAxes_drives_row_count()
    {
        var subject = new System.Reactive.Subjects.BehaviorSubject<MachineState>(new MachineState(3));
        var vm = new MposViewModel(new BehaviorSubjectGrbl(subject));

        subject.OnNext(new MachineState(4));

        Assert.Equal(4, vm.Axes.Count);
        Assert.Equal("A", vm.Axes[3].Letter);

        subject.OnNext(new MachineState(2));

        Assert.Equal(2, vm.Axes.Count);
    }
}

public class OutlineViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly SettingsService _settings;

    public OutlineViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "senderng-outline-" + Guid.NewGuid().ToString("N"));
        _settings = new SettingsService(_dir);
    }

    private sealed class SpyGrbl : IGrblService
    {
        public List<string> Sent { get; } = new();
        public bool IsConnected => true;
        public IObservable<MachineState> State => Observable.Never<MachineState>();
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) { Sent.Add(line); return Task.CompletedTask; }
        public void SendRealtime(byte code) { }
    }

    private static void LoadSquareProgram(ProgramViewModel program)
    {
        program.LoadGCode("G1 F100 X10 Y20\r\nG1 X40 Y30\r\n");
    }

    [Fact]
    public void Go_disabled_without_loaded_program()
    {
        var spy = new SpyGrbl();
        var vm = new OutlineViewModel(spy, _settings, new ProgramViewModel());

        Assert.False(vm.GoCommand.CanExecute(null));
    }

    [Fact]
    public void Go_enabled_after_program_with_limits_loads()
    {
        var spy = new SpyGrbl();
        var program = new ProgramViewModel();
        var vm = new OutlineViewModel(spy, _settings, program);

        LoadSquareProgram(program);

        Assert.True(vm.HasProgramLimits);
        Assert.True(vm.GoCommand.CanExecute(null));
    }

    [Fact]
    public void Go_sends_outline_perimeter_gcode()
    {
        var spy = new SpyGrbl();
        var program = new ProgramViewModel();
        var vm = new OutlineViewModel(spy, _settings, program);
        LoadSquareProgram(program);

        vm.GoCommand.Execute(null);

        Assert.Equal("Outline sent: X/Y", vm.Status);
        var line = Assert.Single(spy.Sent);
        var expected = string.Join(Environment.NewLine,
        [
            "G90G21G1F500",
            "X10Y20",
            "Y30",
            "X40",
            "Y20",
            "X10",
            "",
        ]);
        Assert.Equal(expected, line);
    }

    [Fact]
    public void Go_uses_configured_feed_rate()
    {
        var spy = new SpyGrbl();
        var program = new ProgramViewModel();
        var vm = new OutlineViewModel(spy, _settings, program);
        LoadSquareProgram(program);
        vm.FeedRate = 250;

        vm.GoCommand.Execute(null);

        Assert.StartsWith("G90G21G1F250", Assert.Single(spy.Sent));
    }

    [Fact]
    public void Feed_rate_persists_across_instances()
    {
        var spy = new SpyGrbl();
        var vm = new OutlineViewModel(spy, _settings, new ProgramViewModel());
        vm.FeedRate = 800;

        var second = new OutlineViewModel(new SpyGrbl(), _settings, new ProgramViewModel());

        Assert.Equal(800, second.FeedRate);
    }

    [Fact]
    public void Feed_rate_rejects_non_positive_values()
    {
        var spy = new SpyGrbl();
        var vm = new OutlineViewModel(spy, _settings, new ProgramViewModel());

        vm.FeedRate = 0;
        Assert.Equal(OutlineViewModel.DefaultFeedRate, vm.FeedRate);

        vm.FeedRate = -5;
        Assert.Equal(OutlineViewModel.DefaultFeedRate, vm.FeedRate);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}

public class OutlinePlaneAwareTests : IDisposable
{
    private readonly List<string> _dirs = new();

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private SettingsService MakeSettings(string machineTypeId)
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-outline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        var settings = new SettingsService(dir);
        var profile = settings.Load();
        profile.MachineTypeId = machineTypeId;
        settings.Save(profile);
        return settings;
    }

    private sealed class SpyGrbl : IGrblService
    {
        public List<string> Sent { get; } = new();
        public bool IsConnected => true;
        public IObservable<MachineState> State => System.Reactive.Linq.Observable.Never<MachineState>();
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) { Sent.Add(line); return Task.CompletedTask; }
        public void SendRealtime(byte code) { }
    }

    // X/Y and Z/A both present -> one combined in-sync move set.
    [Fact]
    public void Foam_machine_sweeps_both_planes_in_sync()
    {
        var spy = new SpyGrbl();
        var program = new ProgramViewModel();
        program.LoadGCode("G1 F100 X10 Y20 Z10 A20\r\nG1 X40 Y30 Z40 A30\r\n");
        var vm = new OutlineViewModel(spy, MakeSettings(MachineTypes.Foam), program);

        vm.GoCommand.Execute(null);

        var expected = string.Join(Environment.NewLine,
        [
            "G90G21G1F500",
            "X10Y20Z10A20",
            "Y30A30",
            "X40Z40",
            "Y20A20",
            "X10Z10",
            "",
        ]);
        Assert.Equal(expected, Assert.Single(spy.Sent));
        Assert.Equal("Outline sent: X/Y + Z/A", vm.Status);
    }

    // Foam machine but the part only touches X/Y -> sweep plane 1 only.
    [Fact]
    public void Foam_machine_with_xy_only_program_sweeps_the_first_plane()
    {
        var spy = new SpyGrbl();
        var program = new ProgramViewModel();
        program.LoadGCode("G1 F100 X10 Y20\r\nG1 X40 Y30\r\n");
        var vm = new OutlineViewModel(spy, MakeSettings(MachineTypes.Foam), program);

        vm.GoCommand.Execute(null);

        var expected = string.Join(Environment.NewLine,
        [
            "G90G21G1F500",
            "X10Y20",
            "Y30",
            "X40",
            "Y20",
            "X10",
            "",
        ]);
        Assert.Equal(expected, Assert.Single(spy.Sent));
        Assert.Equal("Outline sent: X/Y", vm.Status);
    }

    // Program limits match no plane (single-axis program on a foam machine).
    [Fact]
    public void No_sweepable_plane_shows_the_generic_message()
    {
        var spy = new SpyGrbl();
        var program = new ProgramViewModel();
        program.LoadGCode("G1 F100 X10\r\n");
        var vm = new OutlineViewModel(spy, MakeSettings(MachineTypes.Foam), program);

        vm.GoCommand.Execute(null);

        Assert.Empty(spy.Sent);
        Assert.Equal("Outline: program has no limits for the loaded machine's planes", vm.Status);
    }

    // The bug-report firmware's foam layout: plane 2 is declared [A, Z] = indices
    // [3, 2], so its horizontal is A and its vertical Z. Slicing by flattened index
    // order (the old legacy-role lookup) inverted exactly these two.
    [Fact]
    public void Foam_plane_two_follows_the_declared_slots_not_the_index_order()
    {
        var settings = MakeSettings(MachineTypes.Foam);
        var preset = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var profile = settings.Load();
        profile.Planes = preset.CreateDefaultPlanes();
        profile.Planes[1].Axes[0].Index = 3; // slot 0 (horizontal) = A
        profile.Planes[1].Axes[1].Index = 2; // slot 1 (vertical)   = Z
        settings.Save(profile);

        var spy = new SpyGrbl();
        var program = new ProgramViewModel();
        program.LoadGCode("G1 F100 X10 Y20 Z10 A30\r\nG1 X40 Y30 Z40 A50\r\n");
        // Through the shared provider — the seam the app injects.
        using var kinematics = new MachineKinematicsProvider(settings, spy);
        var vm = new OutlineViewModel(spy, settings, program, kinematics: kinematics);

        vm.GoCommand.Execute(null);

        var expected = string.Join(Environment.NewLine,
        [
            "G90G21G1F500",
            "X10Y20A30Z10",
            "Y30Z40",
            "X40A50",
            "Y20Z10",
            "X10A30",
            "",
        ]);
        Assert.Equal(expected, Assert.Single(spy.Sent));
        Assert.Equal("Outline sent: X/Y + A/Z", vm.Status);
    }

    // A profile switch (MachineTypeId Mill -> Foam) refreshes the sweep set.
    [Fact]
    public void Profile_switch_refreshes_the_machine_type()
    {
        var settings = MakeSettings(MachineTypes.Mill);
        var spy = new SpyGrbl();
        var program = new ProgramViewModel();
        program.LoadGCode("G1 F100 X10 Y20 Z10 A20\r\nG1 X40 Y30 Z40 A30\r\n");
        var vm = new OutlineViewModel(spy, settings, program);

        vm.GoCommand.Execute(null);
        Assert.Equal("Outline sent: X/Y", vm.Status); // Mill: one sweepable plane

        var second = settings.CreateProfile("second", MachineTypes.Foam);
        settings.Activate(second.Id); // fires Changed -> the next Go re-reads the kinematics

        vm.GoCommand.Execute(null);
        Assert.Equal("Outline sent: X/Y + Z/A", vm.Status);
    }
}
