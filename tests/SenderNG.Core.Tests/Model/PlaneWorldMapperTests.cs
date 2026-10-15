/*
 * PlaneWorldMapperTests.cs - tests for plane-local → world mapping helper
 */

using System.Numerics;
using CNC.Core.Services;
using SenderNG.Modules.Plotter.GL.Model;

namespace SenderNG.Core.Tests;

public class PlaneWorldMapperTests
{
    /// <summary>A resolved machine of the given type with a separation, through
    /// the real resolver — the same object the view model reads.</summary>
    private static MachineKinematics Resolve(string machineTypeId, double separation = 0)
    {
        var profile = new MachineProfile
        {
            Id = "mapping-" + machineTypeId,
            Name = machineTypeId,
            MachineTypeId = machineTypeId,
            Separation = separation,
        };
        return MachineKinematicsResolver.Resolve(profile, null, 0);
    }

    [Fact]
    public void Mill_identity_is_the_default_mapping()
    {
        double[] machinePos = [10, 20, 30];

        var v = PlaneWorldMapper.ToWorld(Vector3.Zero, machinePos, [0, 1, 2], [0, 1, 2]);

        Assert.Equal(new Vector3(10, 20, 30), v);
    }

    /// <summary>The mill is untouched by the direction rule: its one plane spans
    /// Width, Length and Height in that order, i.e. world X, Y and Z.</summary>
    [Fact]
    public void The_mill_plane_spans_x_y_and_z_and_has_no_free_axis()
    {
        MachineKinematics k = Resolve(MachineTypes.Mill, separation: 25);

        Assert.Equal(new[] { 0, 1, 2 }, k.WorldAxesFor(0));
        // Every world axis is spanned, so there is nowhere to put a separation.
        Assert.Equal(-1, k.FreeWorldAxisFor(0));
        Assert.Equal(Vector3.Zero, PlaneWorldMapper.PlacementFor(k, 0));
    }

    /// <summary>The foam tower spans its <b>height</b>, so the mapping puts it on
    /// the vertical plane: Length → world Y, Height → world Z. Mapping slot k to
    /// world axis k instead drew the tower flat on world XY and stacked the two
    /// towers like shelves — the defect this file now pins.</summary>
    [Fact]
    public void A_foam_tower_spans_the_vertical_plane_and_leaves_world_x_free()
    {
        MachineKinematics k = Resolve(MachineTypes.Foam, separation: 25);
        double[] machinePos = [10, 20, 30, 40];

        Assert.Equal(new[] { 1, 2 }, k.WorldAxesFor(1));
        Assert.Equal(0, k.FreeWorldAxisFor(1));      // world X — the separation axis

        var placement = PlaneWorldMapper.PlacementFor(k, 1);
        Assert.Equal(new Vector3(25, 0, 0), placement);

        // Slot 0 = machine axis 2 (30) on world Y, slot 1 = machine axis 3 (40) on
        // world Z.
        var v = PlaneWorldMapper.ToWorld(placement, machinePos, [2, 3], k.WorldAxesFor(1));
        Assert.Equal(new Vector3(25, 30, 40), v);
    }

    /// <summary>Plane 0 is the datum: it sits at the origin even when the profile
    /// carries a separation, because the separation is the distance to the NEXT
    /// plane — and the two towers are one per side, along world X.</summary>
    [Fact]
    public void Plane_zero_is_always_at_the_origin()
    {
        MachineKinematics k = Resolve(MachineTypes.Foam, separation: 25);

        Assert.Equal(Vector3.Zero, PlaneWorldMapper.PlacementFor(k, 0));
        Assert.Equal(new Vector3(25, 0, 0), PlaneWorldMapper.PlacementFor(k, 1));
    }

    /// <summary>The bug-report layout: plane 2's slots are machine indices
    /// <c>[3, 2]</c> (A then Z) — NON-MONOTONIC — and the mapping must slice in
    /// SLOT order, so slot 0 (A) lands on the tower's horizontal (world Y) and slot
    /// 1 (Z) on its vertical (world Z). A mapping that sorted the indices would
    /// transpose the tile and silently drive the wrong motor on the vertical.</summary>
    [Fact]
    public void A_non_monotonic_plane_slices_in_slot_order()
    {
        double[] machinePos = [10, 20, 30, 40];

        var v = PlaneWorldMapper.ToWorld(Vector3.Zero, machinePos, [3, 2], [1, 2]);

        Assert.Equal(new Vector3(0, 40, 30), v);
    }

    /// <summary>A slot that names an axis the reported position does not carry
    /// leaves that world axis at the placement value — never a fabricated
    /// position. The view model's own highest-index check is the primary guard;
    /// this is the one underneath it.</summary>
    [Fact]
    public void A_slot_past_the_reported_position_stays_at_the_placement()
    {
        MachineKinematics k = Resolve(MachineTypes.Foam, separation: 25);
        var placement = PlaneWorldMapper.PlacementFor(k, 1);

        var v = PlaneWorldMapper.ToWorld(placement, [10, 20], [2, 3], k.WorldAxesFor(1));

        Assert.Equal(new Vector3(25, 0, 0), v);
    }

    /// <summary>The direction rule itself, at its single source of truth.</summary>
    [Theory]
    [InlineData(AxisDirection.Width, 0)]
    [InlineData(AxisDirection.Length, 1)]
    [InlineData(AxisDirection.Height, 2)]
    public void A_direction_names_its_world_axis(AxisDirection direction, int worldAxis) =>
        Assert.Equal(worldAxis, MachineKinematics.WorldAxisForDirection(direction));
}
