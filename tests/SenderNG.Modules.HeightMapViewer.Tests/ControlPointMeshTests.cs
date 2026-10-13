/*
 * ControlPointMeshTests.cs - headless tests for the control-point marker mesh
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

using SenderNG.Core.Modules;
using SenderNG.Modules.HeightMapViewer;

namespace SenderNG.Modules.HeightMapViewer.Tests;

public class ControlPointMeshTests
{
    private static HeightMapLayer Map(int cols, int rows, double cell, params double[] z)
        => new("heightmap", 0, 0, cell, cell, cols, rows, z, 0);

    [Fact]
    public void Build_emits_six_vertices_and_eight_faces_per_finite_node()
    {
        var mesh = ControlPointMesh.Build(Map(2, 2, 1, 1, 2, 3, 4), 1.0);

        Assert.Equal(4 * 6 * 3, mesh.Positions.Length);
        Assert.Equal(4 * 6 * 3, mesh.Colors.Length);
        Assert.Equal(4 * 24, mesh.Indices.Length);
    }

    [Fact]
    public void Build_skips_nonfinite_nodes()
    {
        var mesh = ControlPointMesh.Build(
            Map(2, 2, 1, 1, double.NaN, 3, double.PositiveInfinity), 1.0);

        Assert.Equal(2 * 6 * 3, mesh.Positions.Length);
        Assert.Equal(2 * 24, mesh.Indices.Length);
    }

    [Fact]
    public void Build_sizes_the_marker_relative_to_the_cell()
    {
        var mesh = ControlPointMesh.Build(Map(1, 1, 2, 5), 1.0);

        // A single node at (0, 0, 5): each octahedron vertex is `size` away
        // from that node on exactly one axis, so its offset length == size.
        var expected = ControlPointMesh.SizeFraction * 2f;
        for (var v = 0; v < 6; v++)
        {
            var dx = mesh.Positions[v * 3 + 0] - 0f;
            var dy = mesh.Positions[v * 3 + 1] - 0f;
            var dz = mesh.Positions[v * 3 + 2] - 5f;
            Assert.Equal(expected, MathF.Sqrt(dx * dx + dy * dy + dz * dz), 4);
        }
    }

    [Fact]
    public void Build_uses_a_constant_color()
    {
        var mesh = ControlPointMesh.Build(Map(2, 2, 1, 1, 2, 3, 4), 1.0);

        for (var i = 3; i < mesh.Colors.Length; i++)
            Assert.Equal(mesh.Colors[0], mesh.Colors[i], 5);
    }

    [Fact]
    public void Build_is_empty_for_a_zero_cell()
    {
        var mesh = ControlPointMesh.Build(Map(2, 2, 0, 1, 2, 3, 4), 1.0);

        Assert.Empty(mesh.Positions);
        Assert.Empty(mesh.Indices);
    }
}
