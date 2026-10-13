/*
 * AxisArrows.cs - headless RGB axis-arrow mesh builder
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
using SenderNG.Modules.Common.Render;

namespace SenderNG.Modules.HeightMapViewer;

/// <summary>
/// CPU-baked RGB axis arrows for the height-map origin: three shaft lines along
/// the positive axes plus a cone arrowhead at each tip (X red, Y green, Z blue).
/// <para>
/// The viewer has no cone-instancing shader, so unlike the plotter's
/// <c>GlPlotScene</c> the cone heads are transformed on the CPU here:
/// <see cref="GlMeshBuilder.BuildCone"/>'s local <c>pos(3)+normal(3)</c> vertices
/// (apex at the origin, opening along +Z) are rotated by
/// <see cref="ConeOrientation.FromUnitZTo"/> and translated to each tip, then
/// emitted as interleaved <c>pos(3)+color(3)</c> — the layout the scene's
/// per-vertex-color mesh program consumes. The builder touches no GL types and
/// is safe to unit-test off the UI thread.
/// </para>
/// </summary>
internal static class AxisArrows
{
    /// <summary>Arrow length as a fraction of the map's larger XY span.</summary>
    internal const float ArrowLengthFraction = 0.3f;

    /// <summary>Cone height as a fraction of the arrow length.</summary>
    internal const float HeadHeightFraction = 0.20f;

    /// <summary>Cone base radius as a fraction of the arrow length.</summary>
    internal const float HeadRadiusFraction = 0.08f;

    private const int ConeSegments = 24;

    private static readonly Vector4 ColorX = new(0.85f, 0.15f, 0.15f, 1f);
    private static readonly Vector4 ColorY = new(0.15f, 0.70f, 0.20f, 1f);
    private static readonly Vector4 ColorZ = new(0.20f, 0.40f, 0.90f, 1f);

    private static readonly Vector3[] Directions = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
    private static readonly Vector4[] Colors = [ColorX, ColorY, ColorZ];

    /// <summary>Build the three axis arrows anchored at <paramref name="origin"/>.
    /// <para>
    /// The shaft buffer is interleaved <c>pos(3)+color(4)</c> line vertices
    /// (stride 7); the head buffer is interleaved <c>pos(3)+color(3)</c> triangle
    /// vertices (stride 6) with a <c>uint[]</c> index list. All three buffers are
    /// empty for a non-finite or non-positive XY span (a degenerate map), so the
    /// caller uploads nothing.
    /// </para></summary>
    internal static (float[] ShaftVerts, float[] HeadVerts, uint[] HeadIndices) Build(
        Vector3 origin, Vector3 span)
    {
        var arrowLength = ArrowLengthFraction * MathF.Max(span.X, span.Y);
        if (!float.IsFinite(arrowLength) || arrowLength <= 0f)
            return ([], [], []);

        var headHeight = HeadHeightFraction * arrowLength;
        var headRadius = HeadRadiusFraction * arrowLength;
        var (coneVerts, coneIndices) = GlMeshBuilder.BuildCone(headRadius, headHeight, ConeSegments);

        var shafts = new List<float>(3 * 2 * 7);
        var heads = new List<float>(3 * (coneVerts.Length / 6) * 6);
        var indices = new List<uint>(3 * coneIndices.Length);

        for (var axis = 0; axis < Directions.Length; axis++)
        {
            var dir = Directions[axis];
            var color = Colors[axis];

            // The shaft stops where the cone begins; the head covers the last
            // headHeight and its apex lands on origin + dir*arrowLength.
            var shaftEnd = origin + dir * MathF.Max(arrowLength - headHeight, 0f);
            AppendLineVertex(shafts, origin, color);
            AppendLineVertex(shafts, shaftEnd, color);

            var tip = origin + dir * arrowLength;
            var rotation = ConeOrientation.FromUnitZTo(-dir);
            var baseIndex = (uint)(heads.Count / 6);

            for (var v = 0; v < coneVerts.Length; v += 6)
            {
                var local = new Vector3(coneVerts[v], coneVerts[v + 1], coneVerts[v + 2]);
                // Row-vector order: v * R, then translate to the tip.
                var world = Vector3.Transform(local, rotation) + tip;
                heads.Add(world.X);
                heads.Add(world.Y);
                heads.Add(world.Z);
                heads.Add(color.X);
                heads.Add(color.Y);
                heads.Add(color.Z);
            }

            foreach (var index in coneIndices)
                indices.Add(baseIndex + index);
        }

        return (shafts.ToArray(), heads.ToArray(), indices.ToArray());
    }

    private static void AppendLineVertex(List<float> dst, Vector3 p, Vector4 c)
    {
        dst.Add(p.X);
        dst.Add(p.Y);
        dst.Add(p.Z);
        dst.Add(c.X);
        dst.Add(c.Y);
        dst.Add(c.Z);
        dst.Add(c.W);
    }
}
