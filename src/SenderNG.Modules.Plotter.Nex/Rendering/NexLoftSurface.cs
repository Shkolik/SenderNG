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
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders.Frag;
using SenderNG.Modules.Plotter.Nex.Scene;

namespace SenderNG.Modules.Plotter.Nex.Rendering;

/// <summary>
/// One wire-plane loop (the polygon outline) rendered as a long-lived static
/// line list registered once and re-uploaded in place. <see cref="SetWirePlane"/>
/// mutates CPU staging only (no GPU work); the GPU gets the SAME device buffer
/// refreshed through <see cref="NexPlotScene.PumpToolWireAndLofts"/> (a low-Hz
/// pump polled from the view, same as the carriage planes). A fresh geometry is
/// only ever Add-ed to the engine when the loop first appears or outgrows its
/// capacity — never per status poll — so the shared static index buffer never
/// repacks and other static draws (tool cones, loft sheet) keep their baked
/// <c>IndexOffset</c> (the stale-FirstIndex mechanism behind the red fan).
/// This deliberately NEVER writes host-visible mapped buffers that in-flight
/// frames read — in-place dynamic uploads raced the live render loop and
/// produced VK_ERROR_DEVICE_LOST on the loft scenes (helix engine
/// 1.3.0-alpha.41, driver 32.0.15.9608).
/// </summary>
public sealed class NexLoftSurface : ILoftSurface
{
    // Opaque dim blue-grey (Skia's semi-transparent loft colour becomes solid —
    // alpha handling in the Nex line shader is unverified).
    private static readonly Color4 WireColor = new(0.42f, 0.42f, 0.54f, 1f);

    private readonly Engine _engine;
    private readonly Node _root;
    private readonly Geometry _staging;
    private readonly LineNode _node;
    private readonly RebuildGate _rebuildGate = new();
    private Geometry? _staticLoop;
    private int _loopCapacity;
    private MeshNode? _sheetNode;
    private Geometry? _sheetGeometry;
    private PBRMaterialProperties? _sheetMaterial;
    private Vector3[]? _lastLoop;

    /// <summary>DIAGNOSTIC (temporary): skip the per-poll line-loop staging/swap
    /// (SetWirePlane/Pump) while keeping the sheet. App A/B for the red-fan
    /// artifact: loop-off isolates the loop from the sheet+cones.</summary>
    internal bool DiagnosticDisableLoop { get; set; }

    /// <summary>DIAGNOSTIC (temporary): skip the swept sheet mesh (SetLoftSheet)
    /// while keeping the line loop. App A/B: sheet-off isolates the translucent
    /// WBOIT mesh from the (opaque) loop + cones.</summary>
    internal bool DiagnosticDisableSheet { get; set; }

    /// <summary>Create the loft surface under <paramref name="root"/>. The node
    /// stays geometry-less until the first <see cref="Pump"/>.</summary>
    public NexLoftSurface(Node root, Engine engine)
    {
        _engine = engine;
        _root = root;
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
        if (DiagnosticDisableLoop)
            return;
        // Idle status polls repeat the same polygon dozens of times a second;
        // re-staging on every poll keeps a static wire on the rebuild cadence.
        // Only stage when the value actually changed.
        if (LoopEquals(_lastLoop, polygon))
            return;
        _lastLoop = polygon is null or { Length: < 2 } ? null : (Vector3[])polygon.Clone();
        FillLoop(_staging, polygon);
        // GPU content never changes here — mark the swap-pump pending instead.
        _rebuildGate.MarkDirty();
    }

    private static bool LoopEquals(Vector3[]? a, Vector3[]? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return a is null && b is null;
        if (a.Length != b.Length)
            return false;
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i])
                return false;
        return true;
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

    /// <summary>In-place static pump: copy the staging loop into the ONE long-lived
    /// static geometry, register it once via the engine's staged buffer upload
    /// (device-local destination — never a mapped write into a buffer a frame is
    /// reading), and keep re-uploading into that same buffer. Rebuilds are
    /// coalesced by <see cref="RebuildGate"/> (position changes arrive at
    /// status-poll cadence), so this never rewrites a live buffer in place and
    /// never swaps geometries — the static pool is packed at build, not per poll.</summary>
    internal void Pump()
    {
        if (!_rebuildGate.RebuildDue(DateTime.UtcNow))
            return;

        var staged = _staging;
        var n = staged.Vertices.Count;
        if (n == 0)
        {
            // Loop with nothing to draw: hide while keeping the registration.
            _node.Geometry = null;
            return;
        }

        if (_staticLoop is null || n > _loopCapacity)
        {
            if (n > _loopCapacity)
                _loopCapacity = n;
            var fresh = NexCarriagePlaneLayer.BuildStatic(staged);
            _staticLoop?.Dispose();
            _staticLoop = fresh;
            _engine.Add(_staticLoop);
            // Count changed — re-set the reference so the draw stream refreshes
            // its LineCount.
            _node.Geometry = _staticLoop;
        }
        else
        {
            NexCarriagePlaneLayer.RefillLines(
                _staticLoop, staged, _loopCapacity, WireColor.ToVector4());
            _staticLoop.UpdateBuffers(_engine.Context).CheckResult();
        }
    }

    /// <summary>Swept sheet colour — the legacy loft blue-grey at 35% opacity
    /// (matching Skia's semi-transparent coupling-wire tint).</summary>
    private static readonly Color4 SheetColor = new(0.42f, 0.42f, 0.54f, 1f);

    /// <summary>Build (and upload, once) the static translucent sheet. Called once
    /// per program build — never per status poll, so no device-lost hazard. Old
    /// geometry is disposed after the swap.</summary>
    public void SetLoftSheet(IReadOnlyList<Vector3> sideA, IReadOnlyList<Vector3> sideB)
    {
        if (DiagnosticDisableSheet)
            return;
        var sheet = BuildSheet(sideA, sideB);
        var old = _sheetGeometry;
        if (sheet.Vertices.Count == 0)
        {
            if (_sheetNode is not null)
            {
                _sheetNode.Geometry = null;
                _sheetNode.Dispose();
                _sheetNode = null;
            }
            _sheetGeometry = null;
        }
        else
        {
            _sheetNode ??= CreateSheetNode();
            _engine.Add(sheet);
            _sheetNode.Geometry = sheet;
            _sheetGeometry = sheet;
        }
        old?.Dispose();
    }

    private MeshNode CreateSheetNode()
    {
        _sheetMaterial ??= _engine.ResourceManager.PBRPropertyManager.Create(PBRShadingMode.Unlit);
        _sheetMaterial.Albedo = SheetColor;
        _sheetMaterial.Emissive = Color4.Black;
        _sheetMaterial.Opacity = 0.35f;
        var node = _root.World.CreateMeshNode("loft-sheet");
        node.MaterialProperties = _sheetMaterial;
        node.IsTransparent = true;
        _root.AddChild(node);
        return node;
    }

    /// <summary>Release the sheet mesh node + material. Called on scene dispose and
    /// ResetBuild (the engine defers GPU geometry removal itself).</summary>
    internal void DisposeMesh()
    {
        _sheetNode?.Dispose();
        _sheetNode = null;
        _sheetMaterial?.Dispose();
        _sheetMaterial = null;
        _sheetGeometry = null;
        _staticLoop?.Dispose();
        _staticLoop = null;
    }

    /// <summary>Build the static swept sheet as a triangle strip of quads joining
    /// <paramref name="sideA"/> to <paramref name="sideB"/> (equal-length arrays).
    /// One quad per consecutive pair; quads with no width or zero span are skipped
    /// and a pair count below 2 yields an empty geometry. Normals are computed per
    /// quad (0,0,1 fallback) — flat-unlit so they only matter for consistency.</summary>
    internal static Geometry BuildSheet(IReadOnlyList<Vector3> sideA, IReadOnlyList<Vector3> sideB)
    {
        ArgumentNullException.ThrowIfNull(sideA);
        ArgumentNullException.ThrowIfNull(sideB);
        if (sideA.Count != sideB.Count)
            throw new ArgumentException("Loft sheet side point counts must match.", nameof(sideA));

        var g = new Geometry();
        if (sideA.Count < 2)
            return g;

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

            var index = g.Vertices.Count;
            g.Vertices.Add(new Vector4(a0, 1f));
            g.Vertices.Add(new Vector4(b0, 1f));
            g.Vertices.Add(new Vector4(b1, 1f));
            g.Vertices.Add(new Vector4(a1, 1f));

            var normal = SafeNormal(b0 - a0, b1 - a0);
            g.VertexProps.Add(new VertexProperties(normal));
            g.VertexProps.Add(new VertexProperties(normal));
            g.VertexProps.Add(new VertexProperties(normal));
            g.VertexProps.Add(new VertexProperties(normal));

            g.Indices.Add((uint)index);
            g.Indices.Add((uint)index + 1);
            g.Indices.Add((uint)index + 2);
            g.Indices.Add((uint)index);
            g.Indices.Add((uint)index + 2);
            g.Indices.Add((uint)index + 3);
        }

        g.UpdateBounds();
        return g;
    }

    private static bool AlmostSame(Vector3 x, Vector3 y) =>
        Vector3.DistanceSquared(x, y) < 1e-12f;

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
}