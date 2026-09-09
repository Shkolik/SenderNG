/*
 * NexPlotTool.cs - Nex plot tool: wireframe cone + optional wire
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
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Scene;
using SenderNG.Modules.Plotter.Nex.Scene;

namespace SenderNG.Modules.Plotter.Nex.Rendering;

/// <summary>
/// The tool indicator: an apex-at-origin wireframe cone (moved via the node
/// transform) plus an optional connector wire used by multi-plane machines.
/// <see cref="UpdatePosition"/> with one point parks a cone on that point
/// (single-plane mill); two points draw a wire between them with a cone at
/// each end (POC wire/foam machines).
/// </summary>
public sealed class NexPlotTool : IPlotTool
{
    private static readonly Color4 ToolColor = new(0.85f, 0.15f, 0.2f, 1f);

    private const float ConeHeight = 3f;
    private const float ConeRadius = 1.5f;
    private const int ConeSegments = 16;

    private readonly Geometry _segmentGeometry;
    private readonly LineNode _segmentNode;
    private readonly LineNode _coneNodeA;
    private readonly LineNode _coneNodeB;
    private ToolShape _shape;

    /// <summary>Create the tool under <paramref name="root"/>.</summary>
    public NexPlotTool(Node root, ToolShape shape)
    {
        _shape = shape;
        _segmentGeometry = new Geometry(Topology.Line, isDynamic: true);
        _segmentNode = root.World.CreateLineNode("tool-wire");
        _segmentNode.Geometry = _segmentGeometry;
        _segmentNode.LineThickness = 1.4f;
        root.AddChild(_segmentNode);

        var coneGeometry = BuildConeGeometry();
        _coneNodeA = root.World.CreateLineNode("tool-cone");
        _coneNodeA.Geometry = coneGeometry;
        _coneNodeA.LineThickness = 1.1f;
        root.AddChild(_coneNodeA);
        _coneNodeB = root.World.CreateLineNode("tool-cone-2");
        _coneNodeB.Geometry = coneGeometry;
        _coneNodeB.LineThickness = 1.1f;
        root.AddChild(_coneNodeB);
    }

    /// <summary>The wire node (for scene removal).</summary>
    internal Node WireNode => _segmentNode;

    /// <summary>The first cone node (for scene removal).</summary>
    internal Node ConeNodeA => _coneNodeA;

    /// <summary>The second cone node (for scene removal).</summary>
    internal Node ConeNodeB => _coneNodeB;

    /// <inheritdoc/>
    /// <remarks>POC only creates <see cref="ToolShape.Cone"/>; other shapes fall
    /// back to the cone geometry (a cylinder/sphere/crosshair tool is future work).</remarks>
    public ToolShape Shape
    {
        get => _shape;
        set => _shape = value;
    }

    /// <inheritdoc/>
    public void UpdatePosition(Endpoint endpoints)
    {
        if (endpoints.Points.Length < 2)
        {
            _segmentNode.Enabled = false;
            _coneNodeB.Enabled = false;
            _coneNodeA.Transform.Translation =
                endpoints.Points.Length == 1 ? endpoints.Points[0] : Vector3.Zero;
            return;
        }

        _segmentNode.Enabled = true;
        _coneNodeB.Enabled = true;
        var p0 = endpoints.Points[0];
        var p1 = endpoints.Points[1];
        _segmentGeometry.Vertices.Clear();
        _segmentGeometry.VertexColors.Clear();
        var color = ToolColor.ToVector4();
        _segmentGeometry.Vertices.Add(new Vector4(p0, 1f));
        _segmentGeometry.Vertices.Add(new Vector4(p1, 1f));
        _segmentGeometry.VertexColors.Add(color);
        _segmentGeometry.VertexColors.Add(color);
        _segmentGeometry.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
        _coneNodeA.Transform.Translation = p0;
        _coneNodeB.Transform.Translation = p1;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _segmentNode.Dispose();
        _coneNodeA.Dispose();
        _coneNodeB.Dispose();
    }

    /// <summary>Wireframe cone with apex at the origin opening along +Z
    /// (a mill spindle points up from its contact point). Mirrors the spike's
    /// FillSpotCone line recipe: base ring + spokes + center axis.</summary>
    private static Geometry BuildConeGeometry()
    {
        var g = new Geometry(Topology.Line, isDynamic: true);
        var baseCenter = new Vector3(0f, 0f, ConeHeight);
        var color = ToolColor.ToVector4();

        void AddLine(Vector3 a, Vector3 b)
        {
            g.Vertices.Add(new Vector4(a, 1f));
            g.Vertices.Add(new Vector4(b, 1f));
            g.VertexColors.Add(color);
            g.VertexColors.Add(color);
        }

        // Center axis: apex → base center.
        AddLine(Vector3.Zero, baseCenter);

        // Base ring + radial spokes.
        var spokeStep = Math.Max(1, ConeSegments / 8);
        for (var i = 0; i < ConeSegments; i++)
        {
            var next = (i + 1) % ConeSegments;
            var a = Ring(baseCenter, ConeRadius, i);
            var b = Ring(baseCenter, ConeRadius, next);
            AddLine(a, b);
            if (i % spokeStep == 0)
                AddLine(Vector3.Zero, a);
        }

        g.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
        return g;

        static Vector3 Ring(Vector3 center, float radius, int i)
        {
            var ang = i / (float)ConeSegments * MathF.PI * 2f;
            return center + new Vector3(MathF.Cos(ang) * radius, MathF.Sin(ang) * radius, 0f);
        }
    }
}