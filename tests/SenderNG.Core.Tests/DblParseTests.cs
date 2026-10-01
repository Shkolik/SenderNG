using CNC.Core;
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
}
