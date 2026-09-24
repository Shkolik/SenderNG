
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
using System.Xml;

namespace SenderNG.Core.Modules.Probing;

/// <summary>A 2D point (min/max bounds helper).</summary>
public readonly struct Vec2(double x, double y)
{
    public double X { get; } = x;
    public double Y { get; } = y;
}

/// <summary>
/// Rectangular Z-height grid over a workpiece area. Ported from ioSender's
/// <c>HeightMap</c> (originally from OpenCNCPilot, MIT) with the 3D-visual
/// parts dropped; the XML <c>.map</c> file format is preserved so maps are
/// interchangeable with ioSender.
/// </summary>
public class HeightMap
{
    private static readonly NumberFormatInfo DecimalFormat = new() { NumberDecimalSeparator = "." };

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
        var map = new HeightMap();
        using var r = XmlReader.Create(path);
        while (r.Read())
        {
            if (!r.IsStartElement())
                continue;

            switch (r.Name)
            {
                case "heightmap":
                    map.Min = new Vec2(Parse(r["MinX"]), Parse(r["MinY"]));
                    map.Max = new Vec2(Parse(r["MaxX"]), Parse(r["MaxY"]));
                    map.SizeX = int.Parse(r["SizeX"]!, CultureInfo.InvariantCulture);
                    map.SizeY = int.Parse(r["SizeY"]!, CultureInfo.InvariantCulture);
                    map.ZOffset = Parse(r["ZOffset"]);
                    map.Points = new double?[map.SizeX, map.SizeY];
                    break;
                case "point":
                    var x = int.Parse(r["X"]!, CultureInfo.InvariantCulture);
                    var y = int.Parse(r["Y"]!, CultureInfo.InvariantCulture);
                    var height = Parse(r.ReadInnerXml());
                    map.Points[x, y] = height;
                    map.MaxHeight = Math.Max(map.MaxHeight, height);
                    map.MinHeight = Math.Min(map.MinHeight, height);
                    break;
            }
        }
        return map;
    }

    public void Save(string path)
    {
        var settings = new XmlWriterSettings { Indent = true };
        using var w = XmlWriter.Create(path, settings);
        w.WriteStartDocument();
        w.WriteStartElement("heightmap");
        w.WriteAttributeString("MinX", Fmt(Min.X));
        w.WriteAttributeString("MinY", Fmt(Min.Y));
        w.WriteAttributeString("MaxX", Fmt(Max.X));
        w.WriteAttributeString("MaxY", Fmt(Max.Y));
        w.WriteAttributeString("SizeX", SizeX.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("SizeY", SizeY.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("ZOffset", Fmt(ZOffset));
        for (var x = 0; x < SizeX; x++)
        {
            for (var y = 0; y < SizeY; y++)
            {
                if (!Points[x, y].HasValue)
                    continue;
                w.WriteStartElement("point");
                w.WriteAttributeString("X", x.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("Y", y.ToString(CultureInfo.InvariantCulture));
                w.WriteString(Fmt(Points[x, y]!.Value));
                w.WriteEndElement();
            }
        }
        w.WriteEndElement();
    }

    private static double Parse(string? s) =>
        double.TryParse(s, NumberStyles.Float, DecimalFormat, out var v) ? v : 0d;

    private static string Fmt(double v) => v.ToString(DecimalFormat);
}
