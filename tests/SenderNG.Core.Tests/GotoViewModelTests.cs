
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
    public async Task Go_sends_absolute_machine_move()
    {
        using var service = await DemoAsync();
        var vm = new GotoViewModel(service);
        await service.SendAsync("$H");

        Row(vm, "X").Value = "5";
        Row(vm, "Y").Value = "2";
        Row(vm, "Z").Value = "-1.5";
        vm.GoCommand.Execute(null);
        await Task.Delay(50);

        var state = LatestState(service);
        Assert.Equal(5, state.MachinePositions![0]);
        Assert.Equal(2, state.MachinePositions[1]);
        Assert.Equal(-1.5, state.MachinePositions[2]);
        Assert.Contains("Going to position", vm.Status);
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
}
