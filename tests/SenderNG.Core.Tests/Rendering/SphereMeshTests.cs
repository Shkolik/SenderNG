/*
 * SphereMeshTests.cs - shared unit-sphere mesh builder
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

public class SphereMeshTests
{
    [Fact]
    public void Build_creates_a_closed_uv_sphere_on_the_unit_sphere()
    {
        var g = SphereMesh.Build();

        var count = (SphereMesh.DefaultPhiDiv + 1) * SphereMesh.DefaultThetaDiv;
        Assert.Equal(Topology.Triangle, g.Topology);
        Assert.Equal(count, g.Vertices.Count);
        Assert.Equal(SphereMesh.DefaultPhiDiv * SphereMesh.DefaultThetaDiv * 6, g.Indices.Count);
        Assert.True(g.Indices.All(i => i < g.Vertices.Count));
        foreach (var v in g.Vertices)
        {
            var p = new Vector3(v.X, v.Y, v.Z);
            Assert.True(MathF.Abs(p.Length() - 1f) < 1e-3f, $"vertex {v} is not on the unit sphere");
        }
        Assert.Equal(new Vector3(-1f, -1f, -1f), g.BoundingBoxLocal.Minimum);
        Assert.Equal(new Vector3(1f, 1f, 1f), g.BoundingBoxLocal.Maximum);
    }

    [Fact]
    public void Build_accepts_custom_divisions()
    {
        var g = SphereMesh.Build(thetaDiv: 8, phiDiv: 5);

        Assert.Equal((5 + 1) * 8, g.Vertices.Count);
        Assert.Equal(5 * 8 * 6, g.Indices.Count);
        Assert.True(g.Indices.All(i => i < g.Vertices.Count));
    }

    [Fact]
    public void Build_handles_small_division_counts_without_corrupt_indices()
    {
        var g = SphereMesh.Build(thetaDiv: 3, phiDiv: 3);

        Assert.Equal((3 + 1) * 3, g.Vertices.Count);
        Assert.True(g.Indices.All(i => i < g.Vertices.Count));
        // A 3x3 sampling has no ring at the equator (widest radius is
        // sin(pi/3) ~ 0.866), so X/Y stays inside the unit sphere; only the
        // Z poles reach exactly ±1.
        var min = g.BoundingBoxLocal.Minimum;
        var max = g.BoundingBoxLocal.Maximum;
        Assert.True(min.X >= -1f && min.Y >= -1f && min.Z == -1f);
        Assert.True(max.X <= 1f && max.Y <= 1f && max.Z == 1f);
    }
}