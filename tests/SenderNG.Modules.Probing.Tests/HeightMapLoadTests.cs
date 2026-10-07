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

using SenderNG.Modules.Probing.Core;

namespace SenderNG.Modules.Probing.Tests;

/// <summary>
/// <c>HeightMap.Load</c> builds through the private parameterless constructor,
/// so it never sees the public constructor's area/grid guards — and
/// <c>InterpolateZ</c> used <c>MaxHeight</c> (initialised to
/// <c>double.MinValue</c>) as its out-of-bounds "no data" answer, turning an
/// empty <c>.map</c> into a ~-1.8e308 Z word downstream.
/// </summary>
public class HeightMapLoadTests : IDisposable
{
    private readonly string _dir;

    public HeightMapLoadTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WriteMap(string body)
    {
        var path = Path.Combine(_dir, "heightmap.map");
        File.WriteAllText(path, body);
        return path;
    }

    private static string Map(string attributes, string points = "")
        => $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<heightmap {attributes}>{points}</heightmap>";

    private const string Valid2x2 = "MinX=\"0\" MinY=\"0\" MaxX=\"10\" MaxY=\"10\" SizeX=\"2\" SizeY=\"2\" ZOffset=\"0\"";

    [Fact]
    public void Load_rejects_a_map_with_no_points()
    {
        var path = WriteMap(Map(Valid2x2));

        Assert.Throws<InvalidDataException>(() => HeightMap.Load(path));
    }

    [Fact]
    public void Load_rejects_a_grid_smaller_than_two_by_two()
    {
        var path = WriteMap(Map("MinX=\"0\" MinY=\"0\" MaxX=\"10\" MaxY=\"10\" SizeX=\"1\" SizeY=\"1\" ZOffset=\"0\"",
            "<point X=\"0\" Y=\"0\">1.5</point>"));

        Assert.Throws<InvalidDataException>(() => HeightMap.Load(path));
    }

    [Fact]
    public void Load_rejects_a_map_with_no_area()
    {
        var path = WriteMap(Map("MinX=\"5\" MinY=\"5\" MaxX=\"5\" MaxY=\"5\" SizeX=\"2\" SizeY=\"2\" ZOffset=\"0\"",
            "<point X=\"0\" Y=\"0\">1.5</point>"));

        Assert.Throws<InvalidDataException>(() => HeightMap.Load(path));
    }

    [Fact]
    public void InterpolateZ_returns_the_offset_when_nothing_has_been_probed()
    {
        var map = new HeightMap(1, 1, new Vec2(0, 0), new Vec2(10, 10));

        // Out of bounds on an unprobed map: the old code answered with the
        // double.MinValue sentinel, i.e. a -1.7976931348623157E308 coordinate.
        var z = map.InterpolateZ(50, 50);

        Assert.Equal(0d, z);
        Assert.True(double.IsFinite(z), $"InterpolateZ returned {z} on a map with no probed points.");
    }

    [Fact]
    public void InterpolateZ_outside_a_probed_map_still_answers_with_the_max_height()
    {
        // Guards the existing (unchanged) out-of-bounds behaviour: the fix must
        // only replace the "never probed" sentinel, not real surfaces.
        var map = new HeightMap(10, 10, new Vec2(0, 0), new Vec2(10, 10));
        map.AddPoint(0, 0, 3);
        map.AddPoint(0, 1, 7);

        Assert.Equal(7d, map.InterpolateZ(50, 50));
    }

    [Fact]
    public void Load_rejects_a_point_outside_the_declared_grid()
    {
        var path = WriteMap(Map(Valid2x2,
            "<point X=\"0\" Y=\"0\">1.5</point><point X=\"7\" Y=\"0\">2.5</point>"));

        Assert.Throws<InvalidDataException>(() => HeightMap.Load(path));
    }

    [Fact]
    public void Load_rejects_a_file_without_a_heightmap_element()
    {
        var path = WriteMap("<notamap />");

        Assert.Throws<InvalidDataException>(() => HeightMap.Load(path));
    }

    [Fact]
    public void Load_reads_a_complete_map()
    {
        // Deliberately minified (no whitespace between points): ReadInnerXml()
        // used to leave the reader past </point>, so the loop's Read() skipped
        // the following node and every second point was silently dropped.
        var path = WriteMap(Map(Valid2x2,
            "<point X=\"0\" Y=\"0\">1.5</point><point X=\"0\" Y=\"1\">2.5</point>"
            + "<point X=\"1\" Y=\"0\">3.5</point><point X=\"1\" Y=\"1\">4.5</point>"));

        var map = HeightMap.Load(path);

        Assert.Equal(2, map.SizeX);
        Assert.Equal(2, map.SizeY);
        Assert.Equal(4.5, map.MaxHeight);
        Assert.Equal(1.5, map.MinHeight);
        for (var x = 0; x < map.SizeX; x++)
            for (var y = 0; y < map.SizeY; y++)
                Assert.True(map.Points[x, y].HasValue, $"Point ({x},{y}) was dropped on load.");
        Assert.Equal(3.5, map.InterpolateZ(10, 0));
    }

    [Fact]
    public void Load_survives_a_save_and_reload_round_trip()
    {
        var original = new HeightMap(10, 10, new Vec2(0, 0), new Vec2(30, 20));
        for (var x = 0; x < original.SizeX; x++)
            for (var y = 0; y < original.SizeY; y++)
                original.AddPoint(x, y, x + y * 0.5);

        var path = Path.Combine(_dir, "roundtrip.map");
        original.Save(path);
        var reloaded = HeightMap.Load(path);

        Assert.Equal(original.SizeX, reloaded.SizeX);
        Assert.Equal(original.SizeY, reloaded.SizeY);
        Assert.Equal(original.MaxHeight, reloaded.MaxHeight);
        Assert.Equal(original.MinHeight, reloaded.MinHeight);
        for (var x = 0; x < original.SizeX; x++)
            for (var y = 0; y < original.SizeY; y++)
                Assert.Equal(original.Points[x, y], reloaded.Points[x, y]);
    }
}
