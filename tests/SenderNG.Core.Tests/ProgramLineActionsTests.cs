/*
 * ProgramLineActionsTests.cs - send-selection / run-from-here program line actions
 */

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

using CNC.Core.Services;
using SenderNG.Modules.Fixed;
using Xunit;

namespace SenderNG.Core.Tests;

public class ProgramLineActionsTests
{
    private static async Task<DemoGrblService> ConnectDemoAsync()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "sng-line-actions-" + Guid.NewGuid().ToString("N")))
            .Load();
        var svc = new DemoGrblService(settings);
        await svc.ConnectAsync("demo");
        return svc;
    }

    [Fact]
    public async Task SendSelection_sends_only_the_selected_rows_in_file_order()
    {
        var svc = await ConnectDemoAsync();
        var vm = new ProgramViewModel(svc);
        vm.LoadGCode("G21\nG0 X10\nG1 X20 F100\nM5");

        vm.SetSelection([3, 1]);

        Assert.True(vm.SendSelectionCommand.CanExecute(null));
        var send = vm.SendSelectionAsync();
        // The selection is dropped up front: the immutable row records are replaced
        // as statuses update, which would otherwise erode a live selection row by row.
        Assert.False(vm.HasSelection);
        await send;

        Assert.Equal("·", vm.ProgramRows[0].Status); // not selected
        Assert.Equal("✓", vm.ProgramRows[1].Status);
        Assert.Equal("·", vm.ProgramRows[2].Status); // not selected
        Assert.Equal("✓", vm.ProgramRows[3].Status);
        Assert.Equal(2, vm.DoneCount);
        Assert.Equal("Sent 2 blocks", vm.RunMessage);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task SendSelection_skips_non_command_rows_and_counts_them_out()
    {
        var svc = await ConnectDemoAsync();
        var vm = new ProgramViewModel(svc);
        vm.LoadGCode("; heading\nG21\nG0 X10\nM5");

        vm.SetSelection([0, 1, 2, 3]);

        // The label counts what will actually be sent, not what is selected.
        Assert.Contains("(3)", vm.SendSelectionMenuText);

        await vm.SendSelectionAsync();

        Assert.Equal("·", vm.ProgramRows[0].Status);
        Assert.All(vm.ProgramRows.Skip(1), r => Assert.Equal("✓", r.Status));
        Assert.Equal("Sent 3 blocks", vm.RunMessage);
    }

    [Fact]
    public async Task SendSelection_stops_at_a_program_end_and_reports_the_remainder()
    {
        var svc = await ConnectDemoAsync();
        var vm = new ProgramViewModel(svc);
        vm.LoadGCode("G21\nM2\nG0 X99");

        vm.SetSelection([0, 1, 2]);
        await vm.SendSelectionAsync();

        Assert.Equal("✓", vm.ProgramRows[0].Status);
        Assert.Equal("✓", vm.ProgramRows[1].Status);
        Assert.Equal("·", vm.ProgramRows[2].Status); // behind the end marker: never sent
        Assert.Contains("not sent", vm.RunMessage);
    }

    [Fact]
    public async Task RunFrom_marks_rows_from_the_selected_line_only()
    {
        var svc = await ConnectDemoAsync();
        var vm = new ProgramViewModel(svc);
        vm.LoadGCode("G21\nG0 X10\nG1 X20 F100\nM5");

        vm.SetSelection([2]);
        await vm.RunFromAsync(2);

        Assert.Equal("·", vm.ProgramRows[0].Status);
        Assert.Equal("·", vm.ProgramRows[1].Status);
        Assert.Equal("✓", vm.ProgramRows[2].Status);
        Assert.Equal("✓", vm.ProgramRows[3].Status);
        Assert.Equal(2, vm.DoneCount);
        Assert.Equal("Run from line 3 finished (2 blocks)", vm.RunMessage);
    }

    [Fact]
    public async Task RunFrom_echoes_the_executing_block_in_the_status_message()
    {
        var svc = await ConnectDemoAsync();
        var vm = new ProgramViewModel(svc);
        vm.LoadGCode("G21\nG0 X10\nG1 X20 F100\nM5");
        var messages = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProgramViewModel.RunMessage))
                messages.Add(vm.RunMessage);
        };

        vm.SetSelection([2]);
        await vm.RunFromAsync(2);

        // The MDI-style echo appeared while streaming, then the final summary won.
        Assert.Contains("Line 3: G1 X20 F100", messages);
        Assert.Equal("Run from line 3 finished (2 blocks)", vm.RunMessage);
    }

    [Fact]
    public async Task RunFrom_lowers_the_frontier_below_the_previous_full_run()
    {
        var svc = new ScriptedGrblService();
        var vm = new ProgramViewModel(svc);
        vm.LoadGCode("G21\nG0 X10\nG1 X20 F100\nM5");

        await vm.RunAsync();
        Assert.Equal(3, vm.TouchedRow);

        var gate = svc.NextGate();
        vm.SetSelection([2]);
        var run = vm.RunFromAsync(2);

        // The re-run's own "Sent" report may already have raised the frontier back
        // to row 2; what matters is that the old frontier (3) was lowered, which is
        // what makes the plotter rescan and repaint the re-run rows.
        Assert.True(vm.TouchedRow < 3, $"expected the frontier to regress, got {vm.TouchedRow}");

        gate.SetResult();
        await run;
        Assert.Equal(3, vm.TouchedRow);
    }

    [Fact]
    public void Line_actions_are_disabled_without_a_connection()
    {
        var svc = new ScriptedGrblService { IsConnected = false };
        var vm = new ProgramViewModel(svc);
        vm.LoadGCode("G21\nG0 X10");
        vm.SetSelection([1]);

        Assert.False(vm.SendSelectionCommand.CanExecute(null));
        Assert.False(vm.RunFromLineCommand.CanExecute(null));
    }

    [Fact]
    public void Line_actions_are_disabled_when_the_machine_is_not_idle()
    {
        var svc = new ScriptedGrblService();
        var vm = new ProgramViewModel(svc);
        vm.LoadGCode("G21\nG0 X10");
        vm.SetSelection([1]);

        svc.SetState("Run");
        Assert.False(vm.SendSelectionCommand.CanExecute(null));
        Assert.False(vm.RunFromLineCommand.CanExecute(null));

        svc.SetState("Hold");
        Assert.False(vm.SendSelectionCommand.CanExecute(null));

        // Check mode is a legitimate target: it validates lines without motion.
        svc.SetState("Check");
        Assert.True(vm.SendSelectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task Line_actions_are_disabled_while_a_session_runs()
    {
        var svc = new ScriptedGrblService();
        var vm = new ProgramViewModel(svc);
        vm.LoadGCode("G21\nG0 X10");
        vm.SetSelection([1]);
        var gate = svc.NextGate();

        var run = vm.RunAsync();
        Assert.True(vm.IsRunning);
        Assert.False(vm.SendSelectionCommand.CanExecute(null));
        Assert.False(vm.RunFromLineCommand.CanExecute(null));

        gate.SetResult();
        await run;
        Assert.True(vm.SendSelectionCommand.CanExecute(null));
    }

    [Fact]
    public void Loading_a_program_clears_the_selection()
    {
        var vm = new ProgramViewModel();
        vm.LoadGCode("G21\nG0 X10");
        vm.SetSelection([1]);
        Assert.True(vm.HasSelection);

        vm.LoadGCode("G21\nG0 Y10\nM5");

        Assert.False(vm.HasSelection);
        Assert.Empty(vm.SelectedRows);
        Assert.Contains("(0)", vm.SendSelectionMenuText);
    }

    [Fact]
    public void SetSelection_ignores_out_of_range_rows_and_orders_the_rest()
    {
        var vm = new ProgramViewModel();
        vm.LoadGCode("G21\nG0 X10\nM5");

        vm.SetSelection([2, -1, 2, 0, 99]);

        Assert.Equal([0, 2], vm.SelectedRows);
        Assert.False(vm.SendSelectionCommand.CanExecute(null)); // not connected
    }

    [Fact]
    public async Task SendSelection_resets_the_scope_before_re_sending()
    {
        var svc = await ConnectDemoAsync();
        var vm = new ProgramViewModel(svc);
        vm.LoadGCode("G21\nG0 X10\nM5");
        vm.SetSelection([1, 2]);
        await vm.SendSelectionAsync();
        Assert.Equal(2, vm.DoneCount);

        // A second send of the same rows starts from a clean scope and ends at the
        // same ack count (the per-row decrement must not double-count).
        await vm.SendSelectionAsync();

        Assert.Equal(2, vm.DoneCount);
        Assert.All(vm.ProgramRows.Skip(1), r => Assert.Equal("✓", r.Status));
    }
}
