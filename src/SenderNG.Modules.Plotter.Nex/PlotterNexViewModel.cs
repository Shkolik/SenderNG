/*
 * PlotterNexViewModel.cs - G-code 3D plotter view model
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

using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Modules.Plotter.Nex.Model;
using SenderNG.Modules.Plotter.Nex.Scene;

namespace SenderNG.Modules.Plotter.Nex;

/// <summary>
/// 3D plotter view model: turns the loaded program (geometry) into per-plane
/// toolpath segments, maps the controller's send row-state frontier onto those
/// segments as run states (pending / in-flight / executed / error), and tracks
/// the live machine position as the tool appearance. The actual GPU scene is
/// created by whatever <see cref="ISceneFactory"/> is attached (the view wires
/// it in Task 6; headless tests pass a recorder directly — the headless
/// dispatcher keeps every callback inline on the caller's thread).
/// <para>
/// Geometry rebuild follows the Skia plotter's contract: synchronous small
/// builds, a fire-and-forget rebuild for large programs behind a generation
/// counter, and StatusText degradation (no program / no motion / build
/// failure) instead of exceptions. <see cref="FrontierVersion"/> bumps on every
/// program/run/position change so the view can repaint cheaply.
/// </para>
/// </summary>
public sealed class PlotterNexViewModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>Programs with fewer tokens are built synchronously; larger ones
    /// are processed off the UI thread and posted back (generation-guarded).</summary>
    public const int AsyncBuildThreshold = 4000;

    /// <summary>Coalesced run-state window cap — a frontier jump larger than
    /// this falls back to the full rescan instead of walking tens of thousands
    /// of rows on the UI thread (Skia OverlayCap idiom).</summary>
    internal const int RunStateWindowCap = 4096;

    private readonly ILoadedProgramSource _programSource;
    private readonly IGrblService _grbl;
    private readonly SettingsService _settings;
    private readonly LocalizationService _localization;
    private readonly IDisposable _stateSubscription;

    private ISceneFactory? _sceneFactory;
    private IPlotScene? _scene;
    private MachineType _machineType;
    private uint[][] _planeOutputLines = [];
    private Dictionary<uint, int> _lineToRow = new();
    private int _generation;
    private bool _vulkanUnavailable;
    private string? _lastControllerPose;
    private int _lastAppliedTouched = -1; // last TouchedRow whose rows were fully applied

    public PlotterNexViewModel(
        ILoadedProgramSource programSource,
        IGrblService grbl,
        SettingsService settings,
        LocalizationService localization,
        ISceneFactory? sceneFactory = null)
    {
        _programSource = programSource;
        _grbl = grbl;
        _settings = settings;
        _localization = localization;
        _machineType = MachineTypeCatalog.Resolve(_settings.Load().MachineTypeId);

        _programSource.ProgramChanged += OnProgramChanged;
        _programSource.RowsChanged += OnRowsChanged;
        _stateSubscription = _grbl.State.Subscribe(state => PostUi(() => OnControllerState(state)));

        if (sceneFactory is not null)
        {
            AttachScene(sceneFactory);
        }
        else if (_programSource.Program is not null)
        {
            Rebuild();
        }
        else
        {
            ApplyEmpty();
        }
    }

    /// <summary>The current GPU scene, or null until a scene factory is
    /// attached (without one the view model stays inert — Scene null,
    /// HasGeometry false, no event flows and nothing throws).</summary>
    public IPlotScene? Scene => _scene;

    /// <summary>Machine planes: the fabric's layer count, or the machine
    /// type's planes while no fabric is attached.</summary>
    public int PlaneCount => _sceneFactory?.PlaneCount ?? _machineType.Planes.Count;

    /// <summary>Display letters per plane, joined for the header ("XYZ" or
    /// "XY | ZA") — mirrors the Skia plotter's PlaneLetters.</summary>
    public string PlaneLettersText => string.Join(" | ", _machineType.Planes
        .Select((_, p) => new string(
            _machineType.PlaneAxisIndices(p).Select(AxisLetter).ToArray())));

    /// <summary>"{done}/{sendable}" live-run progress over the program rows.</summary>
    public string ProgressText => $"{_programSource.DoneCount}/{_programSource.SendableCount}";

    /// <summary>Monotonic stamp bumped whenever program, run state or tool
    /// position changes — a cheap "repaint" trigger for the view.</summary>
    public int FrontierVersion { get; private set; }

    /// <summary>True when the last acceptable program actually produced
    /// geometry (a scene is attached and at least one plane has segments).</summary>
    public bool HasGeometry => _scene is not null && _planeOutputLines.Any(p => p.Length > 0);

    /// <summary>Healthy empty when geometry is ready; holds a reason otherwise
    /// (no program loaded, program has no motion, build in progress, build
    /// failed).</summary>
    public string? StatusText { get; private set; }

    /// <summary>Set by the view when its Vulkan adapter reports no device —
    /// surfaced on the diagnostics screen; false by default.</summary>
    public bool VulkanUnavailable
    {
        get => _vulkanUnavailable;
        set
        {
            if (_vulkanUnavailable == value)
                return;
            _vulkanUnavailable = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Diagnostic (temporary): the raw controller axes that last drove
    /// the tool endpoint — lets the tick log correlate "VM saw X" with "node
    /// transform shows Y" for the stuck-at-origin cone.</summary>
    public string? LastControllerPose => _lastControllerPose;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Attach a late-arriving scene factory (the view builds the Vulkan scene
    /// off the UI thread and hands it in). If a program is already loaded it is
    /// rebuilt onto the fresh scene so the plot appears immediately — this is
    /// the Task 8 "scene arrives after the program" wiring. A headless test
    /// may supply the factory at construction. Re-attaching is a wiring bug
    /// (a second live shape, an unstubbed first one) — it throws.
    /// </summary>
    public void AttachScene(ISceneFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (_sceneFactory is not null)
            throw new InvalidOperationException("A scene factory is already attached.");
        _sceneFactory = factory;

        if (_programSource.Program is not null)
        {
            // A loaded program rebuilds onto the scene so the plot appears
            // immediately — this is the Task 8 "scene arrives after the
            // program" wiring (the synchronous path; small programs only).
            Rebuild();
        }
        else
        {
            // No program yet: ApplyEmpty registers the fabric shape (plane
            // layers + tool) so live pose updates have objects to land on
            // while the plot is empty. The program registers onto it when it
            // loads.
            ApplyEmpty();
        }

        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(PlaneCount));
        OnPropertyChanged(nameof(PlaneLettersText));
    }

    /// <summary>Drop the attached scene factory — the host view disposed its GPU
    /// scene (visual-tree detach). The next <see cref="AttachScene"/> is a fresh
    /// build; a loaded program reapplies onto it there.</summary>
    public void ReleaseScene()
    {
        _sceneFactory = null;
        _scene = null;
        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(PlaneCount));
        OnPropertyChanged(nameof(PlaneLettersText));
        OnPropertyChanged(nameof(HasGeometry));
    }

    public void Dispose()
    {
        _programSource.ProgramChanged -= OnProgramChanged;
        _programSource.RowsChanged -= OnRowsChanged;
        _stateSubscription.Dispose();
    }

    private void OnProgramChanged() => PostUi(() =>
    {
        _machineType = MachineTypeCatalog.Resolve(_settings.Load().MachineTypeId);
        OnPropertyChanged(nameof(PlaneCount));
        OnPropertyChanged(nameof(PlaneLettersText));
        Rebuild();
    });

    private void OnRowsChanged() => PostUi(() =>
    {
        OnPropertyChanged(nameof(ProgressText));
        Bump();
    });

    private void OnControllerState(MachineState state)
    {
        _lastControllerPose = $"status={state.Status} pos={string.Join(",", state.Positions ?? [])}";
        var positions = state.Positions;
        var factory = _sceneFactory;
        if (positions is null || factory?.Tool is null)
            return;

        var points = new List<Vector3>(_machineType.Planes.Count);
        for (var p = 0; p < _machineType.Planes.Count; p++)
        {
            var indices = _machineType.PlaneAxisIndices(p);
            if (positions.Length <= indices[^1])
                return; // Controller reports fewer axes than the machine needs.
            points.Add(new Vector3(
                (float)positions[indices[0]],
                (float)positions[indices[1]],
                indices.Length >= 3 ? (float)positions[indices[2]] : 0f));
        }

        if (factory.Loft is not null && points.Count >= 2)
            factory.Loft.SetWirePlane([points[0], points[1]]);

        factory.Tool.UpdatePosition(new Endpoint([.. points]));
        Bump();
    }

    private void Rebuild()
    {
        var program = _programSource.Program;
        var generation = ++_generation;
        if (program is null || program.Tokens.Count == 0)
        {
            ApplyEmpty();
            return;
        }

        if (program.Tokens.Count <= AsyncBuildThreshold)
        {
            ApplyBuild(BuildGeometry(program, _machineType), generation);
            return;
        }

        // Large program: build off the UI thread, swap under the generation
        // guard so a program opened while the build runs does not get stomped.
        StatusText = _localization.Resolve("module.plotterNex.building", "Plotting…");
        OnPropertyChanged(nameof(StatusText));
        Bump();

        var machineType = _machineType;
        _ = Task.Run(() =>
        {
            try
            {
                var geometry = BuildGeometry(program, machineType);
                PostUi(() =>
                {
                    if (generation != _generation)
                        return;
                    ApplyBuild(geometry, generation);
                });
            }
            catch (Exception)
            {
                PostUi(() =>
                {
                    if (generation != _generation)
                        return;
                    StatusText = _localization.Resolve(
                        "module.plotterNex.buildFailed", "Program could not be plotted.");
                    OnPropertyChanged(nameof(StatusText));
                    Bump();
                });
            }
        });
    }

    private void ApplyBuild(Geometry geometry, int generation)
    {
        if (generation != _generation)
            return;

        // Re-register fresh plane layers and a fresh tool each build — the
        // scene objects have no "clear" API, and a new program must not leave
        // stale segments behind.
        if (_sceneFactory is not null)
        {
            BuildScene();
            for (var p = 0; p < _machineType.Planes.Count; p++)
                ToolpathSceneBuilder.PopulatePlane(
                    _sceneFactory.PlaneLayer(p), geometry.SourceSegments, _machineType.PlaneAxisIndices(p));
        }

        _planeOutputLines = geometry.PlaneOutputLines;
        _lineToRow = geometry.LineToRow;
        StatusText = geometry.Status;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasGeometry));
        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(PlaneCount));
        OnPropertyChanged(nameof(PlaneLettersText));
        OnPropertyChanged(nameof(ProgressText));
        Bump();
    }

    private void ApplyEmpty()
    {
        // Re-register fresh (empty) fabric like ApplyBuild does — the scene
        // objects have no "clear" API, so a cleared program (File → Close)
        // would otherwise leave the previous model's segments and tool drawn.
        if (_sceneFactory is not null)
            BuildScene();
        _planeOutputLines = [];
        _lineToRow = new Dictionary<uint, int>();
        StatusText = _localization.Resolve("module.plotterNex.noProgram", "No program loaded.");
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasGeometry));
        Bump();
    }

    private Geometry BuildGeometry(GCodeProgram program, MachineType type)
    {
        var segments = ToolpathBuilder.Build(program.Tokens);
        if (segments.Count == 0)
        {
            return new Geometry([], [], new Dictionary<uint, int>(),
                _localization.Resolve("module.plotterNex.noMotion", "Program contains no motion."));
        }

        var lineToRow = BuildLineToRow(program);
        var planeOutputLines = new uint[type.Planes.Count][];
        for (var p = 0; p < type.Planes.Count; p++)
            planeOutputLines[p] = PlaneOutputLines(segments, type.PlaneAxisIndices(p));

        return new Geometry(segments, planeOutputLines, lineToRow, Status: null);
    }

    /// <summary>Entry index by physical line number — blank/comment lines never
    /// carry run state, so only truly parseable lines map.</summary>
    private static Dictionary<uint, int> BuildLineToRow(GCodeProgram program)
    {
        var lineToRow = new Dictionary<uint, int>();
        for (var i = 0; i < program.Entries.Count; i++)
        {
            var fileLine = program.Entries[i].FileLine;
            if (fileLine > 0 && !lineToRow.ContainsKey((uint)fileLine))
                lineToRow[(uint)fileLine] = i;
        }
        return lineToRow;
    }

    /// <summary>
    /// The <see cref="ToolpathSegment.LineNumber"/> of each output segment — in
    /// output order — so run-state index <c>i</c> lands on the very segment
    /// <see cref="ToolpathSceneBuilder.PopulatePlane"/> added at <c>i</c>.
    /// Mirrors PopulatePlane exactly: a segment is emitted only when one of its
    /// plane-mapped axes moves by more than the 1e-9 epsilon.
    /// KEEP IN SYNC with PopulatePlane's move filter.
    /// </summary>
    private static uint[] PlaneOutputLines(
        IReadOnlyList<ToolpathSegment> segments, int[] planeAxisIndices)
    {
        var lines = new List<uint>();
        foreach (var segment in segments)
        {
            var moves = false;
            for (var k = 0; k < planeAxisIndices.Length; k++)
            {
                if (Math.Abs(segment.From[planeAxisIndices[k]] - segment.To[planeAxisIndices[k]]) > 1e-9)
                {
                    moves = true;
                    break;
                }
            }
            if (moves)
                lines.Add(segment.LineNumber);
        }
        return [.. lines];
    }

    /// <summary>Coalesced per-frame frontier apply, driven by the view's tick
    /// loop (Skia pattern): each ack raises RowsChanged cheaply; the next tick
    /// applies only rows in (lastAppliedTouched, TouchedRow]. A TouchedRow
    /// regression (rewind) triggers one full rescan. Cap: a huge window falls
    /// back to the full rescan instead of walking tens of thousands of rows
    /// on the UI thread.</summary>
    public void ApplyRunStateWindow()
    {
        var factory = _sceneFactory;
        if (factory is null || _planeOutputLines.Length == 0)
            return;
        var touched = _programSource.TouchedRow;
        if (touched < _lastAppliedTouched)
        {
            FullApplyRunStates();
            return;
        }
        var endpointStart = Math.Max(0, _lastAppliedTouched + 1);
        for (var p = 0; p < _planeOutputLines.Length; p++)
        {
            var lines = _planeOutputLines[p];
            var end = touched;
            var low = LowerBoundRow(lines, endpointStart);
            var high = LowerBoundRow(lines, end + 1);
            if (high - low > RunStateWindowCap)
            {
                FullApplyRunStates();
                return;
            }
            if (high <= low)
                continue;
            var layer = factory.PlaneLayer(p);
            for (var i = low; i < high; i++)
            {
                if (!_lineToRow.TryGetValue(lines[i], out var row))
                    continue; // synthesized move — stays Pending
                layer.SetSegmentRunState(i, MapRowState(_programSource.GetRowState(row)));
            }
        }
        _lastAppliedTouched = touched;
    }

    private void FullApplyRunStates()
    {
        var factory = _sceneFactory;
        if (factory is null)
            return;
        var lineToRow = _lineToRow;
        for (var p = 0; p < _planeOutputLines.Length; p++)
        {
            var lines = _planeOutputLines[p];
            var layer = factory.PlaneLayer(p);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lineToRow.TryGetValue(lines[i], out var row))
                    continue;
                layer.SetSegmentRunState(i, MapRowState(_programSource.GetRowState(row)));
            }
            layer.RebuildExecuted();
        }
        _lastAppliedTouched = _programSource.TouchedRow;
    }

    /// <summary>First index in <paramref name="lines"/> whose mapped row is ≥
    /// <paramref name="targetRow"/> — rows are monotonic along a plane's output
    /// lines (output order follows program order). Missing rows are -1 and sort
    /// before every target.</summary>
    private int LowerBoundRow(uint[] lines, int targetRow)
    {
        if (targetRow <= 0)
            return 0;
        int lo = 0, hi = lines.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            var row = _lineToRow.TryGetValue(lines[mid], out var r) ? r : -1;
            if (row < targetRow)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    private static RunState MapRowState(ProgramRowState rowState) => rowState switch
    {
        ProgramRowState.Sent => RunState.InFlight,
        ProgramRowState.Ok => RunState.Executed,
        ProgramRowState.Error => RunState.Error,
        _ => RunState.Pending,
    };

    private void BuildScene()
    {
        _scene = _sceneFactory!.BuildScene(_machineType);
    }

    /// <summary>Controller letter for a flattened axis index — the controller's
    /// handshake letters win, the preset's conventional letters are the fallback.</summary>
    private char AxisLetter(int axisIndex)
    {
        var letters = _grbl.Capabilities.AxisLetters;
        if (!string.IsNullOrEmpty(letters) && axisIndex < letters.Length)
            return letters[axisIndex];
        return _machineType.ConventionalLetter(axisIndex);
    }

    private void Bump()
    {
        FrontierVersion++;
        OnPropertyChanged(nameof(FrontierVersion));
    }

    private static void PostUi(Action action) => PlotterNexUi.Post(action);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record Geometry(
        IReadOnlyList<ToolpathSegment> SourceSegments,
        uint[][] PlaneOutputLines,
        Dictionary<uint, int> LineToRow,
        string? Status);
}