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

    /// <summary>The initial tool wire spans the machine's own separation, on the
    /// world axis the two-slot tower does NOT span — world <b>X</b>, because the
    /// tower's Length × Height plane is vertical (world Y × Z). The placement is
    /// the machine model's, not an invented UnitZ. A separation of 0 degenerates to
    /// the same wire the mill case uses.</summary>
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
        Assert.Equal(Vector3.Zero, points[0]);
        // The vertical tower leaves world X free, and plane 1 sits at the separation.
        Assert.Equal(new Vector3(25, 0, 0), points[1]);
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
}

/// <summary>Test double that records all IPlotScene calls for factory wiring assertions.</summary>
public sealed class FakePlotScene : IPlotScene
{
    public List<ICarriagePlaneLayer> CarriagePlanes { get; } = [];
    public List<ILoftSurface> LoftSurfaces { get; } = [];
    public List<ToolShape> ToolShapes { get; } = [];
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
    public void AddAxisArrows(IReadOnlyList<(Vector3 Pos, string Label)> arrows) { }
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
    public bool Disposed { get; private set; }

    public void UpdatePosition(Endpoint endpoints) => Positions.Add(endpoints);
    public void Dispose() => Disposed = true;
}
