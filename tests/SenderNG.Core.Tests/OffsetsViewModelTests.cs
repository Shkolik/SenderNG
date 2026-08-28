using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Plugins;

namespace SenderNG.Core.Tests;

public sealed class OffsetsViewModelTests
{
    [Fact]
    public async Task Get_current_position_updates_selected_row_immediately()
    {
        var settings = new AppSettings();
        settings.Current.DemoAxes = 5;
        using var service = new DemoGrblService(settings);
        await service.ConnectAsync("demo");
        await service.SendAsync("G0 X1 Y2 Z3 A4 B5");
        var vm = new OffsetsViewModel(service);

        vm.GetCurrentPositionCommand.Execute(null);

        var row = Assert.Single(vm.Rows, x => x.Code == "G54");
        Assert.Equal(["1", "2", "3", "4", "5"], row.Values);
    }

    [Fact]
    public async Task Set_all_writes_each_reported_coordinate_system()
    {
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service);
        vm.Rows.Single(x => x.Code == "G54").SetValueAt(0, "12.5");
        vm.Rows.Single(x => x.Code == "G55").SetValueAt(0, "25.5");

        vm.SetAllCommand.Execute(null);
        await Task.Delay(100);

        Assert.Equal(12.5, service.WorkParameters.CoordinateSystems.Single(x => x.Code == "G54").Values[0]);
        Assert.Equal(25.5, service.WorkParameters.CoordinateSystems.Single(x => x.Code == "G55").Values[0]);
        Assert.Contains("Saved", vm.Status);
    }

    [Fact]
    public async Task Five_axis_editor_has_exactly_five_axes_and_offset_names()
    {
        var settings = new AppSettings();
        settings.Current.DemoAxes = 5;
        using var service = new DemoGrblService(settings);
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service);

        Assert.Equal(["X", "Y", "Z", "A", "B"], vm.AxisNames);
        Assert.All(vm.Rows, row => Assert.Equal(5, row.Values.Count));
        Assert.Equal("G54", vm.OffsetCodes[0]);
        Assert.Equal("G59.3", vm.OffsetCodes[8]);
    }
}
