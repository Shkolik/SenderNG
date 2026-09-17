/*
 * ConeMeshTests.cs - shared closed-cone mesh builder
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
using SenderNG.Modules.Plotter.Nex.Rendering;

namespace SenderNG.Core.Tests;

public class ConeMeshTests
{
    [Fact]
    public void Build_revolves_a_closed_cone_apex_at_origin()
    {
        var g = ConeMesh.Build();

        Assert.Equal(Topology.Triangle, g.Topology);
        Assert.Equal(6 * ConeMesh.DefaultSegments, g.Vertices.Count);
        Assert.Equal(6 * ConeMesh.DefaultSegments * 3, g.Indices.Count);
        Assert.True(g.Indices.All(i => i < g.Vertices.Count));
        Assert.Equal(new Vector3(-1.5f, -1.5f, 0f), g.BoundingBoxLocal.Minimum);
        Assert.Equal(new Vector3(1.5f, 1.5f, 3f), g.BoundingBoxLocal.Maximum);
    }

    [Fact]
    public void All_real_triangles_wind_outward()
    {
        var g = ConeMesh.Build();
        var height = ConeMesh.DefaultHeight;

        var capCount = 0;
        var sideCount = 0;
        for (var t = 0; t + 2 < g.Indices.Count; t += 3)
        {
            var p0 = g.Vertices[(int)g.Indices[t]];
            var p1 = g.Vertices[(int)g.Indices[t + 1]];
            var p2 = g.Vertices[(int)g.Indices[t + 2]];
            var e1 = new Vector3(p1.X - p0.X, p1.Y - p0.Y, p1.Z - p0.Z);
            var e2 = new Vector3(p2.X - p0.X, p2.Y - p0.Y, p2.Z - p0.Z);
            var n = Vector3.Cross(e1, e2);
            if (n.Length() < 1e-6f)
                continue; // zero-area joint/sliver triangle, not structurally meaningful

            var verts = new[] { p0, p1, p2 };
            var apexVerts = verts.Count(p => p.Z <= 1e-3f);
            var rimVerts = verts.Where(p => p.Z >= height - 1e-3f).ToList();
            if (apexVerts == 1 && rimVerts.Count == 2)
            {
                // Skirt triangle: normal must point away from the axis at the rim.
                var radial = new Vector3(rimVerts[0].X, rimVerts[0].Y, 0f);
                var dot = Vector3.Dot(n, radial);
                Assert.True(dot > 0f, $"skirt triangle {t / 3} winds inward (n·r = {dot})");
                sideCount++;
            }
            else if (rimVerts.Count == 3)
            {
                // Cap fan triangle: normal must face +Z, out of the base.
                Assert.True(n.Z > 0f, $"cap wedge {t / 3} winds inward (normal Z = {n.Z})");
                capCount++;
            }
        }

        Assert.Equal(ConeMesh.DefaultSegments, capCount);
        Assert.Equal(ConeMesh.DefaultSegments, sideCount);
    }

    [Fact]
    public void Build_accepts_custom_dimensions()
    {
        var g = ConeMesh.Build(thetaDiv: 8, radius: 2f, height: 5f);

        Assert.Equal(6 * 8, g.Vertices.Count);
        Assert.Equal(new Vector3(-2f, -2f, 0f), g.BoundingBoxLocal.Minimum);
        Assert.Equal(new Vector3(2f, 2f, 5f), g.BoundingBoxLocal.Maximum);
    }

    [Fact]
    public void The_tool_delegate_matches_the_shared_builder()
    {
        var shared = ConeMesh.Build();
        var viaTool = NexPlotTool.BuildConeMesh();

        Assert.Equal(shared.Vertices.Count, viaTool.Vertices.Count);
        Assert.Equal(shared.Indices.Count, viaTool.Indices.Count);
        Assert.Equal(shared.BoundingBoxLocal.Minimum, viaTool.BoundingBoxLocal.Minimum);
        Assert.Equal(shared.BoundingBoxLocal.Maximum, viaTool.BoundingBoxLocal.Maximum);
    }
}
