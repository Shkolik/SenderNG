/*
 * HeightMapScene.cs - GL scene that renders a height-map surface + chrome
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
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using SenderNG.Core.Modules;
using SenderNG.Modules.Common.Render;
using GlApi = Silk.NET.OpenGL.GL;

namespace SenderNG.Modules.HeightMapViewer;

/// <summary>
/// Renders a <see cref="HeightMapLayer"/> surface plus its measuring chrome into
/// the shared <see cref="GlReadback"/> target. Ported from the plotter's
/// <c>GlPlotScene</c> loop, but it reuses the <c>Common.Render</c> engine
/// (<see cref="GlContext"/>, <see cref="GlCamera"/>, <see cref="CameraGestures"/>,
/// <see cref="GlShaders"/>) and adds only the per-vertex-color mesh program the
/// surface needs (the shared engine's mesh program shades with normals, which a
/// gradient-colored height surface does not carry).
/// <para>
/// Scene and context are one lifetime: the scene's GL objects only exist on the
/// context that created them, so the view disposes both together on detach and
/// rebuilds both on re-attach.
/// </para>
/// </summary>
internal sealed class HeightMapScene : IDisposable
{
    /// <summary>Sample Z exaggeration. Samples and XY are the same unit (mm), so
    /// no exaggeration by default.</summary>
    public const double ZScale = 1.0;

    /// <summary>Measuring-envelope line color, the plotter's job-envelope cyan.</summary>
    private static readonly Vector4 EnvelopeColor = new(0f, 0.7f, 0.9f, 1f);

    /// <summary>Z-offset reference plane: a translucent blue-gray quad.</summary>
    private static readonly Vector3 PlaneColor = new(0.35f, 0.55f, 0.95f);

    private const float PlaneAlpha = 0.25f;
    private const float EnvelopeWidthPx = 1.6f;
    private const float OrbitSensitivity = 0.005f;

    /// <summary>Per-vertex-color mesh program. The shared
    /// <c>GlShaders.MeshVertex</c> takes pos+normal and lights with the CAD
    /// pipeline; the height surface carries pos+color instead, so this small
    /// unlit program is what actually draws it.</summary>
    private const string SurfaceVertex = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aColor;
        uniform mat4 uMvp;
        out vec3 vColor;
        void main() { gl_Position = uMvp * vec4(aPos, 1.0); vColor = aColor; }
        """;

    private const string SurfaceFragment = """
        in vec3 vColor;
        uniform float uAlpha;
        out vec4 FragColor;
        void main() { FragColor = vec4(vColor, uAlpha); }
        """;

    private readonly GlContext _ctx;
    private readonly GlApi _gl;
    private readonly uint _surfaceProg;
    private readonly uint _lineProg;
    private readonly bool _fatLines;
    private readonly int _surfaceMvp;
    private readonly int _surfaceAlpha;
    private readonly int _lineMvp;
    private readonly int _lineViewport;
    private readonly int _lineWidthPx;
    private readonly ColorMesh _surface;
    private readonly ColorMesh _plane;
    private readonly LineMesh _envelope;

    private readonly GlCamera _ortho = new();
    private readonly GlCamera _persp = new() { IsPerspective = true };
    private GlCamera _active;
    private bool _perspectiveActive;
    private float _orthoZoom = 1f;
    private float _navW = 1f;
    private float _navH = 1f;
    private Vector3? _orbitPivot;
    private ViewPreset _preset = ViewPreset.Isometric3D;

    private HeightMapLayer? _layer;
    private bool _framed;
    private bool _disposed;

    public HeightMapScene(GlContext context)
    {
        _ctx = context;
        context.MakeCurrent();
        _gl = context.Api;
        var gles = context.IsGles;
        _fatLines = GlShaders.SupportsGeometryShaders(context.Version, gles);

        _surfaceProg = GlShaders.Link(_gl,
            GlShaders.Compile(_gl, gles, ShaderType.VertexShader, SurfaceVertex),
            GlShaders.Compile(_gl, gles, ShaderType.FragmentShader, SurfaceFragment));
        _lineProg = _fatLines
            ? GlShaders.Link(_gl,
                GlShaders.Compile(_gl, gles, ShaderType.VertexShader, GlShaders.LineVertex),
                GlShaders.Compile(_gl, gles, ShaderType.GeometryShader, GlShaders.LineGeometry),
                GlShaders.Compile(_gl, gles, ShaderType.FragmentShader, GlShaders.LineFragmentFat))
            : GlShaders.Link(_gl,
                GlShaders.Compile(_gl, gles, ShaderType.VertexShader, GlShaders.LineVertex),
                GlShaders.Compile(_gl, gles, ShaderType.FragmentShader, GlShaders.LineFragment));

        _surfaceMvp = _gl.GetUniformLocation(_surfaceProg, "uMvp");
        _surfaceAlpha = _gl.GetUniformLocation(_surfaceProg, "uAlpha");
        _lineMvp = _gl.GetUniformLocation(_lineProg, "uMvp");
        _lineViewport = _gl.GetUniformLocation(_lineProg, "uViewportPx");
        _lineWidthPx = _gl.GetUniformLocation(_lineProg, "uLineWidthPx");

        _surface = new ColorMesh(context);
        _plane = new ColorMesh(context);
        _envelope = new LineMesh(context);

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);

        _active = _persp;
        _perspectiveActive = true;
        SetViewPreset(ViewPreset.Isometric3D);
    }

    /// <summary>Replace the layer being drawn. A new reference rebuilds the GPU
    /// mesh, the measuring envelope and the Z-offset plane; the first non-null
    /// layer reframes the camera (later maps keep the user's view).</summary>
    public void SetLayer(HeightMapLayer? layer)
    {
        if (ReferenceEquals(_layer, layer))
            return;
        _layer = layer;
        RebuildMeshes();
        if (layer is not null && !_framed)
        {
            _framed = true;
            SetViewPreset(_preset);
        }
    }

    /// <summary>Navigation viewport in on-screen (panel) pixels — cursor px and
    /// pan/zoom math divide by these, so they must match what the user points at.</summary>
    public void SetViewportSize(int wPx, int hPx)
    {
        _navW = Math.Max(1, wPx);
        _navH = Math.Max(1, hPx);
    }

    /// <inheritdoc cref="GlCamera"/>
    public void SetViewPreset(ViewPreset preset)
    {
        _preset = preset;
        _orbitPivot = null;
        var (center, radius, span) = ContentMetrics();
        _orthoZoom = MathF.Max(span.Length() * 1.1f, 1f);
        switch (preset)
        {
            case ViewPreset.XY:
                _ortho.Width = _orthoZoom;
                _ortho.Position = center + Vector3.UnitZ * (radius * 2f);
                _ortho.Target = center;
                _ortho.Up = Vector3.UnitY;
                _active = _ortho;
                _perspectiveActive = false;
                break;
            case ViewPreset.XZ:
                _ortho.Width = _orthoZoom;
                _ortho.Position = center - Vector3.UnitY * (radius * 2f);
                _ortho.Target = center;
                _ortho.Up = Vector3.UnitZ;
                _active = _ortho;
                _perspectiveActive = false;
                break;
            case ViewPreset.YZ:
                _ortho.Width = _orthoZoom;
                _ortho.Position = center + Vector3.UnitX * (radius * 2f);
                _ortho.Target = center;
                _ortho.Up = Vector3.UnitZ;
                _active = _ortho;
                _perspectiveActive = false;
                break;
            default:
                _persp.Position = center
                    + Vector3.Normalize(new Vector3(-1f, -1f, 1f)) * (radius * 2.2f);
                _persp.Target = center;
                _persp.Up = Vector3.Normalize(new Vector3(0.5f, 0.5f, 1f));
                _active = _persp;
                _perspectiveActive = true;
                break;
        }
    }

    public void PanByScreenDelta(Vector2 screenDeltaPx)
    {
        var v = CameraGestures.ScreenPanDelta(_active, screenDeltaPx, _navW, _navH);
        _persp.Position += v;
        _persp.Target += v;
        _ortho.Position += v;
        _ortho.Target += v;
    }

    public void ZoomAround(Vector2 screenPointPx, double factor)
    {
        if (factor <= 0)
            return;
        CameraGestures.AnchorZoom(_active, screenPointPx, factor, _navW, _navH);
    }

    public void BeginOrbit(Vector2 screenPointPx)
    {
        if (!_perspectiveActive)
        {
            _orbitPivot = null;
            return;
        }
        // Pivot about the target: a stable, always-visible point (the plotter's
        // cursor-ray pivot is unnecessary here).
        _orbitPivot = _persp.Target;
    }

    public void RotateByScreenDelta(Vector2 screenDeltaPx)
    {
        if (!_perspectiveActive || _orbitPivot is not Vector3 origin)
            return;
        CameraGestures.RotateAroundPoint(
            _persp, origin, screenDeltaPx, OrbitSensitivity, invertY: true, useWorldUp: false);
    }

    public void EndOrbit() => _orbitPivot = null;

    /// <summary>Draw the frame into the readback's already-bound, cleared
    /// FBO: opaque surface, then the translucent Z-offset plane, then the
    /// opaque measuring-envelope lines.</summary>
    public void RenderFrame(uint rtW, uint rtH)
    {
        if (_active is null)
            return;
        var vp = _active.View() * _active.Projection(rtW, rtH);

        var surfaceMvp = vp;
        _gl.UseProgram(_surfaceProg);
        _gl.UniformMatrix4(_surfaceMvp, false, MemoryMarshal.CreateReadOnlySpan(ref surfaceMvp.M11, 16));
        _gl.Uniform1(_surfaceAlpha, 1f);
        _surface.Draw();

        if (_plane.HasGeometry)
        {
            _gl.Uniform1(_surfaceAlpha, PlaneAlpha);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.DepthMask(false);
            _plane.Draw();
            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);
        }

        if (_envelope.HasGeometry)
        {
            UseLineProgram(in vp, rtW, rtH, EnvelopeWidthPx);
            _envelope.Draw();
        }
    }

    private void UseLineProgram(in Matrix4x4 vp, uint rtW, uint rtH, float widthPx)
    {
        _gl.UseProgram(_lineProg);
        var mvp = vp;
        _gl.UniformMatrix4(_lineMvp, false, MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));
        if (!_fatLines)
            return;
        _gl.Uniform2(_lineViewport, (float)rtW, (float)rtH);
        _gl.Uniform1(_lineWidthPx, widthPx);
    }

    private (Vector3 Center, float Radius, Vector3 Span) ContentMetrics()
    {
        var layer = _layer;
        if (layer is null || layer.Columns < 1 || layer.Rows < 1)
            return (Vector3.Zero, 43f, new Vector3(100f));

        var minZ = double.PositiveInfinity;
        var maxZ = double.NegativeInfinity;
        foreach (var z in layer.Z)
        {
            if (double.IsNaN(z)) continue;
            if (z < minZ) minZ = z;
            if (z > maxZ) maxZ = z;
        }
        if (double.IsPositiveInfinity(minZ))
        {
            minZ = 0;
            maxZ = 0;
        }

        var minX = layer.OriginX;
        var maxX = layer.OriginX + (layer.Columns - 1) * layer.CellX;
        var minY = layer.OriginY;
        var maxY = layer.OriginY + (layer.Rows - 1) * layer.CellY;
        var zLo = Math.Min(minZ * ZScale, layer.ZOffset);
        var zHi = Math.Max(maxZ * ZScale, layer.ZOffset);

        var min = new Vector3((float)minX, (float)minY, (float)zLo);
        var max = new Vector3((float)maxX, (float)maxY, (float)zHi);
        var center = (min + max) * 0.5f;
        var span = max - min;
        var radius = MathF.Max(span.Length() * 0.5f, 1f);
        return (center, radius, span);
    }

    private void RebuildMeshes()
    {
        var layer = _layer;
        if (layer is null || layer.Columns < 1 || layer.Rows < 1
            || layer.Z.Count < (long)layer.Columns * layer.Rows)
        {
            _surface.Upload([], []);
            _plane.Upload([], []);
            _envelope.Upload([]);
            return;
        }

        var mesh = HeightSurfaceMesh.Build(layer, ZScale);
        var vertexCount = mesh.Positions.Length / 3;
        var interleaved = new float[vertexCount * 6];
        for (var i = 0; i < vertexCount; i++)
        {
            interleaved[i * 6 + 0] = mesh.Positions[i * 3 + 0];
            interleaved[i * 6 + 1] = mesh.Positions[i * 3 + 1];
            interleaved[i * 6 + 2] = mesh.Positions[i * 3 + 2];
            interleaved[i * 6 + 3] = mesh.Colors[i * 3 + 0];
            interleaved[i * 6 + 4] = mesh.Colors[i * 3 + 1];
            interleaved[i * 6 + 5] = mesh.Colors[i * 3 + 2];
        }
        _surface.Upload(interleaved, mesh.Indices);
        _envelope.Upload(BuildEnvelope(layer));
        var (planeVerts, planeIdx) = BuildOffsetPlane(layer);
        _plane.Upload(planeVerts, planeIdx);
    }

    private float MinZ(HeightMapLayer layer)
    {
        var min = double.PositiveInfinity;
        foreach (var z in layer.Z)
        {
            if (double.IsNaN(z)) continue;
            if (z < min) min = z;
        }
        return double.IsPositiveInfinity(min) ? 0f : (float)(min * ZScale);
    }

    /// <summary>Axis-aligned envelope loop over the grid's XY extent at the
    /// surface's minimum Z, as interleaved <c>pos(3)+color(4)</c> line vertices.</summary>
    private float[] BuildEnvelope(HeightMapLayer layer)
    {
        var minX = (float)layer.OriginX;
        var maxX = (float)(layer.OriginX + (layer.Columns - 1) * layer.CellX);
        var minY = (float)layer.OriginY;
        var maxY = (float)(layer.OriginY + (layer.Rows - 1) * layer.CellY);
        var z = MinZ(layer);
        var verts = new List<float>(4 * 2 * 7);
        void Seg(float x0, float y0, float x1, float y1)
        {
            AppendVertex(verts, new Vector3(x0, y0, z), EnvelopeColor);
            AppendVertex(verts, new Vector3(x1, y1, z), EnvelopeColor);
        }
        Seg(minX, minY, maxX, minY);
        Seg(maxX, minY, maxX, maxY);
        Seg(maxX, maxY, minX, maxY);
        Seg(minX, maxY, minX, minY);
        return verts.ToArray();
    }

    /// <summary>Translucent quad at <c>Z == layer.ZOffset</c> spanning the grid's
    /// XY envelope, as interleaved <c>pos(3)+color(3)</c> triangle vertices.</summary>
    private (float[] Verts, uint[] Indices) BuildOffsetPlane(HeightMapLayer layer)
    {
        var minX = (float)layer.OriginX;
        var maxX = (float)(layer.OriginX + (layer.Columns - 1) * layer.CellX);
        var minY = (float)layer.OriginY;
        var maxY = (float)(layer.OriginY + (layer.Rows - 1) * layer.CellY);
        var z = (float)layer.ZOffset;
        var verts = new float[4 * 6];
        AddVertex(verts, 0, minX, minY, z);
        AddVertex(verts, 1, maxX, minY, z);
        AddVertex(verts, 2, maxX, maxY, z);
        AddVertex(verts, 3, minX, maxY, z);
        return (verts, [0, 1, 2, 0, 2, 3]);

        void AddVertex(float[] dst, int i, float x, float y, float zz)
        {
            dst[i * 6 + 0] = x;
            dst[i * 6 + 1] = y;
            dst[i * 6 + 2] = zz;
            dst[i * 6 + 3] = PlaneColor.X;
            dst[i * 6 + 4] = PlaneColor.Y;
            dst[i * 6 + 5] = PlaneColor.Z;
        }
    }

    private static void AppendVertex(List<float> dst, Vector3 p, Vector4 c)
    {
        dst.Add(p.X);
        dst.Add(p.Y);
        dst.Add(p.Z);
        dst.Add(c.X);
        dst.Add(c.Y);
        dst.Add(c.Z);
        dst.Add(c.W);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ctx.IsDisposed)
            return;
        _ctx.MakeCurrent();
        _surface.Dispose();
        _plane.Dispose();
        _envelope.Dispose();
        _gl.DeleteProgram(_surfaceProg);
        _gl.DeleteProgram(_lineProg);
    }

    /// <summary>An indexed mesh of interleaved <c>pos(3)+color(3)</c> vertices
    /// (stride 24) drawn as GL_TRIANGLES through the per-vertex-color program.</summary>
    private sealed unsafe class ColorMesh : IDisposable
    {
        private readonly GlContext _ctx;
        private readonly GlApi _gl;
        private uint _vao;
        private uint _vbo;
        private uint _ebo;
        private int _count;
        private bool _disposed;

        internal ColorMesh(GlContext ctx)
        {
            _ctx = ctx;
            ctx.MakeCurrent();
            _gl = ctx.Api;
            _vao = _gl.GenVertexArray();
            _vbo = _gl.GenBuffer();
            _ebo = _gl.GenBuffer();
            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 24, IntPtr.Zero);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 24, (IntPtr)12);
            _gl.EnableVertexAttribArray(1);
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            _gl.BindVertexArray(0);
        }

        internal bool HasGeometry => _count > 0;

        internal void Upload(float[] verts, uint[] indices)
        {
            if (_disposed || _ctx.IsDisposed)
                return;
            _ctx.MakeCurrent();
            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, verts.AsSpan(), BufferUsageARB.StaticDraw);
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            _gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, indices.AsSpan(), BufferUsageARB.StaticDraw);
            _gl.BindVertexArray(0);
            _count = indices.Length;
        }

        internal void Draw()
        {
            if (_disposed || _count == 0 || _ctx.IsDisposed)
                return;
            _ctx.MakeCurrent();
            _gl.BindVertexArray(_vao);
            // NULL = offset 0 into the bound element buffer, not a client pointer.
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_count, DrawElementsType.UnsignedInt, (void*)0);
            _gl.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_ctx.IsDisposed)
                return;
            _ctx.MakeCurrent();
            if (_ebo != 0) _gl.DeleteBuffer(_ebo);
            if (_vbo != 0) _gl.DeleteBuffer(_vbo);
            if (_vao != 0) _gl.DeleteVertexArray(_vao);
            _ebo = _vbo = _vao = 0;
        }
    }

    /// <summary>A line list of interleaved <c>pos(3)+color(4)</c> vertices
    /// (stride 28) drawn as GL_LINES through the shared line program.</summary>
    private sealed unsafe class LineMesh : IDisposable
    {
        private readonly GlContext _ctx;
        private readonly GlApi _gl;
        private uint _vao;
        private uint _vbo;
        private int _count;
        private bool _disposed;

        internal LineMesh(GlContext ctx)
        {
            _ctx = ctx;
            ctx.MakeCurrent();
            _gl = ctx.Api;
            _vao = _gl.GenVertexArray();
            _vbo = _gl.GenBuffer();
            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 28, IntPtr.Zero);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 28, (IntPtr)12);
            _gl.EnableVertexAttribArray(1);
            _gl.BindVertexArray(0);
        }

        internal bool HasGeometry => _count > 0;

        internal void Upload(float[] verts)
        {
            if (_disposed || _ctx.IsDisposed)
                return;
            _ctx.MakeCurrent();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, verts.AsSpan(), BufferUsageARB.StaticDraw);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            _count = verts.Length / 7;
        }

        internal void Draw()
        {
            if (_disposed || _count == 0 || _ctx.IsDisposed)
                return;
            _ctx.MakeCurrent();
            _gl.BindVertexArray(_vao);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_count);
            _gl.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_ctx.IsDisposed)
                return;
            _ctx.MakeCurrent();
            if (_vbo != 0) _gl.DeleteBuffer(_vbo);
            if (_vao != 0) _gl.DeleteVertexArray(_vao);
            _vbo = _vao = 0;
        }
    }
}
