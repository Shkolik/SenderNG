/*
 * GlMeshBuilder.cs - plain-array ports of the shared cone and sphere tessellation
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

namespace SenderNG.Modules.Common.Render;

/// <summary>Static triangle meshes for the chrome heads and the plot tool.
/// <para>
/// The tessellation is shared cone/sphere mesh math: the same
/// ring math, winding, and wrap-around index scheme, emitted as an
/// interleaved <c>pos(3)+normal(3)</c> float buffer plus a triangle-list
/// index array.
/// </para></summary>
public static class GlMeshBuilder
{
    /// <summary>Shared cone dimensions. The axis-arrowhead scale divides by these, so the
    /// axis cone must be built at exactly this size.</summary>
    public const float DefaultRadius = 1.5f;
    public const float DefaultHeight = 3f;

    /// <summary>Closed solid cone: apex at the local origin, base cap closed at
    /// <c>z = height</c>, opening along +Z — exactly the source builder's layout
    /// (6 vertices per ring step, two triangles per step). Returns interleaved
    /// <c>pos(3)+normal(3)</c> vertices.</summary>
    public static (float[] Verts, uint[] Idx) BuildCone(float radius, float height, int segments = 24)
    {
        var thetaDiv = segments;
        var total = 6 * thetaDiv;
        var verts = new float[total * 6];
        var indices = new uint[3 * total];
        var n = 0;
        var k = 0;

        var angle = (float)Math.PI * 2f / thetaDiv;
        var center = new Vector3(0f, 0f, height);
        var capNormal = Vector3.UnitZ;

        for (var i = 0; i < thetaDiv; i++)
        {
            var w = new Vector3(-(float)Math.Cos(i * angle), (float)Math.Sin(i * angle), 0f);
            var ring = new Vector3(w.X * radius, w.Y * radius, height);
            var skirtNormal = Vector3.Normalize(new Vector3(w.X * height, w.Y * height, -radius));

            AddVertex(Vector3.Zero, Vector3.Zero);
            AddVertex(Vector3.Zero, Vector3.Zero);
            AddVertex(Vector3.Zero, skirtNormal);
            AddVertex(ring, skirtNormal);
            AddVertex(ring, capNormal);
            AddVertex(center, capNormal);

            var i0 = i * 6;
            for (var j = 0; j < 3; j++)
            {
                var a = i0 + j * 2;
                var c = (a + 6) % total;
                var d = c + 1;
                // Every triangle winds front-facing OUTWARD (across the cap's rim
                // the +Z face, across the skirt the radial face), so the closed
                // cone reads solid under back-face culling (opaque pass). The
                // apex/skirt joint triangles stay degenerate either way.
                indices[k++] = (uint)(a + 1);
                indices[k++] = (uint)c;
                indices[k++] = (uint)a;
                indices[k++] = (uint)(a + 1);
                indices[k++] = (uint)d;
                indices[k++] = (uint)c;
            }
        }

        return (verts, indices);

        void AddVertex(Vector3 position, Vector3 normal)
        {
            verts[n * 6 + 0] = position.X;
            verts[n * 6 + 1] = position.Y;
            verts[n * 6 + 2] = position.Z;
            verts[n * 6 + 3] = normal.X;
            verts[n * 6 + 4] = normal.Y;
            verts[n * 6 + 5] = normal.Z;
            n++;
        }
    }

    /// <summary>Sphere of <paramref name="radius"/> centred on the local
    /// origin: latitude rings from the +Z pole, per-vertex normal equal to the
    /// unit direction (position / radius), the source builder's wrap-around
    /// quad indexing verbatim. Returns interleaved <c>pos(3)+normal(3)</c>
    /// vertices.</summary>
    public static (float[] Verts, uint[] Idx) BuildSphere(float radius, int rings = 12, int segments = 24)
    {
        var phiDiv = rings;
        var thetaDiv = segments;
        var vertexCount = (phiDiv + 1) * thetaDiv;
        var verts = new float[vertexCount * 6];
        var indices = new uint[phiDiv * thetaDiv * 6];
        var n = 0;
        var k = 0;

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
                var dir = new Vector3(ring * MathF.Cos(theta), ring * MathF.Sin(theta), z);
                AddVertex(dir * radius, dir);
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
                indices[k++] = (uint)i0;
                indices[k++] = (uint)i1;
                indices[k++] = (uint)i2;
                indices[k++] = (uint)i0;
                indices[k++] = (uint)i2;
                indices[k++] = (uint)i3;
            }
        }

        return (verts, indices);

        void AddVertex(Vector3 position, Vector3 normal)
        {
            verts[n * 6 + 0] = position.X;
            verts[n * 6 + 1] = position.Y;
            verts[n * 6 + 2] = position.Z;
            verts[n * 6 + 3] = normal.X;
            verts[n * 6 + 4] = normal.Y;
            verts[n * 6 + 5] = normal.Z;
            n++;
        }
    }
}
