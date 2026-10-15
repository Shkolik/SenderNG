/*
 * PlotterGlViewModel.cs - G-code 3D plotter view model
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
using SenderNG.Modules.Common.Render;
using SenderNG.Modules.Plotter.GL.Model;
using SenderNG.Modules.Plotter.GL.Rendering;
using SenderNG.Modules.Plotter.GL.Scene;
using SenderNG.Modules.Plotter.GL.Settings;

namespace SenderNG.Modules.Plotter.GL;

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
public sealed class PlotterGlViewModel : INotifyPropertyChanged, IDisposable
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
    private readonly LocalizationService _localization;
    private readonly IMachineKinematicsProvider _kinematics;
    /// <summary>True when this view model built the provider itself (a direct
    /// construction: a unit test, or a third-party embedder). An injected
    /// provider belongs to the caller — the app's DI singleton — and must not be
    /// disposed here.</summary>
    private readonly bool _ownsKinematics;
    private readonly IDisposable _stateSubscription;

    private ISceneFactory? _sceneFactory;
    private IPlotScene? _scene;
    /// <summary>The resolved machine: plane count, plane slices, letters and the
    /// separation all come from here, re-read on every provider change.</summary>
    private MachineKinematics _machine;
    /// <summary>Per-plane world placement, index-aligned with
    /// <see cref="MachineKinematics.Planes"/>: the separation applied on the
    /// world axis the plane's slots do not span.</summary>
    private Vector3[] _planePlacements = [];
    private uint[][] _planeOutputLines = [];
    private Dictionary<uint, int> _lineToRow = new();
    private int _generation;
    private PlotGlFailure _renderFailure = PlotGlFailure.None;
    private string? _lastControllerPose;
    private int _lastAppliedTouched = -1; // last TouchedRow whose rows were fully applied
    private int _recoloredUpTo = -1; // last row whose late ack was re-applied to the planes
    private bool _isCheckMode;
    private Vector3?[] _ackedEnds = [];
    private IReadOnlyList<ToolpathSegment> _sourceSegments = [];
    private int _ackedEndRowBaseline = -1;
    private volatile bool _rowsChangedPending;
    private Dictionary<int, ToolpathSegment> _lastSegmentByRow = new();

    /// <summary>Key + English fallback of the current status notice, kept as a
    /// key rather than a resolved string so <see cref="StatusText"/> translates
    /// again on a live locale switch (a resolved string would freeze the
    /// language it was built in until the next program event).</summary>
    private (string Key, string Fallback)? _status;

    public PlotterGlViewModel(
        ILoadedProgramSource programSource,
        IGrblService grbl,
        SettingsService settings,
        LocalizationService localization,
        ISceneFactory? sceneFactory = null,
        IMachineKinematicsProvider? kinematics = null)
    {
        _programSource = programSource;
        _grbl = grbl;
        _localization = localization;
        // Optional in the signature so a direct construction (tests, an
        // embedder) stays possible; the module always injects the app's DI
        // singleton. The fallback is built from the SAME settings and controller
        // this view model was handed, so it resolves the same machine.
        _ownsKinematics = kinematics is null;
        _kinematics = kinematics ?? new MachineKinematicsProvider(settings, grbl);
        _machine = _kinematics.Current;
        _planePlacements = ResolvePlacements(_machine);

        _kinematics.Changed += OnKinematicsChanged;
        _localization.Changed += OnLocalizationChanged;

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

    /// <summary>Machine planes: the fabric's layer count, or the resolved
    /// machine's planes while no fabric is attached.</summary>
    public int PlaneCount => _sceneFactory?.PlaneCount ?? _machine.Planes.Count;

    /// <summary>Display letters per plane, joined for the header ("XYZ" or
    /// "XY | ZA") — the plane's own MACHINE axes in plane order, named through
    /// the controller's letters, so a bug-report foam layout's plane 2 reads
    /// "AZ" rather than a sorted "ZA".</summary>
    public string PlaneLettersText => string.Join(" | ", _machine.Planes
        .Select((_, p) => new string(
            _machine.PlaneIndices(p).Select(_machine.LetterFor).ToArray())));

    /// <summary>The resolved kinematics this view model is drawing (diagnostics
    /// and the tests' handle on the plane layout).</summary>
    public MachineKinematics Machine => _machine;

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
    /// failed). Resolved on every read, so the notice re-translates on a locale
    /// switch — <see cref="OnLocalizationChanged"/> re-raises it.</summary>
    public string? StatusText => _status is { } status
        ? _localization.Resolve(status.Key, status.Fallback)
        : null;

    /// <summary>Publish a new status reason (null = healthy, no notice) and
    /// notify the header binding. The key — not the resolved text — is what is
    /// stored, so the notice follows the active locale for as long as it is
    /// shown.</summary>
    private void SetStatus(string? key, string? fallback = null)
    {
        _status = key is null ? null : (key, fallback ?? key);
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>The language picker switched the locale: re-resolve the status
    /// notice. Raised on the UI thread (the picker's own thread), so no
    /// dispatcher post — matches PlotterGlSettingsViewModel.RelocalizeLabels.</summary>
    private void OnLocalizationChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(nameof(StatusText));

    /// <summary>Set by the view when its GL context creation reports no usable
    /// device; false until <see cref="ReportRenderUnavailable"/> is called. The
    /// only consumers are this module's own XAML — the Plot GL tab's fallback
    /// panel and the scene panel's inverse visibility.</summary>
    public bool RenderUnavailable => _renderFailure != PlotGlFailure.None;

    /// <summary>Why 3D plotting is unavailable; <see cref="PlotGlFailure.None"/>
    /// while it is available.</summary>
    internal PlotGlFailure RenderFailure => _renderFailure;

    /// <summary>Localized one-line explanation of <see cref="RenderFailure"/>,
    /// shown as the Plot GL tab's fallback hint. Empty while healthy
    /// (<see cref="PlotGlFailure.None"/>), so a consumer can bind it
    /// unconditionally and never show a failure explanation on a working
    /// machine. Otherwise the reported kind's own key, degrading through the
    /// existing generic renderUnavailableHint value and only then to a
    /// last-resort English literal — so a locale file that has not gained the
    /// per-kind keys still shows a sensible, localized message rather than a
    /// blank panel.</summary>
    internal string RenderFailureDetail => _renderFailure switch
    {
        PlotGlFailure.None => string.Empty,
        PlotGlFailure.NotSupported => Hint("module.plotter-gl.renderUnavailable.notSupported"),
        PlotGlFailure.RuntimeError => Hint("module.plotter-gl.renderUnavailable.runtimeError"),
        _ => Hint("module.plotter-gl.renderUnavailable.other"),
    };

    /// <summary>Resolve one per-kind hint key, degrading to the module's
    /// existing generic hint and only then to a hardcoded English literal.
    /// <para>The generic hint is itself a localized key, so the degradation
    /// path stays translated: a Russian locale that has not gained the
    /// per-kind keys yields the Russian generic hint, not the English
    /// literal.</para></summary>
    private string Hint(string key) => _localization.Resolve(key, _localization.Resolve(
        "module.plotter-gl.renderUnavailableHint",
        "No OpenGL context could be created."));

    /// <summary>Record why 3D plotting is unavailable and notify the three
    /// properties the fallback panel binds. The first reason wins: a later,
    /// different failure must not overwrite the one the user is already
    /// reading, and re-notifying would churn the bindings for nothing.
    /// <see cref="PlotGlFailure.None"/> is ignored, so a caller cannot report
    /// "unavailable" without a reason.
    /// <para>The flag is raised LAST, and the order is load-bearing: its
    /// handler tears the scene down synchronously, so raising it first would
    /// make the panel visible against a stale hint if any of that work
    /// threw. Publishing the reason and the detail first makes the flag the
    /// commit point. All three raises land in one dispatcher work item, so
    /// reordering them costs nothing visually.</para>
    /// <para><paramref name="message"/> is the raw exception text the view saw
    /// at context creation. It is logged once (the first reason wins, so this
    /// never runs twice) — it is deliberately NOT surfaced to the user: the
    /// panel shows the localized <see cref="RenderFailureDetail"/>.</para></summary>
    internal void ReportRenderUnavailable(PlotGlFailure failure, string? message = null)
    {
        if (failure == PlotGlFailure.None || _renderFailure != PlotGlFailure.None)
            return;
        _renderFailure = failure;
        if (message is not null)
            System.Diagnostics.Trace.WriteLine($"[GL] render unavailable ({failure}): {message}");
        OnPropertyChanged(nameof(RenderFailure));
        OnPropertyChanged(nameof(RenderFailureDetail));
        OnPropertyChanged(nameof(RenderUnavailable));
    }

    /// <summary>Diagnostic (temporary): the raw controller axes that last drove
    /// the tool endpoint — lets the tick log correlate "VM saw X" with "node
    /// transform shows Y" for the stuck-at-origin cone.</summary>
    public string? LastControllerPose => _lastControllerPose;

    /// <summary>True while grbl check mode is active — the controller verifies
    /// blocks but never moves, so the tool cone must use the acked-end source.</summary>
    internal bool IsCheckMode => _isCheckMode;

    /// <summary>Per-plane end of the last acked (✓) segment — the tool source
    /// in Check mode, the fallback otherwise.</summary>
    internal Vector3?[] AckedEnd => _ackedEnds;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Attach a late-arriving scene factory (the view builds the GL scene
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
        _kinematics.Changed -= OnKinematicsChanged;
        _localization.Changed -= OnLocalizationChanged;
        _programSource.ProgramChanged -= OnProgramChanged;
        _programSource.RowsChanged -= OnRowsChanged;
        // Only the provider this view model built: an injected one is the app's
        // DI singleton and outlives every view.
        if (_ownsKinematics && _kinematics is IDisposable disposable)
            disposable.Dispose();
        _stateSubscription.Dispose();
    }

    /// <summary>Re-read the machine after a profile switch, a connect (the
    /// <c>[AXS:]</c> letters and the reported count) or a committed axis edit.
    /// The plane slices themselves may have moved, so the program is re-sliced
    /// rather than merely relabelled.</summary>
    private void OnKinematicsChanged(object? sender, EventArgs e) => PostUi(() =>
    {
        _machine = _kinematics.Current;
        _planePlacements = ResolvePlacements(_machine);
        OnPropertyChanged(nameof(PlaneCount));
        OnPropertyChanged(nameof(PlaneLettersText));
        if (_programSource.Program is not null)
            Rebuild();
    });

    private void OnProgramChanged() => PostUi(() =>
    {
        _machine = _kinematics.Current;
        _planePlacements = ResolvePlacements(_machine);
        OnPropertyChanged(nameof(PlaneCount));
        OnPropertyChanged(nameof(PlaneLettersText));
        Rebuild();
    });

    /// <summary>The per-plane world placements for a resolved machine: plane
    /// <c>p</c> sits at the machine's separation on the world axis its slots do
    /// not span (world Z for a two-slot tower), plane 0 at the origin. Read
    /// through <see cref="PlaneWorldMapper.PlacementFor"/>, the one place that
    /// rule lives, so the scene, the tool and the loft cannot disagree.</summary>
    private static Vector3[] ResolvePlacements(MachineKinematics machine) =>
        [.. machine.Planes.Select((plane, p) => PlaneWorldMapper.PlacementFor(machine, p, plane.Axes.Count))];

    /// <summary>One plane's world placement, or the origin for a plane that does
    /// not exist (a machine with no planes still renders its empty scene rather
    /// than throwing on the way in).</summary>
    private Vector3 PlacementForPlane(int plane) =>
        plane >= 0 && plane < _planePlacements.Length ? _planePlacements[plane] : Vector3.Zero;

    /// <summary>Row events land on the serial thread at stream speed; posting one
    /// dispatcher action per event starves the UI during a fast check run. The
    /// events only flip a flag here — <see cref="PulseAckedEnds"/> (the 30Hz view
    /// tick) folds them into the acked-end cache, progress and bump once per
    /// tick regardless of how many rows resolved meanwhile.</summary>
    private void OnRowsChanged() => _rowsChangedPending = true;

    /// <summary>Coalesced per-tick fold of pending row events. Runs on the UI
    /// thread (called by the view's tick), clears the serial-thread flag, then
    /// advances the acked-end cache, progress text and frontier bump.</summary>
    public void PulseAckedEnds()
    {
        if (!_rowsChangedPending)
            return;
        _rowsChangedPending = false;
        AdvanceAckedEnd();
        OnPropertyChanged(nameof(ProgressText));
        Bump();
    }

    /// <summary>Advance the per-plane acked-end cache over rows that became Ok.
    /// The checkpoint only moves past a terminal row (Ok/Idle/Error); a row still
    /// in flight (Sent) parks it so the trailing ack is picked up on the next
    /// pass — otherwise a Sent-then-Ok transition behind the frontier would never
    /// set the acked tool endpoint (the same late-ack trap as the colors).</summary>
    private void AdvanceAckedEnd()
    {
        var touched = _programSource.TouchedRow;
        if (touched < _ackedEndRowBaseline)
        {
            _ackedEnds = new Vector3?[_machine.Planes.Count];
            _ackedEndRowBaseline = touched;
            return;
        }
        if (_ackedEnds.Length != _machine.Planes.Count)
            _ackedEnds = new Vector3?[_machine.Planes.Count];
        for (var i = _ackedEndRowBaseline + 1; i <= touched; i++)
        {
            var state = _programSource.GetRowState(i);
            if (state == ProgramRowState.Sent)
                break;
            if (state == ProgramRowState.Ok)
            {
                var seg = LastSegmentForRow(i);
                if (seg is null)
                    continue;
                for (var p = 0; p < _machine.Planes.Count; p++)
                {
                    var indices = PlaneAxisIndices(p);
                    // Highest index, not the last entry: a plane's index list is in
                    // plane order and may be non-monotonic ([3, 2] on the shipped
                    // bug firmware), so indices[^1] would test the wrong axis.
                    if (seg.To.Length <= HighestAxisIndex(indices))
                        continue;
                    _ackedEnds[p] = PlaneWorldMapper.ToWorld(PlacementForPlane(p), seg.To, indices);
                }
            }
            _ackedEndRowBaseline = i;
        }
    }

    private ToolpathSegment? LastSegmentForRow(int row) =>
        _lastSegmentByRow.TryGetValue(row, out var s) ? s : null;

    private void OnControllerState(MachineState state)
    {
        _lastControllerPose = $"status={state.Status} pos={string.Join(",", state.Positions ?? [])}";
        _isCheckMode = state.Status.Equals("Check", StringComparison.OrdinalIgnoreCase);
        var positions = state.Positions;
        var factory = _sceneFactory;
        if (positions is null || factory?.Tool is null)
            return;

        var points = new List<Vector3>(_machine.Planes.Count);
        for (var p = 0; p < _machine.Planes.Count; p++)
        {
            var indices = PlaneAxisIndices(p);
            if (positions.Length <= HighestAxisIndex(indices))
                return; // Controller reports fewer axes than the machine needs.
            var live = PlaneWorldMapper.ToWorld(PlacementForPlane(p), positions, indices);
            var acked = p < _ackedEnds.Length ? _ackedEnds[p] : null;
            Vector3? resolved = _isCheckMode ? (acked ?? live) : live;
            if (resolved is null)
                continue; // both null — keep the last pose for this plane
            points.Add(resolved.Value);
        }
        if (points.Count == 0)
            return;

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
            ApplyBuild(BuildGeometry(program, _machine), generation);
            return;
        }

        // Large program: build off the UI thread, swap under the generation
        // guard so a program opened while the build runs does not get stomped.
        SetStatus("module.plotter-gl.building", "Plotting…");
        Bump();

        var machine = _machine;
        _ = Task.Run(() =>
        {
            try
            {
                var geometry = BuildGeometry(program, machine);
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
                    SetStatus("module.plotter-gl.buildFailed", "Program could not be plotted.");
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
            for (var p = 0; p < _machine.Planes.Count; p++)
                ToolpathSceneBuilder.PopulatePlane(
                    _sceneFactory.PlaneLayer(p), geometry.SourceSegments, PlaneAxisIndices(p),
                    PlacementForPlane(p));

            var loft = _sceneFactory.Loft;
            if (loft is not null && _machine.Planes.Count >= 2)
                PopulateLoftSheet(loft, geometry.SourceSegments);
        }

        _planeOutputLines = geometry.PlaneOutputLines;
        _lineToRow = geometry.LineToRow;
        _sourceSegments = geometry.SourceSegments;
        _lastSegmentByRow = BuildLastSegmentByRow(geometry.SourceSegments, geometry.LineToRow);
        _ackedEnds = new Vector3?[_machine.Planes.Count];
        _ackedEndRowBaseline = -1;
        _lastAppliedTouched = -1;
        _recoloredUpTo = -1;
        SetStatus(geometry.Status?.Key, geometry.Status?.Fallback);
        OnPropertyChanged(nameof(HasGeometry));
        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(PlaneCount));
        OnPropertyChanged(nameof(PlaneLettersText));
        OnPropertyChanged(nameof(ProgressText));
        Bump();
    }

    /// <summary>Feed the loft's static swept sheet: one sample per source segment,
    /// mapping each 4-axis From through both plane world mappers — equal-length
    /// arrays by construction (one sample per program step). Runs once per program
    /// build.</summary>
    private void PopulateLoftSheet(ILoftSurface loft, IReadOnlyList<ToolpathSegment> segments)
    {
        var count = segments.Count > 2 ? segments.Count : 2;
        var sideA = new List<Vector3>(count);
        var sideB = new List<Vector3>(count);
        var idx0 = PlaneAxisIndices(0);
        var idx1 = PlaneAxisIndices(1);
        var place0 = PlacementForPlane(0);
        var place1 = PlacementForPlane(1);
        foreach (var seg in segments)
        {
            sideA.Add(PlaneWorldMapper.ToWorld(place0, seg.From, idx0));
            sideB.Add(PlaneWorldMapper.ToWorld(place1, seg.From, idx1));
        }
        loft.SetLoftSheet(sideA, sideB);
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
        _sourceSegments = [];
        _lastSegmentByRow = new();
        _ackedEnds = new Vector3?[_machine.Planes.Count];
        _ackedEndRowBaseline = -1;
        _lastAppliedTouched = -1;
        _recoloredUpTo = -1;
        SetStatus("module.plotter-gl.noProgram", "No program loaded.");
        OnPropertyChanged(nameof(HasGeometry));
        Bump();
    }

    private Geometry BuildGeometry(GCodeProgram program, MachineKinematics machine)
    {
        var segments = ToolpathBuilder.Build(program.Tokens);
        if (segments.Count == 0)
        {
            return new Geometry([], [], new Dictionary<uint, int>(),
                Status: ("module.plotter-gl.noMotion", "Program contains no motion."));
        }

        var lineToRow = BuildLineToRow(program);
        var planeOutputLines = new uint[machine.Planes.Count][];
        for (var p = 0; p < machine.Planes.Count; p++)
            planeOutputLines[p] = PlaneOutputLines(segments, PlaneAxisIndices(p));

        return new Geometry(segments, planeOutputLines, lineToRow, Status: null);
    }

    /// <summary>Entry index by the parser's block counter — which is what token
    /// (and therefore segment) LineNumbers actually are. Blank/comment/percent/
    /// realtime rows never reach the parser's counter, so a file whose header
    /// holds them (foam programs do) starts its first real block at line 1 while
    /// FileLine is already much higher — keying by FileLine swallows the first
    /// blocks into forever-Idle header rows. The k-th sendable row is the k-th
    /// block the parser counted, so the sendable ordinal is the exact key space.
    /// </summary>
    private static Dictionary<uint, int> BuildLineToRow(GCodeProgram program)
    {
        var lineToRow = new Dictionary<uint, int>();
        uint block = 0;
        for (var i = 0; i < program.Entries.Count; i++)
        {
            if (!program.Entries[i].IsSendable)
                continue;
            lineToRow[++block] = i;
        }
        return lineToRow;
    }

    /// <summary>Row index → final output segment in that row (headers/comments
    /// have none). One segment per sendable row in output order.</summary>
    private static Dictionary<int, ToolpathSegment> BuildLastSegmentByRow(
        IReadOnlyList<ToolpathSegment> segments, Dictionary<uint, int> lineToRow)
    {
        var map = new Dictionary<int, ToolpathSegment>();
        foreach (var seg in segments)
        {
            if (!lineToRow.TryGetValue(seg.LineNumber, out var row))
                continue;
            map[row] = seg; // later output wins for a row with several segments
        }
        return map;
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
        if (RunStateUploadsDisabled)
        {
            LogRunStateDisabled();
            return;
        }
        var factory = _sceneFactory;
        if (factory is null || _planeOutputLines.Length == 0)
            return;
        var touched = _programSource.TouchedRow;
        if (touched < _lastAppliedTouched)
        {
            FullApplyRunStates();
            return;
        }
        // Flood-bounded frontier: never walk more than the cap per pass — the
        // remainder is picked up by the next pass. A fast check run spools the
        // frontier far ahead of the ack trail, so a huge window must NOT fall
        // back to the O(n) full rescan (that was the per-tick choke).
        var end = Math.Min(touched, _lastAppliedTouched + RunStateWindowCap);
        var endpointStart = Math.Max(0, _lastAppliedTouched + 1);
        for (var p = 0; p < _planeOutputLines.Length; p++)
        {
            var lines = _planeOutputLines[p];
            var low = LowerBoundRow(lines, endpointStart);
            var high = LowerBoundRow(lines, end + 1);
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
        _lastAppliedTouched = end;
        RecolorAckedRows(end);
    }

    /// <summary>Re-apply plane colors for rows that resolved after an earlier
    /// frontier pass: an ack arriving behind <see cref="_lastAppliedTouched"/>
    /// never enters <see cref="ApplyRunStateWindow"/>'s window again, so OK/Error
    /// transitions would otherwise leave segments permanently InFlight-colored.
    /// The scan parks behind the first in-flight (Sent) row — resolved rows are
    /// contiguous, so trailing acks resolve in order and each pass bites off at
    /// most the newly-resolved run. Idle/comment rows consume the checkpoint so
    /// header text cannot park it. SetSegmentRunState no-ops on an unchanged
    /// state, so a revisited row costs nothing.</summary>
    private void RecolorAckedRows(int touched)
    {
        var factory = _sceneFactory;
        if (factory is null)
            return;
        var from = Math.Max(0, _recoloredUpTo + 1);
        for (var r = from; r <= touched; r++)
        {
            var state = _programSource.GetRowState(r);
            if (state == ProgramRowState.Sent)
                break;
            if (state is ProgramRowState.Ok or ProgramRowState.Error)
            {
                for (var p = 0; p < _planeOutputLines.Length; p++)
                {
                    var lines = _planeOutputLines[p];
                    var low = LowerBoundRow(lines, r);
                    var high = LowerBoundRow(lines, r + 1);
                    if (high <= low)
                        continue;
                    var layer = factory.PlaneLayer(p);
                    var runState = MapRowState(state);
                    for (var i = low; i < high; i++)
                        layer.SetSegmentRunState(i, runState);
                }
            }
            _recoloredUpTo = r;
        }
    }

    /// <summary>Diagnostic (crash forensics): where the frontier apply is vs
    /// the last applied row, so a crash log can show whether run-state color
    /// writes were in flight at death.</summary>
    internal string DumpRunState() =>
        $"touched={_programSource.TouchedRow} lastApplied={_lastAppliedTouched}";

    /// <summary>Temporary device-lost isolation: with PLOTGL_NO_RUN_STATE=1 the
    /// view model stops coloring the plane and touching the done overlay, so a
    /// run can be A/B'd with zero run-state GPU churn (no MarkDirty, no done
    /// node writes). TODO: remove once the device-lost root cause is fixed.</summary>
    private static readonly bool RunStateUploadsDisabled =
        Environment.GetEnvironmentVariable("PLOTGL_NO_RUN_STATE") == "1";

    private static bool _runStateDisabledLogged;

    private static void LogRunStateDisabled()
    {
        if (_runStateDisabledLogged)
            return;
        _runStateDisabledLogged = true;
        System.Diagnostics.Debug.WriteLine("PLOTGL_NO_RUN_STATE=1 active — run-state uploads disabled");
    }

    private void FullApplyRunStates()
    {
        if (RunStateUploadsDisabled)
        {
            LogRunStateDisabled();
            return;
        }
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
        _recoloredUpTo = _lastAppliedTouched;
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
        _scene = _sceneFactory!.BuildScene(_machine);
    }

    /// <summary>Plane <paramref name="plane"/>'s machine axis indices, in plane
    /// order (may be non-monotonic).</summary>
    private int[] PlaneAxisIndices(int plane) => [.. _machine.PlaneIndices(plane)];

    /// <summary>A plane's highest machine axis index, or <c>-1</c> for a plane
    /// with no axes. The list is in PLANE order and may be NON-MONOTONIC — the
    /// bug-report foam firmware's plane 2 is <c>[3, 2]</c> — so "does the plane
    /// fit the reported position / the program frame" is a MAX, never the last
    /// entry.</summary>
    private static int HighestAxisIndex(int[] planeAxisIndices)
    {
        var max = -1;
        foreach (var index in planeAxisIndices)
        {
            if (index > max)
                max = index;
        }
        return max;
    }

    private void Bump()
    {
        FrontierVersion++;
        OnPropertyChanged(nameof(FrontierVersion));
    }

    private static void PostUi(Action action) => PlotterGlUi.Post(action);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record Geometry(
        IReadOnlyList<ToolpathSegment> SourceSegments,
        uint[][] PlaneOutputLines,
        Dictionary<uint, int> LineToRow,
        (string Key, string Fallback)? Status);
}