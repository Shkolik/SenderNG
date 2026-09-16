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
    public void FillWire_parks_the_wire_when_not_a_two_point_wire()
    {
        var g = new Geometry(Topology.Line, isDynamic: true);
        NexPlotTool.FillWire(g, new Vector3(1, 2, 3), new Vector3(4, 5, 6));
        Assert.Equal(2, g.Vertices.Count);

        NexPlotTool.FillWire(g, new Vector3(7, 8, 9), null);
        // Parked wire keeps a constant two vertices (buffer never resizes or
        // moves address) with the endpoints parked far below the table.
        Assert.Equal(2, g.Vertices.Count);
        Assert.Equal(2, g.VertexColors.Count);
        Assert.Equal(new Vector4(0, -50_000, 0, 1), g.Vertices[0]);
        Assert.Equal(new Vector4(0, -50_000, 0, 1), g.Vertices[1]);
    }

    [Fact]
    public void FillWire_parks_when_both_endpoints_missing()
    {
        var g = new Geometry(Topology.Line, isDynamic: true);
        NexPlotTool.FillWire(g, null, null);

        Assert.Equal(2, g.Vertices.Count);
        Assert.Equal(new Vector4(0, -50_000, 0, 1), g.Vertices[0]);
    }

    [Fact]
    public void BuildConeMesh_revolves_a_closed_cone_apex_at_origin()
    {
        var g = NexPlotTool.BuildConeMesh();

        Assert.Equal(Topology.Triangle, g.Topology);
        // 3 profile segments (apex join, skirt, top cap), each a 2-wide ring per
        // theta step (engine AddRevolvedGeometry). 16 sectors default.
        Assert.Equal(6 * 16, g.Vertices.Count);
        Assert.Equal(6 * 16 * 3, g.Indices.Count);
        Assert.True(g.Indices.All(i => i < g.Vertices.Count));

        // Apex at the origin, base ring radius 1.5 at z=3.
        Assert.Equal(new Vector3(-1.5f, -1.5f, 0f), g.BoundingBoxLocal.Minimum);
        Assert.Equal(new Vector3(1.5f, 1.5f, 3f), g.BoundingBoxLocal.Maximum);
        Assert.Contains(g.Vertices, v => new Vector3(v.X, v.Y, v.Z).Length() < 1e-4f);         // apex
        Assert.Contains(g.Vertices, v => v.Z == 3f && v.X == 0f && v.Y == 0f);                 // cap centre
        Assert.Contains(g.Vertices, v => MathF.Abs(v.X * v.X + v.Y * v.Y - 2.25f) < 1e-4f);    // base ring
    }

    [Fact]
    public void BuildConeMesh_accepts_custom_dimensions()
    {
        var g = NexPlotTool.BuildConeMesh(thetaDiv: 8, radius: 2f, height: 5f);

        Assert.Equal(6 * 8, g.Vertices.Count);
        Assert.Equal(new Vector3(-2f, -2f, 0f), g.BoundingBoxLocal.Minimum);
        Assert.Equal(new Vector3(2f, 2f, 5f), g.BoundingBoxLocal.Maximum);
    }
}