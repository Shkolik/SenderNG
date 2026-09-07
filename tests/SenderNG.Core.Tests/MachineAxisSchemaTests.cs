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

using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class MachineAxisSchemaTests
{
    [Fact]
    public void Mill_preset_is_one_plane_with_depth()
    {
        var mill = MachineTypeCatalog.Resolve(MachineTypes.Mill);
        Assert.Equal(MachineTypes.Mill, mill.Id);
        var plane = Assert.Single(mill.Planes);
        Assert.Equal(new[] { AxisRoleKind.Horizontal, AxisRoleKind.Vertical, AxisRoleKind.Depth }, plane.Roles);
        Assert.True(plane.HasDepth);
        Assert.False(mill.IsMultiPlane);
        Assert.Equal(3, mill.AxisCount);
        Assert.Equal(3, mill.DefaultDemoAxes);
        // Conventional fallback letters (the controller's [AXS:] letters override them).
        Assert.Equal('X', mill.ConventionalLetter(0));
        Assert.Equal('Y', mill.ConventionalLetter(1));
        Assert.Equal('Z', mill.ConventionalLetter(2));
    }

    [Fact]
    public void Foam_preset_is_two_coupled_planes_without_depth()
    {
        var foam = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        Assert.Equal(MachineTypes.Foam, foam.Id);
        Assert.Equal(2, foam.Planes.Count);
        Assert.All(foam.Planes, p => Assert.Equal(
            new[] { AxisRoleKind.Horizontal, AxisRoleKind.Vertical }, p.Roles));
        Assert.All(foam.Planes, p => Assert.False(p.HasDepth));
        Assert.True(foam.IsMultiPlane);
        Assert.Equal(4, foam.AxisCount);
        Assert.Equal(4, foam.DefaultDemoAxes);
        // Conventional fallback: tower 2 is Z/A — a controller reporting
        // [AXS:4:XYUV] overrides the display, not the schema (decision #1).
        Assert.Equal('Z', foam.ConventionalLetter(2));
        Assert.Equal('A', foam.ConventionalLetter(3));
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
            Planes = [new CarriagePlane([AxisRoleKind.Horizontal, AxisRoleKind.Vertical])],
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
}
