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
using System.Reactive.Subjects;
using CNC.Core;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Fixed;
using Xunit;

namespace SenderNG.Core.Tests;

// Serialized with the other classes touching the process-global
// Machine.WorkParametersProvider static (the program-limits emulator reads it) —
// see EmulatorWorkParametersBridgeTests' collection comment.
[Collection("LegacyStatics")]
public class ProgramViewModelTests
{
    [Fact]
    public void LoadGCode_fills_program_rows()
    {
        var vm = new ProgramViewModel();

        vm.LoadGCode("G0 X0\nG0 Y0\r\nM30");

        Assert.Equal(3, vm.ProgramRows.Count);
        Assert.Equal(new GCodeProgramRow(1, "·", "G0 X0", null), vm.ProgramRows[0]);
        Assert.Equal(new GCodeProgramRow(2, "·", "G0 Y0", null), vm.ProgramRows[1]);
        Assert.Equal(new GCodeProgramRow(3, "·", "M30", null), vm.ProgramRows[2]);
    }

    [Fact]
    public void LoadGCode_rows_carry_physical_file_line_numbers()
    {
        var vm = new ProgramViewModel();

        vm.LoadGCode("G0 X0\n\nM30");

        // The engine model skips blank lines; rows keep the physical file line.
        Assert.Equal(2, vm.ProgramRows.Count);
        Assert.Equal(1, vm.ProgramRows[0].Line);
        Assert.Equal(3, vm.ProgramRows[1].Line);
    }

    [Fact]
    public void LoadGCode_clears_previous_rows()
    {
        var vm = new ProgramViewModel();
        vm.LoadGCode("G0 X0\nG0 Y0");

        vm.LoadGCode("M2");

        Assert.Single(vm.ProgramRows);
        Assert.Equal("M2", vm.ProgramRows[0].Data);
    }

    [Fact]
    public void ClearProgram_unloads_rows_limits_and_loaded_text()
    {
        var vm = new ProgramViewModel();
        vm.LoadGCode("G0 X0\nG1 X10 Y5");
        Assert.NotEmpty(vm.ProgramRows);
        Assert.True(vm.HasLimits);

        vm.ClearProgram(); // fired on profile switch — a new machine invalidates geometry

        Assert.Empty(vm.ProgramRows);
        Assert.Empty(vm.LimitRows);
        Assert.False(vm.HasLimits);
        Assert.Equal(0, vm.SendableCount);
    }

    [Fact]
    public void LoadGCode_with_empty_text_clears_rows()
    {
        var vm = new ProgramViewModel();
        vm.LoadGCode("G0 X0");

        vm.LoadGCode(string.Empty);

        Assert.Empty(vm.ProgramRows);

        vm.LoadGCode(null!);

        Assert.Empty(vm.ProgramRows);
    }

    [Fact]
    public void LoadProgram_adds_limit_rows_from_the_single_parse()
    {
        var vm = new ProgramViewModel();
        vm.LoadProgram(GCodeProgram.Parse("G21 G90\nG0 X10 Y20\nG1 X30 Y40 F100"), "G21 G90\nG0 X10 Y20\nG1 X30 Y40 F100");

        Assert.True(vm.HasLimits);
        var x = Assert.Single(vm.LimitRows, l => l.Letter == "X");
        Assert.Equal(10, x.Min);
        Assert.Equal(30, x.Max);
    }

    [Fact]
    public void Limits_recompute_when_controller_work_parameters_arrive()
    {
        // Machine.WorkParametersProvider is a process-global static that parallel
        // test classes also touch — save/restore it around the test.
        var savedProvider = Machine.WorkParametersProvider;
        try
        {
            // Loaded before any controller data: the limits emulator is unseeded
            // (zero offsets), so the G53 machine-coordinate moves report raw values.
            Machine.WorkParametersProvider = null;
            var stub = new WorkParamsStub();
            var vm = new ProgramViewModel(stub);
            vm.LoadGCode("G53 X100\nG53 X200");

            var x = Assert.Single(vm.LimitRows, r => r.Letter == "X");
            Assert.Equal(100, x.Min);
            Assert.Equal(200, x.Max);

            // The recompute seeds the emulator from the provider, so point it at
            // the snapshot before publishing (G54 active, X offset 50): the G53
            // machine-coordinate moves re-base to 100 - 50 .. 200 - 50.
            var wp = WorkParameters.Parse(
                ["[G54:50,0,0]", "[G55:0,0,0]", "[G56:0,0,0]", "[G57:0,0,0]", "[G58:0,0,0]", "[G59:0,0,0]"],
                3, 0);
            wp.ActiveCoordinateSystem = "G54";
            Machine.WorkParametersProvider = () => wp;
            stub.Publish(wp);

            x = Assert.Single(vm.LimitRows, r => r.Letter == "X");
            Assert.Equal(50, x.Min);
            Assert.Equal(150, x.Max);
        }
        finally
        {
            Machine.WorkParametersProvider = savedProvider;
        }
    }

    /// <summary>IGrblService double that can publish work-parameters snapshots on demand.</summary>
    private sealed class WorkParamsStub : IGrblService
    {
        private readonly Subject<WorkParameters> _changes = new();
        public bool IsConnected => true;
        public ControllerCapabilities Capabilities { get; } = new() { IsKnown = true, IsGrblHAL = false };
        public IObservable<MachineState> State => Observable.Never<MachineState>();
        public IObservable<WorkParameters> WorkParametersChanges => _changes;
        public void Publish(WorkParameters wp) => _changes.OnNext(wp);
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) => Task.CompletedTask;
    }
}

public class ProgramViewModelRunTests
{
    private static async Task<DemoGrblService> ConnectDemoAsync()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "sng-pvm-" + Guid.NewGuid().ToString("N")))
            .Load();
        var svc = new DemoGrblService(settings);
        await svc.ConnectAsync("demo");
        return svc;
    }

    [Fact]
    public async Task Run_streams_every_row_to_done()
    {
        var svc = await ConnectDemoAsync();
        var vm = new ProgramViewModel(svc);
        vm.LoadGCode("G21\nG0 X10 Y10\nG1 X20 Y20 F100\nM5");

        Assert.True(vm.RunCommand.CanExecute(null));
        Assert.Equal(4, vm.SendableCount);

        await vm.RunAsync();

        Assert.False(vm.IsRunning);
        Assert.All(vm.ProgramRows, r => Assert.Equal("✓", r.Status));
        Assert.Equal("4/4", vm.ProgressText);
        Assert.Contains("Run finished (4 blocks)", vm.RunMessage);
    }

    [Fact]
    public async Task Run_counts_sendable_blocks_only_comments_stay_in_the_grid()
    {
        var svc = await ConnectDemoAsync();
        var vm = new ProgramViewModel(svc);
        vm.LoadGCode("G21\n; heading\nG0 X10 Y10\nG1 X20 Y20 F100\n; tail\nM5");

        Assert.Equal(6, vm.ProgramRows.Count);
        Assert.Equal(4, vm.SendableCount);

        await vm.RunAsync();

        Assert.Equal("4/4", vm.ProgressText);
        Assert.Contains("Run finished (4 blocks)", vm.RunMessage);
        // Comment rows keep their place in the grid but are never streamed.
        Assert.Equal("·", vm.ProgramRows[1].Status);
        Assert.Equal("·", vm.ProgramRows[4].Status);
    }

    [Fact]
    public async Task Run_marks_the_failing_line_and_stops()
    {
        var svc = await ConnectDemoAsync();
        var vm = new ProgramViewModel(svc);
        vm.LoadGCode("G21\nR5\nG0 X1"); // R-first word: rejected by the demo

        await vm.RunAsync();

        Assert.False(vm.IsRunning);
        Assert.Equal("✓", vm.ProgramRows[0].Status);
        Assert.Equal("✕", vm.ProgramRows[1].Status);
        Assert.Equal("error:22", vm.ProgramRows[1].Detail);
        Assert.Equal("·", vm.ProgramRows[2].Status); // never sent
        Assert.Contains("Run failed", vm.RunMessage);
    }

    [Fact]
    public async Task Run_rewinds_the_rows_first()
    {
        var svc = await ConnectDemoAsync();
        var vm = new ProgramViewModel(svc);
        vm.LoadGCode("G21\nG0 X1");

        await vm.RunAsync();
        Assert.All(vm.ProgramRows, r => Assert.Equal("✓", r.Status));

        await vm.RunAsync(); // second run rewinds then streams again

        Assert.All(vm.ProgramRows, r => Assert.Equal("✓", r.Status));
    }

    [Fact]
    public void Run_is_unavailable_without_a_connection()
    {
        var vm = new ProgramViewModel();
        vm.LoadGCode("G0 X1");

        Assert.False(vm.RunCommand.CanExecute(null));
    }

    [Fact]
    public void Rewind_resets_row_statuses_to_un_sent_and_requests_scroll_to_first_line()
    {
        var vm = new ProgramViewModel();
        vm.LoadGCode("G0 X0\nG0 Y0\nM30");
        vm.ProgramRows[0] = vm.ProgramRows[0] with { Status = "✕" };
        vm.ProgramRows[1] = vm.ProgramRows[1] with { Status = "✓" };
        var scrolledTo = -1;
        vm.ScrollRequested += i => scrolledTo = i;

        vm.Rewind();

        Assert.All(vm.ProgramRows, r => Assert.Equal("·", r.Status));
        Assert.Equal(0, scrolledTo);
    }

    [Fact]
    public void Rewind_without_program_does_not_throw()
    {
        var vm = new ProgramViewModel();

        vm.Rewind();

        Assert.Empty(vm.ProgramRows);
    }
}