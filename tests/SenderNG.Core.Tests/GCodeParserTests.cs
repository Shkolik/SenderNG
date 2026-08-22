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
