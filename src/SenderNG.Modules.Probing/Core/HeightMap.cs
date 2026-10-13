
/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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

using System.Globalization;
using System.IO;
using System.Text.Json;

namespace SenderNG.Modules.Probing.Core;

/// <summary>A 2D point (min/max bounds helper).</summary>
public readonly struct Vec2(double x, double y)
{
    public double X { get; } = x;
    public double Y { get; } = y;
}

/// <summary>
/// Rectangular Z-height grid over a workpiece area. Ported from ioSender's
/// <c>HeightMap</c> (originally from OpenCNCPilot, MIT) with the 3D-visual
/// parts dropped; persisted as sparse JSON (<c>.hmap.json</c>).
/// </summary>
public class HeightMap
{
    public double?[,] Points { get; private set; } = new double?[0, 0];
    public int SizeX { get; private set; }
    public int SizeY { get; private set; }

    public int TotalPoints => SizeX * SizeY;

    public Vec2 Min { get; private set; }
    public Vec2 Max { get; private set; }

    public double MinHeight { get; private set; } = double.MaxValue;
    public double MaxHeight { get; private set; } = double.MinValue;
    public double ZOffset { get; set; }

    public double GridX => (Max.X - Min.X) / (SizeX - 1);
    public double GridY => (Max.Y - Min.Y) / (SizeY - 1);

    public HeightMap(double gridSizeX, double gridSizeY, Vec2 min, Vec2 max)
    {
        if (min.X == max.X || min.Y == max.Y)
            throw new ArgumentException("Height map area must have non-zero width and height.");

        var pointsX = (int)Math.Ceiling((max.X - min.X) / gridSizeX) + 1;
        var pointsY = (int)Math.Ceiling((max.Y - min.Y) / gridSizeY) + 1;

        if (pointsX < 2 || pointsY < 2)
            throw new ArgumentException("Height map grid is too small (minimum 2x2 points).");

        if (max.X < min.X)
            (min, max) = (new Vec2(max.X, min.Y), new Vec2(min.X, max.Y));
        if (max.Y < min.Y)
            (min, max) = (new Vec2(min.X, max.Y), new Vec2(max.X, min.Y));

        Min = min;
        Max = max;
        SizeX = pointsX;
        SizeY = pointsY;
        Points = new double?[SizeX, SizeY];
    }

    private HeightMap() { }

    public void AddPoint(int x, int y, double height)
    {
        Points[x, y] = height;
        MaxHeight = Math.Max(MaxHeight, height);
        MinHeight = Math.Min(MinHeight, height);
    }

    /// <summary>Bilinear interpolation of the probed surface at (x, y), plus Z offset.</summary>
    public double InterpolateZ(double x, double y)
    {
        // Nothing has been probed: MaxHeight is still its double.MinValue
        // initialiser, and answering with that would emit ~-1.8e308 as a Z
        // word. Degrade to the flat offset plane instead, as the unprobed-cell
        // paths below already do.
        if (MaxHeight == double.MinValue)
            return ZOffset;

        if (x > Max.X || x < Min.X || y > Max.Y || y < Min.Y)
            return MaxHeight;

        x = (x - Min.X) / GridX;
        y = (y - Min.Y) / GridY;

        var iLX = (int)Math.Floor(x);
        var iLY = (int)Math.Floor(y);
        var iHX = (int)Math.Ceiling(x);
        var iHY = (int)Math.Ceiling(y);

        var fX = x - iLX;
        var fY = y - iLY;

        var v00 = Points[iLX, iLY];
        var v10 = Points[iHX, iLY];
        var v01 = Points[iLX, iHY];
        var v11 = Points[iHX, iHY];

        // Unprobed cells (partial maps) degrade gracefully: bilinear when all
        // four corners are known, else along the x row, else the single value.
        if (v00.HasValue && v10.HasValue && v01.HasValue && v11.HasValue)
        {
            var linUpper = v11.Value * fX + v01.Value * (1 - fX);
            var linLower = v10.Value * fX + v00.Value * (1 - fX);
            return linUpper * fY + linLower * (1 - fY) + ZOffset;
        }
        if (v10.HasValue && v00.HasValue)
            return v10.Value * fX + v00.Value * (1 - fX) + ZOffset;
        if (v10.HasValue)
            return v10.Value + ZOffset;
        if (v00.HasValue)
            return v00.Value + ZOffset;
        return ZOffset;
    }

    /// <summary>Physical (X, Y) coordinates of grid cell (x, y).</summary>
    public Vec2 GetCoordinates(int x, int y) =>
        new(x * (Max.X - Min.X) / (SizeX - 1) + Min.X, y * (Max.Y - Min.Y) / (SizeY - 1) + Min.Y);

    public static HeightMap Load(string path)
    {
        var dto = JsonSerializer.Deserialize(File.ReadAllText(path), HeightMapJsonContext.Default.HeightMapDto)
            ?? throw new InvalidDataException("Height map file is empty or invalid.");

        if (dto.SizeX < 2 || dto.SizeY < 2)
            throw new InvalidDataException($"Height map grid is too small (minimum 2x2 points): {dto.SizeX}x{dto.SizeY}.");
        if (dto.MinX == dto.MaxX || dto.MinY == dto.MaxY)
            throw new InvalidDataException("Height map area must have non-zero width and height.");
        if (dto.Points is null || dto.Points.Count == 0)
            throw new InvalidDataException("Height map does not contain any probed points.");

        // The public ctor derives the grid dimensions from a cell size; the file
        // stores the dimensions directly, so build via the private parameterless
        // ctor and set the fields (all accessible from within HeightMap).
        var map = new HeightMap();
        map.Min = new Vec2(dto.MinX, dto.MinY);
        map.Max = new Vec2(dto.MaxX, dto.MaxY);
        map.SizeX = dto.SizeX;
        map.SizeY = dto.SizeY;
        map.ZOffset = dto.ZOffset;
        map.Points = new double?[dto.SizeX, dto.SizeY];

        foreach (var p in dto.Points)
        {
            if (p is null || p.Length != 3)
                throw new InvalidDataException("Height map point tuple must be [col, row, z].");
            var x = (int)p[0];
            var y = (int)p[1];
            if (x < 0 || x >= map.SizeX || y < 0 || y >= map.SizeY)
                throw new InvalidDataException($"Height map point ({x},{y}) is outside the {map.SizeX}x{map.SizeY} grid.");
            map.AddPoint(x, y, p[2]);
        }

        return map;
    }

    public void Save(string path)
    {
        var dto = new HeightMapDto
        {
            Version = 1,
            MinX = Min.X, MinY = Min.Y, MaxX = Max.X, MaxY = Max.Y,
            SizeX = SizeX, SizeY = SizeY, ZOffset = ZOffset,
            Points = new List<double[]>(),
        };
        for (var x = 0; x < SizeX; x++)
            for (var y = 0; y < SizeY; y++)
                if (Points[x, y].HasValue)
                    dto.Points!.Add([x, y, Points[x, y]!.Value]);

        File.WriteAllText(path, JsonSerializer.Serialize(dto, HeightMapJsonContext.Default.HeightMapDto));
    }
}
