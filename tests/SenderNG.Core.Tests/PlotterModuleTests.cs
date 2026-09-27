/*
 * PlotterModuleTests.cs
 *
 * The plotter ships as a true external plugin (own DLL, Modules/ scan). These
 * tests pin the module contract (id, panel hint, locale keys, plugin metadata)
 * the same way ConverterTests do, the ScanDirectory discovery of the staged
 * DLL, and the view model's plane slicing (foam two-carriage decomposition,
 * letter resolution, frontier mapping) against stubs.
 */

using System.Text;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Modules.Fixed;
using SenderNG.Modules.Plotter;

namespace SenderNG.Core.Tests;

public class PlotterModuleTests
{
    [Fact]
    public void Module_exposes_contract_identity()
    {
        var module = new PlotterModule();

        Assert.Equal("plotter", module.Id);
        Assert.Equal(PanelHint.Center, module.Panel);
        Assert.True(module.IsVisibleByDefault);
        Assert.True(module.CanHide);
        Assert.Equal(22, module.Order);
        Assert.Empty(module.HotKeys);
    }

    [Fact]
    public void Module_declares_locale_keys_with_english_fallbacks()
    {
        var module = new PlotterModule();

        Assert.Equal("Plot", module.LocalizationKeys["module.plotter.name"]);
        Assert.Equal("Plot", module.Name);
        Assert.False(string.IsNullOrWhiteSpace(module.Description));
    }

    [Fact]
    public void Module_carries_plugin_metadata()
    {
        var module = new PlotterModule();

        Assert.False(string.IsNullOrWhiteSpace(module.Version));
        Assert.False(string.IsNullOrWhiteSpace(module.Author));
        Assert.False(string.IsNullOrWhiteSpace(module.License));
    }

    [Fact]
    public void ScanDirectory_discovers_the_staged_plotter_dll()
    {
        // The plugin DLL is staged into this project's OutputDir/Modules (same
        // Link trick the app csproj uses) — the real drop-in path.
        var dir = Path.Combine(AppContext.BaseDirectory, "Modules");
        Assert.True(Directory.Exists(dir), "plugin staging missing — rebuild the test project: " + dir);

        var sc = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        ModuleCatalog.ScanDirectory(sc, dir);


        // LoadFrom context: match by full name, not compile-time type identity.
        Assert.Contains(sc, d => d.ServiceType.FullName == "SenderNG.Modules.Plotter.PlotterModule");

        using var sp = sc.BuildServiceProvider();
        var modules = sp.GetServices<IModule>().ToList();
        Assert.Contains(modules, m => m.Id == "plotter");
    }

    // ---- View-model slicing ----

    private sealed class StubProgramSource : ILoadedProgramSource
    {
        public CNC.Core.Services.GCodeProgram? Program { get; set; }
        public event System.Action? ProgramChanged;
#pragma warning disable CS0067 // Test stub: RowsChanged stays unraised.
        public event System.Action? RowsChanged;
#pragma warning restore CS0067
        public int RowCount => Program?.Entries.Count ?? 0;
        public int SendableCount => Program?.Entries.Count(e => e.IsSendable) ?? 0;
        public int DoneCount => 0;
        public int TouchedRow { get; private set; }
        public ProgramRowState GetRowState(int index) => ProgramRowState.Idle;

        public void Load(string gcode)
        {
            Program = CNC.Core.Services.GCodeProgram.Parse(gcode);
            TouchedRow = 0;
            ProgramChanged?.Invoke();
        }
    }

    private sealed class StubGrbl : IGrblService
    {
        // Minimal stub in the SpyGrbl pattern: only the members without default
        // implementations; Capabilities is overridden to report the letters.
        private readonly string _letters;

        public StubGrbl(string letters = "XYZA") => _letters = letters;

        public bool IsConnected => false;
        public ControllerCapabilities Capabilities => ControllerCapabilities.Full with { AxisLetters = _letters };
        public IObservable<MachineState> State => System.Reactive.Linq.Observable.Never<MachineState>();
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    private static (PlotterViewModel Vm, StubProgramSource Source) MakeVm(
        string machineTypeId = MachineTypes.Mill, string axisLetters = "XYZA")
    {
        var source = new StubProgramSource();
        return (MakeVmAround(source, machineTypeId, axisLetters), source);
    }

    /// <summary>Builds a plotter view model around a caller-supplied program
    /// surface — the live-overlay tests drive the real app <see cref="ProgramViewModel"/>
    /// (the ILoadedProgramSource implementation) instead of the stub.</summary>
    private static PlotterViewModel MakeVmAround(
        ILoadedProgramSource source,
        string machineTypeId = MachineTypes.Mill,
        string axisLetters = "XYZA",
        IGrblService? grbl = null)
    {
        var settingsDir = Path.Combine(Path.GetTempPath(), "plotter-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(settingsDir);
        var settings = new SettingsService(settingsDir);
        var profile = settings.Load();
        profile.MachineTypeId = machineTypeId;
        settings.Save(profile);

        return new PlotterViewModel(source, grbl ?? new StubGrbl(axisLetters), settings,
            new LocalizationService(new MemoryStream("{ \"strings\": {} }"u8.ToArray())));
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("condition not met in time");
            await Task.Delay(20);
        }
    }

    [Fact]
    public void Mill_program_slices_into_one_plane()
    {
        var (vm, source) = MakeVm();
        source.Load("G0 X5 Y5\nG1 X15 Y5 F100\nG1 X15 Y15");

        Assert.Equal(1, vm.PlaneCount);
        var plane = Assert.Single(vm.Planes);
        Assert.True(plane.Count > 0);
        // Letters come from the stub controller (XYZA).
        Assert.Equal("XYZ", vm.PlaneLetters[0]);
    }

    [Fact]
    public void Foam_program_slices_into_two_carriage_planes()
    {
        var (vm, source) = MakeVm(MachineTypes.Foam);
        source.Load("G1 X10 Y2 Z8 A2 F100");

        Assert.Equal(2, vm.PlaneCount);
        Assert.Equal(2, vm.Planes.Count);

        var p0 = vm.Planes[0];
        var p1 = vm.Planes[1];
        var seg0 = Assert.Single(p0);
        var seg1 = Assert.Single(p1);
        // Plane 0 slice (X,Y): 0->10, 0->2. Plane 1 slice (Z,A): 0->8, 0->2.
        Assert.Equal(10, seg0.To[0]);
        Assert.Equal(2, seg0.To[1]);
        Assert.Equal(8, seg1.To[0]);
        Assert.Equal(2, seg1.To[1]);
        // Letters name the WORLD axes each plane feeds, not its machine roles:
        // identity maps both foam planes' roles onto world X and Y, so the
        // second carriage reads "XY" even though its roles are Z/A.
        Assert.Equal("XY", vm.PlaneLetters[0]);
        Assert.Equal("XY", vm.PlaneLetters[1]);
    }

    [Fact]
    public void PlaneAxisIndices_expose_the_schema_slices()
    {
        var (vm, _) = MakeVm(MachineTypes.Foam);
        Assert.Equal([0, 1], vm.PlaneAxisIndices(0));
        Assert.Equal([2, 3], vm.PlaneAxisIndices(1));
    }

    [Fact]
    public void Clearing_the_program_empties_the_planes()
    {
        var (vm, source) = MakeVm();
        source.Load("G1 X10 F100");
        Assert.True(vm.Planes[0].Count > 0);

        // ClearProgram flows through the app's ProgramViewModel; the stub
        // mirrors it with a fresh Load of empty text (same reset path).
        source.Load("");
        Assert.Empty(vm.Planes.SelectMany(p => p));
    }

    [Fact]
    public void Frontier_maps_file_lines_to_rows()
    {
        var (vm, source) = MakeVm();
        source.Load("G0 X5\nG1 X15 F100");

        // Segment lines map to their rows (row index == entry index here).
        Assert.Equal(0, vm.RowForLine(1));
        Assert.Equal(1, vm.RowForLine(2));
        Assert.Equal(-1, vm.RowForLine(999));
    }

    [Fact]
    public void Frontier_follows_the_parser_block_counter_not_the_file_line()
    {
        // Header ;/% rows never reach the parser's block counter, so the first
        // real block carries line 1 while its FileLine is already 4 — the row
        // map must key by the block counter, or the head segments resolve into
        // forever-Idle header rows (the device foam run's uncoloured start).
        var (vm, source) = MakeVm(MachineTypes.Foam);
        source.Load("; header\n; header2\n%\nG21\nG1 X1 Y2 Z8 A4 F100\nG1 X2 Y3 Z9 A5");

        // G21 is block 1 -> row 3; the two motions are blocks 2,3 -> rows 4,5.
        Assert.Equal(3, vm.RowForLine(1));
        Assert.Equal(4, vm.RowForLine(2));
        Assert.Equal(5, vm.RowForLine(3));
        Assert.Equal(-1, vm.RowForLine(999));
    }

    // ---- Degenerate input: no program / no motion / axis mismatch ----

    [Fact]
    public void No_program_yields_empty_geometry_and_status_not_a_crash()
    {
        var (vm, _) = MakeVm();

        // The layout is still known (one plane), but there is nothing to draw
        // and the status explains why — the old behaviour threw here.
        Assert.Equal(1, vm.PlaneCount);
        Assert.Empty(vm.Planes);
        Assert.False(vm.HasGeometry);
        Assert.Equal("No program loaded.", vm.StatusText);
        Assert.Empty(vm.SourceSegments);
    }

    [Fact]
    public void Program_without_motion_yields_no_motion_status()
    {
        var (vm, source) = MakeVm();
        source.Load("F100");

        Assert.False(vm.HasGeometry);
        Assert.Equal("Program contains no motion.", vm.StatusText);
    }

    [Fact]
    public void Foam_machine_with_three_axis_program_renders_plane0_and_plane1_stays_empty()
    {
        // Segments are sized to the emulator's full axis range (X..W), never
        // the connected board's count — a 3-axis PROGRAM simply has no A data.
        var (vm, source) = MakeVm(MachineTypes.Foam, axisLetters: "XYZ");
        source.Load("G1 X10 Y2 Z3 F100");

        // Carriage 1 (X,Y = indices 0,1) renders the X/Y motion; carriage 2
        // (Z,A = indices 2,3) renders the Z motion only (A absent) — silently,
        // since the program itself is not axis-deficient.
        Assert.Equal(2, vm.PlaneCount);
        Assert.Equal(2, vm.Planes.Count);
        Assert.True(vm.Planes[0].Count > 0);
        Assert.Single(vm.Planes[1]); // the Z leg (A static)
        Assert.True(vm.HasGeometry);
        Assert.Null(vm.StatusText);
    }

    [Fact]
    public void Four_axis_foam_program_keeps_its_a_axis_on_a_three_axis_board()
    {
        // Regression guard for the wing report: a 4-axis foam file viewed while
        // the session's controller reports only 3 axes must NOT lose its A data
        // (the old board-driven slot count dropped it, emptying the ZA plane and
        // degrading the iso to a mill-style vertical render).
        var (vm, source) = MakeVm(MachineTypes.Foam, axisLetters: "XYZ");
        source.Load("G1 X10 Y2 Z3 A4 F100");

        // Both carriages move (A moves on plane 1) and both render — the
        // A axis data survives at index 3 (emulator-wide slots).
        Assert.Equal(2, vm.Planes.Count);
        Assert.True(vm.Planes[0].Count > 0);
        Assert.True(vm.Planes[1].Count > 0);
        Assert.True(vm.HasGeometry);
        Assert.Null(vm.StatusText);
        Assert.Equal(9, vm.Bounds.Length);
        Assert.True(vm.Bounds[3][1] > 0, "the A axis must carry its range");
    }

    [Fact]
    public void Mill_machine_with_two_axis_program_renders_the_xy_path()
    {
        var (vm, source) = MakeVm(MachineTypes.Mill, axisLetters: "XY");
        source.Load("G1 X10 Y2 F100");

        // The mill's plane (X,Y,Z) has motion in X/Y regardless of the
        // board's reported count — an XY-only file must still render.
        Assert.True(vm.HasGeometry);
        Assert.True(vm.Planes[0].Count > 0);
        Assert.Null(vm.StatusText);
    }

    // ---- Async build (large programs) ----

    [Fact]
    public async Task Large_program_builds_off_the_ui_thread_and_applies()
    {
        var (vm, source) = MakeVm();
        var sb = new StringBuilder("G0 X1 Y1\n");
        for (var i = 2; i <= 2500; i++)
            sb.Append("G0 X").Append(i % 11).Append(" Y").Append(i % 7).Append('\n');
        // ~2500 lines x several tokens each: comfortably past the threshold.
        source.Load(sb.ToString());

        // While the background build runs, the canvas must NOT keep the stale
        // "No program loaded." notice — it says what is actually happening.
        // (The build is fast on this size, but the status is set synchronously
        // inside the rebuild, so the assert races deterministically on the
        // UI-thread inline path.)
        if (!vm.HasGeometry)
            Assert.Equal("Plotting…", vm.StatusText);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (vm.Planes.Count == 0 || vm.Planes[0].Count == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "async build did not complete");
            await Task.Delay(10);
        }
        Assert.Null(vm.StatusText);
        Assert.True(vm.Planes[0].Count > 0);
    }

    [Fact]
    public async Task Rapid_program_switch_discards_the_stale_async_build()
    {
        var (vm, source) = MakeVm();
        var sb = new StringBuilder("G0 X1 Y1\n");
        for (var i = 2; i <= 2500; i++)
            sb.Append("G0 X").Append(i % 11).Append(" Y").Append(i % 7).Append('\n');
        source.Load(sb.ToString());    // kicks off the async build
        source.Load("G1 X5 F100");    // small program applies synchronously

        // The final geometry must be the small program's (one feed segment) —
        // the stale async result is discarded by the generation guard even if
        // it lands afterwards.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (vm.Planes.Count == 0 || vm.Planes[0].Count != 1)
        {
            Assert.True(DateTime.UtcNow < deadline, "final build did not complete");
            await Task.Delay(10);
        }
        Assert.Null(vm.StatusText);
    }

    // ---- Isometric projection (pure math) ----

    [Fact]
    public void Isometric_projection_raises_z_and_splits_the_floor_diagonal()
    {
        var origin = PlotterProjection.Project(0, 0, 0);
        var up = PlotterProjection.Project(0, 0, 10);
        var px = PlotterProjection.Project(10, 0, 0);
        var py = PlotterProjection.Project(0, 10, 0);

        // +Z increases the projected Y — the canvases' Y-flip then shows it
        // UP on screen (M4 orientation fix: the old minus sign put Z down,
        // rendering the mill iso upside-down and the foam loft's carriage 2
        // BELOW carriage 1 — the reported vertical-wire view).
        Assert.True(up.Y > origin.Y);
        // X and Y run along opposite diagonals of the floor plane.
        Assert.True(px.X > origin.X);
        Assert.True(py.X < origin.X);
        // Isometric: equal floor steps project to equal screen lengths.
        var dx = px.X - origin.X; var dy = px.Y - origin.Y;
        var ex = py.X - origin.X; var ey = py.Y - origin.Y;
        Assert.True(Math.Abs(Math.Sqrt(dx * dx + dy * dy) - Math.Sqrt(ex * ex + ey * ey)) < 1e-9);
    }

    /// <summary>M7 layout decision for the wing file: the foam iso is "2D + 
    /// loft" — both carriage planes draw FLAT (each in its own quadrant with
    /// the semi-transparent coupling mesh between them), so the wing's chord
    /// (X), span (Z) and thickness (A) all stay in their plane slices instead
    /// of being re-projected into a stacked loft. This pins the per-plane
    /// geometry the flat iso renders: plane 0 carries the chord sweep at the
    /// constant tower-1 vertical, plane 1 carries span + thickness.</summary>
    [Fact]
    public void Foam_planes_keep_wing_span_chord_and_thickness_flat_per_carriage()
    {
        var (vm, source) = MakeVm(MachineTypes.Foam);
        var sb = new System.Text.StringBuilder("G21\n");
        for (var i = 0; i <= 400; i++)
        {
            var t = i / 400d;
            var z = 279 * t;
            var x = 55 + 50 * Math.Sin(t * Math.PI * 4);
            var a = 13 + 12 * Math.Sin(t * Math.PI * 4);
            sb.AppendLine($"G1 X{x:F3} Y27 Z{z:F3} A{a:F3} F240");
        }
        source.Load(sb.ToString());

        var p0 = vm.Planes[0]; // (X, Y)
        var p1 = vm.Planes[1]; // (Z, A)
        Assert.True(p0.Count > 0 && p1.Count > 0);

        // Plane 0: the chord sweeps 5..105 while Y stays at the tower-1
        // vertical (27) — the flat wing's chord band.
        double minX = double.MaxValue, maxX = double.MinValue, y = double.NaN;
        foreach (var s in p0)
        {
            minX = Math.Min(minX, Math.Min(s.From[0], s.To[0]));
            maxX = Math.Max(maxX, Math.Max(s.From[0], s.To[0]));
            y = s.From[1];
        }
        Assert.True(minX < 6 && maxX > 104, $"chord sweep must survive the slice: [{minX:F1}, {maxX:F1}]");
        Assert.True(Math.Abs(y - 27) < 1e-9, "plane 0's vertical must stay constant");

        // Plane 1: span sweeps 0..279 along Z, thickness rides A (0..26) —
        // both FLAT inside the carriage plane, nothing stacked.
        double minZ = double.MaxValue, maxZ = double.MinValue, minA = double.MaxValue, maxA = double.MinValue;
        foreach (var s in p1)
        {
            minZ = Math.Min(minZ, Math.Min(s.From[0], s.To[0]));
            maxZ = Math.Max(maxZ, Math.Max(s.From[0], s.To[0]));
            minA = Math.Min(minA, Math.Min(s.From[1], s.To[1]));
            maxA = Math.Max(maxA, Math.Max(s.From[1], s.To[1]));
        }
        Assert.True(minZ < 1 && maxZ > 278, $"span sweep must survive the slice: [{minZ:F1}, {maxZ:F1}]");
        Assert.True(minA < 1 && maxA > 24, $"thickness must survive the slice: [{minA:F1}, {maxA:F1}]");
    }

    // ---- M3: live overlay (real ProgramViewModel + scripted/demo services) ----

    [Fact]
    public async Task Demo_run_drives_the_frontier_progress_and_live_position()
    {
        var settingsDir = Path.Combine(Path.GetTempPath(), "plotter-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(settingsDir);
        var settings = new SettingsService(settingsDir);
        var profile = settings.Load();
        profile.MachineTypeId = MachineTypes.Mill;
        settings.Save(profile);

        var demo = new DemoGrblService(profile, Path.Combine(settingsDir, "demo-settings.txt"));
        try
        {
            await demo.ConnectAsync("demo");
            var source = new ProgramViewModel(demo);
            var vm = MakeVmAround(source, grbl: demo);

            // 6 rows, 4 sendable blocks (comments stay in the grid, never streamed).
            source.LoadGCode("G21\n; heading\nG0 X10 Y10\nG1 X20 Y20 F100\n; tail\nM5");
            Assert.True(vm.HasGeometry);

            await source.RunAsync();

            // Frontier: every sendable row is acknowledged; comments stay idle.
            Assert.Equal(ProgramRowState.Ok, vm.RowState(0));
            Assert.Equal(ProgramRowState.Idle, vm.RowState(1)); // comment
            Assert.Equal(ProgramRowState.Ok, vm.RowState(2));
            Assert.Equal(ProgramRowState.Ok, vm.RowState(3));
            Assert.Equal(ProgramRowState.Idle, vm.RowState(4)); // comment
            Assert.Equal(ProgramRowState.Ok, vm.RowState(5));

            // The counter agrees with the program view's n/n over SENDABLE blocks
            // — a commented file must read 4/4, not 4/6.
            Assert.Equal("4/4", vm.ProgressText);

            // The tool glyph's data: the live position ended at the program's
            // last commanded point (the demo applies motion immediately).
            Assert.NotNull(vm.LivePosition);
            Assert.Equal(20, vm.LivePosition![0], 6);
            Assert.Equal(20, vm.LivePosition![1], 6);

            // Rewind resets the frontier (rows → idle) without rebuilding geometry.
            var versionBefore = vm.GeometryVersion;
            source.Rewind();
            Assert.Equal("0/4", vm.ProgressText);
            ILoadedProgramSource rows = source;
            Assert.All(Enumerable.Range(0, rows.RowCount),
                i => Assert.Equal(ProgramRowState.Idle, vm.RowState(i)));
            Assert.Equal(versionBefore, vm.GeometryVersion);
        }
        finally
        {
            demo.Dispose();
        }
    }

    [Fact]
    public async Task Frontier_colors_the_acked_prefix_and_parks_at_the_inflight_line()
    {
        var svc = new ScriptedGrblService();
        var gate1 = svc.NextGate();
        var gate2 = svc.NextGate();
        var source = new ProgramViewModel(svc);
        var vm = MakeVmAround(source, grbl: svc);
        source.LoadGCode("G21\nG0 X10\nG1 X20 F100\nM5");

        var run = source.RunAsync();
        try
        {
            // Line 1 in flight: the frontier is exactly the sent line.
            await WaitForAsync(() => vm.RowState(0) == ProgramRowState.Sent);
            gate1.SetResult(); // line 1 acks, line 2 goes in flight
            await WaitForAsync(() => vm.RowState(1) == ProgramRowState.Sent);

            // Extent semantics: exactly the acked prefix is done, one line is
            // in flight, everything ahead is untouched.
            Assert.Equal(ProgramRowState.Ok, vm.RowState(0));
            Assert.Equal(ProgramRowState.Sent, vm.RowState(1));
            Assert.Equal(ProgramRowState.Idle, vm.RowState(2));
            Assert.Equal(ProgramRowState.Idle, vm.RowState(3));

            gate2.SetResult();
            await run;
            Assert.All(Enumerable.Range(0, 4),
                i => Assert.Equal(ProgramRowState.Ok, vm.RowState(i)));
        }
        finally
        {
            gate1.TrySetResult();
            gate2.TrySetResult();
        }
    }

    [Fact]
    public async Task Stopping_mid_run_keeps_the_acked_prefix_and_resets_the_inflight_row()
    {
        var svc = new ScriptedGrblService();
        var gate1 = svc.NextGate();
        var gate2 = svc.NextGate();
        var source = new ProgramViewModel(svc);
        var vm = MakeVmAround(source, grbl: svc);
        source.LoadGCode("G21\nG0 X10\nG1 X20 F100\nM5");

        var run = source.RunAsync();
        try
        {
            await WaitForAsync(() => vm.RowState(0) == ProgramRowState.Sent);
            gate1.SetResult();
            await WaitForAsync(() => vm.RowState(1) == ProgramRowState.Sent);

            await source.StopAsync();
            await run; // Finalize runs in RunAsync's continuation

            // Acknowledged rows stay done; the in-flight row returns to idle —
            // no phantom amber after a stop.
            Assert.Equal(ProgramRowState.Ok, vm.RowState(0));
            Assert.Equal(ProgramRowState.Idle, vm.RowState(1));
            Assert.Equal(ProgramRowState.Idle, vm.RowState(2));
            Assert.Equal(ProgramRowState.Idle, vm.RowState(3));
        }
        finally
        {
            gate1.TrySetResult();
            gate2.TrySetResult();
        }
    }

    [Fact]
    public async Task Errored_line_maps_to_the_error_row_state()
    {
        var svc = new ScriptedGrblService();
        svc.Reply("ok");
        svc.Reply("error:22");
        var source = new ProgramViewModel(svc);
        var vm = MakeVmAround(source, grbl: svc);
        source.LoadGCode("G21\nG0 X10\nG1 X20 F100\nM5");

        await source.RunAsync();

        // The plotter colors line 2's segments through the same row state the
        // program grid shows (✕) — the errored-line emphasis path.
        Assert.Equal(1, vm.RowForLine(2));
        Assert.Equal(ProgramRowState.Error, vm.RowState(vm.RowForLine(2)));
        // Lines after the failure were never sent — still idle.
        Assert.Equal(ProgramRowState.Idle, vm.RowState(vm.RowForLine(3)));
    }

    [Fact]
    public void GeometryVersion_bumps_on_rebuild_but_not_on_frontier_updates()
    {
        var source = new ProgramViewModel();
        var vm = MakeVmAround(source);
        var initial = vm.GeometryVersion; // non-zero: the ctor's identity mapping and empty state both count

        source.LoadGCode("G1 X10 F100");
        var afterLoad = vm.GeometryVersion;
        Assert.True(afterLoad > initial);

        // Row-state changes (rewind, run progress) never rebuild geometry — the
        // canvases must not re-fit on them.
        source.Rewind();
        Assert.Equal(afterLoad, vm.GeometryVersion);

        // A clear is a geometry change. It now reports through more than one
        // event (the plane mapping re-derives from the machine type, then the
        // geometry empties), so the count is not pinned — only the direction.
        source.ClearProgram();
        Assert.True(vm.GeometryVersion > afterLoad);
    }
}
