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
    private static AppSettings SettingsFor(int demoAxes)
    {
        var s = new AppSettings();
        s.Current.DemoAxes = demoAxes;
        return s;
    }

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

        Assert.Equal("Outline sent", vm.Status);
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
