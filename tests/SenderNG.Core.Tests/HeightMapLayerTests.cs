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