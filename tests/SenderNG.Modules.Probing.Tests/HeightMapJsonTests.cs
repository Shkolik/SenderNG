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

public class HeightMapJsonTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"hm-{Guid.NewGuid():N}.hmap.json");

    [Fact]
    public void RoundTrip_preserves_dims_area_offset_and_points()
    {
        var map = new HeightMap(1, 1, new Vec2(0, 0), new Vec2(2, 2)) { ZOffset = 0.25 };
        map.AddPoint(0, 0, 1.0);
        map.AddPoint(2, 2, -0.5);

        var path = TempFile();
        try
        {
            map.Save(path);
            var loaded = HeightMap.Load(path);

            Assert.Equal(3, loaded.SizeX);
            Assert.Equal(3, loaded.SizeY);
            Assert.Equal(0.25, loaded.ZOffset);
            Assert.Equal(0.0, loaded.Min.X);
            Assert.Equal(2.0, loaded.Max.Y);
            Assert.Equal(1.0, loaded.Points[0, 0]);
            Assert.Equal(-0.5, loaded.Points[2, 2]);
            Assert.Null(loaded.Points[1, 1]); // unprobed omitted
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_writes_json_not_xml()
    {
        var map = new HeightMap(1, 1, new Vec2(0, 0), new Vec2(1, 1));
        map.AddPoint(0, 0, 1);
        var path = TempFile();
        try
        {
            map.Save(path);
            var text = File.ReadAllText(path);
            Assert.StartsWith("{", text.TrimStart());
            Assert.Contains("\"sizeX\"", text);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{}")]                                   // no points
    [InlineData("{\"version\":1,\"minX\":0,\"minY\":0,\"maxX\":0,\"maxY\":0,\"sizeX\":3,\"sizeY\":3,\"zOffset\":0,\"points\":[[0,0,1]]}")] // zero area
    [InlineData("{\"version\":1,\"minX\":0,\"minY\":0,\"maxX\":2,\"maxY\":2,\"sizeX\":1,\"sizeY\":3,\"zOffset\":0,\"points\":[[0,0,1]]}")] // sizeX<2
    [InlineData("{\"version\":1,\"minX\":0,\"minY\":0,\"maxX\":2,\"maxY\":2,\"sizeX\":3,\"sizeY\":3,\"zOffset\":0,\"points\":[[9,0,1]]}")] // out of range
    [InlineData("{\"version\":1,\"minX\":0,\"minY\":0,\"maxX\":2,\"maxY\":2,\"sizeX\":3,\"sizeY\":3,\"zOffset\":0,\"points\":[[0,0]]}")]   // bad tuple
    [InlineData("{ not json")]                            // syntactically malformed
    public void Invalid_files_throw(string json)
    {
        var path = TempFile();
        try
        {
            File.WriteAllText(path, json);
            Assert.Throws<InvalidDataException>(() => HeightMap.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RoundTrip_is_culture_invariant()
    {
        var prior = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            var map = new HeightMap(1, 1, new Vec2(0, 0), new Vec2(1, 1));
            map.AddPoint(1, 1, 0.5);
            var path = TempFile();
            try
            {
                map.Save(path);
                Assert.Contains("0.5", File.ReadAllText(path));
                Assert.Equal(0.5, HeightMap.Load(path).Points[1, 1]);
            }
            finally { File.Delete(path); }
        }
        finally { Thread.CurrentThread.CurrentCulture = prior; }
    }
}
