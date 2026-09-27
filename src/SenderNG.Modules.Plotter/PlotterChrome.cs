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

using Avalonia;
using Avalonia.Media;
using Point3 = CNC.Core.Point3D;

namespace SenderNG.Modules.Plotter;

/// <summary>Mode-agnostic chrome geometry, shared by the flat and the iso
/// painters. Every helper here is pure arithmetic over plain values — the pens
/// live in <see cref="PlotterChromePens"/> so that touching a helper never
/// runs a static initializer that needs an Avalonia runtime.</summary>
public static class PlotterChrome
{
    /// <summary>Snaps a raw step to 1, 2, 5 or 10 times a power of ten.</summary>
    public static double NiceStep(double raw)
    {
        if (raw <= 0)
            return 1;
        var pow = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var norm = raw / pow;
        return norm switch
        {
            < 1.5 => pow,
            < 3.5 => 2 * pow,
            < 7.5 => 5 * pow,
            _ => 10 * pow,
        };
    }

    /// <summary>Tick positions covering <c>[min,max]</c> at a nice step, aiming
    /// for roughly <paramref name="target"/> divisions. The returned count
    /// never runs past <paramref name="max"/>.</summary>
    public static (double Start, double Step, int Count) AxisTicks(double min, double max, int target)
    {
        if (target < 2 || !(max > min) || double.IsNaN(min) || double.IsNaN(max))
            return (min, 1, 0);
        var step = NiceStep((max - min) / target);
        var start = Math.Ceiling(min / step) * step;
        var count = (int)Math.Floor((max - start) / step) + 1;
        return (start, step, Math.Max(count, 0));
    }

    /// <summary>The twelve edges of the axis-aligned box between two corners.</summary>
    public static (Point3 A, Point3 B)[] BoxEdges(Point3 a, Point3 b)
    {
        var lo = new Point3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        var hi = new Point3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
        Point3 P(int x, int y, int z) => new(
            x == 0 ? lo.X : hi.X, y == 0 ? lo.Y : hi.Y, z == 0 ? lo.Z : hi.Z);
        return
        [
            (P(0, 0, 0), P(1, 0, 0)), (P(1, 0, 0), P(1, 1, 0)),
            (P(1, 1, 0), P(0, 1, 0)), (P(0, 1, 0), P(0, 0, 0)),
            (P(0, 0, 1), P(1, 0, 1)), (P(1, 0, 1), P(1, 1, 1)),
            (P(1, 1, 1), P(0, 1, 1)), (P(0, 1, 1), P(0, 0, 1)),
            (P(0, 0, 0), P(0, 0, 1)), (P(1, 0, 0), P(1, 0, 1)),
            (P(1, 1, 0), P(1, 1, 1)), (P(0, 1, 0), P(0, 1, 1)),
        ];
    }

    /// <summary>The XY floor lattice: X-direction lines then Y-direction lines,
    /// with every fifth line of each family flagged major. Per-family promotion
    /// avoids a seam where a combined counter would break the rhythm at the
    /// join between the two families.</summary>
    public static (Point3 A, Point3 B, bool Major)[] FloorLines(Point3 min, Point3 max, int target)
    {
        if (!(max.X > min.X) || !(max.Y > min.Y))
            return [];

        var lines = new List<(Point3 A, Point3 B, bool Major)>();
        var (xStart, xStep, xCount) = AxisTicks(min.X, max.X, target);
        for (var i = 0; i < xCount; i++)
        {
            var x = xStart + xStep * i;
            lines.Add((new Point3(x, min.Y, min.Z), new Point3(x, max.Y, min.Z), i % 5 == 0));
        }
        var (yStart, yStep, yCount) = AxisTicks(min.Y, max.Y, target);
        for (var i = 0; i < yCount; i++)
        {
            var y = yStart + yStep * i;
            lines.Add((new Point3(min.X, y, min.Z), new Point3(max.X, y, min.Z), i % 5 == 0));
        }
        return lines.ToArray();
    }

    /// <summary>A screen-space arrowhead at <paramref name="tip"/>, opening
    /// back along the shaft from <paramref name="from"/>.</summary>
    public static void ArrowHead(
        DrawingContext ctx, IPen pen, Point from, Point tip, double size = 6)
    {
        var dx = tip.X - from.X;
        var dy = tip.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6)
            return;
        var ux = dx / len;
        var uy = dy / len;
        // Perpendicular unit vector for the two barbs.
        var px = -uy * size;
        var py = ux * size;
        var bx = tip.X - ux * size;
        var by = tip.Y - uy * size;
        ctx.DrawLine(pen, new Point(bx + px, by + py), tip);
        ctx.DrawLine(pen, new Point(bx - px, by - py), tip);
    }
}

/// <summary>The fixed chrome palette. Every color sits outside the grey band
/// <c>PlotterViewHeadlessTests.IsPathGrey</c> matches, so chrome pixels are
/// never mistaken for toolpath pixels.</summary>
public static class PlotterChromePens
{
    /// <summary>#e0e0e0 — above the grey band, faint.</summary>
    public static readonly IPen GridMinor =
        new Pen(new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0)));

    /// <summary>#c0c0c0 — above the grey band, readable.</summary>
    public static readonly IPen GridMajor =
        new Pen(new SolidColorBrush(Color.FromRgb(0xc0, 0xc0, 0xc0)));

    /// <summary>#2f6fb0 — blue: <c>|r-g| = 64</c> keeps it out of the grey band,
    /// and <c>b = 176</c> keeps it out of the loft-wire band.</summary>
    public static readonly IPen Axis =
        new Pen(new SolidColorBrush(Color.FromRgb(0x2f, 0x6f, 0xb0)), 1.5);

    /// <summary>#c07a20 — amber: out of both bands.</summary>
    public static readonly IPen Envelope =
        new Pen(new SolidColorBrush(Color.FromRgb(0xc0, 0x7a, 0x20)), 1.5)
        { LineCap = PenLineCap.Round };
}
