using System.Numerics;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using SenderNG.Modules.Plotter.Nex.Rendering;

namespace SenderNG.Core.Tests;

public class NexPlotToolTests
{
    [Fact]
    public void FillWire_stages_the_two_endpoints()
    {
        var g = new Geometry(Topology.Line, isDynamic: true);

        NexPlotTool.FillWire(g, new Vector3(1, 2, 3), new Vector3(4, 5, 6));

        Assert.Equal(2, g.Vertices.Count);
        Assert.Equal(2, g.VertexColors.Count);
        Assert.Equal(new Vector4(1, 2, 3, 1), g.Vertices[0]);
        Assert.Equal(new Vector4(4, 5, 6, 1), g.Vertices[1]);
    }

    [Fact]
    public void FillWire_clears_existing_content_when_not_a_two_point_wire()
    {
        var g = new Geometry(Topology.Line, isDynamic: true);
        NexPlotTool.FillWire(g, new Vector3(1, 2, 3), new Vector3(4, 5, 6));
        Assert.Equal(2, g.Vertices.Count);

        NexPlotTool.FillWire(g, new Vector3(7, 8, 9), null);
        Assert.Empty(g.Vertices);
        Assert.Empty(g.VertexColors);
    }
}