/*
 * PlaneWorldMapperTests.cs - tests for plane-local → world mapping helper
 */

using System.Numerics;
using SenderNG.Modules.Plotter.GL.Model;
using SenderNG.Modules.Plotter.GL.Settings;

namespace SenderNG.Core.Tests;

public class PlaneWorldMapperTests
{
    [Fact]
    public void Foam_plane_maps_slots_to_world_yz_and_keeps_wire_x()
    {
        // Machine slots [0,1] = [X,Y] -> world Y,Z; world X stays at the origin (-50).
        var g = new GlPlaneGeometry { WorldAxes = [1, 2], Origin = [-50, 0, 0] };
        double[] machinePos = [10, 20, 30, 40];

        var v = PlaneWorldMapper.ToWorld(g, machinePos, new[] { 0, 1 });

        Assert.Equal(new Vector3(-50, 10, 20), v);
    }

    [Fact]
    public void Horizontal_mill_remaps_xy_to_world_xz()
    {
        // Machine X->world X, Y->world Z, Z->world Y ("on its side").
        var g = new GlPlaneGeometry { WorldAxes = [0, 2, 1], Origin = [0, 0, 0] };
        double[] machinePos = [10, 20, 30];

        var v = PlaneWorldMapper.ToWorld(g, machinePos, new[] { 0, 1, 2 });

        Assert.Equal(new Vector3(10, 30, 20), v);
    }

    [Fact]
    public void Mill_identity_is_the_default_mapping()
    {
        var g = new GlPlaneGeometry { WorldAxes = [0, 1, 2], Origin = [0, 0, 0] };
        double[] machinePos = [10, 20, 30];

        var v = PlaneWorldMapper.ToWorld(g, machinePos, new[] { 0, 1, 2 });

        Assert.Equal(new Vector3(10, 20, 30), v);
    }

    [Fact]
    public void Unmapped_world_axes_stay_at_the_origin_value()
    {
        var g = new GlPlaneGeometry { WorldAxes = [1, 2], Origin = [-50, 7, -3] };
        double[] machinePos = [10, 20, 30, 40];

        var v = PlaneWorldMapper.ToWorld(g, machinePos, new[] { 0, 1 });

        Assert.Equal(new Vector3(-50, 17, 17), v); // Y/Z lift from Origin + machine slots
    }
}