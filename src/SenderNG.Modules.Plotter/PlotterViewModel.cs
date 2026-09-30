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
/// machine's carriage planes (MachineType.Planes — positional axis indices),
/// and tracks the run frontier (row states) plus the live tool position for
/// the overlay. UI-agnostic data; the view draws.
/// <para>
/// Nothing here throws on degenerate input: no program, a program without
/// motion, or a machine type whose planes reach beyond the axes the program
/// actually moves all degrade to <see cref="StatusText"/> + empty geometry —
/// the view refuses to render instead of crashing.
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
    private readonly SettingsService _settings;
    private readonly LocalizationService _localization;
    private readonly IDisposable _stateSubscription;
    private int _generation;

    private MachineType _machineType;

    /// <summary>Per-plane axis→world mapping. Identity until the settings VM
    /// (Task 8) pushes a stored section in — which is exactly what the renderer
    /// assumed before this section existed.</summary>
    private List<PlotterPlaneGeometry> _planeGeometries = [];

    private double[][] _bounds = [];
    private Dictionary<uint, int> _lineToRow = new();

    public PlotterViewModel(
        ILoadedProgramSource programSource,
        IGrblService grbl,
        SettingsService settings,
        LocalizationService localization)
    {
        _programSource = programSource;
        _grbl = grbl;
        _settings = settings;
        _localization = localization;
        _machineType = MachineTypeCatalog.Resolve(_settings.Load().MachineTypeId);
        // The mapping is indexed by plane, so it must match the resolved type
        // before anything can read it. Assigned, NOT SetPlaneGeometries: no
        // subscriber exists yet, so the setter's three notifications would be
        // pure noise, and GeometryVersion would start at 2 instead of 1 (the
        // ctor's own empty state) — a geometry event for a state that never
        // existed, which the version's contract does not allow.
        _planeGeometries = IdentityMappings(_machineType);

        _programSource.ProgramChanged += OnProgramChanged;
        _programSource.RowsChanged += OnRowsChanged;
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
    /// maps onto MachineType.Planes[k]'s axis indices (<see cref="PlaneAxisIndices"/>).
    /// The list length matches <see cref="PlaneCount"/>; a plane whose axis
    /// indices reach beyond the program's available axes stays empty (dropped)
    /// and <see cref="StatusText"/> explains why.</summary>
    public IReadOnlyList<IReadOnlyList<PlotSegment>> Planes { get; private set; } = [];

    /// <summary>The unsliced toolpath (one entry per source motion) — the foam
    /// isometric loft draws both carriages plus the connecting wire from this,
    /// which per-plane slices cannot provide (they drop per-plane zero moves
    /// independently, breaking point pairing).</summary>
    public IReadOnlyList<ToolpathSegment> SourceSegments { get; private set; } = [];

    /// <summary>Carriage-plane count of the active machine type (1 = mill-style,
    /// 2 = foam) — drives the view's one-canvas vs two-canvas layout.</summary>
    public int PlaneCount => _machineType.Planes.Count;

    /// <summary>Axis indices (controller axis order) backing plane <paramref name="plane"/> —
    /// the view maps segment/LivePosition slots through these.</summary>
    public int[] PlaneAxisIndices(int plane) => _machineType.PlaneAxisIndices(plane);

    /// <summary>The active per-plane mappings, in plane order.</summary>
    public IReadOnlyList<PlotterPlaneGeometry> PlaneGeometries => _planeGeometries;

    /// <summary>Plane <paramref name="plane"/>'s mapping, falling back to identity
    /// for that plane's role count when none is set or the stored one is
    /// malformed.</summary>
    public PlotterPlaneGeometry GeometryFor(int plane)
    {
        var roles = plane >= 0 && plane < _machineType.Planes.Count
            ? _machineType.Planes[plane].Roles.Count
            : 2;
        var stored = plane >= 0 && plane < _planeGeometries.Count ? _planeGeometries[plane] : null;
        return PlotterPlaneMapper.For(stored, roles);
    }

    /// <summary>The JOB's world XY envelope: every plane's extent, read through
    /// its own mapping and UNIONED. Both foam carriage planes work inside the
    /// same envelope, so the two tiles must measure the same rectangle — see
    /// <see cref="PlotterPlaneMapper.TryJobExtent"/>, which does the arithmetic
    /// and owns the rules about which planes may contribute.
    /// <para>The per-plane inputs are resolved HERE, in one place, because the
    /// machine type owns the plane/axis split and the stored mappings are
    /// untrusted input: <see cref="GeometryFor"/> degrades a missing or
    /// malformed mapping to identity per plane, so a settings file can neither
    /// widen the envelope nor narrow it to nothing.
    /// <para>False means no plane could be read, never a fabricated envelope;
    /// the caller substitutes its own fallback, as it did before this
    /// existed.</summary>
    public bool TryJobEnvelope(out (double MinX, double MinY, double MaxX, double MaxY) envelope)
    {
        var planes = PlaneCount;
        var geometries = new PlotterPlaneGeometry?[planes];
        var indices = new int[planes][];
        for (var p = 0; p < planes; p++)
        {
            geometries[p] = GeometryFor(p);
            indices[p] = PlaneAxisIndices(p);
        }
        return PlotterPlaneMapper.TryJobExtent(geometries, indices, Bounds, out envelope);
    }

    /// <summary>Replaces the mapping set and repaints. The App Settings leaf and the
    /// plot view share this one list, so a change in either is visible in both.
    /// <para>A set that already matches what is applied is a NO-OP. The module
    /// pushes the whole settings section on every chrome toggle (grid, origin,
    /// envelope, run-state), and GeometryVersion is the "the toolpath moved,
    /// drop the view and re-fit" signal: bumping it for an identical mapping is
    /// the zoom snapping back to fitted the moment the user toggles a visibility
    /// button. Nothing has changed, so nothing bumps.</para>
    /// <para>The copy must be a SNAPSHOT, not a reference into
    /// <paramref name="planes"/>: the settings leaf edits its geometry objects
    /// IN PLACE (<c>PlotterSettingsViewModel.ApplyRow</c> replaces the stored
    /// plane's <c>WorldAxes</c> array, <c>ApplyOrigin</c> writes into the stored
    /// array), so aliasing them would make a later in-place edit read as
    /// "already applied" here and never reach the view.</para></summary>
    public void SetPlaneGeometries(IReadOnlyList<PlotterPlaneGeometry> planes)
    {
        if (SameMappings(_planeGeometries, planes))
            return;
        _planeGeometries = [.. planes.Select(p => new PlotterPlaneGeometry
        {
            WorldAxes = [.. p.WorldAxes],
            Origin = [.. p.Origin],
        })];
        // The letters are a header read, the geometry version the re-fit
        // (Bounds moved, so the fit zoom must move with it), the plane letters
        // also the slot layout rebuild (a plane can lose its world Y and stop
        // being drawable) and Bump the plain redraw tick.
        OnPropertyChanged(nameof(PlaneLetters));
        GeometryVersion++;
        OnPropertyChanged(nameof(GeometryVersion));
        Bump();
    }

    /// <summary>Two mapping sets are the same iff every plane maps the same
    /// world axes to the same origin offsets. Arrays compare BY VALUE, not by
    /// reference — the snapshot above and the settings list are deliberately
    /// different objects with the same content.</summary>
    private static bool SameMappings(
        IReadOnlyList<PlotterPlaneGeometry> a, IReadOnlyList<PlotterPlaneGeometry> b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
            if (!a[i].WorldAxes.SequenceEqual(b[i].WorldAxes)
                || !a[i].Origin.SequenceEqual(b[i].Origin))
                return false;
        return true;
    }

    /// <summary>Every plane back to identity — the pre-section mapping.</summary>
    public void ResetPlaneGeometries() => SetPlaneGeometries(IdentityMappings(_machineType));

    /// <summary>The identity mapping for each plane of <paramref name="type"/> —
    /// the state a machine type's mapping starts in, and the fallback
    /// <see cref="GeometryFor"/> degrades to.</summary>
    private static List<PlotterPlaneGeometry> IdentityMappings(MachineType type) =>
        [.. type.Planes.Select(p => PlotterPlaneMapper.Identity(p.Roles.Count))];

    /// <summary>Display letters per plane, naming the <em>world</em> axes that
    /// plane feeds — the same mapping the panes draw, so the header cannot
    /// disagree with the picture.</summary>
    public string[] PlaneLetters => _machineType.Planes
        .Select((plane, p) => new string(
            GeometryFor(p).WorldAxes.Select(AxisLetter).ToArray()))
        .ToArray();

    /// <summary>Per-axis bounds [min,max] over the whole toolpath (program frame).</summary>
    public double[][] Bounds => _bounds;

    /// <summary>True when there is anything to draw — the view's license to
    /// render; false means draw the <see cref="StatusText"/> notice only.</summary>
    public bool HasGeometry => Planes.Any(p => p.Count > 0);

    /// <summary>Human-readable reason there is nothing (or something partial)
    /// to draw: no program, no motion in the program, or a machine type whose
    /// planes need more axes than the program provides. Null when geometry is
    /// complete and renderable.</summary>
    public string? StatusText { get; private set; }

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
        // Profile may have changed since construction (a machine switch clears
        // the program; the type rides the active profile).
        _machineType = MachineTypeCatalog.Resolve(_settings.Load().MachineTypeId);
        // The stored mapping is deliberately NOT reset here: this fires on
        // every program change, and the settings leaf (Task 8) only pushes a
        // mapping on its own change events — resetting would silently drop a
        // stored mapping on the next program load. A mapping from the previous
        // type is harmless anyway: GeometryFor validates each entry against
        // that plane's role count and falls back to identity.
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
            ApplyBuild(BuildGeometry(program, _machineType), generation);
            return;
        }

        // A big build runs on the thread pool — the canvas must not keep the
        // previous (cleared) state's "No program loaded." notice up for the
        // whole build: say what is actually happening. The header's block
        // counter already reflects the loaded program (RowsChanged ticks).
        StatusText = _localization.Resolve(
            "module.plotter.building", "Plotting…");
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
                    StatusText = _localization.Resolve(
                        "module.plotter.buildFailed", "Program could not be plotted.");
                    OnPropertyChanged(nameof(StatusText));
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
        StatusText = _localization.Resolve(
            "module.plotter.noProgram", "No program loaded.");
        OnPropertyChanged(nameof(StatusText));
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
        StatusText = geometry.Status;
        OnPropertyChanged(nameof(StatusText));
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
        string? Status);

    private Geometry BuildGeometry(GCodeProgram program, MachineType type)
    {
        var emptyPlanes = new IReadOnlyList<PlotSegment>[type.Planes.Count];
        for (var i = 0; i < emptyPlanes.Length; i++)
            emptyPlanes[i] = Array.Empty<PlotSegment>();

        var segments = ToolpathBuilder.Build(program.Tokens);
        if (segments.Count == 0)
            return new Geometry(emptyPlanes, segments, [], new Dictionary<uint, int>(),
                _localization.Resolve(
                    "module.plotter.noMotion", "Program contains no motion."));

        // A machine type whose planes reach beyond the axes the program (via
        // the controller's axis count) actually provides must not be sliced —
        // the slice would index out of range. Drop those planes and say why;
        // planes that fit still render.
        var available = segments[0].From.Length;
        var dropped = type.Planes
            .Select((_, p) => type.PlaneAxisIndices(p)[^1])
            .Any(maxIndex => maxIndex >= available);
        var status = dropped
            ? string.Format(
                CultureInfo.InvariantCulture,
                _localization.Resolve(
                    "module.plotter.axisMismatch",
                    "Machine needs {0} axes; program has {1}. Unrenderable planes are hidden."),
                type.AxisCount, available)
            : null;

        var bounds = ComputeBounds(segments);
        var planes = SlicePlanes(segments, type, available);

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

    /// <summary>Controller letter for a flattened axis index — the controller's
    /// handshake letters win, the preset's conventional letters are the fallback.</summary>
    private char AxisLetter(int axisIndex)
    {
        var letters = _grbl.Capabilities.AxisLetters;
        if (!string.IsNullOrEmpty(letters) && axisIndex < letters.Length)
            return letters[axisIndex];
        return _machineType.ConventionalLetter(axisIndex);
    }

    private static IReadOnlyList<IReadOnlyList<PlotSegment>> SlicePlanes(
        IReadOnlyList<ToolpathSegment> segments, MachineType type, int availableAxes)
    {
        var planes = new List<PlotSegment>[type.Planes.Count];
        for (var p = 0; p < planes.Length; p++)
            planes[p] = [];

        var indices = type.Planes.Select((_, p) => type.PlaneAxisIndices(p)).ToArray();

        foreach (var s in segments)
        {
            for (var p = 0; p < planes.Length; p++)
            {
                var idx = indices[p];
                if (idx[^1] >= availableAxes)
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
        _stateSubscription.Dispose();
    }
}
