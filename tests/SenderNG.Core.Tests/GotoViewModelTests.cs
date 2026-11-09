
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
using SenderNG.Modules.Fixed;
using SenderNG.Modules.Plugins;

namespace SenderNG.Core.Tests;

public sealed class GotoViewModelTests
{
    private static async Task<DemoGrblService> DemoAsync(int demoAxes = 3)
    {
        var settings = new MachineProfile { DemoAxes = demoAxes };
        var service = new DemoGrblService(settings);
        await service.ConnectAsync("demo");
        return service;
    }

    private static MachineState LatestState(DemoGrblService service)
    {
        MachineState? latest = null;
        service.State.Subscribe(s => latest = s);
        return latest!;
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline && !condition())
            await Task.Delay(20);
    }

    /// <summary>
    /// A service whose program lines never acknowledge until released, so a real
    /// <see cref="ProgramViewModel"/> session stays running for the test's duration.
    /// </summary>
    private sealed class GatedProgramGrbl : IGrblService
    {
        private readonly System.Reactive.Subjects.BehaviorSubject<MachineState> _state =
            new(new MachineState { NumAxes = 3, IsHomed = true });
        private readonly TaskCompletionSource<string> _line = new();

        public List<string> Sent { get; } = new();
        public bool IsConnected => true;
        public IObservable<MachineState> State => _state;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) { Sent.Add(line); return Task.CompletedTask; }
        public void SendRealtime(byte code) { }
        public Task<string> SendProgramLineAsync(string line, CancellationToken ct = default) => _line.Task;
        public void ReleaseLines() => _line.TrySetResult("ok");
        public void PushStatus(string status) =>
            _state.OnNext(new MachineState { NumAxes = 3, IsHomed = true, Status = status });
    }

    private static GotoAxisRow Row(GotoViewModel vm, string letter) =>
        vm.GotoAxes.Single(r => r.Letter == letter);

    [Fact]
    public async Task Goto_actions_are_blocked_until_homed()
    {
        using var service = await DemoAsync();
        await service.SendAsync("G0 X7 Y3");
        var vm = new GotoViewModel(service);

        Assert.False(vm.IsHomed);
        vm.GotoHomeCommand.Execute(null);
        await Task.Delay(50);

        // No G28 reached the controller: machine position is unchanged.
        Assert.Equal(7, LatestState(service).Positions[0]);
        Assert.Equal("Home the machine first ($H)", vm.Status);
    }

    [Fact]
    public async Task Homing_enables_reference_point_moves()
    {
        using var service = await DemoAsync();
        await service.SendAsync("G0 X7 Y3");
        var vm = new GotoViewModel(service);

        await service.SendAsync("$H");
        Assert.True(vm.IsHomed);

        vm.GotoHomeCommand.Execute(null);
        await Task.Delay(50);
        var state = LatestState(service);
        Assert.Equal(0, state.MachinePositions![0]);
        Assert.Equal(0, state.MachinePositions[1]);
        Assert.Contains("Going to home", vm.Status);
    }

    [Fact]
    public async Task Set_reference_commands_run_when_homed()
    {
        using var service = await DemoAsync();
        var vm = new GotoViewModel(service);
        await service.SendAsync("$H");

        vm.SetHomeCommand.Execute(null);
        await Task.Delay(50);
        Assert.Contains("Home position recorded", vm.Status);

        vm.SetG30Command.Execute(null);
        await Task.Delay(50);
        Assert.Contains("Second reference point recorded", vm.Status);
    }

    [Fact]
    public async Task Go_sends_an_absolute_move_in_the_active_work_coordinates()
    {
        using var service = await DemoAsync();
        var vm = new GotoViewModel(service);
        await service.SendAsync("$H");
        // The reporter's setup in miniature: a negative G54 offset, so the work DRO
        // reads 50 at the machine origin. Typed coordinates are WORK coordinates —
        // the row values the operator reads off the DRO — never machine ones.
        await service.SendAsync("G10 L20 P0 X50 Y50 Z50");
        Assert.Equal(50, LatestState(service).Positions[0]);

        Row(vm, "X").Value = "5";
        Row(vm, "Y").Value = "2";
        Row(vm, "Z").Value = "-1.5";
        vm.GoCommand.Execute(null);
        await Task.Delay(50);

        var state = LatestState(service);
        Assert.Equal([5, 2, -1.5], state.Positions.Take(3));
        Assert.Equal([-45, -48, -51.5], state.MachinePositions!.Take(3));
        Assert.Contains("Going to position", vm.Status);
    }

    [Fact]
    public async Task Go_is_absolute_in_incremental_mode_and_restores_the_modal_mode()
    {
        using var service = await DemoAsync();
        var vm = new GotoViewModel(service);
        await service.SendAsync("$H");
        await service.SendAsync("G0 X10");
        await service.SendAsync("G91"); // a modal G91 must not turn "5" into "+5"

        Row(vm, "X").Value = "5";
        vm.GoCommand.Execute(null);
        await Task.Delay(50);

        // The typed position is absolute, not a +5 step from 10.
        Assert.Equal(5, LatestState(service).Positions[0]);
        // ...and forcing G90 for the move must not leave the operator's G91 session
        // flipped to absolute: the snapshot is put back after the move is accepted.
        Assert.False((await service.ReadModalStateAsync())!.IsAbsolute);

        await service.SendAsync("G0 X1");
        Assert.Equal(6, LatestState(service).Positions[0]); // incremental again
    }

    [Fact]
    public async Task Go_leaves_an_already_absolute_controller_absolute()
    {
        using var service = await DemoAsync();
        var vm = new GotoViewModel(service);
        await service.SendAsync("$H");

        Row(vm, "X").Value = "5";
        vm.GoCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(5, LatestState(service).Positions[0]);
        Assert.True((await service.ReadModalStateAsync())!.IsAbsolute);
    }

    [Fact]
    public async Task Go_rejects_invalid_or_missing_coordinates()
    {
        using var service = await DemoAsync();
        var vm = new GotoViewModel(service);
        await service.SendAsync("$H");

        Row(vm, "X").Value = "abc";
        vm.GoCommand.Execute(null);
        await Task.Delay(50);
        Assert.StartsWith("Invalid value", vm.Status);
        Assert.Equal(0, LatestState(service).MachinePositions![0]);

        foreach (var row in vm.GotoAxes)
            row.Value = "";
        vm.GoCommand.Execute(null);
        await Task.Delay(50);
        Assert.Equal("Enter at least one coordinate", vm.Status);
    }

    [Fact]
    public async Task Soft_reset_clears_homed_state()
    {
        using var service = await DemoAsync();
        var vm = new GotoViewModel(service);
        await service.SendAsync("$H");
        Assert.True(vm.IsHomed);

        service.SendRealtime(GrblRealtime.Reset);
        await Task.Delay(50);
        Assert.False(vm.IsHomed);
        Assert.Equal("Home the machine first ($H)", vm.Status);
    }

    [Fact]
    public async Task Defaults_to_controller_axis_rows()
    {
        using var service = await DemoAsync();
        var vm = new GotoViewModel(service);

        Assert.Equal(["X", "Y", "Z"], vm.GotoAxes.Select(r => r.Letter));
        Assert.Equal(3, vm.GotoAxes.Count);
    }

    [Fact]
    public async Task Shows_all_controller_axes_with_no_preference()
    {
        using var service = await DemoAsync(demoAxes: 5);
        var vm = new GotoViewModel(service);

        // Like mPos/DRO: all controller-reported axes, no cap or preference needed.
        Assert.Equal(["X", "Y", "Z", "A", "B"], vm.GotoAxes.Select(r => r.Letter));
        Assert.Equal(5, vm.GotoAxes.Count);
    }

    [Fact]
    public async Task Axis_rows_follow_controller_axis_count_changes()
    {
        using var service = await DemoAsync();
        var vm = new GotoViewModel(service);
        Assert.Equal(3, vm.GotoAxes.Count);

        service.SetNumAxes(5);

        Assert.Equal(["X", "Y", "Z", "A", "B"], vm.GotoAxes.Select(r => r.Letter));

        service.SetNumAxes(3);

        Assert.Equal(["X", "Y", "Z"], vm.GotoAxes.Select(r => r.Letter));
    }

    [Fact]
    public async Task Row_values_survive_axis_count_change()
    {
        using var service = await DemoAsync(demoAxes: 5);
        var vm = new GotoViewModel(service);
        Row(vm, "X").Value = "12.5";

        service.SetNumAxes(4);

        Assert.Equal("12.5", Row(vm, "X").Value);
        Assert.Equal(4, vm.GotoAxes.Count);
    }

    [Fact]
    public async Task Go_can_move_extra_axes_when_available()
    {
        using var service = await DemoAsync(demoAxes: 5);
        var vm = new GotoViewModel(service);
        await service.SendAsync("$H");

        Row(vm, "A").Value = "3.25";
        vm.GoCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(3.25, LatestState(service).MachinePositions![3]);
        Assert.Contains("Going to position", vm.Status);
    }

    [Fact]
    public async Task Goto_is_blocked_while_a_program_runs_and_unblocks_when_it_ends()
    {
        // The reporter's hazard: a G28/G30 or coordinate move queued between program
        // blocks. ioSender disables its whole Goto panel from IsJobRunning; so must we.
        var grbl = new GatedProgramGrbl();
        var program = new ProgramViewModel(grbl);
        program.LoadGCode("G1 F100 X10\nG1 X20\n");
        var run = program.RunAsync();               // session opens on the first line
        await WaitUntil(() => program.IsRunning);
        Assert.True(program.IsRunning);

        var vm = new GotoViewModel(grbl, program: program);
        Assert.True(vm.IsHomed);
        Assert.True(vm.IsJobRunning);
        Assert.False(vm.CanOperate);
        Assert.False(vm.GoCommand.CanExecute(null));
        Assert.False(vm.GotoHomeCommand.CanExecute(null));
        Assert.False(vm.SetHomeCommand.CanExecute(null));

        // Execute() bypasses CanExecute: the guard inside must still refuse.
        Row(vm, "X").Value = "5";
        vm.GoCommand.Execute(null);
        await Task.Delay(50);
        Assert.Equal("Stop the running program first", vm.Status);
        Assert.DoesNotContain(grbl.Sent, l => l.StartsWith("G90 G0", StringComparison.Ordinal));
        Assert.DoesNotContain("G28", grbl.Sent);

        grbl.ReleaseLines();
        await run;
        await WaitUntil(() => !program.IsRunning);

        Assert.True(vm.CanOperate);
        Assert.True(vm.GoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Goto_stays_blocked_while_a_program_is_paused()
    {
        // A feed-held session is still a run: resume would restart the stream the
        // moment the operator's reference move finished.
        var grbl = new GatedProgramGrbl();
        var program = new ProgramViewModel(grbl);
        program.LoadGCode("G1 F100 X10\n");
        var run = program.RunAsync();
        await WaitUntil(() => program.IsRunning);

        grbl.PushStatus("Hold");
        await WaitUntil(() => program.IsPaused);

        var vm = new GotoViewModel(grbl, program: program);
        Assert.True(program.IsRunning);
        Assert.False(vm.CanOperate);
        Assert.False(vm.GoCommand.CanExecute(null));

        grbl.PushStatus("Run");
        grbl.ReleaseLines();
        await run;
        await WaitUntil(() => !program.IsRunning);
        Assert.True(vm.CanOperate);
    }
}
