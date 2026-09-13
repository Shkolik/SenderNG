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
/// stays the Task 8 perf lever. The GPU gets a fresh STATIC geometry through
/// <see cref="NexPlotScene.PumpCarriagePlanes"/> (a low-Hz pump, see
/// <see cref="PlotterNexView"/>): staging is copied into a brand-new static
/// geometry, registered via the engine's staged buffer upload, and swapped in
/// as the node's geometry. This deliberately NEVER writes host-visible mapped
/// buffers that in-flight frames read — in-place dynamic-color uploads raced
/// the live render loop and produced VK_ERROR_DEVICE_LOST (helix engine
/// 1.3.0-alpha.41, driver 32.0.15.9608).
/// Colors are flat motion/state tints (no alpha — the Nex line shader's
/// alpha handling is unverified, so all colors are opaque).
/// </summary>
public sealed class NexCarriagePlaneLayer : ICarriagePlaneLayer
{
    // Skia-plotter-compatible palette (opaque Color4 values).
    private static readonly Color4 CutColor = new(0.5f, 0.5f, 0.5f, 1f);
    internal static readonly Color4 RapidColor = new(0.9f, 0.12f, 0.12f, 1f);
    private static readonly Color4 ExecutedColor = new(0.106f, 0.62f, 0.243f, 1f);
    private static readonly Color4 InFlightColor = new(0.878f, 0.588f, 0.125f, 1f);
    private static readonly Color4 ErrorColor = new(0.75f, 0.19f, 0.19f, 1f);

    internal const float BaseLineThickness = 1.6f;
    internal const float ExecutedLineThickness = 2.4f;

    internal static bool IsExecuted(RunState state) => state == RunState.Executed;

    private readonly Engine _engine;
    private readonly Geometry _geometry;
    private readonly LineNode _node;
    private readonly List<MotionType> _motionTypes = [];
    private readonly Geometry _doneGeometry;
    private readonly LineNode _doneNode;
    private readonly List<bool> _executed = [];
    private readonly List<RunState> _appliedStates = [];
    private readonly RebuildGate _rebuildGate = new();
    private Geometry? _renderedGeometry;
    private Geometry? _renderedDoneGeometry;
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
        var color = state == RunState.Pending ? BaseColor(_motionTypes[segmentIndex]) : StateColor(state);
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

    /// <summary>Swap-pump: reconcile the CPU staging lists to the GPU as fresh
    /// STATIC geometries. Rebuilds are coalesced by <see cref="RebuildGate"/>
    /// (burned at the mutation APIs): a clean layer does nothing, and bursts
    /// collapse to at most one rebuild per minimum interval, so a large toolpath
    /// is not re-uploaded on every run-state tick. Registered via the engine's
    /// staged buffer upload (device-local destination — never a mapped write
    /// into a buffer a frame is reading), then the node's geometry is swapped
    /// and the previous geometry deferred for frame-boundary removal/reindex.</summary>
    public void Pump()
    {
        if (!_rebuildGate.RebuildDue(DateTime.UtcNow))
            return;

        var plane = BuildStatic(_geometry);
        var done = BuildStatic(_doneGeometry);

        var oldPlane = _renderedGeometry;
        var oldDone = _renderedDoneGeometry;

        if (plane.Vertices.Count > 0)
        {
            _engine.Add(plane);
            _node.Geometry = plane;
            _renderedGeometry = plane;
        }
        else
        {
            _node.Geometry = null;
            _renderedGeometry = null;
        }

        if (done.Vertices.Count > 0)
        {
            _engine.Add(done);
            _doneNode.Geometry = done;
            _renderedDoneGeometry = done;
        }
        else
        {
            _doneNode.Geometry = null;
            _renderedDoneGeometry = null;
        }

        oldPlane?.Dispose();
        oldDone?.Dispose();
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
    /// case a torn tail, padded below to keep colors ≥ vertices).</summary>
    internal static Geometry BuildStatic(Geometry staging)
    {
        var vertices = Snapshot(staging.Vertices);
        var colors = Snapshot(staging.VertexColors, vertices.Length);
        var n = vertices.Length;
        if (n == 0)
            return new Geometry(Topology.Line, isDynamic: false);
        var indices = new uint[n];
        for (var i = 0; i < n; i++)
            indices[i] = (uint)i;
        return new Geometry(vertices, indices, colors, Topology.Line, isDynamic: false);
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