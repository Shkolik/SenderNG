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

using Point3 = CNC.Core.Point3D;
using SenderNG.Modules.Plotter;

namespace SenderNG.Core.Tests;

public class PlotterChromeTests
{
    [Theory]
    [InlineData(0.9, 1.0)]
    [InlineData(1.4, 1.0)]
    [InlineData(3.0, 2.0)]
    [InlineData(6.0, 5.0)]
    [InlineData(12.0, 10.0)]
    public void NiceStep_snaps_to_1_2_5_times_a_power_of_ten(double raw, double expected)
    {
        Assert.Equal(expected, PlotterChrome.NiceStep(raw), 9);
    }

    [Fact]
    public void NiceStep_survives_a_non_positive_request()
    {
        Assert.Equal(1, PlotterChrome.NiceStep(0));
        Assert.Equal(1, PlotterChrome.NiceStep(-5));
    }

    [Fact]
    public void AxisTicks_covers_the_range_without_overshooting()
    {
        var (start, step, count) = PlotterChrome.AxisTicks(0, 100, 10);
        Assert.Equal(10, step);
        Assert.Equal(0, start, 9);
        Assert.Equal(11, count);
        Assert.True(start + step * (count - 1) <= 100 + 1e-9);
    }

    [Fact]
    public void AxisTicks_on_a_degenerate_range_is_empty()
    {
        Assert.Equal(0, PlotterChrome.AxisTicks(5, 5, 10).Count);
        Assert.Equal(0, PlotterChrome.AxisTicks(5, 1, 10).Count);
        Assert.Equal(0, PlotterChrome.AxisTicks(0, 100, 1).Count);
    }

    [Fact]
    public void AxisTicks_of_a_negative_range_starts_on_its_nearest_multiple()
    {
        var (start, step, _) = PlotterChrome.AxisTicks(-37, 12, 10);
        // A multiple of the step it snapped to (5 here), not of a literal 10.
        Assert.Equal(0, start % step, 9);
        Assert.True(start >= -37 - 1e-9);
    }

    [Fact]
    public void BoxEdges_returns_the_twelve_edges_of_a_box()
    {
        var edges = PlotterChrome.BoxEdges(new Point3(0, 0, 0), new Point3(2, 3, 4));
        Assert.Equal(12, edges.Length);
        Assert.Equal(12, edges.Select(e => (e.A, e.B)).Distinct().Count());
        Assert.All(edges, e =>
        {
            var differs = (e.A.X != e.B.X ? 1 : 0) + (e.A.Y != e.B.Y ? 1 : 0) + (e.A.Z != e.B.Z ? 1 : 0);
            Assert.Equal(1, differs);
        });
    }

    [Fact]
    public void FloorLines_walks_both_families_inside_the_footprint()
    {
        var lines = PlotterChrome.FloorLines(new Point3(-10, -10, 0), new Point3(10, 10, 0), 5);
        Assert.NotEmpty(lines);
        Assert.All(lines, l =>
        {
            var alongX = l.A.Y == l.B.Y;
            Assert.True(alongX || l.A.X == l.B.X);
            Assert.InRange(Math.Min(l.A.X, l.B.X), -10 - 1e-9, 10 + 1e-9);
            Assert.InRange(Math.Min(l.A.Y, l.B.Y), -10 - 1e-9, 10 + 1e-9);
        });
    }

    [Fact]
    public void FloorLines_promote_every_fifth_line_of_each_family()
    {
        var lines = PlotterChrome.FloorLines(new Point3(0, 0, 0), new Point3(100, 100, 0), 10);
        var byFamily = lines
            .Select(l => (Family: l.A.Y == l.B.Y ? 'h' : 'v', l.Major))
            .ToList();
        Assert.Equal(2, byFamily.Select(f => f.Family).Distinct().Count());
        foreach (var family in new[] { 'h', 'v' })
        {
            var flags = byFamily.Where(f => f.Family == family).Select(f => f.Major).ToList();
            // Index 0 of each family is major, then every fifth after it.
            Assert.True(flags[0], $"the first line of the {family} family is major");
            Assert.Equal(flags.Where(m => m).Count(), (flags.Count + 4) / 5);
        }
    }

    [Fact]
    public void A_zero_extent_floor_draws_nothing()
    {
        Assert.Empty(PlotterChrome.FloorLines(new Point3(0, 0, 0), new Point3(0, 0, 0), 5));
    }
}
