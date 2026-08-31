
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

using CNC.GCode;
using CNC.Core;
using Xunit;

namespace SenderNG.Core.Tests;

public class GCodeParserTests
{
    [Fact]
    public void Parse_G1_X10()
    {
        var b = GCodeParser.Parse("G1 X10 F200");
        Assert.Equal(1, b.Motion);
    }

    [Fact]
    public void Parse_G0_sets_motion_zero()
    {
        var b = GCodeParser.Parse("G0 X5 Y5");
        Assert.Equal(0, b.Motion);
    }

    [Fact]
    public void Parse_G1_F_and_X()
    {
        var b = GCodeParser.Parse("G1 X10 F200");
        Assert.Equal(10d, b.X);
        Assert.Equal(200d, b.F);
    }

    [Fact]
    public void Parse_G2_arc()
    {
        var b = GCodeParser.Parse("G2 X10 Y10 I5 J0");
        Assert.Equal(2, b.Motion);
        Assert.Equal(5d, b.I);
        Assert.Equal(0d, b.J);
    }

    [Fact]
    public void Parse_instance_ParseBlock_works()
    {
        var parser = new GCodeParser();
        string line = "G1 X10 F200";
        bool ok = parser.ParseBlock(ref line, false);
        Assert.True(ok);
        // tokens should contain linear motion
        Assert.NotEmpty(parser.Tokens);
    }

    [Fact]
    public void Parse_comment_returns_no_motion()
    {
        var b = GCodeParser.Parse("(comment)");
        Assert.Equal(-1, b.Motion); // or 0? check implementation returns -1 for no motion
    }
}
