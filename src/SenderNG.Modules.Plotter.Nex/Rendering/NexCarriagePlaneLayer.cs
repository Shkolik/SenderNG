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
/// One carriage-plane layer rendered as a single dynamic line list.
/// <see cref="AddSegment"/> appends vertex/color pairs; run-state changes
/// overwrite the two colors of the segment in place (O(1), no rebuild) — the
/// Task 8 perf lever for live frontier coloring of large toolpaths.
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

    private readonly Geometry _geometry;
    private readonly LineNode _node;
    private readonly List<MotionType> _motionTypes = [];
    private readonly Geometry _doneGeometry;
    private readonly LineNode _doneNode;
    private readonly List<bool> _executed = [];
    private readonly List<RunState> _appliedStates = [];

    /// <summary>Create the layer under <paramref name="root"/> and attach its line node.</summary>
    public NexCarriagePlaneLayer(Node root, Engine engine)
    {
        _geometry = new Geometry(Topology.Line, isDynamic: true);
        // Register with the engine so Geometry.Valid becomes true; otherwise the
        // node never reaches the line draw stream and renders nothing.
        engine.Add(_geometry);
        _node = root.World.CreateLineNode("carriage-plane");
        _node.Geometry = _geometry;
        _node.LineThickness = BaseLineThickness;
        _node.LineColor = Color4.White;
        root.AddChild(_node);

        _doneGeometry = new Geometry(Topology.Line, isDynamic: true);
        // Register with the engine so Geometry.Valid becomes true; otherwise the
        // node never reaches the line draw stream and renders nothing.
        engine.Add(_doneGeometry);
        _doneNode = root.World.CreateLineNode("plane-done");
        _doneNode.Geometry = _doneGeometry;
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
        _geometry.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
        // Re-set the geometry reference so the draw stream refreshes its LineCount.
        _node.Geometry = _geometry;
    }

    /// <inheritdoc/>
    public void SetSegmentRunState(int segmentIndex, RunState state)
    {
        if (segmentIndex < 0 || segmentIndex >= _motionTypes.Count)
            return;
        if (_appliedStates[segmentIndex] == state)
            return;
        _appliedStates[segmentIndex] = state;
        var color = state == RunState.Pending ? BaseColor(_motionTypes[segmentIndex]) : StateColor(state);
        var v = color.ToVector4();
        _geometry.VertexColors[segmentIndex * 2] = v;
        _geometry.VertexColors[segmentIndex * 2 + 1] = v;
        _geometry.MarkDirty(GeometryBufferType.VertexColor);

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
    }

    /// <summary>Append one done segment (two vertices + two colors) to the
    /// green thickness overlay node.</summary>
    private void AppendDoneSegment(int segmentIndex)
    {
        var v = ExecutedColor.ToVector4();
        _doneGeometry.Vertices.Add(_geometry.Vertices[segmentIndex * 2]);
        _doneGeometry.Vertices.Add(_geometry.Vertices[segmentIndex * 2 + 1]);
        _doneGeometry.VertexColors.Add(v);
        _doneGeometry.VertexColors.Add(v);
        MarkDoneDirty();
    }

    /// <summary>Flush the done geometry to the draw stream: mark both buffers
    /// dirty and re-set the geometry reference so LineCount refreshes.</summary>
    private void MarkDoneDirty()
    {
        _doneGeometry.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
        _doneNode.Geometry = _doneGeometry;
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
        MarkDoneDirty();
    }

    /// <summary>Diagnostic (temporary): executed-overlay vertex count (2 per
    /// done segment).</summary>
    internal int ExecutedVerticesCount => _doneGeometry.Vertices.Count;

    /// <summary>Diagnostic (temporary): executed overlay accounting.</summary>
    public string DumpExecutedOverlay() =>
        $"done verts={_doneGeometry.Vertices.Count} executedSegs={_executed.Count(x => x)}";
}