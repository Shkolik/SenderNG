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

using CNC.Core.Services;

namespace SenderNG.Core.Tests;

/// <summary>
/// Machine-type presets: the direction/base-plane model (BasePlanes,
/// SeparationDirection, DefaultSeparation, CreateDefaultPlanes) and the catalog
/// that resolves and registers them.
/// </summary>
public class MachineAxisSchemaTests
{
    /* ------------------------------- new model ------------------------------- */

    [Fact]
    public void Mill_preset_declares_one_base_plane_of_width_length_height()
    {
        var mill = MachineTypeCatalog.Resolve(MachineTypes.Mill);

        var plane = Assert.Single(mill.BasePlanes);
        Assert.Equal(new[] { AxisDirection.Width, AxisDirection.Length, AxisDirection.Height }, plane);
        Assert.Null(mill.SeparationDirection);
        Assert.Equal(0, mill.DefaultSeparation);
        Assert.Equal(3, mill.DefaultDemoAxes);
    }

    [Fact]
    public void Foam_preset_declares_two_coupled_planes_separated_along_width()
    {
        var foam = MachineTypeCatalog.Resolve(MachineTypes.Foam);

        Assert.Equal(2, foam.BasePlanes.Count);
        Assert.All(foam.BasePlanes, p =>
            Assert.Equal(new[] { AxisDirection.Length, AxisDirection.Height }, p));
        Assert.Equal(AxisDirection.Width, foam.SeparationDirection);
        // A sane non-zero default: the two towers must not start coincident.
        Assert.Equal(200, foam.DefaultSeparation);
        Assert.Equal(4, foam.DefaultDemoAxes);
    }

    [Fact]
    public void CreateDefaultPlanes_binds_each_base_plane_to_the_conventional_indices()
    {
        var mill = MachineTypeCatalog.Resolve(MachineTypes.Mill);
        var millPlane = Assert.Single(mill.CreateDefaultPlanes());
        Assert.Equal(new[] { 0, 1, 2 }, millPlane.Axes.Select(a => a.Index));
        Assert.Equal(
            new[] { AxisDirection.Width, AxisDirection.Length, AxisDirection.Height },
            millPlane.Axes.Select(a => a.Direction));
        Assert.All(millPlane.Axes, a =>
        {
            Assert.Equal(AxisKind.Linear, a.Kind);
            Assert.Equal(1, a.Sign);
            Assert.Null(a.RotationOrigin);
            Assert.Null(a.Alias);
        });

        var foam = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var foamPlanes = foam.CreateDefaultPlanes();
        Assert.Equal(2, foamPlanes.Count);
        // The default layout keeps the flattened index order (0,1 | 2,3) — the bug-report
        // firmware's A-first plane 2 is a profile edit, not the preset's default.
        Assert.Equal(new[] { 0, 1 }, foamPlanes[0].Axes.Select(a => a.Index));
        Assert.Equal(new[] { 2, 3 }, foamPlanes[1].Axes.Select(a => a.Index));
        Assert.All(foamPlanes, p =>
            Assert.Equal(new[] { AxisDirection.Length, AxisDirection.Height },
                p.Axes.Select(a => a.Direction)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NoSuchType")]
    public void Resolve_unknown_ids_fall_back_to_mill(string? typeId)
    {
        Assert.Same(MachineTypeCatalog.Resolve(MachineTypes.Mill), MachineTypeCatalog.Resolve(typeId));
    }

    [Fact]
    public void Register_makes_type_resolvable_and_duplicates_throw()
    {
        // "Test2D" intentionally stays registered for the process lifetime —
        // the catalog has no unregister; other tests must not depend on the
        // registration set beyond the built-ins.
        var custom = new MachineType
        {
            Id = "Test2D",
            Name = "2D",
            BasePlanes = [[AxisDirection.Width, AxisDirection.Length]],
            DefaultDemoAxes = 2,
        };
        MachineTypeCatalog.Register(custom);
        Assert.Same(custom, MachineTypeCatalog.Resolve("Test2D"));

        // Duplicate registration (same instance or same id) is a plugin bug.
        Assert.Throws<ArgumentException>(() => MachineTypeCatalog.Register(custom));
        Assert.Throws<ArgumentException>(() => MachineTypeCatalog.Register(
            new MachineType { Id = "Test2D", Name = "again" }));
    }

    [Fact]
    public void Register_rejects_null_and_empty_ids()
    {
        Assert.Throws<ArgumentNullException>(() => MachineTypeCatalog.Register(null!));
        Assert.Throws<ArgumentException>(() => MachineTypeCatalog.Register(
            new MachineType { Id = "", Name = "bad" }));
    }

    [Fact]
    public void Conventional_letters_follow_the_bare_axis_order()
    {
        Assert.Equal("XYZABCUVW", MachineType.ConventionalLetters);
    }
}
