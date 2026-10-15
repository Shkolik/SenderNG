/*
 * PlotterViewModel.cs - G-code plotter view model
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
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Modules.Plotter.Settings;

namespace SenderNG.Modules.Plotter;

/// <summary>One toolpath segment projected onto a carriage plane: the plane's
/// axis positions at both ends, rapid flag, and the source file line (the
/// run-frontier mapping key). Pure data — buildable and testable headlessly.</summary>
public sealed record PlotSegment(double[] From, double[] To, bool Rapid, uint Line);

/// <summary>
/// Plotter view model: subscribes to the program surface and rebuilds the
/// toolpath geometry on program change (ToolpathBuilder), slices it into the
/// machine's carriage planes (the resolved <see cref="MachineKinematics"/> —
/// axis indices and plane order), and tracks the run frontier (row states) plus
/// the live tool position for the overlay. UI-agnostic data; the view draws.
/// <para>
/// Nothing here throws on degenerate input: no program, a program without
/// motion, or a machine whose planes reach beyond the axes the program
/// actually moves all degrade to <see cref="StatusText"/> + empty geometry —
/// the view refuses to render instead of crashing.
/// </para>
/// <para>
/// The kinematics is a JOIN of the stored profile with live controller state
/// (the <c>[AXS:]</c> letters and the reported axis count), so it is re-read
/// when <see cref="IMachineKinematicsProvider.Changed"/> fires — a profile
/// switch, a connect, or a committed axis edit — rather than resolved once at
/// construction.
/// </para>
/// <para>
/// UI marshaling: the app's UiThread helper is app-assembly code a plugin
/// cannot reference, so the plugin carries its own mirror (<see cref="PlotterUi"/>):
/// the dispatcher is captured when the host realizes the view model (UI thread),
/// headless constructions stay inline — the same determinism rule the app uses.
/// </para>
/// </summary>
public class PlotterViewModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>Programs above this token count build off the UI thread
    /// (Task.Run + generation guard); smaller ones build synchronously —
    /// microseconds of work do not justify a flicker, and headless tests stay
    /// deterministic. Internal-visible for the async-build test.</summary>
    public const int AsyncBuildThreshold = 4000;

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
    private int _generation;

    /// <summary>The resolved machine: plane count, plane slices, letters and the
    /// separation all come from here, re-read on every provider change.</summary>
    private MachineKinematics _machine;

    private double[][] _bounds = [];
    private Dictionary<uint, int> _lineToRow = new();

    /// <summary>Key + English fallback + format args of the current status
    /// notice, kept as a key rather than a resolved string so
    /// <see cref="StatusText"/> translates again on a live locale switch (a
    /// resolved string would freeze the language it was built in until the
    /// next program event).</summary>
    private (string Key, string Fallback, object?[] Args)? _status;

    public PlotterViewModel(
        ILoadedProgramSource programSource,
        IGrblService grbl,
        SettingsService settings,
        LocalizationService localization,
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
        _kinematics.Changed += OnKinematicsChanged;

        _programSource.ProgramChanged += OnProgramChanged;
        _programSource.RowsChanged += OnRowsChanged;
        _localization.Changed += OnLocalizationChanged;
        _stateSubscription = _grbl.State.Subscribe(state => PostUi(() =>
        {
            LivePosition = state.Positions;
            CheckMode = state.Status.Equals("Check", StringComparison.OrdinalIgnoreCase);
            Bump();
        }));

        // Initial geometry (a program may already be loaded when the tab is
        // first realized — module views are created lazily).
        if (_programSource.Program is not null)
            Rebuild();
        else
            ApplyEmpty();
    }

    /// <summary>Toolpath per carriage plane, rebuilt on program change. Plane k
    /// slices <see cref="PlaneAxisIndices"/>(k) of the resolved machine. The
    /// list length matches <see cref="PlaneCount"/>; a plane whose axis
    /// indices reach beyond the program's available axes stays empty (dropped)
    /// and <see cref="StatusText"/> explains why.</summary>
    public IReadOnlyList<IReadOnlyList<PlotSegment>> Planes { get; private set; } = [];

    /// <summary>The unsliced toolpath (one entry per source motion) — the foam
    /// isometric loft draws both carriages plus the connecting wire from this,
    /// which per-plane slices cannot provide (they drop per-plane zero moves
    /// independently, breaking point pairing).</summary>
    public IReadOnlyList<ToolpathSegment> SourceSegments { get; private set; } = [];

    /// <summary>Carriage-plane count of the resolved machine (1 = mill-style,
    /// 2 = foam) — drives the view's one-canvas vs two-canvas layout.</summary>
    public int PlaneCount => _machine.Planes.Count;

    /// <summary>Axis indices (controller axis order) backing plane <paramref name="plane"/>,
    /// in the plane's own order — the view maps segment/LivePosition slots through
    /// these. May be NON-MONOTONIC (the bug-report foam layout's plane 2 is
    /// <c>[3, 2]</c>), so "does the plane fit the program frame" is a MAX over the
    /// list, never its last entry.</summary>
    public int[] PlaneAxisIndices(int plane) => [.. _machine.PlaneIndices(plane)];

    /// <summary>The resolved kinematics this view model is drawing (diagnostics
    /// and the tests' handle on the plane layout).</summary>
    public MachineKinematics Machine => _machine;

    /// <summary>The JOB's world XY envelope: every plane's extent, read through
    /// its own slot order and UNIONED. Both foam carriage planes work inside the
    /// same envelope, so the two tiles must measure the same rectangle — see
    /// <see cref="PlotterPlaneMapper.TryJobExtent"/>, which does the arithmetic
    /// and owns the rules about which planes may contribute.
    /// <para>False means no plane could be read, never a fabricated envelope;
    /// the caller substitutes its own fallback, as it did before this
    /// existed.</summary>
    public bool TryJobEnvelope(out (double MinX, double MinY, double MaxX, double MaxY) envelope)
    {
        var planes = PlaneCount;
        var indices = new int[planes][];
        for (var p = 0; p < planes; p++)
            indices[p] = PlaneAxisIndices(p);
        return PlotterPlaneMapper.TryJobExtent(indices, Bounds, out envelope);
    }

    /// <summary>Display letters per plane: the plane's own MACHINE axes, named
    /// through the controller's letters (conventional fallback) — the same
    /// indices the panes slice, so the header cannot disagree with the picture.
    /// A default foam machine reads "XY | ZA"; the bug-report layout's plane 2
    /// reads "AZ", which is the whole point of showing plane order rather than a
    /// sorted set.</summary>
    public string[] PlaneLetters => _machine.Planes
        .Select((_, p) => new string(PlaneAxisIndices(p).Select(_machine.LetterFor).ToArray()))
        .ToArray();

    /// <summary>Per-axis bounds [min,max] over the whole toolpath (program frame).</summary>
    public double[][] Bounds => _bounds;

    /// <summary>True when there is anything to draw — the view's license to
    /// render; false means draw the <see cref="StatusText"/> notice only.</summary>
    public bool HasGeometry => Planes.Any(p => p.Count > 0);

    /// <summary>Human-readable reason there is nothing (or something partial)
    /// to draw: no program, no motion in the program, or a machine type whose
    /// planes need more axes than the program provides. Null when geometry is
    /// complete and renderable. Resolved (and formatted) on every read, so the
    /// notice re-translates on a locale switch —
    /// <see cref="OnLocalizationChanged"/> re-raises it.</summary>
    public string? StatusText => _status is { } status
        ? string.Format(
            CultureInfo.InvariantCulture,
            _localization.Resolve(status.Key, status.Fallback),
            status.Args)
        : null;

    /// <summary>Publish a new status reason (null = healthy, no notice) and
    /// notify the header. The key and the format args — not the rendered text —
    /// are what is stored, so a formatted notice (axis mismatch) re-formats in
    /// the new locale too.</summary>
    private void SetStatus(string? key, string? fallback = null, params object?[] args)
    {
        _status = key is null ? null : (key, fallback ?? key, args);
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>The language picker switched the locale: re-resolve the status
    /// notice. Raised on the UI thread (the picker's own thread), so no
    /// dispatcher post — matches PlotterSettingsViewModel.OnLocalizationChanged.
    /// The view refreshes the header from its own PropertyChanged handler.</summary>
    private void OnLocalizationChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(nameof(StatusText));

    /// <summary>Isometric rendering toggle (mill: Z-up iso projection; foam:
    /// the two-carriage loft). View state, not persisted.</summary>
    public bool Isometric { get; set; }

    /// <summary>Live tool position (work frame — the same frame the toolpath is
    /// built in) for the glyph overlay; null until the controller reports state.</summary>
    public double[]? LivePosition { get; private set; }

    /// <summary>True while grbl check mode ($C) is active: the controller
    /// verifies blocks but never moves, so the reported machine position stays
    /// frozen while the send frontier advances. The view must then drive the
    /// tool glyph from the acked segment end instead of the live position.</summary>
    public bool CheckMode { get; private set; }

    /// <summary>Row (index-aligned with the program entries) a file line belongs
    /// to, for frontier coloring; -1 when the line maps to no row.</summary>
    public int RowForLine(uint line) =>
        _lineToRow.TryGetValue(line, out var row) ? row : -1;

    /// <summary>Highest row index whose send state has changed — the run
    /// frontier as a row index, O(1) from the source (the view's recolor
    /// window and tool glyph advance along it; comment rows never touch).</summary>
    public int TouchedRow => _programSource.TouchedRow;

    /// <summary>Row send state by row index (the view colors segments by it).</summary>
    public ProgramRowState RowState(int index) => _programSource.GetRowState(index);

    /// <summary>Acknowledged (ok) sendable blocks of the loaded program — the
    /// run's done count, O(1) from the source's incrementally tracked counter
    /// (a per-tick scan of the program is O(n²) over a run — the header froze
    /// the UI thread for tens of seconds on large programs).</summary>
    public int DoneCount => _programSource.DoneCount;

    /// <summary>Sendable blocks of the loaded program (comments excluded) — the
    /// run's block total, O(1) from the source's counter.</summary>
    public int TotalCount => _programSource.SendableCount;

    /// <summary>Current frontier summary for a status line (e.g. "12/348"),
    /// over sendable blocks — agrees with the program view's ProgressText.</summary>
    public string ProgressText => $"{DoneCount}/{TotalCount}";

    /// <summary>Monotonic counter bumped on any geometry/frontier/position
    /// change — the view's redraw tick without fine-grained change events.</summary>
    public int FrontierVersion { get; private set; }

    /// <summary>Monotonic counter bumped ONLY when the geometry is rebuilt
    /// (program load/clear/transform, machine-type change) — the canvases re-fit
    /// (reset zoom/pan to the fitted view) on it, but never on frontier or live
    /// position updates, which would otherwise yank the user's view back to the
    /// fit every row update during a run.</summary>
    public int GeometryVersion { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void Bump()
    {
        FrontierVersion++;
        OnPropertyChanged(nameof(FrontierVersion));
    }

    private void OnProgramChanged() => PostUi(() =>
    {
        // The machine may have changed since construction (a machine switch
        // clears the program; the layout rides the active profile). Re-reading
        // the provider is cheap when nothing changed — it is the same snapshot.
        _machine = _kinematics.Current;
        OnPropertyChanged(nameof(PlaneCount));
        OnPropertyChanged(nameof(PlaneLetters));
        Rebuild();
    });

    /// <summary>The machine's layout changed under us: a profile switch, a
    /// connect/disconnect (the <c>[AXS:]</c> letters and the reported count), or
    /// a committed axis edit. The new snapshot is picked up and the toolpath is
    /// re-sliced, because the plane slices themselves may differ — not just the
    /// header letters. Raised on the thread that triggered it, so it marshals
    /// like every other controller-driven event.</summary>
    private void OnKinematicsChanged(object? sender, EventArgs e) => PostUi(() =>
    {
        _machine = _kinematics.Current;
        OnPropertyChanged(nameof(PlaneCount));
        OnPropertyChanged(nameof(PlaneLetters));
        Rebuild();
    });

    private void OnRowsChanged() => PostUi(() =>
    {
        OnPropertyChanged(nameof(ProgressText));
        Bump();
    });

    private void Rebuild()
    {
        var program = _programSource.Program;
        var generation = ++_generation;

        if (program is null || program.Tokens.Count == 0)
        {
            ApplyEmpty();
            return;
        }

        // Small programs build synchronously (deterministic, no flicker);
        // large ones go to the thread pool — a 100k-line program means
        // millions of segments, far too slow for the UI thread. Staleness is
        // handled by the generation guard (a newer Rebuild bumps it and the
        // stale apply is discarded) — cooperative cancellation inside the
        // CPU-bound walk would complicate ToolpathBuilder for no visible gain.
        if (program.Tokens.Count <= AsyncBuildThreshold)
        {
            ApplyBuild(BuildGeometry(program, _machine), generation);
            return;
        }

        // A big build runs on the thread pool — the canvas must not keep the
        // previous (cleared) state's "No program loaded." notice up for the
        // whole build: say what is actually happening. The header's block
        // counter already reflects the loaded program (RowsChanged ticks).
        SetStatus("module.plotter.building", "Plotting…");
        Bump();

        var machine = _machine;
        _ = Task.Run(() =>
        {
            try
            {
                var geometry = BuildGeometry(program, machine);
                PostUi(() =>
                {
                    // A newer rebuild superseded this one (rapid program
                    // switches) — discard the stale result.
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
                    SetStatus("module.plotter.buildFailed", "Program could not be plotted.");
                });
            }
        });
    }

    private void ApplyEmpty()
    {
        Planes = [];
        SourceSegments = [];
        _bounds = [];
        _lineToRow = new Dictionary<uint, int>();
        SetStatus("module.plotter.noProgram", "No program loaded.");
        GeometryVersion++;
        OnPropertyChanged(nameof(GeometryVersion));
        Bump();
    }

    private void ApplyBuild(Geometry geometry, int generation)
    {
        if (generation != _generation)
            return;
        Planes = geometry.Planes;
        SourceSegments = geometry.SourceSegments;
        _bounds = geometry.Bounds;
        _lineToRow = geometry.LineToRow;
        SetStatus(geometry.Status?.Key, geometry.Status?.Fallback, geometry.Status?.Args ?? []);
        GeometryVersion++;
        OnPropertyChanged(nameof(GeometryVersion));
        Bump();
    }

    /// <summary>The immutable geometry snapshot produced by one build — built
    /// off the UI thread for large programs, applied atomically.</summary>
    private sealed record Geometry(
        IReadOnlyList<IReadOnlyList<PlotSegment>> Planes,
        IReadOnlyList<ToolpathSegment> SourceSegments,
        double[][] Bounds,
        Dictionary<uint, int> LineToRow,
        (string Key, string Fallback, object?[] Args)? Status);

    private Geometry BuildGeometry(GCodeProgram program, MachineKinematics machine)
    {
        var emptyPlanes = new IReadOnlyList<PlotSegment>[machine.Planes.Count];
        for (var i = 0; i < emptyPlanes.Length; i++)
            emptyPlanes[i] = Array.Empty<PlotSegment>();

        var segments = ToolpathBuilder.Build(program.Tokens);
        if (segments.Count == 0)
            return new Geometry(emptyPlanes, segments, [], new Dictionary<uint, int>(),
                Status: ("module.plotter.noMotion", "Program contains no motion.", []));

        // A machine whose planes reach beyond the axes the program (via
        // the controller's axis count) actually provides must not be sliced —
        // the slice would index out of range. Drop those planes and say why;
        // planes that fit still render. The test is the plane's HIGHEST index,
        // because a plane's indices are in plane order and may be non-monotonic.
        var available = segments[0].From.Length;
        var dropped = Enumerable.Range(0, machine.Planes.Count)
            .Select(p => HighestIndex(machine.PlaneIndices(p)))
            .Any(maxIndex => maxIndex >= available);
        (string Key, string Fallback, object?[] Args)? status = dropped
            ? ("module.plotter.axisMismatch",
                "Machine needs {0} axes; program has {1}. Unrenderable planes are hidden.",
                new object?[] { machine.DeclaredAxes, available })
            : null;

        var bounds = ComputeBounds(segments);
        var planes = SlicePlanes(segments, machine, available);

        // Line → row: rows are index-aligned with the program entries, but the
        // KEY is the parser's block counter — what token/segment LineNumbers
        // actually carry. % and ;-header rows never reach the parser's counter,
        // so the k-th sendable row is the k-th block; keying by FileLine would
        // swallow a leading block into an Idle header row (foam programs do).
        var lineToRow = new Dictionary<uint, int>();
        var entries = program.Entries;
        uint block = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (!entries[i].IsSendable)
                continue;
            lineToRow[++block] = i;
        }

        return new Geometry(planes, segments, bounds, lineToRow, status);
    }

    /// <summary>A plane's highest machine axis index, or <c>-1</c> for a plane
    /// with no axes. <see cref="MachineKinematics.PlaneIndices"/> is in plane
    /// order and may be NON-MONOTONIC (the bug-report foam layout's plane 2 is
    /// <c>[3, 2]</c>), so "does this plane fit the program frame" is a MAX over
    /// the list — <c>indices[^1]</c> silently answered for the wrong axis.</summary>
    private static int HighestIndex(IReadOnlyList<int> planeAxisIndices)
    {
        var max = -1;
        foreach (var index in planeAxisIndices)
        {
            if (index > max)
                max = index;
        }
        return max;
    }

    private static IReadOnlyList<IReadOnlyList<PlotSegment>> SlicePlanes(
        IReadOnlyList<ToolpathSegment> segments, MachineKinematics machine, int availableAxes)
    {
        var planes = new List<PlotSegment>[machine.Planes.Count];
        for (var p = 0; p < planes.Length; p++)
            planes[p] = [];

        var indices = Enumerable.Range(0, planes.Length)
            .Select(p => machine.PlaneIndices(p).ToArray())
            .ToArray();

        foreach (var s in segments)
        {
            for (var p = 0; p < planes.Length; p++)
            {
                var idx = indices[p];
                if (idx.Length == 0 || HighestIndex(idx) >= availableAxes)
                    continue; // plane needs axes the program does not provide
                var from = new double[idx.Length];
                var to = new double[idx.Length];
                var moves = false;
                for (var k = 0; k < idx.Length; k++)
                {
                    from[k] = s.From[idx[k]];
                    to[k] = s.To[idx[k]];
                    if (Math.Abs(from[k] - to[k]) > 1e-9)
                        moves = true;
                }

                if (moves)
                    planes[p].Add(new PlotSegment(from, to, s.Rapid, s.LineNumber));
            }
        }

        return planes;
    }

    private static double[][] ComputeBounds(IReadOnlyList<ToolpathSegment> segments)
    {
        var count = segments.Count > 0 ? segments[0].From.Length : 0;
        var bounds = new double[count][];
        for (var i = 0; i < count; i++)
            bounds[i] = [double.MaxValue, double.MinValue];

        foreach (var s in segments)
        {
            for (var i = 0; i < count; i++)
            {
                if (s.From[i] < bounds[i][0]) bounds[i][0] = s.From[i];
                if (s.From[i] > bounds[i][1]) bounds[i][1] = s.From[i];
                if (s.To[i] < bounds[i][0]) bounds[i][0] = s.To[i];
                if (s.To[i] > bounds[i][1]) bounds[i][1] = s.To[i];
            }
        }

        for (var i = 0; i < count; i++)
            if (bounds[i][0] > bounds[i][1])
                bounds[i] = [0, 0];
        return bounds;
    }

    private void PostUi(Action action) => PlotterUi.Post(action);

    public void Dispose()
    {
        _programSource.ProgramChanged -= OnProgramChanged;
        _programSource.RowsChanged -= OnRowsChanged;
        _localization.Changed -= OnLocalizationChanged;
        _kinematics.Changed -= OnKinematicsChanged;
        // Only the provider this view model built: an injected one is the app's
        // DI singleton and outlives every view.
        if (_ownsKinematics && _kinematics is IDisposable disposable)
            disposable.Dispose();
        _stateSubscription.Dispose();
    }
}
