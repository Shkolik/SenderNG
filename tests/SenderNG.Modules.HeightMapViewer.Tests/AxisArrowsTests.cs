/*
 * AxisArrowsTests.cs - headless tests for the axis-arrow mesh builder
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
using SenderNG.Modules.HeightMapViewer;

namespace SenderNG.Modules.HeightMapViewer.Tests;

public class AxisArrowsTests
{
    private static readonly Vector3 Origin = new(1f, 2f, 3f);

    private const int ConeSegments = 24;
    private const int VertsPerCone = 6 * ConeSegments;
    private const int IdxPerCone = 3 * VertsPerCone;

    private static float ArrowLength(Vector3 span)
        => AxisArrows.ArrowLengthFraction * MathF.Max(span.X, span.Y);

    private static Vector3 Vertex(float[] buffer, int vertex)
        => new(buffer[vertex * 6], buffer[vertex * 6 + 1], buffer[vertex * 6 + 2]);

    [Fact]
    public void Build_emits_three_shafts_from_the_origin_with_the_expected_length()
    {
        var span = new Vector3(10f, 8f, 0f);
        var (shafts, _, _) = AxisArrows.Build(Origin, span);

        var expectedShaft = ArrowLength(span) - AxisArrows.HeadHeightFraction * ArrowLength(span);

        Assert.Equal(3 * 2 * 7, shafts.Length);
        for (var axis = 0; axis < 3; axis++)
        {
            var o = axis * 2 * 7;
            var from = new Vector3(shafts[o], shafts[o + 1], shafts[o + 2]);
            var to = new Vector3(shafts[o + 7], shafts[o + 8], shafts[o + 9]);
            Assert.Equal(Origin, from);
            Assert.Equal(expectedShaft, Vector3.Distance(from, to), 4);
        }
    }

    [Fact]
    public void Build_colors_each_shaft_per_axis()
    {
        var (shafts, _, _) = AxisArrows.Build(Origin, new Vector3(10f, 10f, 0f));

        var expected = new[]
        {
            new Vector4(0.85f, 0.15f, 0.15f, 1f),
            new Vector4(0.15f, 0.70f, 0.20f, 1f),
            new Vector4(0.20f, 0.40f, 0.90f, 1f),
        };

        for (var axis = 0; axis < 3; axis++)
        {
            var o = axis * 2 * 7 + 3;
            Assert.Equal(expected[axis].X, shafts[o], 4);
            Assert.Equal(expected[axis].Y, shafts[o + 1], 4);
            Assert.Equal(expected[axis].Z, shafts[o + 2], 4);
            Assert.Equal(expected[axis].W, shafts[o + 3], 4);
        }
    }

    [Fact]
    public void Build_emits_three_cones_with_expected_counts()
    {
        var (_, heads, indices) = AxisArrows.Build(Origin, new Vector3(10f, 10f, 0f));

        Assert.Equal(3 * VertsPerCone * 6, heads.Length);
        Assert.Equal(3 * IdxPerCone, indices.Length);

        var max = (uint)(3 * VertsPerCone - 1);
        foreach (var index in indices)
            Assert.True(index <= max, $"index {index} out of range");
    }

    [Fact]
    public void Build_places_each_cone_tip_at_the_axis_arrow_length()
    {
        var span = new Vector3(10f, 10f, 0f);
        var (_, heads, _) = AxisArrows.Build(Origin, span);

        var arrowLength = ArrowLength(span);
        var headHeight = AxisArrows.HeadHeightFraction * arrowLength;
        var dirs = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };

        for (var axis = 0; axis < 3; axis++)
        {
            var dir = dirs[axis];
            var maxProj = float.NegativeInfinity;
            var minProj = float.PositiveInfinity;
            var baseOffset = axis * VertsPerCone;

            for (var v = 0; v < VertsPerCone; v++)
            {
                var proj = Vector3.Dot(Vertex(heads, baseOffset + v) - Origin, dir);
                maxProj = MathF.Max(maxProj, proj);
                minProj = MathF.Min(minProj, proj);
            }

            Assert.Equal(arrowLength, maxProj, 3);
            Assert.Equal(arrowLength - headHeight, minProj, 3);
        }
    }

    [Fact]
    public void Build_returns_empty_for_a_degenerate_or_nonfinite_span()
    {
        Assert.Empty(AxisArrows.Build(Origin, Vector3.Zero).ShaftVerts);
        Assert.Empty(AxisArrows.Build(Origin, Vector3.Zero).HeadVerts);
        Assert.Empty(AxisArrows.Build(Origin, Vector3.Zero).HeadIndices);

        Assert.Empty(AxisArrows.Build(Origin, new Vector3(0f, 0f, 5f)).ShaftVerts);
        Assert.Empty(AxisArrows.Build(Origin, new Vector3(float.NaN, 10f, 0f)).ShaftVerts);
        Assert.Empty(AxisArrows.Build(Origin, new Vector3(10f, float.PositiveInfinity, 0f)).ShaftVerts);
    }
}
