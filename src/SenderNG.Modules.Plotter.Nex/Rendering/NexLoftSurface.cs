/*
 * NexLoftSurface.cs - Nex loft surface: wire-plane loop visualization
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
/// One wire-plane loop (the polygon outline) rendered as a swap-pumped static
/// line list. <see cref="SetWirePlane"/> mutates CPU staging only (no GPU
/// work); the GPU gets a fresh STATIC geometry through
/// <see cref="NexPlotScene.PumpToolWireAndLofts"/> (a low-Hz pump polled from
/// the view, same as the carriage planes). This deliberately NEVER writes
/// host-visible mapped buffers that in-flight frames read — in-place dynamic
/// uploads raced the live render loop and produced VK_ERROR_DEVICE_LOST on the
/// loft scenes (helix engine 1.3.0-alpha.41, driver 32.0.15.9608).
/// </summary>
public sealed class NexLoftSurface : ILoftSurface
{
    // Opaque dim blue-grey (Skia's semi-transparent loft colour becomes solid —
    // alpha handling in the Nex line shader is unverified).
    private static readonly Color4 WireColor = new(0.42f, 0.42f, 0.54f, 1f);

    private readonly Engine _engine;
    private readonly Geometry _staging;
    private readonly LineNode _node;
    private readonly RebuildGate _rebuildGate = new();
    private Geometry? _renderedGeometry;

    /// <summary>Create the loft surface under <paramref name="root"/>. The node
    /// stays geometry-less until the first <see cref="Pump"/>.</summary>
    public NexLoftSurface(Node root, Engine engine)
    {
        _engine = engine;
        // Staging container only — never registered with the engine, never
        // attached to a node, so nothing here produces GPU buffers. All GPU
        // content arrives via Pump() as a fresh static geometry.
        _staging = new Geometry(Topology.Line, isDynamic: true);
        _node = root.World.CreateLineNode("loft-surface");
        _node.LineThickness = 0.9f;
        _node.LineColor = Color4.White;
        root.AddChild(_node);
    }

    /// <summary>The underlying line node (for scene removal).</summary>
    internal LineNode Node => _node;

    /// <inheritdoc/>
    public void SetWirePlane(Vector3[] polygon)
    {
        FillLoop(_staging, polygon);
        // GPU content never changes here — mark the swap-pump pending instead.
        _rebuildGate.MarkDirty();
    }

    /// <summary>Fill <paramref name="target"/> with the closed polygon loop:
    /// one line per edge (two vertices + two tints), ending back at the first
    /// point. Any short/null polygon clears the target (loop with nothing to
    /// draw).</summary>
    internal static void FillLoop(Geometry target, Vector3[]? polygon)
    {
        target.Vertices.Clear();
        target.VertexColors.Clear();
        if (polygon is null || polygon.Length < 2)
            return;
        var color = WireColor.ToVector4();
        for (var i = 0; i < polygon.Length; i++)
        {
            var next = (i + 1) % polygon.Length;
            target.Vertices.Add(new Vector4(polygon[i], 1f));
            target.Vertices.Add(new Vector4(polygon[next], 1f));
            target.VertexColors.Add(color);
            target.VertexColors.Add(color);
        }
    }

    /// <summary>Swap-pump: copy the staging loop into a fresh STATIC geometry,
    /// register it via the engine's staged buffer upload (device-local
    /// destination — never a mapped write into a buffer a frame is reading),
    /// swap it in as the node's geometry, and defer the previous geometry for
    /// frame-boundary removal. Rebuilds are coalesced by <see cref="RebuildGate"/>
    /// (position changes arrive at status-poll cadence), so this never rewrites
    /// a live buffer in place.</summary>
    internal void Pump()
    {
        if (!_rebuildGate.RebuildDue(DateTime.UtcNow))
            return;

        var loop = NexCarriagePlaneLayer.BuildStatic(_staging);
        var old = _renderedGeometry;
        if (loop.Vertices.Count > 0)
        {
            _engine.Add(loop);
            _node.Geometry = loop;
            _renderedGeometry = loop;
        }
        else
        {
            _node.Geometry = null;
            _renderedGeometry = null;
        }

        old?.Dispose();
    }
}