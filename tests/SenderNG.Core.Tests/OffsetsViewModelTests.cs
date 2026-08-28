using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Fixed;

namespace SenderNG.Core.Tests;

public sealed class OffsetsViewModelTests
{
    internal static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(dir);
        settings.Save(settings.Load());
        return settings;
    }

    [Fact]
    public async Task Get_current_position_updates_selected_row_immediately()
    {
        var settings = new AppSettings();
        settings.Current.DemoAxes = 5;
        using var service = new DemoGrblService(settings);
        await service.ConnectAsync("demo");
        await service.SendAsync("G0 X1 Y2 Z3 A4 B5");
        var vm = new OffsetsViewModel(service, MakeSettings());

        vm.GetCurrentPositionCommand.Execute(null);

        var row = Assert.Single(vm.Rows, x => x.Code == "G54");
        Assert.Equal(["1", "2", "3", "4", "5"], row.Values);
    }

    [Fact]
    public async Task Set_all_writes_each_reported_coordinate_system()
    {
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());
        vm.Rows.Single(x => x.Code == "G54").SetValueAt(0, "12.5");
        vm.Rows.Single(x => x.Code == "G55").SetValueAt(0, "25.5");

        vm.SetAllCommand.Execute(null);
        await Task.Delay(100);

        Assert.Equal(12.5, service.WorkParameters.CoordinateSystems.Single(x => x.Code == "G54").Values[0]);
        Assert.Equal(25.5, service.WorkParameters.CoordinateSystems.Single(x => x.Code == "G55").Values[0]);
        Assert.Contains("Saved", vm.Status);
        Assert.NotEqual(0, service.WorkParameters.CoordinateSystems.Single(x => x.Code == "G54").Values[0]);
    }

    [Fact]
    public async Task Editor_rows_are_restricted_to_g10_writable_systems()
    {
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());

        // G28/G30/G92 have no G10 L2 P number and must not be offered as writable
        // rows (ioSender only writes G54..G59.3 via G10).
        Assert.Equal(9, vm.Rows.Count);
        Assert.DoesNotContain(vm.Rows, row => row.Code is "G28" or "G30" or "G92");
        Assert.Contains("G54", vm.OffsetCodes);
        Assert.Contains("G59.3", vm.OffsetCodes);
    }

    [Fact]
    public async Task Rows_update_live_when_engine_publishes_new_snapshot()
    {
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());
        var row = vm.Rows.Single(x => x.Code == "G54");
        Assert.Equal("0", row.GetValueAt(0));

        // A write issued outside the editor still reaches the VM through the live
        // work-parameters stream (no manual Refresh required).
        await service.SendAsync("G10 L2 P1 X7.5 Y4.25 Z0");

        Assert.Equal("7.5", row.GetValueAt(0));
        Assert.Equal("4.25", row.GetValueAt(1));
    }

    [Fact]
    public void Offset_row_cells_proxy_to_values_both_ways()
    {
        // MVVM plumbing: the view binds TextBoxes to the cell's Value; the cell
        // must read from and write to the row's string store, and re-raise when
        // the row is updated programmatically (Get current position / readback).
        var row = new EditableOffsetRow("G54", 3);

        Assert.Equal(3, row.Cells.Count);
        Assert.Equal("0.000", row.Cells[0].Value);

        // Typing through the cell writes the row store.
        row.Cells[1].Value = "5.5";
        Assert.Equal("5.5", row.Values[1]);

        // A programmatic row update is visible through the cell (raises Value).
        row.SetValueAt(0, "12.5");
        Assert.Equal("12.5", row.Cells[0].Value);
    }

    [Fact]
    public void Tool_row_cells_and_radius_are_bindable()
    {
        var tool = new EditableToolRow(3, 3);

        tool.Cells[2].Value = "10.0";
        Assert.Equal("10.0", tool.Offsets[2]);

        tool.Radius = "4.5";
        Assert.Equal("4.5", tool.Radius);
    }

    [Fact]
    public void Tool_row_tracks_dirtiness_against_loaded_originals()
    {
        // Mirror what the ViewModel does on a snapshot: values written first,
        // then originals recorded from them.
        var tool = new EditableToolRow(3, 3);
        tool.SetOffsetAt(0, "1");
        tool.SetOffsetAt(1, "2");
        tool.SetOffsetAt(2, "3");
        tool.Radius = "0.5";
        tool.SetOriginals(["1", "2", "3"], "0.5");
        Assert.False(tool.IsDirty);

        // An offset cell edit marks the row dirty.
        tool.Cells[1].Value = "9.5";
        Assert.True(tool.IsDirty);

        tool.Cells[1].Value = "2";
        Assert.False(tool.IsDirty);

        // A radius edit marks the row dirty.
        tool.Radius = "1.25";
        Assert.True(tool.IsDirty);
    }

    [Fact]
    public async Task Save_tool_table_writes_only_changed_tools()
    {
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());

        Assert.True(vm.ControllerSupportsToolTable); // demo = grblHAL + 4 tool slots
        Assert.True(vm.ShowToolTable); // default on

        // Edit two tools, leave one untouched.
        var t1 = Assert.Single(vm.Tools, t => t.Id == 1);
        var t2 = Assert.Single(vm.Tools, t => t.Id == 2);
        var t3 = Assert.Single(vm.Tools, t => t.Id == 3);
        t1.Cells[0].Value = "12.5";
        t2.Radius = "3.0";

        vm.SaveToolTableCommand.Execute(null);
        await Task.Delay(100);

        Assert.Equal(12.5, service.WorkParameters.Tools.Single(t => t.Id == 1).Offsets[0]);
        Assert.Equal(3.0, service.WorkParameters.Tools.Single(t => t.Id == 2).Radius);
        // Unchanged tool untouched.
        Assert.Equal(0, service.WorkParameters.Tools.Single(t => t.Id == 3).Offsets[0]);
        Assert.Contains("Saved 2 changed tools", vm.Status);
    }

    [Fact]
    public async Task Save_tool_table_reports_nothing_to_save_when_unchanged()
    {
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());

        vm.SaveToolTableCommand.Execute(null);
        await Task.Delay(100);

        Assert.Equal("No tool changes to save", vm.Status);
    }

    [Fact]
    public async Task Show_tool_table_preference_persists()
    {
        var settings = MakeSettings();
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, settings);

        vm.ShowToolTable = false;
        var reloaded = new OffsetsViewModel(service, settings);
        Assert.False(reloaded.ShowToolTable);
    }

    [Fact]
    public void Tool_table_is_unsupported_on_classic_grbl()
    {
        // Classic grbl 1.1h: no HAL, no tool slots -> no G10 L1 tool table.
        var grbl = new ClassicGrblStub();
        var vm = new OffsetsViewModel(grbl, MakeSettings());
        Assert.False(vm.ControllerSupportsToolTable);

        // A classic fork that reports tool slots (mega5x 1.2i) still can't write
        // the table via G10 L1 -> unsupported.
        var fork = new ClassicGrblStub(new ControllerCapabilities
        {
            IsKnown = true,
            IsGrblHAL = false,
            NumAxes = 5,
            NumTools = 48,
        });
        var forkVm = new OffsetsViewModel(fork, MakeSettings());
        Assert.False(forkVm.ControllerSupportsToolTable);
    }

    /// <summary>Minimal IGrblService double exposing configurable capabilities.</summary>
    private sealed class ClassicGrblStub : IGrblService
    {
        private readonly System.Reactive.Subjects.BehaviorSubject<MachineState> _subject = new(new MachineState(3));
        public ClassicGrblStub(ControllerCapabilities? caps = null) => Capabilities = caps ?? new ControllerCapabilities { IsKnown = true, IsGrblHAL = false };
        public bool IsConnected => true;
        public ControllerCapabilities Capabilities { get; }
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) => Task.CompletedTask;
    }

    [Fact]
    public async Task Five_axis_editor_has_exactly_five_axes_and_offset_names()
    {
        var settings = new AppSettings();
        settings.Current.DemoAxes = 5;
        using var service = new DemoGrblService(settings);
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());

        Assert.Equal(["X", "Y", "Z", "A", "B"], vm.AxisNames);
        Assert.All(vm.Rows, row => Assert.Equal(5, row.Values.Count));
        Assert.Equal("G54", vm.OffsetCodes[0]);
        Assert.Equal("G59.3", vm.OffsetCodes[8]);
    }
}
