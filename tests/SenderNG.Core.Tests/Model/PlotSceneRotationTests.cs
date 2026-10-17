/*
 * PlotSceneRotationTests.cs - display-only scene rotations per machine shape
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
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using System.Numerics;
using CNC.Core.Services;
using SenderNG.Modules.Plotter.GL.Model;

namespace SenderNG.Core.Tests;

public class PlotSceneRotationTests
{
    private static MachineKinematics Resolve(string machineTypeId)
    {
        var profile = new MachineProfile
        {
            Id = "rotation-" + machineTypeId,
            Name = machineTypeId,
            MachineTypeId = machineTypeId,
            Separation = 200,
        };
        return MachineKinematicsResolver.Resolve(profile, null, 0);
    }

    private static MachineKinematics RemappedMill()
    {
        var profile = new MachineProfile
        {
            Id = "rotation-mill-vertical",
            Name = "Mill vertical",
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
        return MachineKinematicsResolver.Resolve(profile, null, 0);
    }

    private static MachineKinematics VerticalWireEdm()
    {
        var profile = new MachineProfile
        {
            Id = "rotation-edm",
            Name = "EDM",
            MachineTypeId = MachineTypes.Foam,
            Separation = 200,
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
        return MachineKinematicsResolver.Resolve(profile, null, 0);
    }

    private static void AssertRotated(Matrix4x4 rotation, Vector3 axis, Vector3 expected)
    {
        var actual = Vector3.TransformNormal(axis, rotation);
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }

    /// <summary>A stock mill's grid is the Z floor, already below the eye; the
    /// scene must not be turned.</summary>
    [Fact]
    public void Stock_mill_keeps_the_scene_unrotated()
    {
        var k = Resolve(MachineTypes.Mill);
        var grid = MachineChrome.For(k).GridNormalAxis;

        Assert.Equal(2, grid);
        Assert.Equal(Matrix4x4.Identity, PlotSceneRotation.For(k, grid));
    }

    /// <summary>A vertical-grid mill is yawed 90° counter-clockwise about world
    /// Z, which moves the Y-normal floor behind the fixed ISO eye without
    /// changing the height axis.</summary>
    [Fact]
    public void Vertical_grid_mill_yaws_ninety_degrees_ccw()
    {
        var k = RemappedMill();
        var grid = MachineChrome.For(k).GridNormalAxis;
        var rotation = PlotSceneRotation.For(k, grid);

        Assert.Equal(1, grid);                         // a Y-normal grid
        AssertRotated(rotation, Vector3.UnitX, Vector3.UnitY);
        AssertRotated(rotation, Vector3.UnitY, -Vector3.UnitX);
        AssertRotated(rotation, Vector3.UnitZ, Vector3.UnitZ);
    }

    /// <summary>A vertical-wire EDM is turned 180° about the horizontal axis
    /// that is neither the wire nor the grid normal: the X-normal floor moves
    /// behind the eye and the two horizontal working planes swap top/bottom, so
    /// plane 0 ends up at the bottom.</summary>
    [Fact]
    public void Vertical_wire_edm_turns_half_about_the_other_horizontal_axis()
    {
        var k = VerticalWireEdm();
        var grid = MachineChrome.For(k).GridNormalAxis;
        var rotation = PlotSceneRotation.For(k, grid);

        Assert.Equal(2, k.FreeWorldAxisFor(0));        // vertical wire
        Assert.Equal(0, grid);                         // X-normal grid
        AssertRotated(rotation, Vector3.UnitX, -Vector3.UnitX);
        AssertRotated(rotation, Vector3.UnitY, Vector3.UnitY);
        AssertRotated(rotation, Vector3.UnitZ, -Vector3.UnitZ);
    }

    /// <summary>Horizontal-wire foam keeps its Z floor below the eye; turning
    /// it would put the floor above the work, so it stays identity.</summary>
    [Fact]
    public void Horizontal_wire_foam_keeps_the_scene_unrotated()
    {
        var k = Resolve(MachineTypes.Foam);
        var grid = MachineChrome.For(k).GridNormalAxis;

        Assert.Equal(0, k.FreeWorldAxisFor(0));        // horizontal wire
        Assert.Equal(2, grid);                         // Z floor
        Assert.Equal(Matrix4x4.Identity, PlotSceneRotation.For(k, grid));
    }
}
