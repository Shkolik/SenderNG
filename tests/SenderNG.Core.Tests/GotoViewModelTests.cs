using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Plugins;

namespace SenderNG.Core.Tests;

public sealed class GotoViewModelTests
{
    private static async Task<DemoGrblService> DemoAsync()
    {
        var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        return service;
    }

    private static MachineState LatestState(DemoGrblService service)
    {
        MachineState? latest = null;
        service.State.Subscribe(s => latest = s);
        return latest!;
    }

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

        vm.X = "5";
        vm.Y = "2";
        vm.Z = "-1.5";
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

        vm.X = "abc";
        vm.GoCommand.Execute(null);
        await Task.Delay(50);
        Assert.StartsWith("Invalid value", vm.Status);
        Assert.Equal(0, LatestState(service).MachinePositions![0]);

        vm.X = "";
        vm.Y = "";
        vm.Z = "";
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
}
