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

using SenderNG.Core.Modules;

namespace SenderNG.Core.Tests;

public class HeightMapLayerTests
{
    [Fact]
    public void Indexer_reads_samples_row_major()
    {
        // 2 columns (X) x 3 rows (Y): values ordered
        // [row0:col0, row0:col1, row1:col0, ...].
        var layer = new HeightMapLayer("m", 0d, 0d, 1d, 1d, 2, 3,
            System.Linq.Enumerable.Range(0, 6).Select(i => (double)i).ToList());

        Assert.Equal(0d, layer[0, 0]);
        Assert.Equal(1d, layer[0, 1]);
        Assert.Equal(2d, layer[1, 0]);
        Assert.Equal(5d, layer[2, 1]);
    }

    [Fact]
    public void Nan_marks_unprobed_cells()
    {
        var layer = new HeightMapLayer("m", 0d, 0d, 1d, 1d, 2, 1,
            [double.NaN, 3d]);

        Assert.True(double.IsNaN(layer[0, 0]));
        Assert.Equal(3d, layer[0, 1]);
    }
}
