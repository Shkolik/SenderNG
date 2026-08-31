
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

using SenderNG.Core.Modules.Probing;
using Xunit;

namespace SenderNG.Core.Tests;

public class ProgramTransformTests
{
    [Fact]
    public void Rotate_90_degrees_swaps_XY()
    {
        var result = ProgramTransform.Rotate("G1 X10 Y0\nG0 X0 Y0", Math.PI / 2, 0, 0);

        Assert.Equal("G1 X0 Y10\nG0 X0 Y0", result);
    }

    [Fact]
    public void Rotate_preserves_non_motion_lines()
    {
        var result = ProgramTransform.Rotate("G21\nG1 X10 Y0\n; comment", Math.PI / 2, 0, 0);

        Assert.Equal("G21\nG1 X0 Y10\n; comment", result);
    }

    [Fact]
    public void ApplyHeightMap_injects_interpolated_Z_on_motion_lines()
    {
        var map = new HeightMap(10, 10, new Vec2(0, 0), new Vec2(20, 10));
        map.AddPoint(1, 0, 2);
        map.AddPoint(1, 1, 2);

        var result = ProgramTransform.ApplyHeightMap("G1 X10 Y5\nG0 X0 Y0\nG21", map);

        // X10 Y5 sits on the x=1 grid line with a height of 2.
        Assert.StartsWith("G1 X10 Y5 Z2", result);
        Assert.EndsWith("G21", result);
    }
}
