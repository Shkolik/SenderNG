/*
 * NexPlotToolOrientationTests.cs - foam tool cone orientation
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
using SenderNG.Modules.Plotter.Nex.Rendering;

namespace SenderNG.Core.Tests;

public class NexPlotToolOrientationTests
{
    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(-1f, 0f, 0f)]
    public void OpeningRotation_points_the_cone_from_one_end_toward_the_other(
        float x, float y, float z)
    {
        var to = new Vector3(x, y, z);

        var mapped = Vector3.Transform(Vector3.UnitZ, NexPlotTool.OpeningRotation(Vector3.Zero, to));

        Assert.True(Vector3.Distance(mapped, Vector3.Normalize(to)) < 1e-4f, $"got {mapped}");
    }

    [Fact]
    public void OpeningRotation_faces_a_tilted_wire()
    {
        var p0 = Vector3.Zero;
        var p1 = new Vector3(10f, 5f, -3f);
        var expected = Vector3.Normalize(p1 - p0);

        var a = Vector3.Transform(Vector3.UnitZ, NexPlotTool.OpeningRotation(p0, p1));
        var b = Vector3.Transform(Vector3.UnitZ, NexPlotTool.OpeningRotation(p1, p0));

        Assert.True(Vector3.Distance(a, expected) < 1e-4f, $"A got {a}");
        Assert.True(Vector3.Distance(b, -expected) < 1e-4f, $"B got {b}");
    }

    [Fact]
    public void OpeningRotation_is_identity_for_a_zero_length_wire()
    {
        var p = new Vector3(3f, 4f, 5f);

        Assert.Equal(Quaternion.Identity, NexPlotTool.OpeningRotation(p, p));
    }
}
