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

using SenderNG.Modules.Plotter.Settings;

namespace SenderNG.Core.Tests;

public class PlotterPlaneMapperTests
{
    [Fact]
    public void Identity_mill_plane_maps_XY_and_Z_to_themselves()
    {
        var g = PlotterPlaneMapper.Identity(3);
        Assert.Equal([0, 1, 2], g.WorldAxes);
        Assert.Equal([0d, 0d, 0d], g.Origin);
    }

    [Fact]
    public void Identity_foam_plane_maps_Z_to_world_X_and_A_to_world_Y()
    {
        var g = PlotterPlaneMapper.Identity(2);
        Assert.Equal([0, 1], g.WorldAxes);
        Assert.Equal(0, PlotterPlaneMapper.SlotForWorldAxis(g.WorldAxes, 0));
        Assert.Equal(1, PlotterPlaneMapper.SlotForWorldAxis(g.WorldAxes, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void IsUsable_rejects_a_wrong_length_mapping(int roles)
    {
        Assert.False(PlotterPlaneMapper.IsUsable(PlotterPlaneMapper.Identity(roles), roles + 1));
    }

    [Fact]
    public void IsUsable_rejects_repeated_and_out_of_range_world_axes()
    {
        Assert.False(PlotterPlaneMapper.IsUsable(
            new PlotterPlaneGeometry { WorldAxes = [0, 0] }, 2));
        Assert.False(PlotterPlaneMapper.IsUsable(
            new PlotterPlaneGeometry { WorldAxes = [0, 5] }, 2));
    }

    [Fact]
    public void IsUsable_rejects_null_and_accepts_a_swapped_mapping()
    {
        Assert.False(PlotterPlaneMapper.IsUsable(null, 2));
        Assert.True(PlotterPlaneMapper.IsUsable(
            new PlotterPlaneGeometry { WorldAxes = [1, 0] }, 2));
    }

    [Fact]
    public void For_falls_back_to_identity_when_the_mapping_is_unusable()
    {
        var bad = new PlotterPlaneGeometry { WorldAxes = [3, 3] };
        Assert.Equal([0, 1], PlotterPlaneMapper.For(bad, 2).WorldAxes);
        Assert.Equal([0, 1, 2], PlotterPlaneMapper.For(null, 3).WorldAxes);
    }

    [Fact]
    public void Feeds_reads_world_X_and_Y_off_the_mapping()
    {
        var swapped = new PlotterPlaneGeometry { WorldAxes = [1, 0] };
        Assert.Equal((1, 0), PlotterPlaneMapper.Feeds(swapped));
    }

    [Fact]
    public void Feeds_reports_minus_one_for_an_unmapped_world_axis()
    {
        // A plane feeding only world X and Z has no world Y — the flat 2D
        // pane cannot draw it and PlaneDrawable must reject it.
        var xz = new PlotterPlaneGeometry { WorldAxes = [0, 2] };
        Assert.Equal((0, -1), PlotterPlaneMapper.Feeds(xz));
    }

    [Fact]
    public void SlotForWorldAxis_returns_minus_one_when_absent()
    {
        Assert.Equal(-1, PlotterPlaneMapper.SlotForWorldAxis([0, 1], 2));
        Assert.Equal(-1, PlotterPlaneMapper.SlotForWorldAxis(null, 0));
    }

    [Fact]
    public void OriginFor_reads_the_offset_and_defaults_to_zero()
    {
        var g = new PlotterPlaneGeometry { WorldAxes = [0, 1, 2], Origin = [1, 2, 3] };
        Assert.Equal(2, PlotterPlaneMapper.OriginFor(g, 1));
        Assert.Equal(0, PlotterPlaneMapper.OriginFor(g, 9));
        Assert.Equal(0, PlotterPlaneMapper.OriginFor(null, 0));
        // A settings file is untrusted input, so a wrong-length Origin clamps
        // to zero instead of indexing — the arm the length guard exists for.
        Assert.Equal(0, PlotterPlaneMapper.OriginFor(new PlotterPlaneGeometry { Origin = [1, 2] }, 0));
    }

    /// <summary>The world XY footprint the iso lattice and the iso job box are
    /// measured against: each world axis is located through the plane's
    /// mapping, read out of the program frame, and UNIONED with the origin so
    /// the lattice always spans the axes themselves (G-code is modal from the
    /// machine origin, so the origin is a corner of the extent, not outside
    /// it).</summary>
    [Fact]
    public void TryWorldFootprint_measures_the_programs_extent_through_the_mapping()
    {
        var bounds = new[] { new[] { 5.0, 100.0 }, new[] { -3.0, 40.0 } };
        // Identity: world X is machine axis 0 ([5,100] union the origin ->
        // 0..100) and world Y is machine axis 1 ([-3,40] union the origin ->
        // -3..40, so the origin only extends Y's low side).
        Assert.True(PlotterPlaneMapper.TryWorldFootprint(
            new PlotterPlaneGeometry { WorldAxes = [0, 1] }, [0, 1], bounds, out var straight));
        Assert.Equal((0.0, -3.0, 100.0, 40.0), straight);
        // Swapped: WorldAxes[0] is world Y, so world X is read from ROLE 1
        // (machine axis 1, [-3, 40]) and world Y from ROLE 0 ([5, 100]). The two
        // answers differ, which is the proof that the mapping is consulted rather
        // than the frame read positionally.
        Assert.True(PlotterPlaneMapper.TryWorldFootprint(
            new PlotterPlaneGeometry { WorldAxes = [1, 0] }, [0, 1], bounds, out var swapped));
        Assert.Equal((-3.0, 0.0, 40.0, 100.0), swapped);
    }

    [Fact]
    public void TryWorldFootprint_unions_the_origin_into_the_extent()
    {
        var bounds = new[] { new[] { 5.0, 100.0 }, new[] { 7.0, 40.0 } };
        Assert.True(PlotterPlaneMapper.TryWorldFootprint(
            new PlotterPlaneGeometry { WorldAxes = [0, 1] }, [0, 1], bounds, out var fp));
        // A program that never crosses the origin still gets it as a corner,
        // because that is where the axis arrows start and where the lattice
        // has to reach to show it.
        Assert.Equal((0.0, 0.0, 100.0, 40.0), fp);
    }

    /// <summary>No footprint means NO lattice, NO arrows and NO job box - the
    /// three things derived from the extent. A magic 1 mm square would put
    /// invented geometry on screen for a plane that maps neither world X nor
    /// world Y, which is why this returns false instead of a value.</summary>
    [Fact]
    public void TryWorldFootprint_refuses_a_plane_that_feeds_no_world_X()
    {
        var bounds = new[] { new[] { 5.0, 100.0 }, new[] { 7.0, 40.0 } };
        Assert.False(PlotterPlaneMapper.TryWorldFootprint(
            new PlotterPlaneGeometry { WorldAxes = [1, 2] }, [0, 1], bounds, out _));
    }

    [Fact]
    public void TryWorldFootprint_refuses_a_plane_that_feeds_no_world_Y()
    {
        var bounds = new[] { new[] { 5.0, 100.0 }, new[] { 7.0, 40.0 } };
        Assert.False(PlotterPlaneMapper.TryWorldFootprint(
            new PlotterPlaneGeometry { WorldAxes = [0, 2] }, [0, 1], bounds, out _));
    }

    /// <summary>The role index and the program frame are both untrusted here: a
    /// stale mapping can name a role the plane does not have, and a plane can
    /// name a machine axis the program frame has no bounds for. Both mean the
    /// same thing — the extent is not locatable — and neither may fall through
    /// to a fabricated footprint.</summary>
    [Fact]
    public void TryWorldFootprint_refuses_an_out_of_range_role_or_frame()
    {
        var bounds = new[] { new[] { 5.0, 100.0 }, new[] { 7.0, 40.0 } };
        // Role index past the plane's own axis indices.
        Assert.False(PlotterPlaneMapper.TryWorldFootprint(
            new PlotterPlaneGeometry { WorldAxes = [0, 1] }, [0], bounds, out _));
        // Machine axis past the program frame's bounds.
        Assert.False(PlotterPlaneMapper.TryWorldFootprint(
            new PlotterPlaneGeometry { WorldAxes = [0, 1] }, [0, 7], bounds, out _));
        // No mapping at all.
        Assert.False(PlotterPlaneMapper.TryWorldFootprint(null, [0, 1], bounds, out _));
        Assert.False(PlotterPlaneMapper.TryWorldFootprint(
            new PlotterPlaneGeometry { WorldAxes = [0, 1] }, [0, 1], [], out _));
    }

    /// <summary>A program that never moves an axis has a zero-thickness extent,
    /// which is a REAL measurement and not a missing one: the caller keeps the
    /// job box (a flat face is a legitimate box) while the lattice itself comes
    /// back empty from <c>PlotterChrome.FloorLines</c>. Refusing here would drop
    /// a job box the program does have.</summary>
    [Fact]
    public void TryWorldFootprint_keeps_a_zero_thickness_extent()
    {
        // Machine axis 0 never moves: world X's span is (0, 0), which unions
        // with the origin to (0, 0) and leaves the footprint zero-thickness in X
        // while world Y keeps its real 7..40.
        var bounds = new[] { new[] { 0.0, 0.0 }, new[] { 7.0, 40.0 } };
        Assert.True(PlotterPlaneMapper.TryWorldFootprint(
            new PlotterPlaneGeometry { WorldAxes = [0, 1] }, [0, 1], bounds, out var fp));
        Assert.Equal((0.0, 0.0, 0.0, 40.0), fp);
    }
}
