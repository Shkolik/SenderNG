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
using System.Globalization;
using Xunit;

namespace SenderNG.Core.Tests;

// Dbl.ParseList is the number parser behind Position.Parse, which is how every
// work-offset and tool row is seeded: CoordinateSystem(code, data) and
// Tool(code, offsets) both chain to it through base(data). So this is not a
// dormant helper - the review in the audit called it dead because it grepped for
// "new Position(" and missed the base-constructor chaining (audit §1.15).

public class DblParseTests
{
    [Fact]
    public void ParseList_reads_exponent_notation()
    {
        // Pre-fix: NumberStyles had no AllowExponent, so "1.5e-3" failed and the
        // element silently became 0.0 - a work offset of 0.0015 read as 0.
        Assert.Equal([0.0015d, 2.5d], Dbl.ParseList("1.5e-3,2.5"));
    }

    [Fact]
    public void ParseList_reads_negative_exponent_notation()
    {
        Assert.Equal([-2.5e-4d], Dbl.ParseList("-2.5E-4"));
    }

    [Fact]
    public void ParseList_tolerates_surrounding_whitespace()
    {
        // Status reports and hand-edited settings routinely carry spaces. The
        // old style flags allowed neither leading nor trailing white, and
        // ParseList does not trim.
        Assert.Equal([1.5d, 2.5d], Dbl.ParseList(" 1.5 , 2.5 "));
    }

    [Fact]
    public void ParseList_still_reads_plain_decimals()
    {
        Assert.Equal([1.5d, -2.5d, 0d], Dbl.ParseList("1.5,-2.5,0"));
    }

    [Fact]
    public void ParseList_keeps_the_zero_fallback_for_an_empty_list()
    {
        // The live call path is CoordinateSystem("G54", ""), which must keep
        // producing a zeroed first element rather than NaN - the offsets code
        // reads that value.
        Assert.Equal([0.0d], Dbl.ParseList(""));
    }

    [Fact]
    public void ParseList_zeroes_only_the_elements_it_cannot_read()
    {
        Assert.Equal([0.0d, 2.5d, 0.0d], Dbl.ParseList("nonsense,2.5,"));
    }

    [Fact]
    public void Position_reaches_the_parser_through_the_coordinate_system_constructor()
    {
        // The chain the review called unreachable, exercised end to end.
        var cs = new CoordinateSystem("G54", "1.5e-3,2.5");

        Assert.Equal("G54", cs.Code);
        Assert.Equal(0.0015d, cs.Values[0]);
        Assert.Equal(2.5d, cs.Values[1]);
    }

    [Fact]
    public void Parse_ignores_the_current_culture()
    {
        // double.Parse uses the current culture by default, so in a de-DE locale
        // "1.5" reads as 15 - or throws. The converters feed this raw parser text
        // from .nc/.plt files, which is always invariant. Swap the culture, prove
        // the round-trip holds, restore it even if the assertion throws.
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal(1.5d, Dbl.Parse("1.5"));
            Assert.Equal(1.5d, Dbl.Parse(Dbl.ToInvariantString(1.5d)));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
