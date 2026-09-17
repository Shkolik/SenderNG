/*
 * NexPlotSceneAxisConeTests.cs - pure axis-cone placement math and shaft staging
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
using HelixToolkit.Nex.Maths;
using SenderNG.Modules.Plotter.Nex.Rendering;

namespace SenderNG.Core.Tests;

public class NexPlotSceneAxisConeTests
{
    [Theory]
    [InlineData("X", 1f, 0f, 0f)]
    [InlineData("Y", 0f, 1f, 0f)]
    [InlineData("Z", 0f, 0f, 1f)]
    public void AxisConeTransform_puts_the_apex_on_the_tip(string label, float dx, float dy, float dz)
    {
        var anchor = new Vector3(2f, 3f, 4f);
        const float len = 10f;

        var (tip, _, _) = NexPlotScene.AxisConeTransform(label, anchor, len);

        Assert.Equal(anchor + new Vector3(dx, dy, dz) * len, tip);
    }

    [Fact]
    public void AxisConeTransform_scales_the_shared_mesh_to_the_requested_size()
    {
        const float len = 10f;
        var height = NexPlotScene.AxisConeHeight(len);
        var radius = NexPlotScene.AxisConeRadius(height);

        var (_, _, scale) = NexPlotScene.AxisConeTransform("X", Vector3.Zero, len);

        Assert.Equal((double)(radius / ConeMesh.DefaultRadius), scale.X, 5);
        Assert.Equal((double)(radius / ConeMesh.DefaultRadius), scale.Y, 5);
        Assert.Equal((double)(height / ConeMesh.DefaultHeight), scale.Z, 5);
    }

    [Theory]
    [InlineData("X", -1f, 0f, 0f)]
    [InlineData("Y", 0f, -1f, 0f)]
    [InlineData("Z", 0f, 0f, -1f)]
    public void AxisConeTransform_rotates_the_cone_to_open_toward_the_origin(
        string label, float ex, float ey, float ez)
    {
        var expected = new Vector3(ex, ey, ez);

        var (_, rotation, _) = NexPlotScene.AxisConeTransform(label, Vector3.Zero, 10f);

        var mapped = Vector3.Transform(Vector3.UnitZ, rotation);
        Assert.True(Vector3.Distance(mapped, expected) < 1e-4f,
            $"expected {expected}, got {mapped}");
    }

    [Fact]
    public void ConeOrientation_is_identity_for_plus_z_and_flip_for_minus_z()
    {
        var identity = ConeOrientation.FromUnitZTo(Vector3.UnitZ);
        Assert.True(Vector3.Distance(Vector3.Transform(Vector3.UnitZ, identity), Vector3.UnitZ) < 1e-4f);

        var flip = ConeOrientation.FromUnitZTo(-Vector3.UnitZ);
        var mapped = Vector3.Transform(Vector3.UnitZ, flip);
        Assert.True(Vector3.Distance(mapped, -Vector3.UnitZ) < 1e-4f, $"got {mapped}");
    }

    [Fact]
    public void FillAxisShaft_stages_two_vertices_ending_at_the_cone_base()
    {
        var g = new Geometry(Topology.Line, isDynamic: true);

        NexPlotScene.FillAxisShaft(
            g, new Vector3(1f, 1f, 1f), Vector3.UnitX, shaftLength: 10f, coneHeight: 3.2f,
            new Color4(1f, 0f, 0f, 1f));

        Assert.Equal(2, g.Vertices.Count);
        Assert.Equal(2, g.VertexColors.Count);
        Assert.Equal(new Vector4(1f, 1f, 1f, 1f), g.Vertices[0]);
        Assert.Equal(new Vector4(1f + (10f - 3.2f), 1f, 1f, 1f), g.Vertices[1]);
    }

    [Fact]
    public void FillAxisShaft_does_not_invert_a_shaft_shorter_than_the_cone()
    {
        var g = new Geometry(Topology.Line, isDynamic: true);

        NexPlotScene.FillAxisShaft(
            g, Vector3.Zero, Vector3.UnitY, shaftLength: 1f, coneHeight: 3f,
            new Color4(0f, 1f, 0f, 1f));

        Assert.Equal(new Vector4(0f, 0f, 0f, 1f), g.Vertices[0]);
        Assert.Equal(new Vector4(0f, 0f, 0f, 1f), g.Vertices[1]);
    }

    [Theory]
    [InlineData("X", 1f, 0f, 0f)]
    [InlineData("Z", 0f, 0f, 1f)]
    public void The_cone_base_meets_the_shaft_end_staged_by_FillAxisShaft(
        string label, float dx, float dy, float dz)
    {
        var anchor = new Vector3(2f, 3f, 4f);
        const float len = 10f;
        var direction = new Vector3(dx, dy, dz);

        var (tip, _, _) = NexPlotScene.AxisConeTransform(label, anchor, len);
        var coneBase = tip - direction * NexPlotScene.AxisConeHeight(len);
        var shaftEnd = anchor + direction * (len - NexPlotScene.AxisConeHeight(len));

        Assert.Equal(shaftEnd, coneBase);
    }
}
