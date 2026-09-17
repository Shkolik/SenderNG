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

    private readonly Engine _engine;
    private readonly Geometry _wireGeometry;
    private readonly LineNode _segmentNode;
    private readonly Geometry _coneGeometry;
    private readonly PBRMaterialProperties _coneMaterial;
    private readonly MeshNode _coneNodeA;
    private readonly MeshNode _coneNodeB;
    private readonly RebuildGate _rebuildGate = new();
    private Vector3? _lastWireA;
    private Vector3? _lastWireB;
    private ToolShape _shape;

    /// <summary>Create the tool under <paramref name="root"/>. The connector
    /// wire is ONE long-lived static line geometry created and registered here
    /// (stable pool slot / MeshInfo row / GpuAddress for the tool's lifetime);
    /// each <see cref="Pump"/> only re-uploads the two endpoints into the same
    /// device buffer instead of cycling fresh geometries.</summary>
    public NexPlotTool(Node root, Engine engine, ToolShape shape)
    {
        _shape = shape;
        _engine = engine;
        // Sample-style wire: a single static geometry, initially parked far
        // below the table. The engine registers it once; Geometry.UpdateBuffers
        // reuses the existing ElementBuffer (see Geometry._vertexBuffer ??=), so
        // every later endpoint update is a fenced staged copy into a fixed
        // address — no geometry add/remove/dispose, no staging-offset reuse.
        _wireGeometry = new Geometry(
            new[] { new Vector4(ParkedPosition, 1f), new Vector4(ParkedPosition + new Vector3(1f, 0f, 0f), 1f) },
            new[] { 0u, 1u },
            new[] { ToolColor.ToVector4(), ToolColor.ToVector4() },
            Topology.Line,
            isDynamic: false);
        _engine.Add(_wireGeometry);
        _segmentNode = root.World.CreateLineNode("tool-wire");
        _segmentNode.LineThickness = 1.4f;
        _segmentNode.LineColor = Color4.White;
        _segmentNode.Geometry = _wireGeometry;
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
            SetConePosition(_coneNodeB, ParkedPosition, Quaternion.Identity);
            SetConePosition(_coneNodeA,
                endpoints.Points.Length == 1 ? endpoints.Points[0] : Vector3.Zero,
                Quaternion.Identity);
            // Only re-stage the wire when the value changed. Idle polls
            // call UpdatePosition with the same endpoints tens of times a second;
            // re-staging (new static geometry + dispose) on every poll kept the
            // wire on a ~150ms rebuild cadence for a static scene, and a freshly
            // staged static geometry read before its device upload completed
            // produced a garbage wire billboard vertex -> the giant red fan.
            if (_lastWireA is not null || _lastWireB is not null)
            {
                _lastWireA = null;
                _lastWireB = null;
                _rebuildGate.MarkDirty();
            }
            return;
        }

        _segmentNode.Enabled = true;
        _coneNodeB.Enabled = true;
        var p0 = endpoints.Points[0];
        var p1 = endpoints.Points[1];
        if (_lastWireA == p0 && _lastWireB == p1)
            return;
        _lastWireA = p0;
        _lastWireB = p1;
        // GPU content never changes here — mark the swap-pump pending instead.
        _rebuildGate.MarkDirty();
        SetConePosition(_coneNodeA, p0, OpeningRotation(p0, p1));
        SetConePosition(_coneNodeB, p1, OpeningRotation(p1, p0));
    }

    /// <summary>Write the connector wire into <paramref name="target"/>: two
    /// vertices + two tool-tint colors at the two endpoints, parked far below
    /// the table when a <c>null</c> endpoint is given (single-point/parked
    /// machines). The parked degenerate line keeps the wire buffer a constant
    /// two vertices so the registered geometry never resizes or moves address.</summary>
    internal static void FillWire(Geometry target, Vector3? p0, Vector3? p1)
    {
        target.Vertices.Clear();
        target.VertexColors.Clear();
        var color = ToolColor.ToVector4();
        if (p0 is null || p1 is null)
        {
            target.Vertices.Add(new Vector4(ParkedPosition, 1f));
            target.Vertices.Add(new Vector4(ParkedPosition, 1f));
        }
        else
        {
            target.Vertices.Add(new Vector4(p0.Value, 1f));
            target.Vertices.Add(new Vector4(p1.Value, 1f));
        }
        target.VertexColors.Add(color);
        target.VertexColors.Add(color);
        target.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
    }

    /// <summary>Swap-pump the connector wire (see <see cref="NexLoftSurface.Pump"/>):
    /// the wire geometry is registered ONCE (see ctor); a rebuild only
    /// re-uploads the two staged endpoints into the same fixed device buffer.
    /// No geometry is created/disposed and no MeshInfo row or staging offset
    /// moves, so the line shader can never read a recycled address — the giant
    /// red fan's only viable source in this path. The cones are untouched —
    /// their transforms ride the NodeInfo path (proven safe in the mill scene
    /// at the same cadence).</summary>
    internal void Pump()
    {
        if (!_rebuildGate.RebuildDue(DateTime.UtcNow))
            return;

        FillWire(_wireGeometry, _lastWireA, _lastWireB);
        // Fenced staged upload into the registered static buffer: UpdateBuffers
        // reuses the existing ElementBuffer (Geometry._vertexBuffer ??=) and
        // pushes through Context.Upload, which lands on the same graphics queue
        // ahead of this tick's frame submit (FIFO) — no swap, no dispose.
        _wireGeometry.UpdateBuffers(_engine.Context).CheckResult();
        _segmentNode.Enabled = _wireGeometry.Vertices.Count == 2 && _lastWireA is not null;
    }

    /// <summary>Write the cone transform and notify the ECS change so the engine
    /// refreshes the node's GPU row. Direct writes alone keep the drawn cone at
    /// its first-upload transform forever.</summary>
    private static void SetConePosition(Node node, Vector3 position, Quaternion rotation)
    {
        node.Transform.Translation = position;
        node.Transform.Rotation = rotation;
        node.NotifyTransformChanged();
    }

    /// <summary>Rotation carrying the cone's local +Z (its opening) from
    /// <paramref name="from"/> toward <paramref name="to"/>, so two cones on the
    /// same wire face each other. Identity for a zero-length wire.</summary>
    internal static Quaternion OpeningRotation(Vector3 from, Vector3 to)
    {
        var delta = to - from;
        if (delta.LengthSquared() <= 1e-12f)
            return Quaternion.Identity;
        return ConeOrientation.FromUnitZTo(Vector3.Normalize(delta));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _segmentNode.Dispose();
        _coneNodeA.Dispose();
        _coneNodeB.Dispose();
        _coneGeometry.Dispose();
        _coneMaterial.Dispose();
        _wireGeometry.Dispose();
    }

    /// <summary>DIAGNOSTIC (temporary): CPU-side mesh/bounds/transform state of the
    /// three tool nodes.</summary>
    internal string DumpContent()
    {
        return $"wire(en={_segmentNode.Enabled}, lc={_segmentNode.LineCount}, vc={_wireGeometry.Vertices.Count}) "
            + $"coneA(en={_coneNodeA.Enabled}, vc={_coneGeometry.Vertices.Count}, r={_coneGeometry.BoundingSphereLocal.Radius:F2}, t={_coneNodeA.Transform.Translation}) "
            + $"coneB(en={_coneNodeB.Enabled}, vc={_coneGeometry.Vertices.Count}, t={_coneNodeB.Transform.Translation})";
    }

    /// <summary>Closed cone mesh with the apex at the origin opening along +Z
    /// (a mill spindle points up from its contact point). The cap disk at
    /// z=height closes the base and the degenerate apex joint registers the
    /// apex, so each theta step emits a 6-vertex ring across the three profile
    /// segments (apex joint, skirt, cap). Static CPU geometry, written once —
    /// identical silhouette to the previous wireframe cone.</summary>
    internal static Geometry BuildConeMesh(
        int thetaDiv = ConeMesh.DefaultSegments,
        float radius = ConeMesh.DefaultRadius,
        float height = ConeMesh.DefaultHeight)
        => ConeMesh.Build(thetaDiv, radius, height);
}
