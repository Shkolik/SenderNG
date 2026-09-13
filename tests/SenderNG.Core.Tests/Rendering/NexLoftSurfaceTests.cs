using System.Numerics;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using SenderNG.Modules.Plotter.Nex.Rendering;

namespace SenderNG.Core.Tests;

public class NexLoftSurfaceTests
{
    [Fact]
    public void FillLoop_of_two_points_makes_a_closed_two_edge_loop()
    {
        var g = new Geometry(Topology.Line, isDynamic: true);

        NexLoftSurface.FillLoop(g, [new Vector3(0, 0, 0), new Vector3(10, 0, 0)]);

        // 2 edges, closing back to the start: p0->p1 then p1->p0 (4 vertices).
        Assert.Equal(4, g.Vertices.Count);
        Assert.Equal(4, g.VertexColors.Count);
        Assert.Equal(new Vector4(0, 0, 0, 1), g.Vertices[0]);
        Assert.Equal(new Vector4(10, 0, 0, 1), g.Vertices[1]);
        Assert.Equal(new Vector4(10, 0, 0, 1), g.Vertices[2]);
        Assert.Equal(new Vector4(0, 0, 0, 1), g.Vertices[3]);
    }

    [Fact]
    public void FillLoop_of_three_points_closes_the_triangle()
    {
        var g = new Geometry(Topology.Line, isDynamic: true);

        NexLoftSurface.FillLoop(g, [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)]);

        Assert.Equal(6, g.Vertices.Count);
        // Third edge closes back to the first point.
        Assert.Equal(new Vector4(0, 1, 0, 1), g.Vertices[4]);
        Assert.Equal(new Vector4(0, 0, 0, 1), g.Vertices[5]);
    }

    [Fact]
    public void FillLoop_with_null_or_short_polygon_clears_the_geometry()
    {
        var g = new Geometry(Topology.Line, isDynamic: true);
        g.Vertices.Add(new Vector4(1, 1, 1, 1));
        g.VertexColors.Add(new Vector4(1, 0, 0, 1));

        NexLoftSurface.FillLoop(g, null);
        Assert.Empty(g.Vertices);
        Assert.Empty(g.VertexColors);

        g.Vertices.Add(new Vector4(1, 1, 1, 1));
        g.VertexColors.Add(new Vector4(1, 0, 0, 1));
        NexLoftSurface.FillLoop(g, [new Vector3(5, 5, 5)]);
        Assert.Empty(g.Vertices);
        Assert.Empty(g.VertexColors);
    }
}