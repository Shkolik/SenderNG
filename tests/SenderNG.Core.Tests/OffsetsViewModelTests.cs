
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
    public async Task Selecting_an_offset_activates_it_on_the_controller()
    {
        // iSender parity: picking G56 in the editor sends "G56" to the
        // controller, so the DRO zero (G10 L20 P0 = active system) and any
        // writes land in the row the user is looking at.
        var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());

        vm.SelectedOffset = "G56";

        Assert.Equal("G56", vm.SelectedOffset);
        Assert.Equal("G56", service.WorkParameters.ActiveCoordinateSystem);
    }

    [Fact]
    public async Task Editor_follows_an_activation_issued_outside_the_editor()
    {
        // G56 can be activated from anywhere (MDI, a program line). The editor
        // selection must follow, WITHOUT re-sending the activation (that would
        // echo forever through the snapshot stream).
        var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());

        await service.SendAsync("G56"); // activation from outside the editor

        Assert.Equal("G56", vm.SelectedOffset);
    }

    [Fact]
    public async Task Constructing_the_editor_does_not_change_the_active_offset()
    {
        // The initial row selection is programmatic — it must not send G54 and
        // yank the controller away from the system the user had active.
        var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        await service.SendAsync("G55");

        var vm = new OffsetsViewModel(service, MakeSettings());

        Assert.Equal("G54", vm.SelectedOffset);
        Assert.Equal("G55", service.WorkParameters.ActiveCoordinateSystem);
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
        Assert.Equal("0", row.Cells[0].Value);

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
        tool.SetOriginals(["1", "2", "3"], "0.5", string.Empty);
        Assert.False(tool.IsDirty);

        // A local name edit also marks the row dirty (names persist on save).
        tool.Name = "6 mm endmill";
        Assert.True(tool.IsDirty);
        tool.SetOriginals(["1", "2", "3"], "0.5", "6 mm endmill");
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

        vm.SetAllToolsCommand.Execute(null);
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

        vm.SetAllToolsCommand.Execute(null);
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
        // No grayed-out placeholder: the whole section hides unless supported AND enabled.
        Assert.False(vm.ShowToolTableSection);

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
        Assert.False(forkVm.ShowToolTableSection);
    }

    [Fact]
    public async Task Demo_shows_tool_table_section_when_enabled()
    {
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());

        Assert.True(vm.ControllerSupportsToolTable);
        Assert.True(vm.ShowToolTableSection);

        // User preference hides the whole section.
        vm.ShowToolTable = false;
        Assert.False(vm.ShowToolTableSection);
    }

    [Fact]
    public void Editor_offers_only_supported_systems_on_classic_grbl()
    {
        // Classic grbl 1.1h reports G54..G59 only and has no G59.x — offering rows
        // for G59.1/2/3 would produce error 29 on save (mega5x 1.2i).
        var grbl = new ClassicGrblStub(
            new ControllerCapabilities { IsKnown = true, IsGrblHAL = false, NumAxes = 5 });
        var vm = new OffsetsViewModel(grbl, MakeSettings());

        Assert.Equal(6, vm.Rows.Count);
        Assert.DoesNotContain(vm.Rows, row => row.Code.StartsWith("G59."));
        Assert.Equal(["G54", "G55", "G56", "G57", "G58", "G59"], vm.Rows.Select(r => r.Code));
        Assert.Equal(6, vm.OffsetCodes.Count);
    }

    [Fact]
    public async Task Alarm_state_disables_editor_writes()
    {
        var grbl = new ClassicGrblStub();
        var vm = new OffsetsViewModel(grbl, MakeSettings());
        Assert.True(vm.IsReady);

        grbl.Push(new MachineState(3) { Status = "Alarm" });

        Assert.False(vm.IsReady);
        vm.SetAllCommand.Execute(null);
        await Task.Delay(100);
        Assert.Contains("alarm", vm.Status, StringComparison.OrdinalIgnoreCase);

        // Leaving alarm re-enables the editor.
        grbl.Push(new MachineState(3) { Status = "Idle" });
        Assert.True(vm.IsReady);
    }

    /// <summary>Minimal IGrblService double exposing configurable capabilities.
    /// Defaults to classic grbl (no HAL) with the classic $# system set.
    /// </summary>
    private sealed class ClassicGrblStub : IGrblService
    {
        private readonly System.Reactive.Subjects.BehaviorSubject<MachineState> _subject = new(new MachineState(3));
        public ClassicGrblStub(ControllerCapabilities? caps = null)
        {
            Capabilities = caps ?? new ControllerCapabilities { IsKnown = true, IsGrblHAL = false };
            WorkParameters = WorkParameters.Parse(
                ["[G54:0,0,0]", "[G55:0,0,0]", "[G56:0,0,0]", "[G57:0,0,0]", "[G58:0,0,0]", "[G59:0,0,0]"],
                Capabilities.NumAxes, Capabilities.NumTools);
        }
        public bool IsConnected => true;
        public ControllerCapabilities Capabilities { get; }
        public WorkParameters WorkParameters { get; }
        public IObservable<MachineState> State => _subject;
        public void Push(MachineState state) => _subject.OnNext(state);
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

    [Fact]
    public async Task Selection_follows_the_active_tool()
    {
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());

        Assert.Equal(1, vm.SelectedToolId); // first numbered tool preselected at boot

        await service.SendAsync("T2");
        Assert.Equal(2, vm.SelectedToolId);
    }

    [Fact]
    public async Task Selection_is_not_yanked_from_a_dirty_tool()
    {
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());
        await service.SendAsync("T2");

        vm.Tools.Single(t => t.Id == 2).Radius = "1.5"; // mid-edit
        await service.SendAsync("T1"); // controller switches tool

        Assert.Equal(2, vm.SelectedToolId); // stay on the dirty row
    }

    [Fact]
    public async Task Tool_names_are_local_metadata_and_persist()
    {
        var settings = MakeSettings();
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, settings);

        var t1 = vm.Tools.Single(t => t.Id == 1);
        Assert.Equal(string.Empty, t1.Name);
        t1.Name = "6 mm endmill";
        Assert.True(t1.IsDirty); // a name change counts as a change

        vm.SetAllToolsCommand.Execute(null);
        await Task.Delay(100);

        // Names are never sent to the controller (there is no G10 name word).
        Assert.Equal(0, service.WorkParameters.Tools.Single(t => t.Id == 1).Offsets[0]);
        Assert.Equal(0.5, service.WorkParameters.Tools.Single(t => t.Id == 1).Radius);

        // Persisted locally: a fresh VM over the same settings still sees it.
        var reloaded = new OffsetsViewModel(service, settings);
        Assert.Equal("6 mm endmill", reloaded.Tools.Single(t => t.Id == 1).Name);
    }

    [Fact]
    public async Task Touch_off_copies_machine_position_into_the_tool()
    {
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());
        var t1 = vm.Tools.Single(t => t.Id == 1);

        await service.SendAsync("T1");
        await service.SendAsync("$J=X10Y20Z30F500");

        vm.TouchOff(1);

        Assert.Equal("10", t1.Cells[0].Value);
        Assert.Equal("20", t1.Cells[1].Value);
        Assert.Equal("30", t1.Cells[2].Value);
        Assert.True(t1.IsDirty);
        Assert.Contains("T1", vm.Status);
        Assert.Equal(1, vm.SelectedToolId);
    }

    [Fact]
    public async Task Touch_off_rejects_the_none_slot_and_keeps_values()
    {
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());
        var t1 = vm.Tools.Single(t => t.Id == 1);
        var before = t1.Cells[0].Value;

        // Slot 0 has no grid row anymore (T0 is a no-op tool — G43 H0 cancels
        // compensation, so an offset written to P0 is never applied); the guard
        // still rejects it without touching any row.
        vm.TouchOff(0);
        Assert.Equal("Touch-off applies to a numbered tool only", vm.Status);
        Assert.Equal(before, t1.Cells[0].Value);
    }

    [Fact]
    public void Tool_selector_label_uses_name_with_slot_fallback()
    {
        var t0 = new EditableToolRow(0, 3);
        Assert.Equal("None", t0.DisplayName);

        var t1 = new EditableToolRow(1, 3);
        Assert.Equal("T1", t1.DisplayName);
        t1.Name = "6 mm endmill";
        Assert.Equal("6 mm endmill", t1.DisplayName);
        t1.Name = "   ";
        Assert.Equal("T1", t1.DisplayName); // blank names fall back to the slot
    }

    [Fact]
    public void Tool_row_radius_is_the_last_uniform_cell()
    {
        // The radius shares the axis uniform grid so its column is exactly as
        // wide as an axis column (name column keeps the remaining space).
        var tool = new EditableToolRow(5, 3);
        Assert.Equal(4, tool.Cells.Count);
        Assert.Equal("0", tool.Cells[3].Value);

        tool.Cells[3].Value = "2.5";
        Assert.Equal("2.5", tool.Radius);

        tool.Radius = "1.75";
        Assert.Equal("1.75", tool.Cells[3].Value);
    }

    [Fact]
    public async Task Clear_tools_zeroes_every_row_in_the_editor()
    {
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());

        var t1 = vm.Tools.Single(t => t.Id == 1);
        t1.Cells[0].Value = "12.5";
        t1.Radius = "3.0";
        Assert.True(t1.IsDirty);

        vm.ClearToolsCommand.Execute(null);

        // Every row zeroed in the editor; the previously-edited row is now dirty
        // (rows that were already zero are not replayed on Set all). Zero renders
        // as plain "0" — same format the board readback produces after a write,
        // so Clear/Set never flip between "0.000" and "0".
        Assert.All(vm.Tools, tool => Assert.Equal("0", tool.Cells[0].Value));
        Assert.All(vm.Tools, tool => Assert.Equal("0", tool.Radius));
        Assert.True(t1.IsDirty);
        Assert.Contains("Set all", vm.Status);
    }

    [Fact]
    public async Task Clear_all_offsets_uses_plain_zero()
    {
        using var service = new DemoGrblService(new AppSettings());
        await service.ConnectAsync("demo");
        var vm = new OffsetsViewModel(service, MakeSettings());
        vm.Rows.Single(x => x.Code == "G54").SetValueAt(0, "12.5");

        vm.ClearAllCommand.Execute(null);

        // Same plain-zero convention as the tool table (readback renders 0 as "0").
        Assert.All(vm.Rows, row => Assert.All(row.Values, value => Assert.Equal("0", value)));
        Assert.Contains("Set all", vm.Status);
    }

    [Fact]
    public void Tool_row_exposes_a_ghost_slot_placeholder()
    {
        // The name cell shows "Tx" in ghost text while the tool has no name
        // (rendered via the TextBox watermark; same fallback as the selector).
        Assert.Equal("T0", new EditableToolRow(0, 3).NamePlaceholder);
        Assert.Equal("T3", new EditableToolRow(3, 3).NamePlaceholder);
    }

    [Fact]
    public void Selector_preselects_the_first_numbered_tool_at_boot()
    {
        using var service = new DemoGrblService(new AppSettings());
        var vm = new OffsetsViewModel(service, MakeSettings());

        Assert.Equal(1, vm.SelectedToolId);
        Assert.NotNull(vm.SelectedToolRow);
        Assert.Equal(1, vm.SelectedToolRow!.Id);
    }

    [Fact]
    public void Visible_tools_is_a_per_mode_setting_that_rebuilds_rows()
    {
        var settings = MakeSettings();
        using var service = new DemoGrblService(new AppSettings());
        var vm = new OffsetsViewModel(service, settings);
        // Demo controller has 4 tool slots, so the default 8 is clamped to 4.
        Assert.Equal([1, 2, 3, 4], vm.Tools.Select(t => t.Id));
        Assert.Equal(8, vm.VisibleTools);

        // Editing the mode-aware setting (App Settings -> Offsets) rebuilds the grid.
        var app = settings.Load();
        app.Current.VisibleTools = 3;
        settings.Save(app);
        Assert.Equal([1, 2, 3], vm.Tools.Select(t => t.Id));
        Assert.Equal(3, vm.VisibleTools);

        // Foam keeps its own value (demo still clamps to its 4 slots).
        app = settings.Load();
        app.Mode = AppSettings.FoamMode;
        app.Current.VisibleTools = 2;
        settings.Save(app);
        Assert.Equal([1, 2], vm.Tools.Select(t => t.Id));
        Assert.Equal(2, vm.VisibleTools);

        // Back to Mill: 3 again, and a fresh VM reads the persisted mode value.
        app = settings.Load();
        app.Mode = AppSettings.MillMode;
        settings.Save(app);
        Assert.Equal([1, 2, 3], vm.Tools.Select(t => t.Id));
        var reloaded = new OffsetsViewModel(service, settings);
        Assert.Equal(3, reloaded.VisibleTools);
        Assert.Equal([1, 2, 3], reloaded.Tools.Select(t => t.Id));
    }

    [Fact]
    public void Tool_table_rows_are_clamped_to_the_controller_slot_count()
    {
        var settings = MakeSettings();
        // User asks for 64 slots (grblHAL's compile-time max), but the controller
        // only has 32 — the grid renders 32 rows, never more.
        settings.Load().Current.VisibleTools = 64;
        settings.Save(settings.Load());
        var grbl = new ToolGrblStub("c1", axisCount: 3, capsTools: 32);
        var vm = new OffsetsViewModel(grbl, settings);

        Assert.Equal(64, vm.VisibleTools); // the setting itself is kept
        Assert.Equal(32, vm.Tools.Count);
        Assert.Equal(1, vm.Tools[0].Id);
        Assert.Equal(32, vm.Tools[^1].Id);

        // 0 hides the grid entirely.
        settings.Load().Current.VisibleTools = 0;
        settings.Save(settings.Load());
        var hidden = new OffsetsViewModel(grbl, settings);
        Assert.Empty(hidden.Tools);
        Assert.Equal(0, hidden.SelectedToolId);
    }

    [Fact]
    public void Editor_uses_the_controller_axis_count_not_the_snapshot_width()
    {
        // A snapshot with over-wide systems (e.g. the demo's resizable work
        // parameters) must never inflate the grids beyond the controller's $I
        // axis count — a 4-axis controller shows exactly X/Y/Z/A.
        var grbl = new ToolGrblStub("wide", axisCount: 9, capsAxes: 4);
        var vm = new OffsetsViewModel(grbl, MakeSettings());

        Assert.Equal(["X", "Y", "Z", "A"], vm.AxisNames);
        Assert.All(vm.Rows, row => Assert.Equal(4, row.Values.Count));
        Assert.Equal(5, vm.Tools[0].Cells.Count); // 4 axes + radius
        Assert.Equal(["X", "Y", "Z", "A", "Radius"], vm.ToolColumnHeaders);
    }

    [Fact]
    public async Task Switching_controllers_rebuilds_tool_rows_without_leaking_names()
    {
        var settings = MakeSettings();
        var c1 = new ToolGrblStub("c1");
        var c2 = new ToolGrblStub("c2");
        // C2 carries a table that differs from C1's (T2 has a value).
        var c2Table = c2.WorkParameters.Clone();
        c2Table.Tools.Single(t => t.Id == 2).Offsets[0] = 4.25;
        c2.Publish(c2Table);

        var proxy = new GrblServiceProxy(c1);
        var vm = new OffsetsViewModel(proxy, settings);

        // Name a tool on C1 and persist it (per-controller local metadata).
        vm.Tools.Single(t => t.Id == 1).Name = "Drill";
        vm.SetAllToolsCommand.Execute(null);
        await Task.Delay(100);
        Assert.Equal("Drill", vm.Tools.Single(t => t.Id == 1).Name);

        // Switch to C2: the grid rebuilds for the new controller — its data
        // shows, C1's names do NOT leak over. The stub reports 2 tool slots, so
        // the grid renders T1..T2 (no T0 row).
        proxy.Inner = c2;
        Assert.Equal(2, vm.Tools.Count);
        Assert.Equal(string.Empty, vm.Tools.Single(t => t.Id == 1).Name);
        Assert.Equal("4.25", vm.Tools.Single(t => t.Id == 2).Cells[0].Value);

        // Switching back restores C1's annotations.
        proxy.Inner = c1;
        Assert.Equal("Drill", vm.Tools.Single(t => t.Id == 1).Name);
    }

    [Fact]
    public async Task Visible_count_change_rebuild_keeps_saved_tool_names_and_values()
    {
        // Regression: save tools (T1-T4) -> change "Visible tools" in App
        // Settings (8 -> 4) -> the grid rebuilds but must NOT lose the saved
        // information. The rebuild used to create blank rows (BuildTools alone);
        // only an app restart re-merged the snapshot + name store via the ctor,
        // which is why the data reappeared after relaunch.
        var settings = MakeSettings();
        var grbl = new ToolGrblStub("c1", axisCount: 3, capsTools: 8);
        // The board already carries T1 X=12.5 (as after a real Set all + $# readback).
        var table = grbl.WorkParameters.Clone();
        table.Tools.Single(t => t.Id == 1).Offsets[0] = 12.5;
        grbl.Publish(table);

        var vm = new OffsetsViewModel(grbl, settings);
        Assert.Equal(8, vm.Tools.Count); // min(8 setting, 8 slots)
        vm.Tools.Single(t => t.Id == 1).Name = "6 mm endmill";
        vm.SetAllToolsCommand.Execute(null);
        await Task.Delay(100);
        Assert.Equal("6 mm endmill", vm.Tools.Single(t => t.Id == 1).Name);

        // App Settings -> Offsets: "Visible tools" 8 -> 4.
        var app = settings.Load();
        app.Current.VisibleTools = 4;
        settings.Save(app);

        // Grid rebuilt to 4 rows — and T1 keeps its board value AND its name.
        Assert.Equal(4, vm.Tools.Count);
        Assert.Equal([1, 2, 3, 4], vm.Tools.Select(t => t.Id));
        var t1 = vm.Tools.Single(t => t.Id == 1);
        Assert.Equal("12.5", t1.Cells[0].Value);
        Assert.Equal("6 mm endmill", t1.Name);
        Assert.False(t1.IsDirty); // originals re-recorded from the merged state

        // Growing the count back rebuilds again and still shows the data.
        app = settings.Load();
        app.Current.VisibleTools = 6;
        settings.Save(app);
        Assert.Equal(6, vm.Tools.Count);
        Assert.Equal("12.5", vm.Tools.Single(t => t.Id == 1).Cells[0].Value);
        Assert.Equal("6 mm endmill", vm.Tools.Single(t => t.Id == 1).Name);
    }

    [Fact]
    public async Task Same_com_port_on_a_different_machine_does_not_leak_names()
    {
        // The user's scenario: two DIFFERENT boards that share the same COM port
        // (never simultaneously). ConnectionId is identical, but each machine's
        // $I firmware fingerprint differs -> separate namespaces, so board B
        // never inherits board A's tool names.
        var settings = MakeSettings();
        var boardA = new ToolGrblStub("COM9", controllerKey: "grblHAL|1.1f.20260827|VNMSL+,100,1024,4,32|4:XYZA");
        var boardB = new ToolGrblStub("COM9", controllerKey: "grblHAL|1.1f.20260827|VNMSL+,100,1024,4,48|4:XYZA");

        var proxy = new GrblServiceProxy(boardA);
        var vm = new OffsetsViewModel(proxy, settings);

        // Name a tool on board A and persist it.
        vm.Tools.Single(t => t.Id == 1).Name = "Drill";
        vm.SetAllToolsCommand.Execute(null);
        await Task.Delay(100);
        Assert.Equal("Drill", vm.Tools.Single(t => t.Id == 1).Name);

        // Board B connects on the SAME COM port: its fingerprint differs -> the
        // grid rebuilds for the new machine and A's names must NOT show.
        proxy.Inner = boardB;
        Assert.Equal(string.Empty, vm.Tools.Single(t => t.Id == 1).Name);

        // Back to board A: its names come back.
        proxy.Inner = boardA;
        Assert.Equal("Drill", vm.Tools.Single(t => t.Id == 1).Name);
    }

    /// <summary>Minimal grblHAL-like service double with a configurable connection
    /// identity, used for controller-switch tests. By default the controller key
    /// equals the connection id (like the DIM default); pass an explicit
    /// controllerKey to simulate two different boards sharing one COM port.</summary>
    private sealed class ToolGrblStub : IGrblService
    {
        private readonly System.Reactive.Subjects.BehaviorSubject<MachineState> _machine = new(new MachineState(3));
        private readonly System.Reactive.Subjects.BehaviorSubject<WorkParameters> _parameters;

        public ToolGrblStub(string connectionId, int axisCount = 3, int capsAxes = 0, int capsTools = 2,
            string? controllerKey = null)
        {
            ConnectionId = connectionId;
            ControllerKey = controllerKey ?? connectionId;
            _parameters = new System.Reactive.Subjects.BehaviorSubject<WorkParameters>(
                WorkParameters.CreateDefault(axisCount, 2));
            Capabilities = new ControllerCapabilities
            {
                IsKnown = true,
                IsGrblHAL = true,
                NumAxes = capsAxes > 0 ? capsAxes : axisCount,
                NumTools = capsTools,
            };
        }

        public string ConnectionId { get; }
        public string ControllerKey { get; }
        public bool IsConnected => true;
        public ControllerCapabilities Capabilities { get; }
        public WorkParameters WorkParameters => _parameters.Value;
        public IObservable<MachineState> State => _machine;
        public IObservable<WorkParameters> WorkParametersChanges => _parameters;
        public void Publish(WorkParameters parameters) => _parameters.OnNext(parameters);
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) => Task.CompletedTask;
    }
}
