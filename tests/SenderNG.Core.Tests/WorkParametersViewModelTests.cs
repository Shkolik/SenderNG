using System.Reactive.Linq;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Fixed;

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
    public void Apply_line_handles_tool_length_reference_and_isolates_clones()
    {
        var parameters = WorkParameters.CreateDefault(3);
        parameters.ApplyLine("[TLR:45.5]");
        Assert.Equal(45.5, parameters.ToolLengthOffsetReference);

        parameters.ApplyLine("[G54:1,2,3]");
        Assert.Equal([1, 2, 3], parameters.CoordinateSystems.Single(x => x.Code == "G54").Values[..3]);

        // Clones are deep copies: mutating one must not affect the other.
        var clone = parameters.Clone();
        clone.ApplyLine("[G54:9,9,9]");
        Assert.Equal(1, parameters.CoordinateSystems.Single(x => x.Code == "G54").Values[0]);
        Assert.Equal(9, clone.CoordinateSystems.Single(x => x.Code == "G54").Values[0]);
    }

    [Fact]
    public void Coordinate_system_numbers_and_writability()
    {
        Assert.Equal(1, WorkParameters.CoordinateSystemNumber("G54"));
        Assert.Equal(9, WorkParameters.CoordinateSystemNumber("G59.3"));
        Assert.Equal(0, WorkParameters.CoordinateSystemNumber("G92"));
        Assert.True(WorkParameters.IsG10Writable("G55"));
        Assert.False(WorkParameters.IsG10Writable("G28"));
        Assert.False(WorkParameters.IsG10Writable("G92"));
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

    [Fact]
    public void Classic_grbl_selector_omits_unsupported_g59x()
    {
        // Classic grbl 1.1 rejects G59.1/2/3 (error 29) — never offer them.
        var grbl = new CapsStub(new ControllerCapabilities { IsKnown = true, IsGrblHAL = false });
        var vm = new WorkParametersViewModel(grbl);

        Assert.Equal(["G54", "G55", "G56", "G57", "G58", "G59"], vm.CoordinateSystemCodes);
    }

    [Fact]
    public void GrblHal_selector_offers_all_nine_systems()
    {
        var grbl = new CapsStub(ControllerCapabilities.Full);
        var vm = new WorkParametersViewModel(grbl);

        Assert.Equal(9, vm.CoordinateSystemCodes.Count);
        Assert.Contains("G59.3", vm.CoordinateSystemCodes);
    }

    private sealed class CapsStub(ControllerCapabilities caps) : IGrblService
    {
        public bool IsConnected => true;
        public ControllerCapabilities Capabilities => caps;
        public IObservable<MachineState> State => Observable.Never<MachineState>();
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) => Task.CompletedTask;
    }
}
