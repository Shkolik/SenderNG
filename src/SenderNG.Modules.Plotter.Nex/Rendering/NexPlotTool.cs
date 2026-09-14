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
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders.Frag;
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

    // The engine writes NodeInfo.Enabled into the node-info row (SceneState),
    // but the line render path still submits disabled nodes; the cone would
    // linger at the origin. Park it far below the table instead.
    private static readonly Vector3 ParkedPosition = new(0f, -50_000f, 0f);

    private const float ConeHeight = 3f;
    private const float ConeRadius = 1.5f;
    private const int ConeSegments = 16;

    private readonly Engine _engine;
    private readonly Geometry _stagingSegmentGeometry;
    private readonly LineNode _segmentNode;
    private readonly Geometry _coneGeometry;
    private readonly PBRMaterialProperties _coneMaterial;
    private readonly MeshNode _coneNodeA;
    private readonly MeshNode _coneNodeB;
    private readonly RebuildGate _rebuildGate = new();
    private Geometry? _renderedSegmentGeometry;
    private ToolShape _shape;

    /// <summary>Create the tool under <paramref name="root"/>. The connector
    /// wire starts geometry-less until the first <see cref="Pump"/>.</summary>
    public NexPlotTool(Node root, Engine engine, ToolShape shape)
    {
        _shape = shape;
        _engine = engine;
        // Staging container only — never registered with the engine, never
        // attached to a node, so nothing here produces GPU buffers. All GPU
        // wire content arrives via Pump() as a fresh static geometry.
        _stagingSegmentGeometry = new Geometry(Topology.Line, isDynamic: true);
        _segmentNode = root.World.CreateLineNode("tool-wire");
        _segmentNode.LineThickness = 1.4f;
        _segmentNode.LineColor = Color4.White;
        root.AddChild(_segmentNode);

        // Filled cone — two nodes sharing ONE static geometry and ONE Unlit material.
        // IsTransparent routes both through the WBOIT pass (CullMode.None), so the
        // cone renders double-sided (no invisible-from-below culling) even at full
        // opacity. Static geometry is uploaded once here; the cones then only ever
        // move via node transforms (NodeInfo path).
        _coneGeometry = BuildConeMesh();
        _engine.Add(_coneGeometry);
        _coneMaterial = _engine.ResourceManager.PBRPropertyManager.Create(PBRShadingMode.Unlit);
        _coneMaterial.Albedo = ToolColor;
        _coneMaterial.Emissive = Color4.Black;
        _coneMaterial.Opacity = 1f;
        _coneNodeA = root.World.CreateMeshNode("tool-cone");
        _coneNodeA.Geometry = _coneGeometry;
        _coneNodeA.MaterialProperties = _coneMaterial;
        _coneNodeA.IsTransparent = true;
        root.AddChild(_coneNodeA);
        _coneNodeB = root.World.CreateMeshNode("tool-cone-2");
        _coneNodeB.Geometry = _coneGeometry;
        _coneNodeB.MaterialProperties = _coneMaterial;
        _coneNodeB.IsTransparent = true;
        root.AddChild(_coneNodeB);
    }

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
            SetConePosition(_coneNodeB, ParkedPosition);
            SetConePosition(_coneNodeA,
                endpoints.Points.Length == 1 ? endpoints.Points[0] : Vector3.Zero);
            // Stage an empty wire; the pump clears the rendered segment.
            FillWire(_stagingSegmentGeometry, null, null);
            // GPU content never changes here — mark the swap-pump pending instead.
            _rebuildGate.MarkDirty();
            return;
        }

        _segmentNode.Enabled = true;
        _coneNodeB.Enabled = true;
        var p0 = endpoints.Points[0];
        var p1 = endpoints.Points[1];
        FillWire(_stagingSegmentGeometry, p0, p1);
        // GPU content never changes here — mark the swap-pump pending instead.
        _rebuildGate.MarkDirty();
        SetConePosition(_coneNodeA, p0);
        SetConePosition(_coneNodeB, p1);
    }

    /// <summary>Fill <paramref name="target"/> with the connector wire: two
    /// vertices + two tool-tint colors at the two endpoints, cleared when a
    /// <c>null</c> endpoint is given (single-point/parked machines).</summary>
    internal static void FillWire(Geometry target, Vector3? p0, Vector3? p1)
    {
        target.Vertices.Clear();
        target.VertexColors.Clear();
        if (p0 is null || p1 is null)
            return;
        var color = ToolColor.ToVector4();
        target.Vertices.Add(new Vector4(p0.Value, 1f));
        target.Vertices.Add(new Vector4(p1.Value, 1f));
        target.VertexColors.Add(color);
        target.VertexColors.Add(color);
    }

    /// <summary>Swap-pump the connector wire (see <see cref="NexLoftSurface.Pump"/>):
    /// stage its GPU content as a fresh STATIC geometry under the engine's
    /// staged upload, swap it in as the node's geometry, and defer the previous
    /// geometry's removal. The cones are untouched — their transforms ride the
    /// NodeInfo path (proven safe in the mill scene at the same cadence).</summary>
    internal void Pump()
    {
        if (!_rebuildGate.RebuildDue(DateTime.UtcNow))
            return;

        var wire = NexCarriagePlaneLayer.BuildStatic(_stagingSegmentGeometry);
        var old = _renderedSegmentGeometry;
        if (wire.Vertices.Count > 0)
        {
            _engine.Add(wire);
            _segmentNode.Geometry = wire;
            _renderedSegmentGeometry = wire;
        }
        else
        {
            _segmentNode.Geometry = null;
            _renderedSegmentGeometry = null;
        }

        old?.Dispose();
    }

    /// <summary>Write the cone transform and notify the ECS change so the engine
    /// refreshes the node's GPU row. Direct writes alone keep the drawn cone at
    /// its first-upload transform forever.</summary>
    private static void SetConePosition(Node node, Vector3 position)
    {
        node.Transform.Translation = position;
        node.NotifyTransformChanged();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _segmentNode.Dispose();
        _coneNodeA.Dispose();
        _coneNodeB.Dispose();
        _coneGeometry.Dispose();
        _coneMaterial.Dispose();
    }

    /// <summary>DIAGNOSTIC (temporary): CPU-side mesh/bounds/transform state of the
    /// three tool nodes.</summary>
    internal string DumpContent()
    {
        return $"wire(en={_segmentNode.Enabled}, lc={_segmentNode.LineCount}, vc={_stagingSegmentGeometry.Vertices.Count}) "
            + $"coneA(en={_coneNodeA.Enabled}, vc={_coneGeometry.Vertices.Count}, r={_coneGeometry.BoundingSphereLocal.Radius:F2}, t={_coneNodeA.Transform.Translation}) "
            + $"coneB(en={_coneNodeB.Enabled}, vc={_coneGeometry.Vertices.Count}, t={_coneNodeB.Transform.Translation})";
    }

    /// <summary>Closed cone mesh with the apex at the origin opening along +Z
    /// (a mill spindle points up from its contact point). The cap disk at
    /// z=height closes the base and the degenerate apex joint registers the
    /// apex, so each theta step emits a 6-vertex ring across the three profile
    /// segments (apex joint, skirt, cap). Static CPU geometry, written once —
    /// identical silhouette to the previous wireframe cone.</summary>
    internal static Geometry BuildConeMesh(int thetaDiv = ConeSegments, float radius = ConeRadius, float height = ConeHeight)
    {
        var g = new Geometry(Topology.Triangle);
        var total = 6 * thetaDiv;
        g.Vertices.Capacity = total;
        g.VertexProps.Capacity = total;
        g.Indices.Capacity = 3 * total;

        // Same unit circle as the engine's AddRevolvedGeometry (x = -cos, y = +sin,
        // computed in double then cast) so the +/-radius ring vertices — and with
        // them the axis-aligned bounds — land exactly.
        var angle = (float)Math.PI * 2f / thetaDiv;
        var center = new Vector3(0f, 0f, height);
        var capNormal = Vector3.UnitZ;
        for (var i = 0; i < thetaDiv; i++)
        {
            var u = (float)i / (thetaDiv - 1);
            var w = new Vector3(-(float)Math.Cos(i * angle), (float)Math.Sin(i * angle), 0f);
            var ring = new Vector3(w.X * radius, w.Y * radius, height);
            var skirtNormal = Vector3.Normalize(new Vector3(w.X * height, w.Y * height, -radius));

            // Three profile segments, two vertices each: apex joint (degenerate
            // apex→apex), skirt (apex→ring), cap (ring→centre).
            AddVertex(Vector3.Zero, Vector3.Zero, new Vector2(u, 0f));
            AddVertex(Vector3.Zero, Vector3.Zero, new Vector2(u, 1f));
            AddVertex(Vector3.Zero, skirtNormal, new Vector2(u, 1f));
            AddVertex(ring, skirtNormal, new Vector2(u, 0f));
            AddVertex(ring, capNormal, new Vector2(u, 0f));
            AddVertex(center, capNormal, new Vector2(u, 1f));

            // Two triangles per segment, wired across to the next theta step.
            var i0 = i * 6;
            for (var j = 0; j < 3; j++)
            {
                var a = i0 + j * 2;
                var c = (a + 6) % total;
                var d = c + 1;
                g.Indices.Add((uint)(a + 1));
                g.Indices.Add((uint)a);
                g.Indices.Add((uint)c);
                g.Indices.Add((uint)(a + 1));
                g.Indices.Add((uint)c);
                g.Indices.Add((uint)d);
            }
        }

        g.UpdateBounds();
        g.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexProp | GeometryBufferType.Index);
        return g;

        void AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            g.Vertices.Add(new Vector4(position, 1f));
            g.VertexProps.Add(new VertexProperties(normal, uv));
        }
    }
}