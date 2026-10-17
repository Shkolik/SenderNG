/*
 * KinematicSceneFactoryTests.cs - factory wiring tests per machine kinematic
 */

/*

Copyright (c) 2026 Andrew Shkolik
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using SenderNG.Modules.Common.Render;
using System.Numerics;
using CNC.Core.Services;
using SenderNG.Modules.Plotter.GL.Model;
using SenderNG.Modules.Plotter.GL.Scene;

namespace SenderNG.Core.Tests;

public class KinematicSceneFactoryTests
{
    /// <summary>Resolve a freshly created profile of <paramref name="machineTypeId"/>
    /// — the same path the app takes (the profile's planes are seeded from the
    /// preset inside the resolver), with no controller attached.</summary>
    private static MachineKinematics Resolve(string machineTypeId, double separation = 0)
    {
        var profile = new MachineProfile
        {
            Id = "scene-" + machineTypeId,
            Name = machineTypeId,
            MachineTypeId = machineTypeId,
            Separation = separation,
        };
        return MachineKinematicsResolver.Resolve(profile, null, 0);
    }

    [Fact]
    public void Mill_creates_one_plane_layer_and_a_single_point_tool()
    {
        var fake = new FakePlotScene();
        var f = new KinematicSceneFactory(fake);
        f.BuildScene(Resolve(MachineTypes.Mill));
        Assert.Equal(1, f.PlaneCount);
        Assert.NotNull(f.PlaneLayer(0));
        Assert.NotNull(f.Tool);
        Assert.Null(f.Loft);

        Assert.Single(fake.CarriagePlanes);
        Assert.Empty(fake.LoftSurfaces);
        Assert.Single(fake.ToolShapes);
        Assert.Equal(ToolShape.Cone, fake.ToolShapes[0]);
        Assert.Single(((FakePlotTool)f.Tool!).Positions[0].Points);
    }

    [Fact]
    public void Foam_creates_two_synchronized_planes_and_a_wire_loft()
    {
        var fake = new FakePlotScene();
        var f = new KinematicSceneFactory(fake);
        f.BuildScene(Resolve(MachineTypes.Foam));
        Assert.Equal(2, f.PlaneCount);
        Assert.NotNull(f.PlaneLayer(0));
        Assert.NotNull(f.PlaneLayer(1));
        Assert.NotNull(f.Loft);
        Assert.NotNull(f.Tool);

        Assert.Equal(2, fake.CarriagePlanes.Count);
        Assert.Single(fake.LoftSurfaces);
        Assert.Single(fake.ToolShapes);
        Assert.Equal(ToolShape.Cone, fake.ToolShapes[0]);
        Assert.Equal(2, ((FakePlotTool)f.Tool!).Positions[0].Points.Length);
    }

    /// <summary>The initial tool wire spans the two planes' symmetric placements —
    /// ±half the separation about the origin — on the world axis the two-slot
    /// tower does NOT span (world <b>X</b>, because the tower's Length × Height
    /// plane is vertical, world Y × Z). The machine's zero at 0,0,0,0 sits
    /// mid-wire at the scene centre. A separation of 0 degenerates to the same
    /// wire the mill case uses.</summary>
    [Fact]
    public void Foam_initial_tool_wire_spans_the_separation_on_the_unspanned_axis()
    {
        var fake = new FakePlotScene();
        var f = new KinematicSceneFactory(fake);
        f.BuildScene(Resolve(MachineTypes.Foam, separation: 25));
        Assert.Equal(2, f.PlaneCount);
        Assert.NotNull(f.Loft);

        var points = ((FakePlotTool)f.Tool!).Positions[0].Points;
        Assert.Equal(2, points.Length);
        // The vertical tower leaves world X free; the towers straddle the origin.
        Assert.Equal(new Vector3(-12.5f, 0, 0), points[0]);
        Assert.Equal(new Vector3(12.5f, 0, 0), points[1]);
    }

    /// <summary>A vertical-wire machine (two Length × Width planes stacked along
    /// Height) reads top-to-bottom: the placeholder wire starts on plane 0 at
    /// +sep/2 and ends on plane 1 at -sep/2 — the same reading order the flat
    /// canvas uses, and the horizontal foam layout rotated about its length
    /// axis. This is the EDM profile's geometry, not a preset default.</summary>
    [Fact]
    public void Vertical_stack_initial_tool_wire_runs_top_to_bottom()
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
        var kinematics = MachineKinematicsResolver.Resolve(profile, null, 0);

        var fake = new FakePlotScene();
        var f = new KinematicSceneFactory(fake);
        f.BuildScene(kinematics);

        Assert.Equal(2, f.PlaneCount);
        Assert.NotNull(f.Loft);
        var points = ((FakePlotTool)f.Tool!).Positions[0].Points;
        Assert.Equal(2, points.Length);
        Assert.Equal(new Vector3(0, 0, 12.5f), points[0]);
        Assert.Equal(new Vector3(0, 0, -12.5f), points[1]);
    }

    /// <summary>A rotary axis is never a plane slot, so it must not conjure a
    /// carriage layer or a loft: the plane count comes from the machine's planes
    /// alone, and the rotary lives in the optional axes (invariant 3).</summary>
    [Fact]
    public void A_rotary_axis_adds_no_plane_and_no_loft()
    {
        var profile = new MachineProfile
        {
            Id = "rotary-mill",
            Name = "Rotary mill",
            MachineTypeId = MachineTypes.Mill,
        };
        profile.OptionalAxes.Add(new MachineAxis
        {
            Index = 3,
            Kind = AxisKind.Rotary,
            Direction = AxisDirection.Width,
            RotationOrigin = [0, 0],
        });
        var kinematics = MachineKinematicsResolver.Resolve(profile, null, 0);
        Assert.Equal([3], kinematics.RotaryAxes);

        var fake = new FakePlotScene();
        var f = new KinematicSceneFactory(fake);
        f.BuildScene(kinematics);

        Assert.Equal(1, f.PlaneCount);
        Assert.Null(f.Loft);
        Assert.Single(fake.CarriagePlanes);
        Assert.Empty(fake.LoftSurfaces);
    }

    [Fact]
    public void Empty_planes_creates_empty_scene_without_crash()
    {
        // A preset that declares no topology at all: the resolver seeds nothing,
        // so the machine genuinely has zero planes.
        var preset = new MachineType { Id = "proto-" + Guid.NewGuid().ToString("N"), Name = "Proto" };
        var kinematics = MachineKinematicsResolver.Resolve(
            new MachineProfile { Id = "proto", Name = "Proto", MachineTypeId = preset.Id }, preset, null, 0);
        Assert.Empty(kinematics.Planes);

        var fake = new FakePlotScene();
        var f = new KinematicSceneFactory(fake);
        f.BuildScene(kinematics);
        Assert.Equal(0, f.PlaneCount);
        Assert.NotNull(f.Tool);
        Assert.Null(f.Loft);

        Assert.Empty(fake.CarriagePlanes);
        Assert.Empty(fake.LoftSurfaces);
        Assert.Single(fake.ToolShapes);
        Assert.Equal(ToolShape.Cone, fake.ToolShapes[0]);
        Assert.Single(((FakePlotTool)f.Tool!).Positions[0].Points);
    }

    /// <summary>Stock mill: the gnomon keeps the classic X/Y/Z letters at the
    /// origin, the grid stays the plane perpendicular to the Height axis
    /// (world Z, sign +1), and the tool plunges along +Z.</summary>
    [Fact]
    public void Mill_chrome_keeps_the_classic_letters_and_floor()
    {
        var fake = new FakePlotScene();
        var f = new KinematicSceneFactory(fake);
        f.BuildScene(Resolve(MachineTypes.Mill));

        Assert.Equal(3, fake.LastChromeArrows.Count);
        Assert.All(fake.LastChromeArrows, a => Assert.Equal(Vector3.Zero, a.Pos));
        Assert.Equal(["X", "Y", "Z"], fake.LastChromeArrows.Select(a => a.Letter));
        Assert.Equal(
            [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ],
            fake.LastChromeArrows.Select(a => a.Direction));
        Assert.Equal(2, fake.LastGridNormalAxis);
        Assert.Equal(1, fake.LastGridNormalSign);
        Assert.Equal(Vector3.UnitZ, ((FakePlotTool)f.Tool!).PlungeDirection);
    }

    /// <summary>The bug report's "x→height" mill: the up arrow is named by the
    /// letter that actually rides it (X), the horizontals by theirs (Y, Z). The
    /// grid lies on the work pair's own worlds — slot 0 × slot 1 (world Z × X) —
    /// and the cone plunges along the perpendicular slot, machine Z (world Y).</summary>
    [Fact]
    public void Remapped_mill_letters_follow_the_axes_that_ride_each_direction()
    {
        var profile = new MachineProfile
        {
            Id = "remap",
            Name = "Remap",
            MachineTypeId = MachineTypes.Mill,
        };
        profile.Planes.Add(new ProfilePlane
        {
            Axes =
            {
                new MachineAxis { Index = 0, Kind = AxisKind.Linear, Direction = AxisDirection.Height },
                new MachineAxis { Index = 1, Kind = AxisKind.Linear, Direction = AxisDirection.Width },
                new MachineAxis { Index = 2, Kind = AxisKind.Linear, Direction = AxisDirection.Length },
            },
        });
        var kinematics = MachineKinematicsResolver.Resolve(profile, null, 0);

        var fake = new FakePlotScene();
        var f = new KinematicSceneFactory(fake);
        f.BuildScene(kinematics);

        // worldX ← ctrl Y ('Y'), worldY ← ctrl Z ('Z'), worldZ ← ctrl X ('X').
        Assert.Equal(["Y", "Z", "X"], fake.LastChromeArrows.Select(a => a.Letter));
        Assert.Equal(1, fake.LastGridNormalAxis);             // ⊥ machine Z = world Y
        Assert.Equal(1, fake.LastGridNormalSign);
        Assert.Equal(Vector3.UnitY, ((FakePlotTool)f.Tool!).PlungeDirection);
    }

    /// <summary>An inverted perpendicular axis — the mill's plunging slot, here
    /// ctrl Z — moves the machine's floor to the other end of its values: the
    /// grid plane's sign flips, and the tool's plunge flips with it.</summary>
    [Fact]
    public void Inverted_perpendicular_axis_flips_the_grid_end_and_the_plunge()
    {
        var profile = new MachineProfile
        {
            Id = "flipped",
            Name = "Flipped",
            MachineTypeId = MachineTypes.Mill,
        };
        profile.Planes.Add(new ProfilePlane
        {
            Axes =
            {
                new MachineAxis { Index = 0, Kind = AxisKind.Linear, Direction = AxisDirection.Width },
                new MachineAxis { Index = 1, Kind = AxisKind.Linear, Direction = AxisDirection.Length },
                new MachineAxis
                {
                    Index = 2,
                    Kind = AxisKind.Linear,
                    Direction = AxisDirection.Height,
                    Sign = -1,
                },
            },
        });
        var kinematics = MachineKinematicsResolver.Resolve(profile, null, 0);

        var fake = new FakePlotScene();
        var f = new KinematicSceneFactory(fake);
        f.BuildScene(kinematics);

        Assert.Equal(2, fake.LastGridNormalAxis);
        Assert.Equal(-1, fake.LastGridNormalSign);
        Assert.Equal(-Vector3.UnitZ, ((FakePlotTool)f.Tool!).PlungeDirection);
        Assert.Equal("Z", fake.LastChromeArrows[2].Letter);   // the Height axis is ctrl Z here
    }

    /// <summary>A topology with no Height axis at all (towers remapped to
    /// Width × Length): the grid falls back to the plane perpendicular to the
    /// Width-role axis, and the world direction no axis rides stays unnamed.</summary>
    [Fact]
    public void A_topology_without_height_falls_back_to_the_width_perpendicular_grid()
    {
        var profile = new MachineProfile
        {
            Id = "flat",
            Name = "Flat",
            MachineTypeId = MachineTypes.Mill,
        };
        profile.Planes.Add(new ProfilePlane
        {
            Axes =
            {
                new MachineAxis { Index = 0, Kind = AxisKind.Linear, Direction = AxisDirection.Width },
                new MachineAxis { Index = 1, Kind = AxisKind.Linear, Direction = AxisDirection.Length },
            },
        });
        var kinematics = MachineKinematicsResolver.Resolve(profile, null, 0);

        var fake = new FakePlotScene();
        var f = new KinematicSceneFactory(fake);
        f.BuildScene(kinematics);

        Assert.Equal(["X", "Y", "?"], fake.LastChromeArrows.Select(a => a.Letter));
        Assert.Equal(0, fake.LastGridNormalAxis);             // ⊥ the Width axis
        Assert.Equal(1, fake.LastGridNormalSign);
        Assert.Equal(Vector3.UnitZ, ((FakePlotTool)f.Tool!).PlungeDirection);
    }

    /// <summary>Stock foam towers: the gnomon stands at the origin between the
    /// towers (their ±separation/2 placements put the midpoint on it), the
    /// world-Y arrow is named by the tower's Length letter ('X'), the vertical
    /// by its Height letter ('Y'), and the separation direction — no machine
    /// axis — stays unnamed ("?").</summary>
    [Fact]
    public void Foam_gnomon_sits_between_the_towers_and_speaks_their_letters()
    {
        var fake = new FakePlotScene();
        new KinematicSceneFactory(fake).BuildScene(Resolve(MachineTypes.Foam, separation: 25));

        Assert.Equal(3, fake.LastChromeArrows.Count);
        Assert.All(fake.LastChromeArrows, a => Assert.Equal(Vector3.Zero, a.Pos));
        Assert.Equal(["?", "X", "Y"], fake.LastChromeArrows.Select(a => a.Letter));
        Assert.Equal(2, fake.LastGridNormalAxis);
        Assert.Equal(1, fake.LastGridNormalSign);
    }
}

/// <summary>Test double that records all IPlotScene calls for factory wiring assertions.</summary>
public sealed class FakePlotScene : IPlotScene
{
    public List<ICarriagePlaneLayer> CarriagePlanes { get; } = [];
    public List<ILoftSurface> LoftSurfaces { get; } = [];
    public List<ToolShape> ToolShapes { get; } = [];
    public List<(Vector3 Pos, Vector3 Direction, string Letter)> LastChromeArrows { get; private set; } = [];    public int LastGridNormalAxis { get; private set; } = -1;
    public int LastGridNormalSign { get; private set; }
    public bool Disposed { get; private set; }

    public void SetViewPreset(ViewPreset preset) { }
    public void Pan(PanDelta delta) { }
    public void ZoomAround(Vector2 screenPoint, double factor) { }
    public void SetViewportSize(int wPx, int hPx) { }
    public void PanByScreenDelta(Vector2 screenDeltaPx) { }
    public void RotateByScreenDelta(Vector2 screenDeltaPx) { }
    public void BeginOrbit(Vector2 screenPointPx) { }
    public void EndOrbit() { }
    public void Reframe() { }

    public void SetMachineChrome(
        IReadOnlyList<(Vector3 Pos, Vector3 Direction, string Letter)> arrows,
        int gridNormalAxis, int gridNormalSign)
    {
        LastChromeArrows = [.. arrows];
        LastGridNormalAxis = gridNormalAxis;
        LastGridNormalSign = gridNormalSign;
    }

    public void AddAxisArrows(IReadOnlyList<(Vector3 Pos, Vector3 Direction, string Letter)> arrows) { }
    public void SetGrid(BoundingBox bounds, double minor, double major) { }
    public void SetOrigin(Vector3 origin) { }
    public void SetJobEnvelope(BoundingBox box) { }

    public ICarriagePlaneLayer AddCarriagePlane()
    {
        var layer = new FakeCarriagePlaneLayer();
        CarriagePlanes.Add(layer);
        return layer;
    }

    public ILoftSurface AddLoftSurface()
    {
        var loft = new FakeLoftSurface();
        LoftSurfaces.Add(loft);
        return loft;
    }

    public IPlotTool CreateTool(ToolShape shape)
    {
        ToolShapes.Add(shape);
        return new FakePlotTool(shape);
    }

    public void Dispose() => Disposed = true;
}

/// <summary>Test double for ICarriagePlaneLayer that records segment additions.</summary>
public sealed class FakeCarriagePlaneLayer : ICarriagePlaneLayer
{
    public List<(LineSegment Seg, MotionType Type)> Segments { get; } = [];

    public void AddSegment(LineSegment seg, MotionType type) => Segments.Add((seg, type));
    public void SetSegmentRunState(int segmentIndex, RunState state) { }
    public void RebuildExecuted() { }
}

/// <summary>Test double for ILoftSurface that records wire plane sets.</summary>
public sealed class FakeLoftSurface : ILoftSurface
{
    public List<Vector3[]> WirePlanes { get; } = [];

    public void SetWirePlane(Vector3[] polygon) => WirePlanes.Add(polygon);
    public void SetLoftSheet(IReadOnlyList<Vector3> sideA, IReadOnlyList<Vector3> sideB) { }
}

/// <summary>Test double for IPlotTool that tracks position updates and shape.</summary>
public sealed class FakePlotTool : IPlotTool
{
    public FakePlotTool(ToolShape shape) => Shape = shape;

    public List<Endpoint> Positions { get; } = [];
    public ToolShape Shape { get; set; }
    public Vector3 PlungeDirection { get; set; } = Vector3.UnitZ;
    public bool Disposed { get; private set; }

    public void UpdatePosition(Endpoint endpoints) => Positions.Add(endpoints);
    public void Dispose() => Disposed = true;
}
