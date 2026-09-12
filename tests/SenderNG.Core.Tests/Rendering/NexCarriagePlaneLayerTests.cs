using HelixToolkit.Nex.Maths;
using SenderNG.Modules.Plotter.Nex.Rendering;
using SenderNG.Modules.Plotter.Nex.Scene;

namespace SenderNG.Core.Tests;

public class NexCarriagePlaneLayerTests
{
    [Fact]
    public void RapidColor_is_the_bright_rapid_red()
    {
        var c = NexCarriagePlaneLayer.RapidColor;
        Assert.Equal(0.9f, c.Red);
        Assert.Equal(0.12f, c.Green);
        Assert.Equal(0.12f, c.Blue);
        Assert.Equal(1f, c.Alpha);
    }

    [Fact]
    public void Executed_overlay_thickness_is_1_5x_the_base()
    {
        Assert.Equal(2.4f, NexCarriagePlaneLayer.ExecutedLineThickness);
        Assert.Equal(1.6f, NexCarriagePlaneLayer.BaseLineThickness);
        Assert.Equal(1.5f, NexCarriagePlaneLayer.ExecutedLineThickness / NexCarriagePlaneLayer.BaseLineThickness);
    }

    [Theory]
    [InlineData(RunState.Pending, false)]
    [InlineData(RunState.InFlight, false)]
    [InlineData(RunState.Error, false)]
    [InlineData(RunState.Executed, true)]
    public void IsExecuted_maps_only_the_acked_state(RunState s, bool expected)
    {
        Assert.Equal(expected, NexCarriagePlaneLayer.IsExecuted(s));
    }
}