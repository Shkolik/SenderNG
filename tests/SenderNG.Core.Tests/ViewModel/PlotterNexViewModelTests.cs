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
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Modules.Plotter.Nex;
using SenderNG.Modules.Plotter.Nex.Scene;

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
    public void Rows_changed_maps_row_states_to_segment_run_states()
    {
        var source = new StubProgramSource();
        var scene = new RecorderScene();
        using var vm = MakeVm(source, scene);
        source.Load(MillProgram());
        var layer = scene.Layers[^1];
        Assert.Empty(layer.RunStateCalls);

        // Rows 1..2 (the two first G1 motions) acked ok — the run frontier moved.
        source.AckRows(1, 3);

        // Output segments are lines 2,3,4 → rows 1,2,3; rows 1,2 are Ok
        // (Executed), row 3 is still Idle (Pending).
        Assert.Equal(
            [(0, RunState.Executed), (1, RunState.Executed), (2, RunState.Pending)],
            layer.RunStateCalls);
        Assert.True(vm.FrontierVersion > 0);
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

        public void AckRows(int from, int to, ProgramRowState state = ProgramRowState.Ok)
        {
            for (var i = from; i < to && i < _states.Length; i++)
                _states[i] = state;
            TouchedRow = Math.Max(TouchedRow, Math.Min(to - 1, _states.Length - 1));
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

        public void SetViewPreset(ViewPreset preset) { }
        public void Pan(PanDelta delta) { }
        public void ZoomAround(Vector2 screenPoint, double factor) { }
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

        public ILoftSurface AddLoftSurface() => new NullLoft();

        public IPlotTool CreateTool(ToolShape shape)
        {
            var tool = new RecorderTool(shape);
            Tools.Add(tool);
            return tool;
        }

        public void Dispose() { }

        private sealed class NullLoft : ILoftSurface
        {
            public void SetWirePlane(Vector3[] polygon) { }
        }
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