using System.Text.Json.Serialization;

namespace SenderNG.Modules.Probing.Core;

/// <summary>Wire shape of a .hmap.json file (version 1). Points are sparse
/// [col, row, z] triples; unprobed cells are omitted.</summary>
internal sealed class HeightMapDto
{
    public int Version { get; set; } = 1;
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public int SizeX { get; set; }
    public int SizeY { get; set; }
    public double ZOffset { get; set; }
    public List<double[]>? Points { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HeightMapDto))]
internal partial class HeightMapJsonContext : JsonSerializerContext;
