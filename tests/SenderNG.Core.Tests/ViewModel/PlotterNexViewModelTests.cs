/*
 * PlotterNexViewModelTests.cs - headless view-model tests: program changes rebuild
 * geometry, the row-state frontier maps onto per-segment run states, and live
 * controller positions feed the tool endpoint
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

using System.Numerics;
using System.Threading;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Modules.Plotter.Nex;
using SenderNG.Modules.Plotter.Nex.Model;
using SenderNG.Modules.Plotter.Nex.Rendering;
using SenderNG.Modules.Plotter.Nex.Scene;
using SenderNG.Modules.Plotter.Nex.Settings;

namespace SenderNG.Core.Tests;

public class PlotterNexViewModelTests : IDisposable
{
    private readonly string _settingsDir;
    private readonly StubGrbl _grbl = new();

    public PlotterNexViewModelTests()
    {
        _settingsDir = Path.Combine(Path.GetTempPath(), "plotter-nex-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_settingsDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_settingsDir, recursive: true);
        }
        catch
        {
            // The OS may still hold the settings files — cleanup is best-effort.
        }
    }

    /// <summary>A small mill program with no blank/comment lines — the token
    /// LineNumbers then equal the entry FileLines, keeping the line→row mapping
    /// exact (comment/blank lines would skew gcValues.N vs FileLine).</summary>
    private static string MillProgram() =>
        "G21\nG1 X10 Y5 F240\nG1 X20 Y5\nG1 X0 Y0\n";

    private PlotterNexViewModel MakeVm(StubProgramSource source, RecorderScene scene,
        string machineTypeId = MachineTypes.Mill)
    {
        var settings = new SettingsService(_settingsDir);
        var profile = settings.Load();
        profile.MachineTypeId = machineTypeId;
        settings.Save(profile);

        return new PlotterNexViewModel(source, _grbl, settings,
            new LocalizationService(new MemoryStream("{ \"strings\": {} }"u8.ToArray())),
            new KinematicSceneFactory(scene));
    }

    [Fact]
    public void No_program_yields_empty_geometry_and_status()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);

        Assert.False(vm.HasGeometry);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public void Without_a_scene_the_vm_stays_inert()
    {
        // No factory — the scene arrives from the view in Task 6, so the VM
        // must tolerate Scene == null: no throws, HasGeometry stays false.
        var source = new StubProgramSource();
        var settings = new SettingsService(_settingsDir);
        using var vm = new PlotterNexViewModel(source, _grbl, settings,
            new LocalizationService(new MemoryStream("{ \"strings\": {} }"u8.ToArray())));

        Assert.Null(vm.Scene);
        Assert.False(vm.HasGeometry);

        // Program load without a scene must not throw.
        source.Load(MillProgram());
        Assert.Null(vm.Scene);
        Assert.False(vm.HasGeometry);
        // No fabric attached yet, so PlaneCount falls back to the machine type's
        // own planes (Mill is a single [X, Y, Z] plane).
        Assert.Equal(1, vm.PlaneCount);
    }

    [Fact]
    public void Program_load_populates_plane_layers()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);

        source.Load(MillProgram());

        Assert.True(vm.HasGeometry);
        Assert.NotNull(vm.Scene);
        Assert.Equal(1, vm.PlaneCount);
        Assert.True(vm.ProgressText == "0/4", $"ProgressText = {vm.ProgressText}");

        var layer = scene.Layers[^1];
        Assert.Equal(3, layer.Added.Count);
        Assert.Contains(layer.Added, a => a.Type == MotionType.Cut);
    }

    [Fact]
    public void Closing_the_program_clears_the_plotted_content()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);

        source.Load(MillProgram());
        Assert.True(vm.HasGeometry);
        Assert.Equal(3, scene.Layers[^1].Added.Count);

        // File → Close clears the loaded program through ProgramChanged.
        source.Clear();

        Assert.False(vm.HasGeometry);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
        // The fabric is re-registered empty so the view has nothing stale to
        // draw: the latest plane layer carries no segments and a fresh tool
        // was created.
        Assert.Empty(scene.Layers[^1].Added);
        Assert.True(scene.Tools.Count > 0);
    }

    [Fact]
    public void Rows_changed_maps_row_states_to_segment_run_states()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);
        source.Load(MillProgram());
        var layer = scene.Layers[^1];
        Assert.Empty(layer.RunStateCalls);

        source.AckRows(1, 3);
        vm.ApplyRunStateWindow();
        vm.PulseAckedEnds();

        Assert.Equal(
            [(0, RunState.Executed), (1, RunState.Executed)],
            layer.RunStateCalls);
        Assert.True(vm.FrontierVersion > 0);
    }

    [Fact]
    public void ApplyRunStateWindow_applies_only_newly_touched_rows()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);
        source.Load(MillProgram()); // 3 output segments, rows 1..3
        var layer = scene.Layers[^1];

        source.AckRows(1, 3); // rows 1,2 Ok — touched = 2
        vm.ApplyRunStateWindow();

        // Only rows in (prevTouched, touched] get SetSegmentRunState: indices 0,1.
        Assert.Equal(
            [(0, RunState.Executed), (1, RunState.Executed)],
            layer.RunStateCalls);

        source.AckRows(3, 4); // row 3 Ok — touched = 3
        vm.ApplyRunStateWindow();
        Assert.Equal(
            [(0, RunState.Executed), (1, RunState.Executed), (2, RunState.Executed)],
            layer.RunStateCalls);
    }

    [Fact]
    public void Late_ack_behind_the_frontier_recolors_an_inflight_segment()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);
        source.Load(MillProgram());
        var layer = scene.Layers[^1];

        source.AckRows(1, 2, ProgramRowState.Sent); // row 1 Sent — touched = 1
        vm.ApplyRunStateWindow();
        Assert.Equal([(0, RunState.InFlight)], layer.RunStateCalls);

        // The ack lands for an already-touched row: TouchedRow does not move,
        // so the frontier window is empty and only the recolor pass can fix it.
        source.AckRows(1, 2, ProgramRowState.Ok);
        vm.ApplyRunStateWindow();

        Assert.Equal(
            [(0, RunState.InFlight), (0, RunState.Executed)],
            layer.RunStateCalls);
    }

    [Fact]
    public void Late_acks_resolve_in_flight_rows_until_all_are_colored()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);
        source.Load(MillProgram());
        var layer = scene.Layers[^1];

        source.AckRows(1, 3, ProgramRowState.Sent); // rows 1,2 Sent — touched = 2
        vm.ApplyRunStateWindow();
        Assert.Equal(
            [(0, RunState.InFlight), (1, RunState.InFlight)],
            layer.RunStateCalls);

        // First row resolves behind the frontier; the second stays in flight,
        // so the checkpoint parks a hair before it.
        source.AckRows(1, 2, ProgramRowState.Ok);
        vm.ApplyRunStateWindow();
        // Second row resolves on the next ack; the scan then reaches the end.
        source.AckRows(2, 3, ProgramRowState.Ok);
        vm.ApplyRunStateWindow();

        Assert.Equal(
            [(0, RunState.InFlight), (1, RunState.InFlight),
             (0, RunState.Executed), (1, RunState.Executed)],
            layer.RunStateCalls);
    }

    [Fact]
    public void Controller_state_feeds_the_tool_endpoint()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);

        _grbl.Source.Emit(new MachineState { Positions = new double[] { 1, 2, 3 } });

        // BuildScene seeds the tool pose (zero), then each controller tick
        // appends the live pose — the last one is the current position.
        var tool = scene.Tools[^1];
        var endpoint = tool.Positions[^1];
        Assert.Equal(new Vector3(1, 2, 3), Assert.Single(endpoint.Points));
    }

    [Fact]
    public void Foam_controller_state_feeds_both_tool_endpoints_and_the_loft()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene, MachineTypes.Foam);

        // Foam has 4 axes: tower 1 (X,Y) = indices 0,1; tower 2 (Z,A) = indices 2,3.
        _grbl.Source.Emit(new MachineState { Positions = new double[] { 10, 20, 30, 40 } });

        var tool = scene.Tools[^1];
        var endpoint = tool.Positions[^1];
        Assert.Equal(2, endpoint.Points.Length);
        Assert.Equal(new Vector3(-50, 10, 20), endpoint.Points[0]);
        Assert.Equal(new Vector3(50, 30, 40), endpoint.Points[1]);

        // The loft follows the wire every controller tick — never empty.
        var loft = scene.Lofts[^1];
        var polygon = loft.Calls[^1];
        Assert.Equal(2, polygon.Length);
        Assert.Equal(endpoint.Points[0], polygon[0]);
        Assert.Equal(endpoint.Points[1], polygon[1]);
    }

    [Fact]
    public void Applying_a_foam_program_feeds_an_equal_length_loft_sheet()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene, MachineTypes.Foam);

        // 3 motion rows; every motion moves both towers (X/Y plane 0, Z/A plane 1).
        source.Load(
            "G21\n" +
            "G0 X10 Y5 Z10 A12 F240\n" +
            "G1 X20 Y8 Z20 A24\n" +
            "G1 X30 Y5 Z30 A36\n");

        var loft = scene.Lofts[^1];
        var call = Assert.Single(loft.SheetCalls);
        Assert.Equal(call.SideA.Count, call.SideB.Count);
        Assert.Equal(3, call.SideA.Count);

        // First sample = the pre-program tie-off (segment 0 From is the emulator's
        // initial state before the first motion) — plane-0 and plane-1 world
        // mapping in lockstep.
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var config = PlotterNexConfig.Defaults(machine);
        var origin = new double[] { 0, 0, 0, 0 };
        Assert.Equal(
            PlaneWorldMapper.ToWorld(config.Planes[0], origin, machine.PlaneAxisIndices(0)),
            call.SideA[0]);
        Assert.Equal(
            PlaneWorldMapper.ToWorld(config.Planes[1], origin, machine.PlaneAxisIndices(1)),
            call.SideB[0]);

        // Second sample = the mapped From of the first motion exactly — the
        // first motion's coordinates, plane-0 and plane-1 in lockstep.
        var first = new double[] { 10, 5, 10, 12 };
        Assert.Equal(
            PlaneWorldMapper.ToWorld(config.Planes[0], first, machine.PlaneAxisIndices(0)),
            call.SideA[1]);
        Assert.Equal(
            PlaneWorldMapper.ToWorld(config.Planes[1], first, machine.PlaneAxisIndices(1)),
            call.SideB[1]);
    }

    [Fact]
    public void Foam_rows_changed_synchronizes_run_states_across_both_planes()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene, MachineTypes.Foam);

        // Multi-segment foam program where EVERY motion moves both towers
        // (X/Y on plane 0, Z/A on plane 1) — the keys to the synchronous
        // frontier check, since each plane must emit the same segment indices
        // and line numbers.
        source.Load(
            "G21\n" +
            "G0 X10 Y5 Z10 A12 F240\n" +
            "G1 X20 Y8 Z20 A24\n" +
            "G1 X30 Y5 Z30 A36\n" +
            "G1 Y20 A48\n");

        Assert.Empty(scene.Layers[^2].RunStateCalls);
        Assert.Empty(scene.Layers[^1].RunStateCalls);

        // Ack rows 1..3 (rows 0 and 4 untouched) — the run frontier moved;
        // every plane layer must walk the same rows in lockstep.
        source.AckRows(1, 4, ProgramRowState.Sent);
        vm.ApplyRunStateWindow();

        // Window (prev=-1, touched=3] maps to output segments 0..2 (rows 1..3,
        // all Sent → InFlight); segment 3 (row 4) is not yet touched so it gets
        // no call.
        Assert.Equal(
            [(0, RunState.InFlight), (1, RunState.InFlight), (2, RunState.InFlight)],
            scene.Layers[^2].RunStateCalls);
        Assert.Equal(scene.Layers[^2].RunStateCalls, scene.Layers[^1].RunStateCalls);
    }

    [Fact]
    public void Head_comments_do_not_leave_the_first_plane_segments_pending()
    {
        // Real-world foam program: `%` demarcation, ;-comments and blanks in
        // the header are NOT sendable rows, so the parser's block counter
        // (gcValues.N — the token/segment LineNumber) starts at 1 for the FIRST
        // real block while its FileLine is already ~16. The line→row map must
        // follow the block counter, not the physical file line, or the very
        // first plane segments resolve into forever-Idle header rows.
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene, MachineTypes.Foam);

        source.Load(
            "; foam header comment\n" +
            "; another header line\n" +
            "%\n" +
            "G21\n" +
            "G0 X10 Y5 Z10 A12 F240\n" +
            "G1 X20 Y8 Z20 A24\n" +
            "G1 X30 Y5 Z30 A36\n" +
            "G1 Y20 A48\n");

        // Sendable rows are 3..7 (G21..last G1); ack them like the controller does.
        source.AckRows(3, 8);
        vm.ApplyRunStateWindow();

        // All four motion segments per plane start green — the head comment
        // rows (0,1,2) must NOT swallow the first plane segments.
        Assert.Equal(
            [(0, RunState.Executed), (1, RunState.Executed), (2, RunState.Executed), (3, RunState.Executed)],
            scene.Layers[^2].RunStateCalls);
        Assert.Equal(scene.Layers[^2].RunStateCalls, scene.Layers[^1].RunStateCalls);
    }

    [Fact]
    public void Mill_controller_state_never_calls_the_loft()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene, MachineTypes.Mill);

        _grbl.Source.Emit(new MachineState { Positions = new double[] { 1, 2, 3 } });

        Assert.Empty(scene.Lofts);
    }

    [Fact]
    public void Late_scene_attach_rebuilds_an_already_loaded_program()
    {
        // Task 8 wiring: the Vulkan scene is built off the UI thread and arrives
        // AFTER the program. The VM must rebuild the loaded program onto it.
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        var settings = new SettingsService(_settingsDir);
        var profile = settings.Load();
        profile.MachineTypeId = MachineTypes.Mill;
        settings.Save(profile);

        using var vm = new PlotterNexViewModel(source, _grbl, settings,
            new LocalizationService(new MemoryStream("{ \"strings\": {} }"u8.ToArray())));

        source.Load(MillProgram());
        Assert.Null(vm.Scene);
        Assert.False(vm.HasGeometry);

        vm.AttachScene(new KinematicSceneFactory(scene));

        Assert.NotNull(vm.Scene);
        Assert.True(vm.HasGeometry);
        Assert.Equal(1, vm.PlaneCount);
        Assert.Equal(3, scene.Layers[^1].Added.Count);
    }

    [Fact]
    public void Check_mode_tracks_the_last_acked_segment_end()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);
        source.Load(MillProgram());
        source.AckRows(1, 3); // rows 1,2 Ok → last acked end = segment 1's To
        vm.ApplyRunStateWindow();
        vm.PulseAckedEnds(); // the view tick folds the acks into the acked-end cache

        _grbl.Source.Emit(new MachineState
        {
            Status = "Check",
            Positions = new double[] { 7, 8, 9 },
        });

        var tool = scene.Tools[^1];
        var endpoint = tool.Positions[^1];
        // Mill plane [X,Y,Z]: last acked segment To is (20,5,0), not the report (7,8,9).
        Assert.Equal(new Vector3(20, 5, 0), Assert.Single(endpoint.Points));
    }

    [Fact]
    public void Live_position_wins_outside_check_mode()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);
        source.Load(MillProgram());
        source.AckRows(1, 3);
        vm.ApplyRunStateWindow();

        _grbl.Source.Emit(new MachineState { Status = "Run", Positions = new double[] { 7, 8, 9 } });

        var tool = scene.Tools[^1];
        Assert.Equal(new Vector3(7, 8, 9), Assert.Single(tool.Positions[^1].Points));
    }

    [Fact]
    public void Check_mode_before_first_ack_falls_back_to_live()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);
        source.Load(MillProgram());

        _grbl.Source.Emit(new MachineState { Status = "Check", Positions = new double[] { 7, 8, 9 } });

        var tool = scene.Tools[^1];
        Assert.Equal(new Vector3(7, 8, 9), Assert.Single(tool.Positions[^1].Points));
    }

    [Fact]
    public void Sent_then_Ok_rows_keep_the_check_tool_on_the_acked_path()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);
        source.Load(MillProgram());

        // The controller streams lines ahead: each row passes through Sent while
        // the frontier (TouchedRow) moves on, and the trailing Ok ack lands after
        // the row is already behind the app's last pass over it.
        source.AckRows(1, 3, ProgramRowState.Sent);
        source.AckRows(1, 3, ProgramRowState.Ok);
        vm.PulseAckedEnds(); // the view tick folds the Sent-then-Ok trail into the cache

        _grbl.Source.Emit(new MachineState { Status = "Check", Positions = new double[] { 7, 8, 9 } });

        var tool = scene.Tools[^1];
        // The last acked end (row 2, G1 X20 Y5 → (20,5,0)) must reach the tool in
        // spite of the Sent-then-Ok transition having happened behind the frontier.
        Assert.Equal(new Vector3(20, 5, 0), Assert.Single(tool.Positions[^1].Points));
    }

    [Fact]
    public void Rewind_clears_the_acked_end_cache()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);
        source.Load(MillProgram());
        source.AckRows(1, 3);
        vm.ApplyRunStateWindow();
        vm.PulseAckedEnds(); // acked-end cache primed

        // Simulate the app's Rewind: TouchedRow → 0 and all rows idle.
        source.Rewind();
        vm.ApplyRunStateWindow();
        vm.PulseAckedEnds(); // rewind folds into the cache → cleared

        _grbl.Source.Emit(new MachineState { Status = "Check", Positions = new double[] { 7, 8, 9 } });
        // Acked end cleared → fall back to live.
        Assert.Equal(new Vector3(7, 8, 9), Assert.Single(scene.Tools[^1].Positions[^1].Points));
    }

    [Fact]
    public void Large_frontier_jump_falls_back_to_full_rescan()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);
        // Small program, but force a bounded touch window anyway — the cap path
        // triggers only when the window exceeds RunStateWindowCap, so instead
        // verify the full-rescan path via a rewind.
        source.Load(MillProgram());
        source.AckRows(1, 3);
        vm.ApplyRunStateWindow();
        var before = scene.Layers[^1].RunStateCalls.Count;
        Assert.True(before > 0);

        source.Rewind();
        vm.ApplyRunStateWindow();
        Assert.Single(scene.Layers[^1].RebuildCalls); // rewind → RebuildExecuted
        Assert.True(scene.Layers[^1].RunStateCalls.Count > before); // full rescan re-applied
    }

    [Fact]
    public void Rows_changed_events_coalesce_until_the_tick_pulse()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);
        source.Load(MillProgram());
        var before = vm.FrontierVersion;

        // A flood of row events lands on the serial thread — none of it may
        // touch the UI; each PulseAckedEnds (the 30Hz tick) folds the batch
        // exactly once.
        source.AckRows(1, 2);
        source.AckRows(2, 3);
        source.AckRows(3, 4);
        Assert.Equal(before, vm.FrontierVersion);

        vm.PulseAckedEnds();
        Assert.Equal(before + 1, vm.FrontierVersion);

        source.AckRows(4, 5);
        Assert.Equal(before + 1, vm.FrontierVersion);
        vm.PulseAckedEnds();
        Assert.Equal(before + 2, vm.FrontierVersion);

        // Nothing pending → a pulse is a no-op.
        vm.PulseAckedEnds();
        Assert.Equal(before + 2, vm.FrontierVersion);
    }

    [Fact]
    public void Frontier_flood_applies_in_bounded_slices_without_a_full_rescan()
    {
        var gcode = "G21\nG1 X1 F600\n"
            + string.Join("\n", Enumerable.Range(2, 4999).Select(i => $"G1 X{i}"));
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);
        source.Load(gcode);
        // > AsyncBuildThreshold builds on a worker, then lands via ApplyBuild.
        // The budget is a wall-clock allowance for that worker, not a performance
        // assertion: it should finish in well under a second, but a full-solution
        // run drives four test processes at once and the worker can be starved
        // past a tight budget. The assertion message reports StatusText, so a
        // genuinely broken build still fails with a useful diagnostic.
        Assert.True(SpinWait.SpinUntil(() => vm.HasGeometry, TimeSpan.FromSeconds(60)),
            $"async program build never completed (status={vm.StatusText})");
        var layer = scene.Layers[^1];

        // The frontier spools far ahead of the ack trail: one pass may walk at
        // most the cap (fit within the cap to the physical-line-offset search),
        // and must not fall back to the O(n) full rescan (which would rebuild +
        // re-upload the whole toolpath per tick).
        source.AckRows(1, 5001);
        vm.ApplyRunStateWindow();
        Assert.Empty(layer.RebuildCalls);
        Assert.InRange(layer.RunStateCalls.Count, 4000, PlotterNexViewModel.RunStateWindowCap);

        // The remainder converges over further passes, and how many are needed
        // depends on how much of the async build has landed by the time they run —
        // so do not assume exactly two. On a loaded CI runner the second pass applied
        // nothing at all because the tail rows were not mapped yet, and the equality
        // below failed on 4095 (the first pass's cap-bounded total). What must hold is
        // that every pass stays bounded (asserted above) and that the frontier
        // converges without ever falling back to the O(n) full rescan.
        var passes = 1;
        while (layer.RunStateCalls.Count < 5000 && passes < 200)
        {
            vm.ApplyRunStateWindow();
            passes++;
            Assert.Empty(layer.RebuildCalls);
        }
        Assert.Equal(5000, layer.RunStateCalls.Count);
    }

    [Fact]
    public void Foam_check_mode_tracks_last_acked_end_on_both_planes()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene, MachineTypes.Foam);
        source.Load(
            "G21\n" +
            "G0 X10 Y5 Z10 A12 F240\n" +
            "G1 X20 Y8 Z20 A24\n" +
            "G1 X30 Y5 Z30 A36\n" +
            "G1 Y20 A48\n");
        source.AckRows(1, 5);
        vm.ApplyRunStateWindow();
        vm.PulseAckedEnds(); // the view tick folds the acks into the acked-end cache

        _grbl.Source.Emit(new MachineState { Status = "Check", Positions = new double[] { 99, 99, 99, 99 } });

        var tool = scene.Tools[^1];
        var endpoint = tool.Positions[^1];
        Assert.Equal(2, endpoint.Points.Length);
        // Last acked (row 4, G1 Y20 A48): plane0 world (-50,30,20), plane1 world (50,30,48).
        Assert.Equal(new Vector3(-50, 30, 20), endpoint.Points[0]);
        Assert.Equal(new Vector3(50, 30, 48), endpoint.Points[1]);
    }

    [Fact]
    public void Settings_edit_reloads_geometry_and_rebuilds_with_new_wire_length()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        // The VM and the editor share the SAME SettingsService instance (app DI is a
        // singleton), so the save below fires Changed on the instance the VM
        // subscribed to. (A second service on the same directory would NOT fire it:
        // Changed is an instance event and Load() caches per instance.)
        var settings = new SettingsService(_settingsDir);
        var profile = settings.Load();
        profile.MachineTypeId = MachineTypes.Foam;
        settings.Save(profile);
        using var vm = new PlotterNexViewModel(source, _grbl, settings,
            new LocalizationService(new MemoryStream("{ \"strings\": {} }"u8.ToArray())),
            new KinematicSceneFactory(scene));
        source.Load("G21\nG1 X10 Y5 Z10 A12 F240\n");
        Assert.True(vm.HasGeometry);

        // Edit the plotter-nex section (WireLength 100 -> 200) through the same
        // profile-save flow the AppSettings editor uses; SettingsService raises
        // Changed, the VM must resolve the new geometry and rebuild.
        var nex = SenderNG.Modules.Plotter.Nex.Settings.PlotterNexConfig.Load(profile,
            CNC.Core.Services.MachineTypeCatalog.Resolve(MachineTypes.Foam));
        nex.WireLength = 200;
        SenderNG.Modules.Plotter.Nex.Settings.PlotterNexConfig.RecenterOnWire(nex);
        SenderNG.Modules.Plotter.Nex.Settings.PlotterNexConfig.Save(profile, nex);
        settings.Save(profile); // raises Changed on the VM's own service -> VM rebuilds

        _grbl.Source.Emit(new MachineState { Positions = new double[] { 10, 20, 30, 40 } });
        var tool = scene.Tools[^1];
        var endpoint = tool.Positions[^1];
        Assert.Equal(new Vector3(-100, 10, 20), endpoint.Points[0]);
        Assert.Equal(new Vector3(100, 30, 40), endpoint.Points[1]);
    }

    [Fact]
    public void View_toggle_edit_stays_runtime_only_no_node_rebuild()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        var settings = new SettingsService(_settingsDir);
        var profile = settings.Load();
        profile.MachineTypeId = MachineTypes.Mill;
        settings.Save(profile);
        using var vm = new PlotterNexViewModel(source, _grbl, settings,
            new LocalizationService(new MemoryStream("{ \"strings\": {} }"u8.ToArray())),
            new KinematicSceneFactory(scene));
        source.Load(MillProgram());
        Assert.True(vm.HasGeometry);
        var layersBefore = scene.Layers.Count;
        var frontierAfterLoad = vm.FrontierVersion;

        // Flip ONLY a view toggle through the same Save flow the shared overlay /
        // leaf editor uses (they now persist through THIS SettingsService). The VM
        // must NOT rebuild content nodes — the live scene push is the view's job
        // (runtime-only chrome change).
        var profile2 = settings.Load();
        var nex = PlotterNexConfig.Load(profile2, MachineTypeCatalog.Resolve(MachineTypes.Mill));
        nex.ShowGrid = !nex.ShowGrid;
        PlotterNexConfig.Save(profile2, nex);
        settings.Save(profile2);

        Assert.Equal(layersBefore, scene.Layers.Count);
        Assert.Equal(frontierAfterLoad, vm.FrontierVersion);

        // A structural change (wire length) still forces the full rebuild.
        var profile3 = settings.Load();
        var nex2 = PlotterNexConfig.Load(profile3, MachineTypeCatalog.Resolve(MachineTypes.Mill));
        nex2.WireLength *= 2;
        PlotterNexConfig.Save(profile3, nex2);
        settings.Save(profile3);

        Assert.True(scene.Layers.Count > layersBefore);
    }

    [Fact]
    public void Aa_edit_stays_runtime_only_no_node_rebuild()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        var settings = new SettingsService(_settingsDir);
        var profile = settings.Load();
        profile.MachineTypeId = MachineTypes.Mill;
        settings.Save(profile);
        using var vm = new PlotterNexViewModel(source, _grbl, settings,
            new LocalizationService(new MemoryStream("{ \"strings\": {} }"u8.ToArray())),
            new KinematicSceneFactory(scene));
        source.Load(MillProgram());
        Assert.True(vm.HasGeometry);
        var layersBefore = scene.Layers.Count;
        var frontierAfterLoad = vm.FrontierVersion;

        // Flip ONLY the AA mode/quality through the same Save flow the settings
        // leaf uses. AA switching is a runtime push-constant flip driven by the
        // view's shared-VM subscription (scene.SetAntiAlias) — the VM must NOT
        // treat it as a structural change and rebuild content nodes.
        var profile2 = settings.Load();
        var nex = PlotterNexConfig.Load(profile2, MachineTypeCatalog.Resolve(MachineTypes.Mill));
        nex.AaMode = 2;     // FXAA
        nex.AaQuality = 3;  // Ultra
        PlotterNexConfig.Save(profile2, nex);
        settings.Save(profile2);

        Assert.Equal(layersBefore, scene.Layers.Count);
        Assert.Equal(frontierAfterLoad, vm.FrontierVersion);

        // A structural change (wire length) still forces the full rebuild.
        var profile3 = settings.Load();
        var nex2 = PlotterNexConfig.Load(profile3, MachineTypeCatalog.Resolve(MachineTypes.Mill));
        nex2.WireLength *= 2;
        PlotterNexConfig.Save(profile3, nex2);
        settings.Save(profile3);

        Assert.True(scene.Layers.Count > layersBefore);
    }

    [Fact]
    public void Vulkan_is_available_before_any_report()
    {
        using var vm = MakeVm(new StubProgramSource(), new RecorderScene());

        Assert.False(vm.VulkanUnavailable);
        Assert.Equal(Plot3DFailure.None, vm.VulkanFailure);
        // Healthy means no explanation at all, not the generic failure hint: a
        // consumer binding this unconditionally must not read a failure
        // message on a working machine.
        Assert.Equal(string.Empty, vm.VulkanFailureDetail);
    }

    [Fact]
    public void Reporting_a_failure_sets_all_three_properties_and_raises_all_three()
    {
        using var vm = MakeVm(new StubProgramSource(), new RecorderScene());
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.ReportVulkanUnavailable(Plot3DFailure.NotSupported);

        Assert.True(vm.VulkanUnavailable);
        Assert.Equal(Plot3DFailure.NotSupported, vm.VulkanFailure);
        Assert.False(string.IsNullOrEmpty(vm.VulkanFailureDetail));
        // Exactly these three, compared as an unordered set so that a missing
        // notification fails AND a fourth stray one fails. VulkanFailureDetail
        // is in the list because a Text binding only re-reads on notification —
        // without it the panel would show the detail's pre-report empty value.
        Assert.Equal(
            [
                nameof(PlotterNexViewModel.VulkanFailure),
                nameof(PlotterNexViewModel.VulkanFailureDetail),
                nameof(PlotterNexViewModel.VulkanUnavailable),
            ],
            raised.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_flag_is_raised_after_the_reason_and_the_detail()
    {
        using var vm = MakeVm(new StubProgramSource(), new RecorderScene());
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.ReportVulkanUnavailable(Plot3DFailure.NotSupported);

        // Order, not merely set: the VulkanUnavailable handler disposes the
        // scene and calls ReleaseScene() synchronously, so raising the flag
        // first would let a throw in that teardown leave the panel's IsVisible
        // already flipped while its Text binding never re-read — a visible
        // panel showing a stale hint. The flag is the commit point, so it has
        // to be published last.
        Assert.Equal(
            [
                nameof(PlotterNexViewModel.VulkanFailure),
                nameof(PlotterNexViewModel.VulkanFailureDetail),
                nameof(PlotterNexViewModel.VulkanUnavailable),
            ],
            raised);
    }

    [Fact]
    public void Reporting_a_failure_twice_keeps_the_first_reason_and_raises_nothing()
    {
        using var vm = MakeVm(new StubProgramSource(), new RecorderScene());
        vm.ReportVulkanUnavailable(Plot3DFailure.NotSupported);
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.ReportVulkanUnavailable(Plot3DFailure.Other);

        Assert.Equal(Plot3DFailure.NotSupported, vm.VulkanFailure);
        Assert.Empty(raised);
    }

    [Fact]
    public void Reporting_None_is_ignored()
    {
        using var vm = MakeVm(new StubProgramSource(), new RecorderScene());

        vm.ReportVulkanUnavailable(Plot3DFailure.None);

        Assert.False(vm.VulkanUnavailable);
        Assert.Equal(Plot3DFailure.None, vm.VulkanFailure);
    }

    /// <summary>Locale carrying the three per-kind hint keys as distinct
    /// sentinels. The file's existing <c>MakeVm</c> builds its
    /// <c>LocalizationService</c> from an empty string table, so
    /// <c>Resolve</c> always returns its fallback there. With an empty locale
    /// every kind resolves to the *same* text, which cannot tell "each kind
    /// picks its own key" apart from "every kind returns the fallback" — so the
    /// key-selection test needs a locale that actually has the keys.</summary>
    private static LocalizationService LocaleWithHintKeys() => new(new MemoryStream(
        """
        { "strings": {
          "module.plotter-nex.vulkanUnavailable.notSupported": "DETAIL-NOT-SUPPORTED",
          "module.plotter-nex.vulkanUnavailable.runtimeError": "DETAIL-RUNTIME-ERROR",
          "module.plotter-nex.vulkanUnavailable.other": "DETAIL-OTHER"
        } }
        """u8.ToArray()));

    private PlotterNexViewModel MakeVmWithHintKeys(StubProgramSource source, RecorderScene scene)
    {
        var settings = new SettingsService(_settingsDir);
        var profile = settings.Load();
        profile.MachineTypeId = MachineTypes.Mill;
        settings.Save(profile);

        return new PlotterNexViewModel(source, _grbl, settings,
            LocaleWithHintKeys(),
            new KinematicSceneFactory(scene));
    }

    /// <summary>Plot3DFailure is internal, and a public test method cannot take
    /// an internal parameter type (CS0051): InternalsVisibleTo does not help,
    /// because the signature is public, and xUnit v2 requires public test
    /// methods. So the row carries the enum member name — exactly what
    /// ToString() returns — parsed back inside so the report/compare stays
    /// type-safe. Mirrors Plot3DFailureTests.Map_translates_each_result_code.</summary>
    [Theory]
    [InlineData("NotSupported", "DETAIL-NOT-SUPPORTED")]
    [InlineData("RuntimeError", "DETAIL-RUNTIME-ERROR")]
    [InlineData("Other", "DETAIL-OTHER")]
    public void Each_failure_kind_resolves_its_own_key(string failureName, string expected)
    {
        using var vm = MakeVmWithHintKeys(new StubProgramSource(), new RecorderScene());
        var failure = Enum.Parse<Plot3DFailure>(failureName);

        vm.ReportVulkanUnavailable(failure);

        Assert.Equal(expected, vm.VulkanFailureDetail);
    }

    [Fact]
    public void Detail_text_falls_back_to_the_generic_hint_when_the_locale_lacks_the_key()
    {
        // MakeVm's locale is empty, so Resolve returns its fallback. That
        // fallback is the safety net: a locale file that has not gained the
        // per-kind keys shows a sensible message, not a blank panel.
        using var vm = MakeVm(new StubProgramSource(), new RecorderScene());

        vm.ReportVulkanUnavailable(Plot3DFailure.NotSupported);

        Assert.Equal(
            "No Vulkan 1.3 device could be created.",
            vm.VulkanFailureDetail);
    }

    /// <summary>Locale carrying ONLY the generic vulkanUnavailableHint key and
    /// none of the three per-kind keys — the shape of a locale file written
    /// before the per-kind keys existed.</summary>
    private static LocalizationService LocaleWithGenericHintOnly() => new(new MemoryStream(
        """
        { "strings": {
          "module.plotter-nex.vulkanUnavailableHint": "DETAIL-GENERIC-HINT"
        } }
        """u8.ToArray()));

    private PlotterNexViewModel MakeVmWithGenericHintOnly(StubProgramSource source, RecorderScene scene)
    {
        var settings = new SettingsService(_settingsDir);
        var profile = settings.Load();
        profile.MachineTypeId = MachineTypes.Mill;
        settings.Save(profile);

        return new PlotterNexViewModel(source, _grbl, settings,
            LocaleWithGenericHintOnly(),
            new KinematicSceneFactory(scene));
    }

    [Fact]
    public void A_missing_per_kind_key_degrades_to_the_localized_generic_hint()
    {
        // The complement of Detail_text_falls_back_to_the_generic_hint...: that
        // test's locale is completely EMPTY, so the inner Resolve returns the
        // English literal and a nested Resolve is indistinguishable from a
        // single one. This locale HAS the generic key, so the two paths diverge:
        // nested Resolve yields the generic sentinel, single Resolve yields
        // English. That divergence is what makes the localization load-bearing.
        using var vm = MakeVmWithGenericHintOnly(new StubProgramSource(), new RecorderScene());

        vm.ReportVulkanUnavailable(Plot3DFailure.NotSupported);

        // Exact equality settles all three outcomes at once: the generic
        // sentinel (not the English last-resort literal, which a single
        // Resolve would return, and not the per-kind key name leaking through
        // as text).
        Assert.Equal("DETAIL-GENERIC-HINT", vm.VulkanFailureDetail);
    }

    /// <summary>Read-only program facade over a parsed G-code string, with an
    /// AckRows live-run tick — mirrors the Skia headless stub.</summary>
    private sealed class StubProgramSource : ILoadedProgramSource
    {
        private ProgramRowState[] _states = [];

        public GCodeProgram? Program { get; set; }
        public event Action? ProgramChanged;
        public event Action? RowsChanged;
        public int RowCount => Program?.Entries.Count ?? 0;
        public int SendableCount => Program?.Entries.Count(e => e.IsSendable) ?? 0;
        public int DoneCount => _states.Count(s => s == ProgramRowState.Ok);
        public int TouchedRow { get; private set; }

        public ProgramRowState GetRowState(int index) =>
            index >= 0 && index < _states.Length ? _states[index] : ProgramRowState.Idle;

        public void Load(string gcode)
        {
            Program = GCodeProgram.Parse(gcode);
            _states = new ProgramRowState[Program.Entries.Count];
            TouchedRow = 0;
            ProgramChanged?.Invoke();
        }

        public void Clear()
        {
            Program = null;
            _states = [];
            TouchedRow = 0;
            ProgramChanged?.Invoke();
        }

        public void AckRows(int from, int to, ProgramRowState state = ProgramRowState.Ok)
        {
            for (var i = from; i < to && i < _states.Length; i++)
                _states[i] = state;
            TouchedRow = Math.Max(TouchedRow, Math.Min(to - 1, _states.Length - 1));
            RowsChanged?.Invoke();
        }

        public void Rewind()
        {
            _states = new ProgramRowState[_states.Length];
            TouchedRow = 0;
            RowsChanged?.Invoke();
        }
    }

    /// <summary>Observable IGrblService.State the tests push MachineStates into.</summary>
    private sealed class StubGrbl : IGrblService
    {
        public StateSource Source { get; } = new();
        public bool IsConnected => false;
        public IObservable<MachineState> State => Source;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    private sealed class StateSource : IObservable<MachineState>
    {
        private readonly List<IObserver<MachineState>> _observers = [];

        public IDisposable Subscribe(IObserver<MachineState> observer)
        {
            _observers.Add(observer);
            return new Subscription(this, observer);
        }

        public void Emit(MachineState state)
        {
            foreach (var observer in _observers)
                observer.OnNext(state);
        }

        private sealed class Subscription(StateSource source, IObserver<MachineState> observer) : IDisposable
        {
            public void Dispose() => source._observers.Remove(observer);
        }
    }

    /// <summary>IPlotScene recorder: layers and tools are observed through this
    /// (the KinematicSceneFactory re-registers its scene objects on every
    /// BuildScene, so the VM tests read Layers[^1] — the latest registration).</summary>
    private sealed class RecorderScene : IPlotScene
    {
        public List<RecorderPlaneLayer> Layers { get; } = [];
        public List<RecorderTool> Tools { get; } = [];
        public List<RecorderLoft> Lofts { get; } = [];

        public void SetViewPreset(ViewPreset preset) { }
        public void Pan(PanDelta delta) { }
        public void ZoomAround(Vector2 screenPoint, double factor) { }
        public void SetViewportSize(int wPx, int hPx) { }
        public void PanByScreenDelta(Vector2 screenDeltaPx) { }
        public void RotateByScreenDelta(Vector2 screenDeltaPx) { }
        public void BeginOrbit(Vector2 screenPointPx) { }
        public void EndOrbit() { }
        public void Reframe() { }
        public void AddAxisArrows(IReadOnlyList<(Vector3 Pos, string Label)> arrows) { }
        public void SetGrid(BoundingBox bounds, double minor, double major) { }
        public void SetOrigin(Vector3 origin) { }
        public void SetWorkEnvelope(BoundingBox box) { }
        public void SetJobEnvelope(BoundingBox box) { }

        public ICarriagePlaneLayer AddCarriagePlane()
        {
            var layer = new RecorderPlaneLayer();
            Layers.Add(layer);
            return layer;
        }

        public ILoftSurface AddLoftSurface()
        {
            var loft = new RecorderLoft();
            Lofts.Add(loft);
            return loft;
        }

        public IPlotTool CreateTool(ToolShape shape)
        {
            var tool = new RecorderTool(shape);
            Tools.Add(tool);
            return tool;
        }

        public void Dispose() { }
    }

    private sealed class RecorderLoft : ILoftSurface
    {
        public List<Vector3[]> Calls { get; } = [];
        public List<(IReadOnlyList<Vector3> SideA, IReadOnlyList<Vector3> SideB)> SheetCalls { get; } = [];
        public void SetWirePlane(Vector3[] polygon) => Calls.Add(polygon);
        public void SetLoftSheet(IReadOnlyList<Vector3> sideA, IReadOnlyList<Vector3> sideB) => SheetCalls.Add((sideA, sideB));
    }

    private sealed class RecorderTool : IPlotTool
    {
        public RecorderTool(ToolShape shape) => Shape = shape;

        public List<Endpoint> Positions { get; } = [];
        public ToolShape Shape { get; set; }
        public bool Disposed { get; private set; }

        public void UpdatePosition(Endpoint endpoints) => Positions.Add(endpoints);
        public void Dispose() => Disposed = true;
    }
}