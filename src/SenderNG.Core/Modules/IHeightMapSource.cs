namespace SenderNG.Core.Modules;

/// <summary>
/// A height map as probed by a tool like the probing module: a rectangular Z
/// grid over an (x, y) workpiece area. Rows run along +Y (starting at
/// <see cref="OriginY"/>), columns along +X (starting at <see cref="OriginX"/>);
/// <see cref="CellX"/>/<see cref="CellY"/> are the sample spacing.
/// Samples are row-major: <c>Z[row * Columns + col]</c>. A
/// <see cref="double.NaN"/> in Z marks a grid cell the probe never reached.
/// </summary>
public sealed record HeightMapLayer(
    string Id,
    double OriginX, double OriginY,
    double CellX, double CellY,
    int Columns, int Rows,
    IReadOnlyList<double> Z)
{
    /// <summary>Sample at grid column <paramref name="col"/>, row <paramref name="row"/> (row-major).</summary>
    public double this[int row, int col] => Z[row * Columns + col];
}

/// <summary>
/// Emits the probing feature's latest height map. Implemented by the probing
/// module and consumed by third-party visualizers (e.g. a 3D renderer plugin).
/// A null emission means the map was cleared — a new probe session, a profile
/// switch, or a program change.
/// </summary>
public interface IHeightMapSource
{
    IObservable<HeightMapLayer?> Snapshot { get; }
}

/// <summary>
/// Presence marker: a registered height-map viewer (3D or otherwise). The
/// probing module's "Preview in 3D" action offers itself only while such a
/// renderer is available. Implemented by the renderer's own module.
/// </summary>
public interface IHeightMap3DRenderer { }