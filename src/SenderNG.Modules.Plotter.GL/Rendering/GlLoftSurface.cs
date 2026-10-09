/*
 * GlLoftSurface.cs - the translucent swept sheet between the carriage planes
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
using SenderNG.Modules.Plotter.GL.Scene;
using GlApi = Silk.NET.OpenGL.GL;

namespace SenderNG.Modules.Plotter.GL.Rendering;

/// <summary>The translucent swept sheet between the two carriage planes —
/// the GL port of <c>NexLoftSurface</c> with a container swap (Nex's
/// <c>Geometry</c> → plain interleaved arrays) and direct GL uploads instead
/// of engine nodes. <see cref="SetLoftSheet"/> ports
/// <c>NexLoftSurface.BuildSheet</c> (lines 140-185) verbatim: one quad per
/// consecutive pair, 4 unshared vertices and 6 indices each, both
/// degenerate-pair skip rules and the flat per-quad normal with its
/// <c>(0,0,1)</c> fallback. <para>
/// The line-loop wire (<see cref="SetWirePlane"/>) is permanently disabled —
/// fork #7 kept it that way in Nex and the interface only survives so the
/// build pipeline can keep calling it. The sheet is static per program build
/// (uploaded by <see cref="SetLoftSheet"/>, never pumped) and drawn last in
/// the frame through the shared mesh program by
/// <see cref="GlPlotScene.DrawLoft"/>.</para></summary>
internal sealed unsafe class GlLoftSurface : ILoftSurface, IDisposable
{
    private readonly GlContext _ctx;
    private readonly GlApi _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private bool _disposed;

    /// <summary>Create the sheet's VAO/VBO/EBO on <paramref name="context"/>'s
    /// context — the same build-time call context as <see cref="GlPlotTool"/>'s
    /// constructor mesh (the factory builds scene objects with the context
    /// current, no pump). Empty storage until the first
    /// <see cref="SetLoftSheet"/>.</summary>
    public GlLoftSurface(GlContext context)
    {
        _ctx = context;
        _ctx.MakeCurrent();
        _gl = _ctx.Api;
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, ReadOnlySpan<float>.Empty,
            BufferUsageARB.DynamicDraw);
        // MeshVertex layout: location 0 = position, location 1 = normal,
        // interleaved float[6] per vertex (stride 24).
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 24, IntPtr.Zero);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 24, (IntPtr)12);
        _gl.EnableVertexAttribArray(1);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        _gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, ReadOnlySpan<uint>.Empty,
            BufferUsageARB.DynamicDraw);
        _gl.BindVertexArray(0);
    }

    /// <summary>Uploaded vertex count of the current sheet (6 floats each) —
    /// internal read seam for the geometry tests.</summary>
    internal int VertexCount { get; private set; }

    /// <summary>Uploaded index count of the current sheet — internal read seam
    /// for the geometry tests.</summary>
    internal int IndexCount { get; private set; }

    /// <summary>True when a non-empty sheet is uploaded and
    /// <see cref="GlPlotScene.DrawLoft"/> may draw it.</summary>
    internal bool HasGeometry => IndexCount > 0;

    /// <summary>Permanently disabled: the line-loop wire was attributed to the
    /// red-fan artifact (Nex fork #7, 2026-09-16) and removed. The interface
    /// is retained so the build pipeline can keep calling it — behavior
    /// preserved verbatim from <c>NexLoftSurface.SetWirePlane</c>.</summary>
    public void SetWirePlane(Vector3[] polygon)
    {
    }

    /// <summary>Build and upload the static sheet between the two equal-length
    /// side arrays — the <c>NexLoftSurface.BuildSheet</c> port (lines
    /// 140-185), a container swap from Nex's <c>Geometry</c> to interleaved
    /// position+normal arrays. Called once per program build, never per
    /// status poll. Count mismatch throws; fewer than two points (or a
    /// degenerate-only strip) uploads an empty sheet, which
    /// <see cref="HasGeometry"/> reports as no geometry.</summary>
    public void SetLoftSheet(IReadOnlyList<Vector3> sideA, IReadOnlyList<Vector3> sideB)
    {
        ArgumentNullException.ThrowIfNull(sideA);
        ArgumentNullException.ThrowIfNull(sideB);
        if (sideA.Count != sideB.Count)
            throw new ArgumentException("Loft sheet side point counts must match.", nameof(sideA));

        var verts = new List<float>();
        var indices = new List<uint>();
        if (sideA.Count >= 2)
        {
            for (var i = 0; i + 1 < sideA.Count; i++)
            {
                var a0 = sideA[i];
                var a1 = sideA[i + 1];
                var b0 = sideB[i];
                var b1 = sideB[i + 1];
                // No width (both sides stationary) or zero span — nothing to draw.
                if (AlmostSame(a0, a1) && AlmostSame(b0, b1))
                    continue;
                if (AlmostSame(a0, b0) && AlmostSame(a1, b1))
                    continue;

                var index = (uint)(verts.Count / 6);
                var normal = SafeNormal(b0 - a0, b1 - a0);
                AppendVertex(verts, a0, normal);
                AppendVertex(verts, b0, normal);
                AppendVertex(verts, b1, normal);
                AppendVertex(verts, a1, normal);

                indices.Add(index);
                indices.Add(index + 1);
                indices.Add(index + 2);
                indices.Add(index);
                indices.Add(index + 2);
                indices.Add(index + 3);
            }
        }

        Upload(verts, indices);
    }

    /// <summary>Re-upload the rebuilt arrays into the SAME device buffers
    /// (DynamicDraw — replaced, never reallocated) and latch the counts. An
    /// empty build stores a zero count, so <see cref="Draw"/> skips like
    /// Nex's lazily-absent sheet node.</summary>
    private void Upload(List<float> verts, List<uint> indices)
    {
        if (_disposed || _ctx.IsDisposed)
            return;
        _ctx.MakeCurrent();
        VertexCount = verts.Count / 6;
        IndexCount = indices.Count;
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer,
            CollectionsMarshal.AsSpan(verts), BufferUsageARB.DynamicDraw);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        _gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer,
            CollectionsMarshal.AsSpan(indices), BufferUsageARB.DynamicDraw);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    /// <summary>Issue the sheet's triangles. The pass uniforms (program,
    /// <c>uMvp = vp</c>, identity model, the CAD camera uniforms with the
    /// raw tone-map discriminator, sheet colour, blend and depth-mask state)
    /// belong to the caller — <see cref="GlPlotScene.DrawLoft"/> owns the
    /// frame's only blend pass — so this only binds the VAO and draws;
    /// <paramref name="vp"/> is the frame's view-projection that pass pushed,
    /// carried here for draw-loop parity with <see cref="GlPlotTool.Draw"/>.
    /// Empty or disposed sheets return without drawing.</summary>
    internal void Draw(in Matrix4x4 vp)
    {
        if (_disposed || _ctx.IsDisposed || IndexCount == 0)
            return;
        _ctx.MakeCurrent();
        _gl.BindVertexArray(_vao);
        // Offset 0 into the bound element buffer: the pointer is a byte
        // offset there, so NULL is the first index, not a client pointer.
        _gl.DrawElements(PrimitiveType.Triangles, (uint)IndexCount,
            DrawElementsType.UnsignedInt, (void*)0);
        _gl.BindVertexArray(0);
    }

    /// <summary>Free the sheet's GL objects. Scene and context are one
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
        if (_ebo != 0)
            _gl.DeleteBuffer(_ebo);
        if (_vbo != 0)
            _gl.DeleteBuffer(_vbo);
        if (_vao != 0)
            _gl.DeleteVertexArray(_vao);
    }

    /// <summary>Nex's <c>AlmostSame</c> (NexLoftSurface.cs:187-188),
    /// verbatim.</summary>
    private static bool AlmostSame(Vector3 x, Vector3 y) =>
        Vector3.DistanceSquared(x, y) < 1e-12f;

    /// <summary>Nex's <c>SafeNormal</c> (NexLoftSurface.cs:190-200),
    /// verbatim: cross with the reversed cross, then a (0,0,1) fallback.</summary>
    private static Vector3 SafeNormal(Vector3 u, Vector3 v)
    {
        var n = Vector3.Cross(u, v);
        if (n.LengthSquared() < 1e-12f)
        {
            n = Vector3.Cross(v, u);
            if (n.LengthSquared() < 1e-12f)
                n = new Vector3(0, 0, 1);
        }
        return Vector3.Normalize(n);
    }

    /// <summary>One interleaved mesh vertex: <c>vec3 pos + vec3 normal</c>,
    /// stride 24 — the <c>MeshVertex</c> locations 0/1.</summary>
    private static void AppendVertex(List<float> dst, Vector3 p, Vector3 n)
    {
        dst.Add(p.X);
        dst.Add(p.Y);
        dst.Add(p.Z);
        dst.Add(n.X);
        dst.Add(n.Y);
        dst.Add(n.Z);
    }
}
