
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

namespace SenderNG.Core.Tests;

public sealed class WorkParametersTests
{
    internal static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(dir);
        settings.Save(settings.Load());
        return settings;
    }

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
    public void Parse_reads_tool_records_with_tip_radius()
    {
        // Real grblHAL shape (maintainer-confirmed): [T:<id>|<axis CSV>|<tip radius>]
        // — the R word is a separate pipe field, not a trailing axis.
        var parameters = WorkParameters.Parse(
        [
            "[G54:1.000,2.000,3.000]",
            "[T:1|1.5,0,3.25|0.75]",
            "[T:0|0,0,0|0]",
        ], 3);

        var t1 = parameters.Tools.Single(x => x.Id == 1);
        Assert.Equal([1.5, 0, 3.25], t1.Offsets);
        Assert.Equal(0.75, t1.Radius);

        Assert.Equal(0.0, parameters.Tools.Single(x => x.Id == 0).Radius);
    }

    [Fact]
    public void Clone_copies_and_isolates_tool_radius()
    {
        var parameters = WorkParameters.CreateDefault(3, 1);
        parameters.Tools.Single(x => x.Id == 1).Radius = 2.5;

        var clone = parameters.Clone();
        Assert.Equal(2.5, clone.Tools.Single(x => x.Id == 1).Radius);

        clone.Tools.Single(x => x.Id == 1).Radius = 9;
        Assert.Equal(2.5, parameters.Tools.Single(x => x.Id == 1).Radius);
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
        var settings = new MachineProfile { DemoAxes = 3 };
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
    public async Task Tool_selector_lists_none_first_with_names_and_defaults_to_none()
    {
        var settings = MakeSettings();
        var app = new MachineProfile();
        using var grbl = new DemoGrblService(app);
        await grbl.ConnectAsync("demo");
        var vm = new WorkParametersViewModel(grbl, settings);

        // None (slot 0) is offered first and selected by default — previously the
        // selector had no None row, so an unselected ActiveTool rendered blank.
        Assert.Equal(0, vm.SelectedTool);
        Assert.Equal("None", vm.SelectedToolRow?.DisplayName);
        Assert.Equal(
            ["None", "T1", "T2", "T3", "T4"],
            vm.Tools.Select(t => t.DisplayName));

        // A name saved under the demo controller key (Offsets editor "Set all")
        // shows up in the label — through the settings-changed event...
        var loaded = settings.Load();
        var config = OffsetsConfigStore.Load(loaded);
        config.ToolNamesFor("demo")[1] = "6 mm endmill";
        OffsetsConfigStore.Save(loaded, config);
        settings.Save(loaded);
        Assert.Equal("T1 (6 mm endmill)", vm.Tools.Single(t => t.Id == 1).DisplayName);

        // ...and on a snapshot refresh the names are re-applied.
        vm.Refresh();
        Assert.Equal("T1 (6 mm endmill)", vm.Tools.Single(t => t.Id == 1).DisplayName);
        Assert.Equal(0, vm.SelectedTool);
    }

    [Fact]
    public async Task Tool_selector_follows_the_visible_tools_setting()
    {
        var settings = MakeSettings();
        var app = new MachineProfile();
        using var grbl = new DemoGrblService(app);
        await grbl.ConnectAsync("demo");
        var vm = new WorkParametersViewModel(grbl, settings);

        // Demo has 4 tool slots; default VisibleTools (8) clamps to 4.
        Assert.Equal(["None", "T1", "T2", "T3", "T4"], vm.Tools.Select(t => t.DisplayName));

        // App Settings -> Offsets: "Visible tools" 8 -> 2 propagates here (the
        // selector must NOT keep listing all firmware slots).
        var loaded = settings.Load();
        loaded.VisibleTools = 2;
        settings.Save(loaded);
        Assert.Equal(["None", "T1", "T2"], vm.Tools.Select(t => t.DisplayName));
        Assert.Equal(0, vm.SelectedTool); // still None

        // 0 hides every slot — only "None" remains selectable.
        loaded = settings.Load();
        loaded.VisibleTools = 0;
        settings.Save(loaded);
        Assert.Equal(["None"], vm.Tools.Select(t => t.DisplayName));
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
