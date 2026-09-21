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
/// The translucent swept sheet between the two carriage planes. The line-loop
/// wire (<see cref="SetWirePlane"/>) is permanently disabled — fork #7
/// (2026-09-16) attributed the red-fan artifact to the static index repack that
/// re-baking the loop geometry could trigger; the sheet is a single static mesh
/// uploaded once per program build and never re-registered, so it keeps its
/// baked <c>IndexOffset</c> for the life of the build.
/// </summary>
public sealed class NexLoftSurface : ILoftSurface
{
    private readonly Engine _engine;
    private readonly Node _root;
    private MeshNode? _sheetNode;
    private Geometry? _sheetGeometry;
    private PBRMaterialProperties? _sheetMaterial;

    /// <summary>Create the loft surface under <paramref name="root"/>. The sheet
    /// node is created lazily on the first <see cref="SetLoftSheet"/>.</summary>
    public NexLoftSurface(Node root, Engine engine)
    {
        _engine = engine;
        _root = root;
    }

    /// <summary>Permanently disabled: the line-loop wire was attributed to the
    /// red-fan artifact (fork #7, 2026-09-16) and removed. The interface is
    /// retained so the build pipeline can keep calling it.</summary>
    public void SetWirePlane(Vector3[] polygon)
    {
    }

    /// <summary>Swept sheet colour — the legacy loft blue-grey at 35% opacity
    /// (matching Skia's semi-transparent coupling-wire tint).</summary>
    private static readonly Color4 SheetColor = new(0.42f, 0.42f, 0.54f, 1f);

    /// <summary>Build (and upload, once) the static translucent sheet. Called once
    /// per program build — never per status poll, so no device-lost hazard. Old
    /// geometry is disposed after the swap.</summary>
    public void SetLoftSheet(IReadOnlyList<Vector3> sideA, IReadOnlyList<Vector3> sideB)
    {
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
        _sheetMaterial ??= _engine.ResourceManager.PBRPropertyManager.Create(PBRShadingMode.CAD);
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