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
    public void ParseCaps_grblHAL_axes_update()
    {
        // ensure IsGrblHAL true still updates NumAxes via s[3]
        GrblInfo.IsGrblHAL = true;
        var m = Grbl.ParseCaps("[OPT:V,16,128,6]");
        Assert.Equal(6, m.NumAxes);
        GrblInfo.IsGrblHAL = false;
    }
}
