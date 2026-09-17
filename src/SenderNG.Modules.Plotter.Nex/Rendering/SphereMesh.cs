/*
 * SphereMesh.cs - shared unit-sphere mesh builder
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

namespace SenderNG.Modules.Plotter.Nex.Rendering;

/// <summary>
/// Static builder for the closed unit-sphere mesh shared by the scene chrome:
/// radius 1 centred on the local origin, per-vertex normal equal to the
/// position (a unit sphere), UV-wrapped latitude rings from the +Z pole.
/// </summary>
internal static class SphereMesh
{
    public const int DefaultThetaDiv = 16;
    public const int DefaultPhiDiv = 12;

    internal static Geometry Build(
        int thetaDiv = DefaultThetaDiv,
        int phiDiv = DefaultPhiDiv)
    {
        var g = new Geometry(Topology.Triangle);
        var vertexCount = (phiDiv + 1) * thetaDiv;
        g.Vertices.Capacity = vertexCount;
        g.VertexProps.Capacity = vertexCount;
        g.Indices.Capacity = phiDiv * thetaDiv * 6;

        var thetaStep = MathF.PI * 2f / thetaDiv;
        var phiStep = MathF.PI / phiDiv;
        for (var row = 0; row <= phiDiv; row++)
        {
            var phi = row * phiStep;
            var z = MathF.Cos(phi);
            var ring = MathF.Sin(phi);
            for (var col = 0; col < thetaDiv; col++)
            {
                var theta = col * thetaStep;
                var pos = new Vector3(ring * MathF.Cos(theta), ring * MathF.Sin(theta), z);
                g.Vertices.Add(new Vector4(pos, 1f));
                g.VertexProps.Add(new VertexProperties(pos, new Vector2(theta / (MathF.PI * 2f), phi / MathF.PI)));
            }
        }

        // Two triangles per quad; wrap the closing column back to col 0 so the
        // ring seam shares the row's first vertex (never writes an out-of-row
        // index, which would point past the last vertex at the top latitude).
        for (var row = 0; row < phiDiv; row++)
        {
            for (var col = 0; col < thetaDiv; col++)
            {
                var i0 = row * thetaDiv + col;
                var colNext = col + 1 == thetaDiv ? 0 : col + 1;
                var i1 = i0 + thetaDiv;
                var i2 = i1 + (colNext - col);
                var i3 = i0 + (colNext - col);
                g.Indices.Add((uint)i0);
                g.Indices.Add((uint)i1);
                g.Indices.Add((uint)i2);
                g.Indices.Add((uint)i0);
                g.Indices.Add((uint)i2);
                g.Indices.Add((uint)i3);
            }
        }

        g.UpdateBounds();
        g.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexProp | GeometryBufferType.Index);
        return g;
    }
}