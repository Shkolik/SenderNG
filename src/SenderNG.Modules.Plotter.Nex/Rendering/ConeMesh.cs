/*
 * ConeMesh.cs - closed cone mesh shared by the plot tool and the axis chrome
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
/// Static builder for the closed solid cone shared by the plot tool and the
/// axis arrowheads: apex at the local origin, opening along +Z, base cap closed.
/// </summary>
internal static class ConeMesh
{
    public const int DefaultSegments = 16;
    public const float DefaultRadius = 1.5f;
    public const float DefaultHeight = 3f;

    internal static Geometry Build(
        int thetaDiv = DefaultSegments,
        float radius = DefaultRadius,
        float height = DefaultHeight)
    {
        var g = new Geometry(Topology.Triangle);
        var total = 6 * thetaDiv;
        g.Vertices.Capacity = total;
        g.VertexProps.Capacity = total;
        g.Indices.Capacity = 3 * total;

        var angle = (float)Math.PI * 2f / thetaDiv;
        var center = new Vector3(0f, 0f, height);
        var capNormal = Vector3.UnitZ;

        for (var i = 0; i < thetaDiv; i++)
        {
            var u = (float)i / (thetaDiv - 1);
            var w = new Vector3(-(float)Math.Cos(i * angle), (float)Math.Sin(i * angle), 0f);
            var ring = new Vector3(w.X * radius, w.Y * radius, height);
            var skirtNormal = Vector3.Normalize(new Vector3(w.X * height, w.Y * height, -radius));

            AddVertex(Vector3.Zero, Vector3.Zero, new Vector2(u, 0f));
            AddVertex(Vector3.Zero, Vector3.Zero, new Vector2(u, 1f));
            AddVertex(Vector3.Zero, skirtNormal, new Vector2(u, 1f));
            AddVertex(ring, skirtNormal, new Vector2(u, 0f));
            AddVertex(ring, capNormal, new Vector2(u, 0f));
            AddVertex(center, capNormal, new Vector2(u, 1f));

            var i0 = i * 6;
            for (var j = 0; j < 3; j++)
            {
                var a = i0 + j * 2;
                var c = (a + 6) % total;
                var d = c + 1;
                g.Indices.Add((uint)(a + 1));
                g.Indices.Add((uint)a);
                g.Indices.Add((uint)c);
                g.Indices.Add((uint)(a + 1));
                g.Indices.Add((uint)c);
                g.Indices.Add((uint)d);
            }
        }

        g.UpdateBounds();
        g.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexProp | GeometryBufferType.Index);
        return g;

        void AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            g.Vertices.Add(new Vector4(position, 1f));
            g.VertexProps.Add(new VertexProperties(normal, uv));
        }
    }
}