using System.Reactive.Linq;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Plugins;

namespace SenderNG.Core.Tests;

public sealed class WorkParametersTests
{
    [Fact]
    public void Parse_reads_coordinate_system_tool_length_and_probe()
    {
        var parameters = WorkParameters.Parse(
        [
            "[G54:1.000,2.000,3.000]",
            "[G55:-1.000,0,4.5]",
            "[TLO:0,0,6.25]",
            "[PRB:10,20,30:1]",
        ], 3);

        Assert.Equal([1, 2, 3], parameters.CoordinateSystems.Single(x => x.Code == "G54").Values[..3]);
        Assert.Equal(6.25, parameters.ToolLengthOffset[2]);
        Assert.Equal([10, 20, 30], parameters.ProbePosition![..3]);
        Assert.True(parameters.ProbeSuccess);
    }

    [Fact]
    public async Task Demo_work_parameters_send_coordinate_and_tool_selection()
    {
        var settings = new AppSettings();
        settings.Current.DemoAxes = 3;
        using var grbl = new DemoGrblService(settings);
        await grbl.ConnectAsync("demo");
        var vm = new WorkParametersViewModel(grbl);

        vm.SelectCoordinateSystemCommand.Execute("G55");
        await Task.Delay(20);
        vm.SelectToolCommand.Execute(3);
        await Task.Delay(20);

        Assert.Equal("G55", grbl.WorkParameters.ActiveCoordinateSystem);
        Assert.Equal(3, grbl.WorkParameters.ActiveTool);
        Assert.Equal("G55", vm.SelectedCoordinateSystem);
        Assert.Equal(3, vm.SelectedTool);
    }
}
