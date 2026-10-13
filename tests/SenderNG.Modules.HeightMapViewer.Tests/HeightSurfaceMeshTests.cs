/*
 * HeightSurfaceMeshTests.cs - tests for the headless height-surface mesh builder
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

public class HeightSurfaceMeshTests
{
    private static HeightMapLayer Layer(int cols, int rows, double[] z) =>
        new("hm", 0, 0, 1, 1, cols, rows, z, 0);

    [Fact]
    public void Full_grid_produces_two_triangles_per_cell()
    {
        // 2x2 grid = 1 cell -> 2 triangles -> 6 indices, 4 vertices.
        var mesh = HeightSurfaceMesh.Build(Layer(2, 2, [0, 0, 0, 0]), 1.0);
        Assert.Equal(4, mesh.Positions.Length / 3);
        Assert.Equal(6, mesh.Indices.Length);
    }

    [Fact]
    public void NaN_centre_drops_every_touching_cell()
    {
        // A 3x3 node grid bounds only 2x2 = 4 cells (the brief's "8 full cells"
        // is a miscount). The centre node is a corner of all four cells, so none
        // survive. The invariant under test: no triangle references a NaN vertex.
        var z = new double[9];
        z[4] = double.NaN;
        var mesh = HeightSurfaceMesh.Build(Layer(3, 3, z), 1.0);
        Assert.Empty(mesh.Indices);
        AssertNoIndexReferencesNaN(mesh, z);
    }

    [Fact]
    public void NaN_corner_skips_only_the_touching_cell()
    {
        // NaN at node (2,2): only the one cell it corners is dropped, leaving
        // 3 of 4 cells -> 6 triangles -> 18 indices.
        var z = new double[9];
        z[8] = double.NaN;
        var mesh = HeightSurfaceMesh.Build(Layer(3, 3, z), 1.0);
        Assert.Equal(18, mesh.Indices.Length);
        AssertNoIndexReferencesNaN(mesh, z);
    }

    [Fact]
    public void Colors_map_min_to_max_gradient()
    {
        var mesh = HeightSurfaceMesh.Build(Layer(2, 2, [0, 0, 0, 10]), 1.0);
        // 4 vertices, RGB each; min-height vertex != max-height vertex color.
        var first = (mesh.Colors[0], mesh.Colors[1], mesh.Colors[2]);
        var last = (mesh.Colors[9], mesh.Colors[10], mesh.Colors[11]);
        Assert.NotEqual(first, last);
    }

    private static void AssertNoIndexReferencesNaN(HeightSurfaceMesh mesh, double[] z)
    {
        Assert.Equal(0, mesh.Indices.Length % 3);
        foreach (var i in mesh.Indices)
            Assert.False(double.IsNaN(z[(int)i]), $"index {i} references a NaN sample");
    }
}
