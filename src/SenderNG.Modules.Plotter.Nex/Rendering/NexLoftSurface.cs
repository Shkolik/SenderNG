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
/// One wire-plane loop (the polygon outline) as a dynamic line list. Cheap and
/// synchronous — the caller throttles <see cref="SetWirePlane"/> frequency,
/// so the whole loop is simply rebuilt on each call.
/// </summary>
public sealed class NexLoftSurface : ILoftSurface
{
    // Opaque dim blue-grey (Skia's semi-transparent loft colour becomes solid —
    // alpha handling in the Nex line shader is unverified).
    private static readonly Color4 WireColor = new(0.42f, 0.42f, 0.54f, 1f);

    private readonly Geometry _geometry;
    private readonly LineNode _node;

    /// <summary>Create the loft surface under <paramref name="root"/>.</summary>
    public NexLoftSurface(Node root, Engine engine)
    {
        _geometry = new Geometry(Topology.Line, isDynamic: true);
        // Register with the engine so Geometry.Valid becomes true; otherwise the
        // node never reaches the line draw stream and renders nothing.
        engine.Add(_geometry);
        _node = root.World.CreateLineNode("loft-surface");
        _node.Geometry = _geometry;
        _node.LineThickness = 0.9f;
        _node.LineColor = Color4.White;
        root.AddChild(_node);
    }

    /// <summary>The underlying line node (for scene removal).</summary>
    internal LineNode Node => _node;

    /// <inheritdoc/>
    public void SetWirePlane(Vector3[] polygon)
    {
        _geometry.Vertices.Clear();
        _geometry.VertexColors.Clear();
        if (polygon is not null && polygon.Length >= 2)
        {
            var color = WireColor.ToVector4();
            for (var i = 0; i < polygon.Length; i++)
            {
                var next = (i + 1) % polygon.Length;
                _geometry.Vertices.Add(new Vector4(polygon[i], 1f));
                _geometry.Vertices.Add(new Vector4(polygon[next], 1f));
                _geometry.VertexColors.Add(color);
                _geometry.VertexColors.Add(color);
            }
        }
        _geometry.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
        // Re-set the geometry reference so the draw stream refreshes its LineCount.
        _node.Geometry = _geometry;
    }
}