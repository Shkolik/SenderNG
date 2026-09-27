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
    }
}
