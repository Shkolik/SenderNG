/*
 * GlPlotTool.cs - GL plot tool: oriented cone + connector wire
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
using Silk.NET.OpenGL;
using SenderNG.Modules.Common.Render;
using SenderNG.Modules.Plotter.GL.Scene;
using GlApi = Silk.NET.OpenGL.GL;

namespace SenderNG.Modules.Plotter.GL.Rendering;

/// <summary>The tool indicator: an apex-at-origin cone mesh moved by a
/// model matrix (row-vector order <c>v * S * R * T</c>) plus a two-vertex
/// connector wire used by multi-plane machines. <see cref="UpdatePosition"/>
/// with one point parks the primary cone on that point (single-plane mill);
/// two points draw a wire between them with a cone at each end (POC
/// wire/foam machines).
/// <para>
/// Parking skips the hidden geometry's draws instead of parking it at a
/// far-off coordinate — see <c>_parked</c> and the two visibility flags, so
/// no far-off coordinate exists here.
/// </para>
/// <para>
/// <see cref="Draw"/> issues the wire (GL_LINES) BEFORE the cone mesh
/// (GL_TRIANGLES): the checkpoint-9 Intel MSAA rule — any in-frame triangles
/// draw kills MSAA coverage for every later line draw until the next clear —
/// makes wire-before-mesh mandatory. See the comment at the call site in
/// <see cref="GlPlotScene.RenderFrame"/>.
/// </para></summary>
internal sealed unsafe class GlPlotTool : IPlotTool, IDisposable
{
    /// <summary>The tool palette: cone albedo and connector-wire vertex tint
    /// alike.</summary>
    internal static readonly Vector4 ToolColor = new(0.85f, 0.15f, 0.2f, 1f);

    /// <summary>The connector-wire thickness (<c>LineThickness = 2.0f</c>),
    /// pushed to the expansion shader as <c>uLineWidthPx</c> by the scene —
    /// never through <c>glLineWidth</c>.</summary>
    internal const float WireThickness = 2.0f;

    private readonly GlContext _ctx;
    private readonly GlApi _gl;
    private readonly GlPlotScene _scene;
    private readonly RebuildGate _gate = new();       // copied GREEN: swap-pump gate
    private readonly uint _wireVao, _wireVbo;         // dynamic 2-vertex line, stride 28
    private readonly uint _meshVao, _meshVbo, _meshEbo;
    private readonly int _meshIndexCount;
    private readonly Vector3 _meshScale;
    private Vector3? _lastWireA;
    private Vector3? _lastWireB;
    private bool _endpointsDirty;                     // mirror of the gate's dirty bit (read seam)
    private bool _wireUploaded;
    private bool _parked = true;                      // no endpoints yet — draw nothing
    private bool _wireVisible;                        // wire draw enabled
    private bool _secondConeVisible;                  // second cone draw enabled
    private bool _disposed;
    private ToolShape _shape;

    /// <summary>Create the tool's static mesh (one VAO/VBO/EBO, uploaded once
    /// — the static cone geometry) and its two-vertex wire buffer on
    /// <paramref name="scene"/>'s context; the cones then only ever move via
    /// <see cref="ModelMatrix"/>/<see cref="ModelMatrixB"/>, the wire only via
    /// <see cref="Pump"/>.</summary>
    public GlPlotTool(GlPlotScene scene, ToolShape shape)
    {
        _scene = scene;
        _shape = shape;
        _ctx = scene.Context;
        _ctx.MakeCurrent();
        _gl = _ctx.Api;
        (_meshVao, _meshVbo, _meshEbo, _meshIndexCount, _meshScale) = CreateMesh(shape);
        (_wireVao, _wireVbo) = CreateWireArrays();
    }

    /// <summary>The requested shape. The mesh is built once in the
    /// constructor and the setter only stores the request — POC only creates
    /// <see cref="ToolShape.Cone"/>, so other shapes fall back to the cone
    /// geometry. See <see cref="CreateMesh"/> for the GL mapping.</summary>
    public ToolShape Shape
    {
        get => _shape;
        set => _shape = value;
    }

    /// <summary>The single-endpoint (mill) cone's opening direction — the
    /// machine's plunge: toward the spindle, flipped when the height axis is
    /// inverted. Default +Z, which is the identity rotation (the pre-plunge
    /// behavior). Two-point (foam) tools draw along-the-wire cones and
    /// ignore this.</summary>
    public Vector3 PlungeDirection { get; set; } = Vector3.UnitZ;

    /// <summary>Primary cone model matrix (scale, aim, place — row-vector
    /// <c>v * S * R * T</c>) — the matrix <see cref="Draw"/> pushes as the
    /// cone's MVP, and the internal read seam the orientation tests assert
    /// through.</summary>
    internal Matrix4x4 ModelMatrix { get; private set; } = Matrix4x4.Identity;

    /// <summary>Second-endpoint cone model matrix (foam machines) — the same
    /// read seam for the <c>B</c> orientation.</summary>
    internal Matrix4x4 ModelMatrixB { get; private set; } = Matrix4x4.Identity;

    /// <summary>Whole tool parked (no endpoints seen yet) — read seam.</summary>
    internal bool Parked => _parked;

    /// <summary>Connector wire draw enabled — read seam.</summary>
    internal bool WireVisible => _wireVisible;

    /// <summary>Second cone draw enabled — read seam.</summary>
    internal bool SecondConeVisible => _secondConeVisible;

    /// <summary>Pending swap-pump marks the gate has recorded and not yet
    /// flushed — the "marked, not GPU-touched" dedup seam (same read seam
    /// <see cref="GlCarriagePlaneLayer.PendingMutations"/> exposes).</summary>
    internal int PendingMutations => _gate.PendingMutations;

    /// <summary>True while endpoints changed but <see cref="Pump"/> has not
    /// uploaded the wire yet — read seam.</summary>
    internal bool WireUploadPending => _endpointsDirty;

    /// <summary>Dedup + park detection: a single point parks the primary
    /// cone on it and switches the wire and second cone off; zero points
    /// parks the whole tool; two points dedup against the last endpoints and,
    /// on a real change, refresh both cone orientations and mark the wire's
    /// swap-pump — the GPU is never touched here.</summary>
    public void UpdatePosition(Endpoint endpoints)
    {
        var pts = endpoints.Points;
        if (pts.Length < 2)
        {
            // The wire and second cone switch off; the primary cone goes to
            // the single endpoint (mill) or to "parked" (skipped entirely).
            _wireVisible = false;
            _secondConeVisible = false;
            _parked = pts.Length == 0;
            var p = pts.Length == 1 ? pts[0] : Vector3.Zero;
            // The single cone opens along the machine's plunge so it hangs
            // toward the spindle — identity for the default +Z, flipped for an
            // inverted height axis. The rotation precedes the placement
            // (row-vector v * S * R * T).
            var plunge = PlungeDirection.LengthSquared() > 1e-12f
                ? PlungeDirection
                : Vector3.UnitZ;
            ModelMatrix = Matrix4x4.CreateScale(_meshScale)
                * Matrix4x4.CreateFromQuaternion(ConeOrientation.FromUnitZTo(plunge))
                * Matrix4x4.CreateTranslation(p);
            ModelMatrixB = Matrix4x4.Identity;
            // Only re-stage the wire when the value changed. Idle polls call
            // UpdatePosition with the same endpoints tens of times a second;
            // re-staging on every poll would keep the wire on a ~150ms
            // rebuild cadence for a static scene.
            if (_lastWireA is not null || _lastWireB is not null)
            {
                _lastWireA = null;
                _lastWireB = null;
                _endpointsDirty = true;
                _gate.MarkDirty();
            }
            return;
        }

        _wireVisible = true;
        _secondConeVisible = true;
        _parked = false;
        var p0 = pts[0];
        var p1 = pts[1];
        if (_lastWireA == p0 && _lastWireB == p1)
            return;
        _lastWireA = p0;
        _lastWireB = p1;
        // GPU content never changes here — mark the swap-pump pending instead.
        _endpointsDirty = true;
        _gate.MarkDirty();
        var (rotationA, rotationB) = ConeOrientations(p0, p1);
        ModelMatrix = Matrix4x4.CreateScale(_meshScale)
            * Matrix4x4.CreateFromQuaternion(rotationA)
            * Matrix4x4.CreateTranslation(p0);
        ModelMatrixB = Matrix4x4.CreateScale(_meshScale)
            * Matrix4x4.CreateFromQuaternion(rotationB)
            * Matrix4x4.CreateTranslation(p1);
    }

    /// <summary>Swap-pump the connector wire: the RebuildGate decides whether
    /// this poll may upload (coalesced cadence, immediate on a burst), then
    /// the two staged endpoints are re-uploaded into the SAME fixed device
    /// buffer — no geometry is created or disposed. The wire's draw enable
    /// re-derives from the staged endpoints.</summary>
    internal void Pump()
    {
        if (_disposed || _ctx.IsDisposed || !_endpointsDirty)
            return;
        if (!_gate.RebuildDue(DateTime.UtcNow))
            return;
        _ctx.MakeCurrent();
        var data = FillWire(_lastWireA, _lastWireB);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _wireVbo);
        _gl.BufferSubData<float>(BufferTargetARB.ArrayBuffer, 0, data);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _wireUploaded = true;
        _wireVisible = _lastWireA is not null && _lastWireB is not null;
        _endpointsDirty = false;
    }

    /// <summary>Draw this tool: the connector wire FIRST, then the cone
    /// mesh(es). Called by <see cref="GlPlotScene"/> between the layer pass
    /// and the axis-cone pass.</summary>
    /// <remarks>
    /// WHY the wire comes first (checkpoint-9, hardware-proven — do not
    /// reorder): on the Intel driver (UHD 630, 31.0.101.2137) any in-frame
    /// GL_TRIANGLES draw kills MSAA coverage for every subsequent line draw
    /// until the next frame clear. The wire is lines, the cone is triangles,
    /// so the wire must land before this tool's own triangle pass to keep its
    /// AA. The mesh is self-hard either way (all triangle draws render
    /// hard-edged under MSAA on this driver regardless of position), so
    /// drawing it second loses nothing. This internal order is the one
    /// deliberate deviation from a naive port — see also the call site in
    /// <see cref="GlPlotScene.RenderFrame"/>.
    /// </remarks>
    internal void Draw(in Matrix4x4 viewProj)
    {
        if (_disposed || _ctx.IsDisposed || _parked)
            return;
        if (_wireVisible && _wireUploaded)
        {
            _scene.UseToolWireProgram(viewProj);
            _ctx.MakeCurrent();
            _gl.BindVertexArray(_wireVao);
            _gl.DrawArrays(PrimitiveType.Lines, 0, 2);
            _gl.BindVertexArray(0);
        }

        if (Shape == ToolShape.Crosshair)
            return;                                 // crosshair = wire only, no mesh

        _scene.UseToolMeshProgram();
        DrawCone(ModelMatrix, viewProj);
        if (_secondConeVisible)
            DrawCone(ModelMatrixB, viewProj);
    }

    private void DrawCone(in Matrix4x4 model, in Matrix4x4 viewProj)
    {
        _scene.SetToolMeshInstance(model, viewProj, ToolColor);
        _ctx.MakeCurrent();
        _gl.BindVertexArray(_meshVao);
        // Offset 0 into the bound element buffer: the pointer is a byte
        // offset there, so NULL is the first index, not a client pointer.
        _gl.DrawElements(PrimitiveType.Triangles, (uint)_meshIndexCount,
            DrawElementsType.UnsignedInt, (void*)0);
        _gl.BindVertexArray(0);
    }

    /// <summary>Orientations for the two wire-end cones: each cone's local +Z
    /// (its opening) lies along the wire with its apex at the endpoint and
    /// its body opening <em>away</em> from the other end, so the two points
    /// meet the wire from opposite sides. Identity for a zero-length
    /// wire.</summary>
    internal static (Quaternion A, Quaternion B) ConeOrientations(Vector3 p0, Vector3 p1)
    {
        var delta = p1 - p0;
        if (delta.LengthSquared() <= 1e-12f)
            return (Quaternion.Identity, Quaternion.Identity);
        var d = Vector3.Normalize(delta);
        return (ConeOrientation.FromUnitZTo(-d), ConeOrientation.FromUnitZTo(d));
    }

    /// <summary>The wire's two interleaved <c>vec3 pos + vec4 color</c>
    /// vertices (stride 28): a null endpoint degenerates the line — a
    /// zero-length segment is uploaded AND the draw is skipped entirely
    /// (<see cref="_wireVisible"/> false).</summary>
    private static float[] FillWire(Vector3? p0, Vector3? p1)
    {
        var a = p0 ?? Vector3.Zero;
        var b = p1 ?? Vector3.Zero;
        return
        [
            a.X, a.Y, a.Z, ToolColor.X, ToolColor.Y, ToolColor.Z, ToolColor.W,
            b.X, b.Y, b.Z, ToolColor.X, ToolColor.Y, ToolColor.Z, ToolColor.W,
        ];
    }

    /// <summary>The static tool mesh, built once from the requested shape:
    /// <list type="bullet">
    /// <item><see cref="ToolShape.Cone"/> — the shared default-dimension cone,
    /// apex at the local origin opening along +Z;</item>
    /// <item><see cref="ToolShape.Sphere"/> — <see cref="GlMeshBuilder"/>
    /// .BuildSphere, centred, orientation irrelevant;</item>
    /// <item><see cref="ToolShape.Cylinder"/> — the brief maps it onto a
    /// scaled cone (the cone mesh at half height): GlMeshBuilder has no
    /// cylinder tessellator and nothing in the app passes Cylinder — the
    /// kinematic factory only ever asks for Cone, and other shapes fall back
    /// to the cone geometry;</item>
    /// <item><see cref="ToolShape.Crosshair"/> — still builds the cone mesh
    /// so a later shape flip has geometry to draw, but <see cref="Draw"/>
    /// never issues it (crosshair = wire only).</item>
    /// </list></summary>
    private (uint Vao, uint Vbo, uint Ebo, int Count, Vector3 Scale) CreateMesh(ToolShape shape)
    {
        float[] verts;
        uint[] idx;
        var scale = Vector3.One;
        switch (shape)
        {
            case ToolShape.Sphere:
                (verts, idx) = GlMeshBuilder.BuildSphere(GlMeshBuilder.DefaultRadius);
                break;
            case ToolShape.Cylinder:
                (verts, idx) = GlMeshBuilder.BuildCone(
                    GlMeshBuilder.DefaultRadius, GlMeshBuilder.DefaultHeight);
                scale = new Vector3(1f, 1f, 0.5f);
                break;
            default:
                (verts, idx) = GlMeshBuilder.BuildCone(
                    GlMeshBuilder.DefaultRadius, GlMeshBuilder.DefaultHeight);
                break;
        }

        var vao = _gl.GenVertexArray();
        var vbo = _gl.GenBuffer();
        var ebo = _gl.GenBuffer();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, verts.AsSpan(), BufferUsageARB.StaticDraw);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 24, IntPtr.Zero);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 24, (IntPtr)12);
        _gl.EnableVertexAttribArray(1);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        _gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, idx.AsSpan(), BufferUsageARB.StaticDraw);
        _gl.BindVertexArray(0);
        return (vao, vbo, ebo, idx.Length, scale);
    }

    /// <summary>The wire's VAO/VBO: the chrome line layout (<c>vec3 pos +
    /// vec4 color</c>, stride 28), two vertices, dynamic — re-uploaded in
    /// place by <see cref="Pump"/>, never reallocated. Zero-length until the
    /// first pump, and not drawn before it.</summary>
    private (uint Vao, uint Vbo) CreateWireArrays()
    {
        var vao = _gl.GenVertexArray();
        var vbo = _gl.GenBuffer();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, new float[14], BufferUsageARB.DynamicDraw);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 28, IntPtr.Zero);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 28, (IntPtr)12);
        _gl.EnableVertexAttribArray(1);
        _gl.BindVertexArray(0);
        return (vao, vbo);
    }

    /// <summary>Free the tool's GL objects. Scene and context are one
    /// lifetime: once the context is already gone the driver released
    /// everything, and there is no API left to call.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ctx.IsDisposed)
            return;
        _ctx.MakeCurrent();
        if (_wireVbo != 0)
            _gl.DeleteBuffer(_wireVbo);
        if (_wireVao != 0)
            _gl.DeleteVertexArray(_wireVao);
        if (_meshEbo != 0)
            _gl.DeleteBuffer(_meshEbo);
        if (_meshVbo != 0)
            _gl.DeleteBuffer(_meshVbo);
        if (_meshVao != 0)
            _gl.DeleteVertexArray(_meshVao);
    }
}
