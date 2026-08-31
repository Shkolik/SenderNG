using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Plugins;

namespace SenderNG.Core.Tests;

public sealed class GotoViewModelTests
{
    internal static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(dir);
        settings.Save(settings.Load());
        return settings;
    }

    private static async Task<DemoGrblService> DemoAsync(int demoAxes = 3)
    {
        var settings = new AppSettings();
        settings.Current.DemoAxes = demoAxes;
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
        var vm = new GotoViewModel(service, MakeSettings());

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
        var vm = new GotoViewModel(service, MakeSettings());

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
        var vm = new GotoViewModel(service, MakeSettings());
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
        var vm = new GotoViewModel(service, MakeSettings());
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
        var vm = new GotoViewModel(service, MakeSettings());
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
        var vm = new GotoViewModel(service, MakeSettings());
        await service.SendAsync("$H");
        Assert.True(vm.IsHomed);

        service.SendRealtime(GrblRealtime.Reset);
        await Task.Delay(50);
        Assert.False(vm.IsHomed);
        Assert.Equal("Home the machine first ($H)", vm.Status);
    }

    [Fact]
    public async Task Defaults_to_three_axis_rows()
    {
        using var service = await DemoAsync();
        var vm = new GotoViewModel(service, MakeSettings());

        Assert.Equal(["X", "Y", "Z"], vm.GotoAxes.Select(r => r.Letter));
        Assert.Equal(3, vm.AxisCount);
    }

    [Fact]
    public async Task Preference_is_capped_by_controller_axis_count()
    {
        using var service = await DemoAsync(demoAxes: 3);
        var settings = MakeSettings();
        var vm = new GotoViewModel(service, settings);
        vm.SelectedAxisCount = 4;

        // 3-axis controller caps the 4-axis preference to 3 rows.
        Assert.Equal(3, vm.AxisCount);
        Assert.Equal(["X", "Y", "Z"], vm.GotoAxes.Select(r => r.Letter));
    }

    [Fact]
    public async Task Preference_shows_full_rows_when_controller_has_more_axes()
    {
        using var service = await DemoAsync(demoAxes: 5);
        var vm = new GotoViewModel(service, MakeSettings());
        vm.SelectedAxisCount = 4;

        Assert.Equal(4, vm.AxisCount);
        Assert.Equal(["X", "Y", "Z", "A"], vm.GotoAxes.Select(r => r.Letter));
    }

    [Fact]
    public async Task Axis_count_preference_persists_and_reloads()
    {
        using var service = await DemoAsync(demoAxes: 5);
        var settings = MakeSettings();
        var first = new GotoViewModel(service, settings);
        first.SelectedAxisCount = 4;

        var second = new GotoViewModel(service, settings);

        Assert.Equal(4, second.SelectedAxisCount);
        Assert.Equal(["X", "Y", "Z", "A"], second.GotoAxes.Select(r => r.Letter));
    }

    [Fact]
    public async Task Row_values_survive_axis_count_change()
    {
        using var service = await DemoAsync(demoAxes: 5);
        var vm = new GotoViewModel(service, MakeSettings());
        Row(vm, "X").Value = "12.5";

        vm.SelectedAxisCount = 4;

        Assert.Equal("12.5", Row(vm, "X").Value);
        Assert.Equal(4, vm.GotoAxes.Count);
    }

    [Fact]
    public async Task Go_can_move_extra_axes_when_available()
    {
        using var service = await DemoAsync(demoAxes: 5);
        var vm = new GotoViewModel(service, MakeSettings());
        vm.SelectedAxisCount = 4;
        await service.SendAsync("$H");

        Row(vm, "A").Value = "3.25";
        vm.GoCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(3.25, LatestState(service).MachinePositions![3]);
        Assert.Contains("Going to position", vm.Status);
    }
}

public sealed class GotoConfigTests
{
    [Fact]
    public void Load_with_no_section_returns_default()
    {
        var config = GotoConfigStore.Load(new AppSettings());
        Assert.Equal(3, config.AxisCount);
    }

    [Fact]
    public void Save_then_load_roundtrips_and_clamps()
    {
        var settings = new AppSettings();
        GotoConfigStore.Save(settings, new GotoConfig { AxisCount = 4 });
        Assert.Equal(4, GotoConfigStore.Load(settings).AxisCount);

        GotoConfigStore.Save(settings, new GotoConfig { AxisCount = 99 });
        Assert.Equal(9, GotoConfigStore.Load(settings).AxisCount);

        GotoConfigStore.Save(settings, new GotoConfig { AxisCount = 0 });
        Assert.Equal(1, GotoConfigStore.Load(settings).AxisCount);
    }
}
