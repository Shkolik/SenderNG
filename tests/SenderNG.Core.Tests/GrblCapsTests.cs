
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

using CNC.Core;
using Xunit;

namespace SenderNG.Core.Tests;

public class GrblCapsTests
{
    [Fact]
    public void ParseCaps_sets_NumAxes()
    {
        var m = Grbl.ParseCaps("[OPT:35,128,5]");
        Assert.Equal(5, m.NumAxes);
    }

    [Fact]
    public void ParseCaps_sets_PlanAndSerialBuffer()
    {
        var m = Grbl.ParseCaps("[OPT:V,35,128]");
        Assert.Equal(35, m.PlanBufferSize);
        Assert.Equal(128, m.SerialBufferSize);
    }

    [Fact]
    public void ParseCaps_with_flags_sets_all()
    {
        var m = Grbl.ParseCaps("[OPT:V,15,128,4]");
        Assert.Equal(15, m.PlanBufferSize);
        Assert.Equal(128, m.SerialBufferSize);
        Assert.Equal(4, m.NumAxes);
    }

    [Fact]
    public void ParseCaps_with_tools()
    {
        var m = Grbl.ParseCaps("[OPT:V,15,128,3,7]");
        Assert.Equal(7, m.NumTools);
    }

    [Fact]
    public void ParseCaps_can_report_six_axes()
    {
        var m = Grbl.ParseCaps("[OPT:V,16,128,6]");
        Assert.Equal(6, m.NumAxes);
    }
}
