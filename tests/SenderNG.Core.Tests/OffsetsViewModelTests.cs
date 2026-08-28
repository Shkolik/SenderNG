using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Fixed;

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
        Assert.NotEqual(0, service.WorkParameters.CoordinateSystems.Single(x => x.Code == "G54").Values[0]);
    }

    [Fact]
    public async Task Editor_rows_are_restricted_to_g10_writable_systems()
    {
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service);

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
        var vm = new OffsetsViewModel(service);
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
