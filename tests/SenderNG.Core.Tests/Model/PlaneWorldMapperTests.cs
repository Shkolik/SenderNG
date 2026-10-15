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

        var v = PlaneWorldMapper.ToWorld(Vector3.Zero, machinePos, [0, 1, 2]);

        Assert.Equal(new Vector3(10, 20, 30), v);
    }

    /// <summary>The mapping is POSITIONAL: slot k feeds world axis k, so a plane
    /// whose slots are machine axes 2 and 3 (a foam tower's Z and A) draws its
    /// first slot on world X and its second on world Y — no mapping table.</summary>
    [Fact]
    public void Foam_plane_maps_its_slots_positionally_onto_world_x_and_y()
    {
        MachineKinematics k = Resolve(MachineTypes.Foam, separation: 25);
        double[] machinePos = [10, 20, 30, 40];
        var placement = PlaneWorldMapper.PlacementFor(k, 1, k.Planes[1].Axes.Count);

        var v = PlaneWorldMapper.ToWorld(placement, machinePos, [2, 3]);

        // Slot 0 = machine axis 2 (30) on world X, slot 1 = machine axis 3 (40) on
        // world Y, and the plane sits 25 mm along the free axis (world Z).
        Assert.Equal(new Vector3(30, 40, 25), v);
    }

    /// <summary>Plane 0 is the datum: it sits at the origin even when the
    /// profile carries a separation, because the separation is the distance to
    /// the NEXT plane.</summary>
    [Fact]
    public void Plane_zero_is_always_at_the_origin()
    {
        MachineKinematics k = Resolve(MachineTypes.Foam, separation: 25);

        Assert.Equal(Vector3.Zero, PlaneWorldMapper.PlacementFor(k, 0, 2));
        Assert.Equal(new Vector3(0, 0, 25), PlaneWorldMapper.PlacementFor(k, 1, 2));
    }

    /// <summary>No free world axis means nowhere to put a separation: a
    /// three-slot (mill) plane spans X, Y and Z, so its placement is 0.</summary>
    [Fact]
    public void A_three_slot_plane_has_no_free_axis_to_place()
    {
        MachineKinematics k = Resolve(MachineTypes.Mill, separation: 25);

        Assert.Equal(Vector3.Zero, PlaneWorldMapper.PlacementFor(k, 0, 3));
    }

    /// <summary>The bug-report layout: plane 2's slots are machine indices
    /// <c>[3, 2]</c> (A then Z) — NON-MONOTONIC — and the mapping must slice in
    /// SLOT order, so world X is A and world Y is Z. A mapping that sorted the
    /// indices would transpose the tile and silently drive the wrong motor on
    /// the vertical.</summary>
    [Fact]
    public void A_non_monotonic_plane_slices_in_slot_order()
    {
        MachineKinematics k = Resolve(MachineTypes.Foam);
        double[] machinePos = [10, 20, 30, 40];

        var v = PlaneWorldMapper.ToWorld(Vector3.Zero, machinePos, [3, 2]);

        Assert.Equal(new Vector3(40, 30, 0), v);
    }

    /// <summary>A slot that names an axis the reported position does not carry
    /// leaves that world axis at the placement value — never a fabricated
    /// position. The view model's own highest-index check is the primary guard;
    /// this is the one underneath it.</summary>
    [Fact]
    public void A_slot_past_the_reported_position_stays_at_the_placement()
    {
        MachineKinematics k = Resolve(MachineTypes.Foam, separation: 25);
        var placement = PlaneWorldMapper.PlacementFor(k, 1, 2);

        var v = PlaneWorldMapper.ToWorld(placement, [10, 20], [2, 3]);

        Assert.Equal(new Vector3(0, 0, 25), v);
    }
}
