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

/// <summary>The positional plane→world rules: slot <c>k</c> feeds world axis
/// <c>k</c>, so the only question left is which world axes a plane's slot COUNT
/// reaches. The per-plane <c>WorldAxes</c> table these cases used to exercise is
/// deleted, and with it the mapping-shaped answers (identity, permutations,
/// malformed-input fallbacks): there is nothing left to be malformed.</summary>
public class PlotterPlaneMapperTests
{
    [Fact]
    public void A_two_slot_plane_spans_world_X_and_Y_and_not_Z()
    {
        Assert.True(PlotterPlaneMapper.FeedsWorldAxis([2, 3], 0));
        Assert.True(PlotterPlaneMapper.FeedsWorldAxis([2, 3], 1));
        Assert.False(PlotterPlaneMapper.FeedsWorldAxis([2, 3], 2));
    }

    [Fact]
    public void A_three_slot_plane_spans_all_three_world_axes()
    {
        Assert.True(PlotterPlaneMapper.FeedsWorldAxis([0, 1, 2], 2));
        Assert.False(PlotterPlaneMapper.FeedsWorldAxis([0, 1, 2], 3));
        Assert.False(PlotterPlaneMapper.FeedsWorldAxis([0, 1, 2], -1));
    }

    /// <summary>A one-slot plane has world X and no world Y — the case the flat
    /// pane refuses to draw.</summary>
    [Fact]
    public void A_one_slot_plane_has_no_world_Y()
    {
        Assert.True(PlotterPlaneMapper.FeedsWorldAxis([2], 0));
        Assert.False(PlotterPlaneMapper.FeedsWorldAxis([2], 1));
        Assert.False(PlotterPlaneMapper.FeedsWorldAxis([], 0));
        Assert.False(PlotterPlaneMapper.FeedsWorldAxis(null, 0));
    }

    /// <summary>The world XY footprint the iso lattice and the iso job box are
    /// measured against: each world axis is read positionally out of the
    /// program frame and UNIONED with the origin so the lattice always spans the
    /// axes themselves (G-code is modal from the machine origin, so the origin
    /// is a corner of the extent, not outside it).</summary>
    [Fact]
    public void TryWorldFootprint_measures_the_programs_extent_positionally()
    {
        var bounds = new[] { new[] { 5.0, 100.0 }, new[] { -3.0, 40.0 } };
        // Slot 0 is machine axis 0 ([5,100] union the origin -> 0..100) and slot 1
        // is machine axis 1 ([-3,40] union the origin -> -3..40, so the origin
        // only extends Y's low side).
        Assert.True(PlotterPlaneMapper.TryWorldFootprint([0, 1], bounds, out var straight));
        Assert.Equal((0.0, -3.0, 100.0, 40.0), straight);
    }

    /// <summary>A plane's slots need not be machine axes 0 and 1: the slice is
    /// positional, so plane 1's <c>[2, 3]</c> reads machine axes 2 and 3 into
    /// world X and Y. That is the foam tower's whole geometry.</summary>
    [Fact]
    public void TryWorldFootprint_reads_the_planes_own_slots()
    {
        var bounds = new[]
        {
            new[] { 5.0, 100.0 }, new[] { 7.0, 40.0 },
            new[] { 10.0, 50.0 }, new[] { 8.0, 30.0 },
        };
        Assert.True(PlotterPlaneMapper.TryWorldFootprint([2, 3], bounds, out var fp));
        Assert.Equal((0.0, 0.0, 50.0, 30.0), fp);
    }

    [Fact]
    public void TryWorldFootprint_unions_the_origin_into_the_extent()
    {
        var bounds = new[] { new[] { 5.0, 100.0 }, new[] { 7.0, 40.0 } };
        Assert.True(PlotterPlaneMapper.TryWorldFootprint([0, 1], bounds, out var fp));
        // A program that never crosses the origin still gets it as a corner,
        // because that is where the axis arrows start and where the lattice
        // has to reach to show it.
        Assert.Equal((0.0, 0.0, 100.0, 40.0), fp);
    }

    /// <summary>No footprint means NO lattice, NO arrows and NO job box - the
    /// three things derived from the extent. A magic 1 mm square would put
    /// invented geometry on screen for a plane with no world XY face.</summary>
    [Fact]
    public void TryWorldFootprint_refuses_a_plane_with_no_world_Y()
    {
        var bounds = new[] { new[] { 5.0, 100.0 }, new[] { 7.0, 40.0 } };
        Assert.False(PlotterPlaneMapper.TryWorldFootprint([2], bounds, out _));
        Assert.False(PlotterPlaneMapper.TryWorldFootprint([], bounds, out _));
        Assert.False(PlotterPlaneMapper.TryWorldFootprint(null, bounds, out _));
    }

    /// <summary>The program frame is untrusted here: a plane can name a machine
    /// axis it has no bounds for, which means the extent is not locatable — it
    /// may not fall through to a fabricated footprint.</summary>
    [Fact]
    public void TryWorldFootprint_refuses_an_axis_past_the_frame()
    {
        var bounds = new[] { new[] { 5.0, 100.0 }, new[] { 7.0, 40.0 } };
        Assert.False(PlotterPlaneMapper.TryWorldFootprint([0, 7], bounds, out _));
        // No frame at all.
        Assert.False(PlotterPlaneMapper.TryWorldFootprint([0, 1], [], out _));
        Assert.False(PlotterPlaneMapper.TryWorldFootprint([0, 1], null, out _));
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
        Assert.True(PlotterPlaneMapper.TryWorldFootprint([0, 1], bounds, out var fp));
        Assert.Equal((0.0, 0.0, 0.0, 40.0), fp);
    }

    /// <summary>The job envelope belongs to the JOB, not to a carriage, so the
    /// planes are UNIONED: min of the minima, max of the maxima. The worked
    /// example is the foam preset's own axis split — plane 0 is machine axes
    /// [0,1] (X,Y) and plane 1 is [2,3] (Z,A) — and it is the case the whole
    /// change exists for.</summary>
    [Fact]
    public void TryJobExtent_unions_every_plane_through_its_own_slots()
    {
        // Plane 0 travels X 0..100, Y 0..30. Plane 1 travels Z 10..50, A 0..20.
        var bounds = new[]
        {
            new[] { 0.0, 100.0 }, new[] { 0.0, 30.0 },
            new[] { 10.0, 50.0 }, new[] { 0.0, 20.0 },
        };
        Assert.True(PlotterPlaneMapper.TryJobExtent([[0, 1], [2, 3]], bounds, out var job));
        // X is 0..100 from plane 0 and 10..50 from plane 1; Y is 0..30 and
        // 0..20. Both planes' tiles carry the union, so neither tile is
        // 40x20 while the other is 100x30.
        Assert.Equal((0.0, 0.0, 100.0, 30.0), job);
    }

    /// <summary>The no-op property, and the reason a mill frame cannot change:
    /// one contributing plane's union IS that plane's own extent, byte for
    /// byte. This is the guard the whole change rests on, so it is derived here
    /// from <see cref="PlotterPlaneMapper.TryWorldFootprint"/> rather than pinned
    /// as a literal — a literal would only restate today's answer, and a later
    /// change to the origin unioning would quietly break the property with this
    /// test still green. The Y minimum is negative on purpose: that is the case
    /// where the origin unioning has something to do, so a copy that skipped the
    /// union would differ from the footprint instead of coinciding with it.</summary>
    [Fact]
    public void TryJobExtent_with_one_plane_equals_that_plane_alone()
    {
        var bounds = new[] { new[] { 5.0, 100.0 }, new[] { -3.0, 40.0 } };
        Assert.True(PlotterPlaneMapper.TryWorldFootprint([0, 1], bounds, out var fp));
        Assert.True(PlotterPlaneMapper.TryJobExtent([[0, 1]], bounds, out var job));
        Assert.Equal(fp, job);
    }

    [Fact]
    public void TryJobExtent_skips_a_plane_with_no_world_Y()
    {
        var bounds = new[]
        {
            new[] { 0.0, 100.0 }, new[] { 0.0, 30.0 },
            new[] { 10.0, 50.0 }, new[] { 0.0, 20.0 },
        };
        // Plane 1 has a single slot, so it has no world Y extent to give. It
        // must not widen the envelope, and it must not throw.
        Assert.True(PlotterPlaneMapper.TryJobExtent([[0, 1], [2]], bounds, out var job));
        Assert.Equal((0.0, 0.0, 100.0, 30.0), job);
    }

    /// <summary>The axis-mismatch drop: a foam preset needing four axes against
    /// a three-axis program. The plane's indices run past the frame, so it is
    /// skipped by the same guard that keeps the single-plane version from
    /// indexing out of range.</summary>
    [Fact]
    public void TryJobExtent_skips_a_plane_whose_axes_run_past_the_frame()
    {
        var bounds = new[]
        {
            new[] { 0.0, 100.0 }, new[] { 0.0, 30.0 },
            new[] { 10.0, 50.0 }, new[] { 0.0, 20.0 },
        };
        // [2, 9] against a four-axis frame: slot 1's machine axis 9 is past it.
        Assert.True(PlotterPlaneMapper.TryJobExtent([[0, 1], [2, 9]], bounds, out var job));
        Assert.Equal((0.0, 0.0, 100.0, 30.0), job);
    }

    [Fact]
    public void TryJobExtent_unions_the_origin_into_every_plane()
    {
        // Both planes strictly positive. The origin is still a corner of the
        // extent, because that is where the axis arrows start and where the
        // lattice has to reach to show it.
        var bounds = new[]
        {
            new[] { 5.0, 100.0 }, new[] { 7.0, 40.0 },
            new[] { 10.0, 50.0 }, new[] { 8.0, 30.0 },
        };
        Assert.True(PlotterPlaneMapper.TryJobExtent([[0, 1], [2, 3]], bounds, out var job));
        Assert.Equal((0.0, 0.0, 100.0, 40.0), job);
    }

    [Fact]
    public void TryJobExtent_keeps_a_negative_plane_below_the_origin()
    {
        // Plane 0 sits entirely below the origin, plane 1 above it. The union
        // spans both and touches 0 on neither axis's outer side.
        var bounds = new[]
        {
            new[] { -100.0, -50.0 }, new[] { -40.0, -10.0 },
            new[] { 20.0, 60.0 }, new[] { 10.0, 30.0 },
        };
        Assert.True(PlotterPlaneMapper.TryJobExtent([[0, 1], [2, 3]], bounds, out var job));
        Assert.Equal((-100.0, -40.0, 60.0, 30.0), job);
    }

    /// <summary>False means NO envelope, never a fabricated one — the same
    /// contract as <c>TryWorldFootprint</c>, for the same reason: the caller
    /// has extent-derived chrome to skip, and a magic 1 mm square would put
    /// invented geometry on screen.</summary>
    [Fact]
    public void TryJobExtent_refuses_when_no_plane_can_be_read()
    {
        var bounds = new[] { new[] { 5.0, 100.0 }, new[] { 7.0, 40.0 } };
        // A single plane with one slot: neither world X nor world Y is readable
        // as a pair.
        Assert.False(PlotterPlaneMapper.TryJobExtent([[2]], bounds, out _));
        // No planes at all.
        Assert.False(PlotterPlaneMapper.TryJobExtent([], bounds, out _));
        // A null element is a caller bug the method still answers.
        Assert.False(PlotterPlaneMapper.TryJobExtent(new[] { (int[])null! }, bounds, out _));
    }

    /// <summary>Both inputs are untrusted and need not agree: a null list, a
    /// null element and a short frame must all be answered, not thrown on.</summary>
    [Fact]
    public void TryJobExtent_survives_ragged_inputs()
    {
        var bounds = new[] { new[] { 5.0, 100.0 }, new[] { 7.0, 40.0 } };
        // A null entry beside a real plane: the real one still contributes.
        Assert.True(PlotterPlaneMapper.TryJobExtent(
            new[] { (int[])null!, new[] { 0, 1 } }, bounds, out var job));
        Assert.Equal((0.0, 0.0, 100.0, 40.0), job);
        // An empty frame cannot locate any plane.
        Assert.False(PlotterPlaneMapper.TryJobExtent([[0, 1]], [], out _));
        // Absent lists. The null-forgiving operator is the idiomatic way to say
        // "a null reference reaches this parameter on purpose" to the compiler,
        // so the untrusted-input contract can be pinned without paying CS8625
        // warnings against the build's zero-warning contract.
        Assert.False(PlotterPlaneMapper.TryJobExtent(null!, bounds, out _));
        Assert.False(PlotterPlaneMapper.TryJobExtent([[0, 1]], null!, out _));
    }
}
