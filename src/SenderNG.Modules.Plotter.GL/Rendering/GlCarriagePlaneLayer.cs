/*
 * GlCarriagePlaneLayer.cs - carriage plane layer: segments + run states as
 * two dynamic GL line lists
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
using SenderNG.Modules.Plotter.GL.Scene;
using GlApi = Silk.NET.OpenGL.GL;

namespace SenderNG.Modules.Plotter.GL.Rendering;

/// <summary>
/// One carriage-plane layer rendered as two pumped GL line lists (plane +
/// executed overlay). The staging logic — O(1) <see cref="AddSegment"/>,
/// idempotent <see cref="SetSegmentRunState"/> with incremental overlay
/// append, rewind to an O(n) <see cref="RebuildExecuted"/> — is CPU-only;
/// the GL half uses two dynamic VBOs re-uploaded in place by
/// <see cref="Pump"/>.
/// <para>
/// GPU content never changes at a mutation API — mutations only stage and
/// <see cref="RebuildGate.MarkDirty"/>. <see cref="Pump"/> (called at the
/// view's apply cadence) reconciles the staging into the buffers, coalesced by
/// the gate (150 ms / 1000 mutations), so a large toolpath is not re-uploaded
/// on every run-state tick. <see cref="Draw"/> renders the uploaded prefix only:
/// staged-but-unpumped content is not on the GPU yet, and the buffer never
/// carries stale bytes past the count the last pump shipped.
/// </para></summary>
internal sealed unsafe class GlCarriagePlaneLayer : ICarriagePlaneLayer, IDisposable
{
    // Skia-plotter-compatible palette (opaque — the line shader forces alpha 1).
    internal static readonly Vector4 CutColor = new(0.5f, 0.5f, 0.5f, 1f);
    internal static readonly Vector4 RapidColor = new(0.9f, 0.12f, 0.12f, 1f);
    internal static readonly Vector4 ExecutedColor = new(0.106f, 0.62f, 0.243f, 1f);
    internal static readonly Vector4 InFlightColor = new(0.878f, 0.588f, 0.125f, 1f);
    internal static readonly Vector4 ErrorColor = new(0.75f, 0.19f, 0.19f, 1f);

    internal const float BaseLineThickness = 3.2f;
    internal const float ExecutedLineThickness = 3.2f;

    internal static bool IsExecuted(RunState state) => state == RunState.Executed;

    private readonly GlContext _ctx;
    private readonly GlApi _gl;
    private readonly List<Vector3> _verts = new();        // 2 per segment (staging, GREEN logic)
    private readonly List<Vector4> _colors = new();       // 2 per segment (current per-segment color)
    private readonly List<MotionType> _motion = new();
    private readonly List<RunState> _applied = new();
    private readonly List<bool> _executed = new();
    private readonly List<Vector3> _doneVerts = new();    // executed overlay staging
    private readonly List<Vector4> _doneColors = new();
    private readonly RebuildGate _gate = new();           // copied GREEN: 150 ms / 1000 mutations
    private bool _runStateVisible = true;
    private int _planeCapacity, _doneCapacity;            // fixed VBO budgets (GL_DYNAMIC_DRAW, never shrink)
    private int _planeDrawCount, _doneDrawCount;          // vertices the last pump shipped
    private uint _planeVao, _planeVbo, _doneVao, _doneVbo;
    private bool _disposed;

    /// <summary>Create the layer's two VAO/VBO pairs on
    /// <paramref name="ctx"/>'s context. The draw widths are the palette
    /// constants: the scene pushes them to the expansion shader as
    /// <c>uLineWidthPx</c>, so nothing here depends on what the driver's
    /// <c>glLineWidth</c> accepts (checkpoint-4 items 3+4). Empty until the
    /// first <see cref="Pump"/>.</summary>
    public GlCarriagePlaneLayer(GlContext ctx)
    {
        _ctx = ctx;
        ctx.MakeCurrent();
        _gl = ctx.Api;
        (_planeVao, _planeVbo) = CreateLineArrays();
        (_doneVao, _doneVbo) = CreateLineArrays();
    }

    /// <summary>Pending mutations the layer's <see cref="RebuildGate"/> has
    /// recorded and not yet flushed — read seam for the "marked, not
    /// GPU-touched" test.</summary>
    internal int PendingMutations => _gate.PendingMutations;

    /// <summary>Read seam: the width this layer's draws render at — the
    /// <see cref="BaseLineThickness"/> palette value, pushed by the scene as
    /// <c>uLineWidthPx</c> (never driver-picked).</summary>
    internal float BaseLineWidth => BaseLineThickness;

    /// <summary>Executed-overlay vertex count (2 per done segment) — the
    /// diagnostic count.</summary>
    internal int ExecutedVerticesCount => _doneVerts.Count;

    /// <summary>Whether the last pump shipped plane content to the GPU — the
    /// spike error probe's "this frame really drew layers" read seam.</summary>
    internal bool HasContent => _planeDrawCount > 0;

    /// <summary>The staged base/state color of a segment (both vertices share
    /// one color — the color-table read seam).</summary>
    internal Vector4 ColorAt(int segmentIndex) => _colors[segmentIndex * 2];

    /// <inheritdoc/>
    public void AddSegment(LineSegment seg, MotionType type)
    {
        var color = BaseColor(type);
        _verts.Add(seg.From);
        _verts.Add(seg.To);
        _colors.Add(color);
        _colors.Add(color);
        _motion.Add(type);
        _executed.Add(false);
        _applied.Add(RunState.Pending);
        _gate.MarkDirty();
    }

    /// <inheritdoc/>
    public void SetSegmentRunState(int segmentIndex, RunState state)
    {
        if (segmentIndex < 0 || segmentIndex >= _motion.Count)
            return;
        if (_applied[segmentIndex] == state)
            return;
        _applied[segmentIndex] = state;
        var color = !_runStateVisible || state == RunState.Pending
            ? BaseColor(_motion[segmentIndex])
            : StateColor(state);
        _colors[segmentIndex * 2] = color;
        _colors[segmentIndex * 2 + 1] = color;

        // Overlay bookkeeping: append the done
        // segment exactly once per Pending/InFlight → Executed transition; the
        // flag flip keeps repeated grants idempotent. Rewind (Executed → any
        // other state) rebuilds the overlay from the flags. GPU content never
        // changes here — mark the pump pending instead.
        var wasExecuted = _executed[segmentIndex];
        var nowExecuted = IsExecuted(state);
        _executed[segmentIndex] = nowExecuted;
        if (nowExecuted && !wasExecuted)
            AppendDoneSegment(segmentIndex);
        else if (!nowExecuted && wasExecuted)
            RebuildDoneFromFlags();
        _gate.MarkDirty();
    }

    /// <inheritdoc/>
    public void RebuildExecuted() => RebuildDoneFromFlags();

    /// <summary>Gate the whole plane's run-state tinting (scene ShowRunState
    /// toggle). Hidden: every segment restages its base Rapid/Cut color, so the
    /// next pump ships a plain toolpath and <see cref="Draw"/> skips the
    /// executed overlay. Reapplied from the stored state set, so re-visiting a
    /// run window after toggling still matches what the pump last shipped.</summary>
    internal void SetRunStateVisible(bool visible)
    {
        if (_runStateVisible == visible)
            return;
        _runStateVisible = visible;
        for (var s = 0; s < _applied.Count; s++)
        {
            var state = _applied[s];
            var color = !visible || state == RunState.Pending
                ? BaseColor(_motion[s])
                : StateColor(state);
            _colors[s * 2] = color;
            _colors[s * 2 + 1] = color;
        }
        _gate.MarkDirty();
    }

    /// <summary>Append one done segment (two vertices + two colors) to the
    /// green thickness overlay staging list.</summary>
    private void AppendDoneSegment(int segmentIndex)
    {
        _doneVerts.Add(_verts[segmentIndex * 2]);
        _doneVerts.Add(_verts[segmentIndex * 2 + 1]);
        _doneColors.Add(ExecutedColor);
        _doneColors.Add(ExecutedColor);
    }

    private static Vector4 BaseColor(MotionType type) =>
        type == MotionType.Rapid ? RapidColor : CutColor;

    private static Vector4 StateColor(RunState state) => state switch
    {
        RunState.Executed => ExecutedColor,
        RunState.InFlight => InFlightColor,
        RunState.Error => ErrorColor,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>Clear and re-append the done overlay from the current executed
    /// set (rewind only; rare O(n)).</summary>
    private void RebuildDoneFromFlags()
    {
        _doneVerts.Clear();
        _doneColors.Clear();
        for (var i = 0; i < _executed.Count; i++)
        {
            if (_executed[i])
                AppendDoneSegment(i);
        }
        _gate.MarkDirty();
    }

    /// <summary>Reconcile the CPU staging lists into the two dynamic VBOs.
    /// Coalesced by <see cref="RebuildGate"/> (burned at the mutation APIs): a
    /// clean layer does nothing, bursts collapse to at most one upload per
    /// minimum interval. A buffer only ever grows — <c>glBufferData</c> at
    /// first content / growth beyond capacity, <c>glBufferSubData</c> of the
    /// active prefix otherwise — so the budget never shrinks mid-run.</summary>
    internal void Pump()
    {
        if (_disposed || _ctx.IsDisposed)
            return;
        if (!_gate.RebuildDue(DateTime.UtcNow))
            return;
        _ctx.MakeCurrent();
        UploadPlane();
        UploadDone();
    }

    private void UploadPlane()
    {
        var n = _verts.Count;
        _planeDrawCount = n;
        if (n == 0)
            return;
        var data = Interleave(_verts, _colors);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _planeVbo);
        if (n > _planeCapacity)
        {
            _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, data, BufferUsageARB.DynamicDraw);
            _planeCapacity = n;
        }
        else
        {
            _gl.BufferSubData<float>(BufferTargetARB.ArrayBuffer, 0, data);
        }
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    private void UploadDone()
    {
        var n = _doneVerts.Count;
        _doneDrawCount = n;
        if (n == 0)
            return;
        var data = Interleave(_doneVerts, _doneColors);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _doneVbo);
        if (n > _doneCapacity)
        {
            _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, data, BufferUsageARB.DynamicDraw);
            _doneCapacity = n;
        }
        else
        {
            _gl.BufferSubData<float>(BufferTargetARB.ArrayBuffer, 0, data);
        }
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    /// <summary>Draw the uploaded prefix of both lists — chrome first, then
    /// layers (the scene calls this after <c>DrawChrome</c> under GL_LEQUAL,
    /// so the executed overlay wins an equal depth through its later draw).
    /// The line program, its MVP, the viewport size and the palette width are
    /// the scene's — set up by the caller before this runs.</summary>
    internal void Draw()
    {
        if (_disposed || _ctx.IsDisposed || _planeDrawCount == 0)
            return;
        _ctx.MakeCurrent();
        _gl.BindVertexArray(_planeVao);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_planeDrawCount);
        if (_runStateVisible && _doneDrawCount > 0)
        {
            _gl.BindVertexArray(_doneVao);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_doneDrawCount);
        }
        _gl.BindVertexArray(0);
    }

    /// <summary>Extents of the staged segments — false when the layer is
    /// empty (the scene falls back to the grid bounds).</summary>
    internal bool TryGetBounds(out BoundingBox b)
    {
        if (_verts.Count == 0)
        {
            b = default;
            return false;
        }
        var min = _verts[0];
        var max = _verts[0];
        for (var i = 1; i < _verts.Count; i++)
        {
            min = Vector3.Min(min, _verts[i]);
            max = Vector3.Max(max, _verts[i]);
        }
        b = new BoundingBox(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
        return true;
    }

    private (uint Vao, uint Vbo) CreateLineArrays()
    {
        var vao = _gl.GenVertexArray();
        var vbo = _gl.GenBuffer();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 28, IntPtr.Zero);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 28, (IntPtr)12);
        _gl.EnableVertexAttribArray(1);
        _gl.BindVertexArray(0);
        return (vao, vbo);
    }

    /// <summary>One interleaved line vertex run: <c>vec3 pos + vec4 color</c>
    /// per vertex, stride 28 (the chrome line layout).</summary>
    private static float[] Interleave(List<Vector3> verts, List<Vector4> colors)
    {
        var data = new float[verts.Count * 7];
        for (var i = 0; i < verts.Count; i++)
        {
            var p = verts[i];
            var c = i < colors.Count ? colors[i] : CutColor;
            var o = i * 7;
            data[o] = p.X;
            data[o + 1] = p.Y;
            data[o + 2] = p.Z;
            data[o + 3] = c.X;
            data[o + 4] = c.Y;
            data[o + 5] = c.Z;
            data[o + 6] = c.W;
        }
        return data;
    }

    /// <summary>Free the two VAO/VBO pairs. Scene and context are one
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
        if (_planeVbo != 0)
            _gl.DeleteBuffer(_planeVbo);
        if (_planeVao != 0)
            _gl.DeleteVertexArray(_planeVao);
        if (_doneVbo != 0)
            _gl.DeleteBuffer(_doneVbo);
        if (_doneVao != 0)
            _gl.DeleteVertexArray(_doneVao);
        _planeVbo = 0;
        _planeVao = 0;
        _doneVbo = 0;
        _doneVao = 0;
    }
}
