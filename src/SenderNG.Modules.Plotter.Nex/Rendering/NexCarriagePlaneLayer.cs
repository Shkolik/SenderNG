/*
 * NexCarriagePlaneLayer.cs - Nex carriage plane layer: segments + run states
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
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Scene;
using SenderNG.Modules.Plotter.Nex.Scene;

namespace SenderNG.Modules.Plotter.Nex.Rendering;

/// <summary>
/// One carriage-plane layer rendered as a swap-pumped static line list.
/// <see cref="AddSegment"/> and <see cref="SetSegmentRunState"/> mutate CPU
/// staging lists only (O(1) per segment, no GPU work) — run-state coloring
/// stays the Task 8 perf lever. The GPU gets a long-lived STATIC geometry
/// through <see cref="NexPlotScene.PumpCarriagePlanes"/> (a low-Hz pump, see
/// <see cref="PlotterNexView"/>): each pump re-uploads the staging into the SAME
/// registered device buffer via the engine's staged FIFO upload.
///
/// Why in place instead of fresh-swapping: the engine packs every static draw's
/// <c>FirstIndex</c> from <c>IndexOffset</c> into a shared static index buffer,
/// and REPACKS (reassigning <c>IndexOffset</c> for all static geometries)
/// whenever ANY static geometry is Added. A layer that swapped in a fresh static
/// geometry on every run-state grant shifted the offsets baked into the other
/// static draws (tool cones, loft sheet), which kept reading stale
/// <c>FirstIndex</c> and drew garbage indices from the shared buffer — the giant
    /// red fan. So the plane and the executed overlay register ONE static geometry
    /// each at the build-wave pump (sized to the full segment budget) and never
    /// Add/dispose again: the static pool is packed exactly once, before the run
    /// starts. (The overlay used to register lazily at the FIRST executed segment
    /// — a mid-run Add that repacked the pool and mangled the sheet + one spike
    /// at the first move; it now registers at the same pump as the plane.)
///
/// This deliberately NEVER writes host-visible mapped buffers that in-flight
/// frames read — in-place dynamic-color uploads raced the live render loop and
/// produced VK_ERROR_DEVICE_LOST (helix engine 1.3.0-alpha.41, driver
/// 32.0.15.9608). Colors are flat motion/state tints (no alpha — the Nex line
/// shader's alpha handling is unverified, so all colors are opaque).
/// </summary>
public sealed class NexCarriagePlaneLayer : ICarriagePlaneLayer
{
    // Skia-plotter-compatible palette (opaque Color4 values).
    private static readonly Color4 CutColor = new(0.5f, 0.5f, 0.5f, 1f);
    internal static readonly Color4 RapidColor = new(0.9f, 0.12f, 0.12f, 1f);
    private static readonly Color4 ExecutedColor = new(0.106f, 0.62f, 0.243f, 1f);
    private static readonly Color4 InFlightColor = new(0.878f, 0.588f, 0.125f, 1f);
    private static readonly Color4 ErrorColor = new(0.75f, 0.19f, 0.19f, 1f);

    internal const float BaseLineThickness = 3.2f;
    internal const float ExecutedLineThickness = 3.2f;

    internal static bool IsExecuted(RunState state) => state == RunState.Executed;

    /// <summary>Parked position for parked-degenerate padding lines — far below
    /// the table (mirrors <c>NexPlotTool.ParkedPosition</c>) so a padded, zero-length
    /// line is invisible and never affects the content framing (the crust is only
    /// ever written into the executed overlay and the loft loop, neither of which
    /// is a content node).</summary>
    internal static readonly Vector3 ParkedPosition = new(0f, -50_000f, 0f);

    private readonly Engine _engine;
    private readonly Geometry _geometry;
    private readonly LineNode _node;
    private readonly List<MotionType> _motionTypes = [];
    private readonly Geometry _doneGeometry;
    private readonly LineNode _doneNode;
    private readonly List<bool> _executed = [];
    private readonly List<RunState> _appliedStates = [];
    private readonly RebuildGate _rebuildGate = new();
    // Scene run-state visibility gate (SetRunStateVisible). When false every
    // segment stages its base Rapid/Cut color regardless of the applied state;
    // the executed overlay node is hidden by the scene.
    private bool _runStateVisible = true;
    private Geometry? _staticPlane;
    private Geometry? _staticDone;
    private int _planeCapacity;
    private int _doneCapacity;
    private int _planeColorWrites;

    /// <summary>Create the layer under <paramref name="root"/> and attach its line nodes.
    /// The planes are empty until the first <see cref="Pump"/>.</summary>
    public NexCarriagePlaneLayer(Node root, Engine engine)
    {
        _engine = engine;
        // Staging containers only — never registered with the engine, never
        // attached to a node, so nothing here produces GPU buffers. All GPU
        // content arrives via Pump() as fresh static geometries.
        _geometry = new Geometry(Topology.Line, isDynamic: true);
        _node = root.World.CreateLineNode("carriage-plane");
        _node.LineThickness = BaseLineThickness;
        _node.LineColor = Color4.White;
        root.AddChild(_node);

        _doneGeometry = new Geometry(Topology.Line, isDynamic: true);
        _doneNode = root.World.CreateLineNode("plane-done");
        _doneNode.LineThickness = ExecutedLineThickness;
        _doneNode.LineColor = ExecutedColor;
        root.AddChild(_doneNode);
    }

    /// <summary>The underlying line node (for scene framing and removal).</summary>
    internal LineNode Node => _node;

    /// <summary>The executed-overlay line node (for scene framing and removal).</summary>
    internal LineNode DoneNode => _doneNode;

    /// <inheritdoc/>
    public void AddSegment(LineSegment seg, MotionType type)
    {
        var color = BaseColor(type);
        _geometry.Vertices.Add(new Vector4(seg.From, 1f));
        _geometry.Vertices.Add(new Vector4(seg.To, 1f));
        var v = color.ToVector4();
        _geometry.VertexColors.Add(v);
        _geometry.VertexColors.Add(v);
        _motionTypes.Add(type);
        _executed.Add(false);
        _appliedStates.Add(RunState.Pending);
        _rebuildGate.MarkDirty();
    }

    /// <inheritdoc/>
    public void SetSegmentRunState(int segmentIndex, RunState state)
    {
        if (segmentIndex < 0 || segmentIndex >= _motionTypes.Count)
            return;
        if (_appliedStates[segmentIndex] == state)
            return;
        _appliedStates[segmentIndex] = state;
        _planeColorWrites++;
        var color = !_runStateVisible || state == RunState.Pending
            ? BaseColor(_motionTypes[segmentIndex])
            : StateColor(state);
        var v = color.ToVector4();
        _geometry.VertexColors[segmentIndex * 2] = v;
        _geometry.VertexColors[segmentIndex * 2 + 1] = v;

        // Overlay bookkeeping: append the done segment exactly once per
        // Pending/InFlight → Executed transition; the flag flip keeps window
        // re-visits (same segment ticked across rows) idempotent. Rewind
        // (Executed → any other state) rebuilds the overlay from the flags.
        var wasExecuted = _executed[segmentIndex];
        var nowExecuted = IsExecuted(state);
        _executed[segmentIndex] = nowExecuted;
        if (nowExecuted && !wasExecuted)
            AppendDoneSegment(segmentIndex);
        else if (!nowExecuted && wasExecuted)
            RebuildFromExecuted();
        // GPU content never changes here — mark the swap-pump pending instead.
        _rebuildGate.MarkDirty();
    }

    /// <summary>Gate the whole plane's run-state tinting (scene ShowRunState
    /// toggle). Hidden: every segment restages its base Rapid/Cut color, so the
    /// swap-pump ships a plain toolpath; the executed overlay node visibility is
    /// handled by the scene. Reapplied from the stored state set, so re-visiting
    /// a run window after toggling still matches what the pump last shipped.</summary>
    internal void SetRunStateVisible(bool visible)
    {
        if (_runStateVisible == visible)
            return;
        _runStateVisible = visible;
        for (var s = 0; s < _appliedStates.Count; s++)
        {
            var state = _appliedStates[s];
            var color = !visible || state == RunState.Pending
                ? BaseColor(_motionTypes[s])
                : StateColor(state);
            var v = color.ToVector4();
            _geometry.VertexColors[s * 2] = v;
            _geometry.VertexColors[s * 2 + 1] = v;
        }
        _rebuildGate.MarkDirty();
    }

    /// <summary>Append one done segment (two vertices + two colors) to the
    /// green thickness overlay staging list.</summary>
    private void AppendDoneSegment(int segmentIndex)
    {
        var v = ExecutedColor.ToVector4();
        _doneGeometry.Vertices.Add(_geometry.Vertices[segmentIndex * 2]);
        _doneGeometry.Vertices.Add(_geometry.Vertices[segmentIndex * 2 + 1]);
        _doneGeometry.VertexColors.Add(v);
        _doneGeometry.VertexColors.Add(v);
    }

    private static Color4 BaseColor(MotionType type) =>
        type == MotionType.Rapid ? RapidColor : CutColor;

    private static Color4 StateColor(RunState state) => state switch
    {
        RunState.Executed => ExecutedColor,
        RunState.InFlight => InFlightColor,
        RunState.Error => ErrorColor,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <inheritdoc/>
    public void RebuildExecuted() => RebuildFromExecuted();

    /// <summary>Clear and re-append the done overlay from the current executed
    /// set (rewind only; rare O(n)).</summary>
    private void RebuildFromExecuted()
    {
        _doneGeometry.Vertices.Clear();
        _doneGeometry.VertexColors.Clear();
        for (var i = 0; i < _executed.Count; i++)
        {
            if (_executed[i])
                AppendDoneSegment(i);
        }
        _rebuildGate.MarkDirty();
    }

    /// <summary>In-place static pump: reconcile the CPU staging lists to the GPU
    /// as ONE long-lived STATIC geometry per list. Rebuilds are coalesced by
    /// <see cref="RebuildGate"/> (burned at the mutation APIs): a clean layer does
    /// nothing, and bursts collapse to at most one rebuild per minimum interval,
    /// so a large toolpath is not re-uploaded on every run-state tick. The plane
    /// and the executed overlay register their geometry ONCE (at first content,
    /// sized to the full segment budget); every later rebuild only re-uploads the
    /// staging into the same device buffer via the engine's staged FIFO upload.
    /// No geometry is Added during a run, so the shared static index buffer never
    /// repacks and other static draws keep their baked <c>IndexOffset</c>.</summary>
    public void Pump()
    {
        if (!_rebuildGate.RebuildDue(DateTime.UtcNow))
            return;

        PumpPlane();
        PumpDone();
    }

    private void PumpPlane()
    {
        var staged = _geometry;
        var n = staged.Vertices.Count;
        if (n > _planeCapacity)
        {
            // First content, or the staging outgrew the registered geometry
            // (the app stages every segment before the first pump, so this runs
            // at most once per layer — at build, never per run-state tick).
            var fresh = BuildStatic(staged);
            _staticPlane?.Dispose();
            _staticPlane = fresh;
            _planeCapacity = n;
            _engine.Add(_staticPlane);
            // Count changed — re-set the reference so the draw stream refreshes
            // its LineCount (the chrome's record for the same reason).
            _node.Geometry = _staticPlane;
        }
        else if (_staticPlane is not null)
        {
            if (n == 0)
            {
                // Content emptied after registration (not an app path): hide the
                // plane but keep the registration for the next refill.
                _node.Geometry = null;
            }
            else
            {
                // Stable count — rewrite the same vertices/colors into the same
                // device buffer. No index offset or draw record moves.
                RefillLines(_staticPlane, staged, _planeCapacity, CutColor.ToVector4());
                _staticPlane.UpdateBuffers(_engine.Context).CheckResult();
            }
        }
    }

    private void PumpDone()
    {
        var staged = _doneGeometry;
        var n = staged.Vertices.Count;
        if (_staticDone is null)
        {
            // First pump (the build wave): register the FULL-potential overlay
            // NOW, never lazily at the first executed segment. The done set can
            // only ever be a subset of the plane's segments, so the plane's
            // registered budget is its capacity; the empty tail stays
            // parked-degenerate. A static Add during the run would repack the
            // shared static index buffer and shift the offsets baked into the
            // cone/sheet draws → a one-frame spike + mangled loft at the first
            // move after Start.
            if (_staticPlane is null || _planeCapacity == 0)
            {
                // No plane budget yet (empty layer); keep the overlay hidden.
                _doneNode.Geometry = null;
                return;
            }
            _doneCapacity = _planeCapacity;
            _staticDone = NewFixedStatic(_doneCapacity, ExecutedColor.ToVector4());
            _engine.Add(_staticDone);
            _doneNode.Geometry = _staticDone;
        }
        else if (n > _doneCapacity)
        {
            // Paranoid growth (the overlay exceeded its budget — impossible in
            // the app); re-register once at the new capacity.
            _doneCapacity = n;
            var fresh = NewFixedStatic(_doneCapacity, ExecutedColor.ToVector4());
            _staticDone.Dispose();
            _staticDone = fresh;
            _engine.Add(_staticDone);
            _doneNode.Geometry = _staticDone;
        }

        // Rewrite the active prefix in place, parked tail fills the rest.
        // LineCount stays constant, so no reference re-set.
        RefillLines(_staticDone, staged, _doneCapacity, ExecutedColor.ToVector4());
        _staticDone.UpdateBuffers(_engine.Context).CheckResult();
    }

    /// <summary>Release the static geometries owned by this layer. Nodes are
    /// disposed by the scene.</summary>
    internal void ReleaseGeometries()
    {
        _staticPlane?.Dispose();
        _staticPlane = null;
        _staticDone?.Dispose();
        _staticDone = null;
    }

    /// <summary>Copy one staging list into a fresh static line geometry.
    /// Static lines draw through the engine's shared static index buffer
    /// (FirstIndex = IndexOffset), so identity indices are required and the
    /// draw is skipped entirely when the index count is zero.
    /// Sources are snapshotted into plain arrays BEFORE the engine ctor: its
    /// AddRange fast path memcpy's FastList sources via FastList.CopyTo, and a
    /// writer growing a source between Count and CopyTo overruns the freshly
    /// sized destination ("Destination array was not long enough"). Fixed arrays
    /// cannot grow mid-copy; the element-read snapshot is race-benign (worst
    /// case a torn tail, adjusted below so colors exactly match vertices — the
    /// engine ctor Debug.Fails otherwise, and the renderer only ever reads as
    /// many colors as there are vertices anyway).</summary>
    internal static Geometry BuildStatic(Geometry staging)
    {
        var vertices = Snapshot(staging.Vertices);
        var colors = Snapshot(staging.VertexColors, vertices.Length);
        var n = vertices.Length;
        if (n == 0)
            return new Geometry(Topology.Line, isDynamic: false);
        if (colors.Length > n)
            colors = colors.AsSpan(0, n).ToArray();
        var indices = new uint[n];
        for (var i = 0; i < n; i++)
            indices[i] = (uint)i;
        return new Geometry(vertices, indices, colors, Topology.Line, isDynamic: false);
    }

    /// <summary>Create an EMPTY static line geometry with a FIXED
    /// <paramref name="capacity"/> vertices (identity indices, parked-degenerate
    /// vertices, one tint per vertex) registered once and re-uploaded in place by
    /// <see cref="RefillLines"/> afterwards. The fixed-size buffer keeps the
    /// draw record (FirstIndex, LineCount) constant for the geometry's lifetime —
    /// the parking never produces pixels (zero-length lines).</summary>
    internal static Geometry NewFixedStatic(int capacity, Vector4 parkColor)
    {
        var g = new Geometry(Topology.Line, isDynamic: false);
        if (capacity <= 0)
            return g;
        g.Vertices.Capacity = capacity;
        g.VertexColors.Capacity = capacity;
        g.Indices.Capacity = capacity;
        var park = new Vector4(ParkedPosition, 1f);
        for (var i = 0; i < capacity; i++)
        {
            g.Vertices.Add(park);
            g.VertexColors.Add(parkColor);
            g.Indices.Add((uint)i);
        }
        return g;
    }

    /// <summary>Overwrite the fixed-size static <paramref name="target"/>
    /// (which must have exactly <paramref name="capacity"/> vertices registered)
    /// with the first <paramref name="capacity"/> vertices of
    /// <paramref name="source"/>, padding any tail with parked-degenerate lines
    /// so the vertex count NEVER moves. Marks the buffers dirty for the
    /// caller's staged in-place upload (<c>Geometry.UpdateBuffers</c>). Used by
    /// the layer and the loft loop so a run never Adds a fresh static geometry
    /// and the shared static index buffer never repacks.</summary>
    internal static void RefillLines(Geometry target, Geometry source, int capacity, Vector4 parkColor)
    {
        var n = Math.Min(source.Vertices.Count, capacity);
        var colors = source.VertexColors;
        var parkVertex = new Vector4(ParkedPosition, 1f);
        for (var i = 0; i < capacity; i++)
        {
            if (i < n)
            {
                target.Vertices[i] = source.Vertices[i];
                target.VertexColors[i] = i < colors.Count ? colors[i] : parkColor;
            }
            else
            {
                target.Vertices[i] = parkVertex;
                target.VertexColors[i] = parkColor;
            }
        }
        target.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
    }

    /// <summary>Passive read of one staging list into a plain array. Iteration
    /// re-reads the list's size and backing array per step (growth-only), so
    /// concurrent appends can only produce a tear, never a copy error. A torn
    /// tail shorter than <paramref name="minLength"/> is padded with the cut
    /// tint so the renderer's per-vertex color index never runs off the list.</summary>
    private static Vector4[] Snapshot(IEnumerable<Vector4> staging, int minLength = 0)
    {
        var list = staging is ICollection<Vector4> c ? new List<Vector4>(c.Count) : new List<Vector4>();
        foreach (var v in staging)
            list.Add(v);
        if (list.Count < minLength)
        {
            var pad = CutColor.ToVector4();
            while (list.Count < minLength)
                list.Add(pad);
        }
        return list.ToArray();
    }

    /// <summary>Diagnostic (temporary): executed-overlay vertex count (2 per
    /// done segment).</summary>
    internal int ExecutedVerticesCount => _doneGeometry.Vertices.Count;

    /// <summary>Diagnostic (temporary): executed overlay accounting.</summary>
    public string DumpExecutedOverlay() =>
        $"done verts={_doneGeometry.Vertices.Count} executedSegs={_executed.Count(x => x)} planeWrites={_planeColorWrites}";
}