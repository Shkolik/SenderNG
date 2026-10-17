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
        Assert.Equal(new Vector3(12.5f, 0, 0), placement);

        // Slot 0 = machine axis 2 (30) on world Y, slot 1 = machine axis 3 (40) on
        // world Z.
        var v = PlaneWorldMapper.ToWorld(placement, machinePos, [2, 3], k.WorldAxesFor(1));
        Assert.Equal(new Vector3(12.5f, 30, 40), v);
    }

    /// <summary>Two tower planes straddle the origin: each sits at half the
    /// separation on one side of the world axis the tower does not span (world X
    /// for the shipped Length × Height tower), so machine zero at 0,0,0,0 is the
    /// wire's middle at the scene centre — not one of its ends.</summary>
    [Fact]
    public void The_two_tower_planes_straddle_the_origin_at_half_the_separation()
    {
        MachineKinematics k = Resolve(MachineTypes.Foam, separation: 25);

        Assert.Equal(new Vector3(-12.5f, 0, 0), PlaneWorldMapper.PlacementFor(k, 0));
        Assert.Equal(new Vector3(12.5f, 0, 0), PlaneWorldMapper.PlacementFor(k, 1));
    }

    /// <summary>A vertical-wire machine — two Length × Width planes stacked
    /// along Height — reads top-to-bottom: plane 0 is the HIGH plane and plane 1
    /// the low one, so the 3D scene matches the flat canvas's plane-0-top order
    /// and the horizontal foam layout rotated ninety degrees about its length
    /// axis. Without the reading-order sign plane 0 landed at -sep/2 (bottom).</summary>
    [Fact]
    public void A_vertical_stack_reads_plane_0_on_top()
    {
        var profile = new MachineProfile
        {
            Id = "vertical-edm",
            Name = "Vertical EDM",
            MachineTypeId = MachineTypes.Foam,
            Separation = 25,
        };
        for (var plane = 0; plane < 2; plane++)
        {
            profile.Planes.Add(new ProfilePlane
            {
                Axes =
                {
                    new MachineAxis
                    {
                        Index = plane * 2, Kind = AxisKind.Linear, Direction = AxisDirection.Length,
                    },
                    new MachineAxis
                    {
                        Index = plane * 2 + 1, Kind = AxisKind.Linear, Direction = AxisDirection.Width,
                    },
                },
            });
        }
        var k = MachineKinematicsResolver.Resolve(profile, null, 0);

        Assert.Equal(2, k.FreeWorldAxisFor(0));      // world Z — the vertical wire
        Assert.Equal(new Vector3(0, 0, 12.5f), PlaneWorldMapper.PlacementFor(k, 0));
        Assert.Equal(new Vector3(0, 0, -12.5f), PlaneWorldMapper.PlacementFor(k, 1));
    }

    /// <summary>A single-plane machine has nothing to be separated from: even a
    /// stray profile separation (warned about, invariant 6) resolves plane 0's
    /// separation to 0, so no half-offset may move the machine off the origin.</summary>
    [Fact]
    public void A_single_plane_machine_stays_at_the_origin_despite_a_profile_separation()
    {
        var profile = new MachineProfile
        {
            Id = "single-plane",
            Name = "Single plane",
            MachineTypeId = MachineTypes.Mill,
            Separation = 25,
        };
        profile.Planes.Add(new ProfilePlane
        {
            Axes =
            {
                new MachineAxis { Index = 0, Kind = AxisKind.Linear, Direction = AxisDirection.Width },
                new MachineAxis { Index = 1, Kind = AxisKind.Linear, Direction = AxisDirection.Length },
            },
        });
        var k = MachineKinematicsResolver.Resolve(profile, null, 0);

        Assert.Equal(Vector3.Zero, PlaneWorldMapper.PlacementFor(k, 0));
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

        Assert.Equal(new Vector3(12.5f, 0, 0), v);
    }

    /// <summary>The direction rule itself, at its single source of truth.</summary>
    [Theory]
    [InlineData(AxisDirection.Width, 0)]
    [InlineData(AxisDirection.Length, 1)]
    [InlineData(AxisDirection.Height, 2)]
    public void A_direction_names_its_world_axis(AxisDirection direction, int worldAxis) =>
        Assert.Equal(worldAxis, MachineKinematics.WorldAxisForDirection(direction));
}
