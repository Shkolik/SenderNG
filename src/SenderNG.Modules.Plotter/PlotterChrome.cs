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

/// <summary>One plane's placement in the shared world composition. ONE scale
/// (Sx = Sy = 1 → true machine proportions). In the flat view the tiles are
/// STACKED — plane 0 in the top half, plane 1 in the bottom, sharing one
/// column — so the whole composition pans and zooms as one canvas; the iso
/// view keeps its own diagonal placement in its own canvas.</summary>
public readonly record struct PlaneMap(
    double MinX, double SpanX, double MinY, double SpanY,
    double BiasX, double BiasY, double Sx, double Sy, double Ox, double Oy)
{
    public double Wx(double rawX) => Ox + (rawX - MinX + BiasX) * Sx;
    public double Wy(double rawY) => Oy + (rawY - MinY + BiasY) * Sy;
}

/// <summary>The four view toggles, in one value. Defaults are all on, so a
/// canvas with no settings view model attached still draws the full chrome.
/// Public because <c>PlotterSettingsViewModel.Flags</c> hands it across the
/// assembly boundary to <c>SenderNG.Core.Tests</c>.</summary>
public readonly record struct ChromeFlags(bool Grid, bool Origin, bool Envelope, bool RunState)
{
    public static ChromeFlags Default => new(true, true, true, true);
}

/// <summary>Mode-agnostic chrome geometry, shared by the flat and the iso
/// painters. Every helper here is pure arithmetic over plain values — the pens
/// live in <see cref="PlotterChromePens"/> so that touching a helper never
/// runs a static initializer that needs an Avalonia runtime.</summary>
public static class PlotterChrome
{
    /// <summary>The ONE world-to-screen transform the whole chrome shares:
    /// scale by <paramref name="zoom"/>, offset by <paramref name="pan"/>, and
    /// flip Y (world Y up is screen Y down). <c>PlotCanvas.WorldToScreen</c>
    /// and both painters' own projection helpers call this, so the flip and the
    /// pan sign are written down once — a private copy per painter is exactly
    /// how they drifted apart before.</summary>
    public static Point ToScreen(double x, double y, double zoom, Vector pan) =>
        new(zoom * x + pan.X, -zoom * y + pan.Y);

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
        // The IsNaN terms cannot decide this branch: `max > min` is already
        // false whenever either bound is NaN, so they are unreachable defense
        // kept as written rather than trimmed. If the guard below is ever
        // rewritten to use a NaN-true comparison, they are what still hold
        // the line.
        if (target < 2 || !(max > min) || double.IsNaN(min) || double.IsNaN(max))
            return (min, 1, 0);
        var step = NiceStep((max - min) / target);
        var start = Math.Ceiling(min / step) * step;
        var count = (int)Math.Floor((max - start) / step) + 1;
        return (start, step, Math.Max(count, 0));
    }

    /// <summary>Tick positions covering <c>[min,max]</c> — the same nice-step
    /// grid <see cref="AxisTicks"/> returns, PLUS the band's own two ends.
    /// <para>AxisTicks starts on the first multiple of the step at or above min
    /// and stops at the last one at or below max, so both ends are ticks only
    /// when the step happens to DIVIDE the band: on a 108 mm span with a 10 mm
    /// step the last tick is 100 and the far edge simply is not one. A floor
    /// built from that grid is OPEN at its boundary — the mill pane lost all
    /// four edges of the job envelope this way, and the foam loft's wire floor
    /// two of its four. Appending the two ends, and collapsing them when the
    /// step already landed there, is what closes the floor; see
    /// <see cref="FloorLines"/> and the foam loft's
    /// <c>PlotterView.IsoCanvas.DrawWireFloor</c>.
    /// <para>Widening <see cref="AxisTicks"/> itself instead was ruled out: it
    /// is the general nice-tick helper, its own unit tests pin the
    /// start/step/count contract, and the floor families' major promotion is
    /// read off the run of positions this returns.</para></summary>
    public static IReadOnlyList<double> AxisTicksWithEdges(double min, double max, int target)
    {
        var (start, step, count) = AxisTicks(min, max, target);
        var values = new List<double>(count + 2);
        for (var i = 0; i < count; i++)
        {
            var v = start + step * i;
            if (v < min || v > max)
                continue;
            values.Add(v);
        }
        if (values.Count == 0)
        {
            values.Add(min);
            if (max > min)
                values.Add(max);
            return values;
        }
        if (values[0] > min + step * 1e-9)
            values.Insert(0, min);
        if (values[^1] < max - step * 1e-9)
            values.Add(max);
        return values;
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
    /// join between the two families.
    /// <para>Each family comes from <see cref="AxisTicksWithEdges"/>, so the
    /// lattice CLOSES on the footprint: the outermost line of each family is the
    /// footprint's own edge, which is what makes the floor's four boundary edges
    /// part of the floor instead of gaps around it.</para></summary>
    public static (Point3 A, Point3 B, bool Major)[] FloorLines(Point3 min, Point3 max, int target)
    {
        if (!(max.X > min.X) || !(max.Y > min.Y))
            return [];

        var lines = new List<(Point3 A, Point3 B, bool Major)>();
        var xs = AxisTicksWithEdges(min.X, max.X, target);
        for (var i = 0; i < xs.Count; i++)
        {
            lines.Add((new Point3(xs[i], min.Y, min.Z), new Point3(xs[i], max.Y, min.Z), i % 5 == 0));
        }
        var ys = AxisTicksWithEdges(min.Y, max.Y, target);
        for (var i = 0; i < ys.Count; i++)
        {
            lines.Add((new Point3(min.X, ys[i], min.Z), new Point3(max.X, ys[i], min.Z), i % 5 == 0));
        }
        return lines.ToArray();
    }

    /// <summary>A screen-space arrowhead at <paramref name="tip"/>, opening
    /// back along the shaft from <paramref name="from"/>. The one member with
    /// no unit test: it can only be observed through a real
    /// <see cref="DrawingContext"/>, so a test would need an Avalonia runtime
    /// rather than the plain arithmetic every other helper here is made of.</summary>
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

    /// <summary>#d93333 — X, red. Matches <c>NexPlotScene.AxisColor</c>'s X.</summary>
    public static readonly IPen AxisX =
        new Pen(new SolidColorBrush(Color.FromRgb(0xd9, 0x33, 0x33)), 1.5);

    /// <summary>#33cc33 — Y, green. Matches <c>NexPlotScene.AxisColor</c>'s Y.</summary>
    public static readonly IPen AxisY =
        new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0xcc, 0x33)), 1.5);

    /// <summary>#3359e6 — Z, blue. Matches <c>NexPlotScene.AxisColor</c>'s Z.</summary>
    public static readonly IPen AxisZ =
        new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x59, 0xe6)), 1.5);

    /// <summary>Per world axis, indexed 0 = X, 1 = Y, 2 = Z — the order
    /// <c>PlotterIsoChrome.DrawAxes</c> walks its tips in. One array so the
    /// painter cannot pair a tip with the wrong axis by reordering one list and
    /// not the other.</summary>
    public static readonly IPen[] AxisByIndex = [AxisX, AxisY, AxisZ];

    // Why these three are safe in the bands the headless detectors use, measured
    // rather than assumed (all deltas are on the 0-255 channels):
    //   IsPathGrey  |r-g| <= 14 && |g-b| <= 14 && r in [95,175]
    //     X |r-g| 166, Y 153, Z 38            — all clear, and Z's r = 51 is
    //     outside the value window regardless
    //   IsWireBlend  b > r + 8 && b > 190 && g >= 180 && |r-g| <= 16
    //     X b = 51, Y b = 51                   — far below 190
    //     Z b = 230 passes the first two but g = 89 fails the third
    // Y is the tight one, and it is deliberately LEFT matching IsDoneGreen
    // (g > 100 && r < 90 && b < 110). That detector is a run-state probe, and a
    // permanently-drawn green arrow is a false positive in it; the fix is in the
    // probe's call sites, which now compare green WITH and WITHOUT the iso
    // arrows, not a colour change that would make the Y arrow something other
    // than the green Nex draws. See PlotterViewHeadlessTests.

    /// <summary>#00b2e6 at 50% alpha — teal, Nex's <c>JobEnvelopeColor</c>
    /// (0, 0.7, 0.9), which is drawn OPAQUE there. The alpha is a deliberate
    /// departure from Nex parity and not an oversight: the envelope is a frame
    /// measured AROUND the toolpath (see <c>PlotterFlatChrome.DrawEnvelope</c>,
    /// which passes a null brush on purpose), so half-transparent keeps it
    /// legible without competing with the path it exists to measure. Nex can
    /// afford full opacity because it draws last over its own wireframe; this
    /// plotter's envelope is also the gate for the foam tile frames and the iso
    /// box, so the same stroke is a carrier for three subjects and a heavier one
    /// would flatten the hierarchy.
    ///
    /// <para>Out of both detector bands, as its neighbours are: teal's
    /// separation between r and g is 178 at full opacity, and the headless
    /// probes' grey band is <c>|r - g| &lt;= 14</c>, so no amount of blending can
    /// pull it in. The test-side detector exploits that same ratio for the
    /// inverse reason — see <c>IsEnvelopeTeal</c>.</para></summary>
    public static readonly IPen Envelope =
        new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0xb2, 0xe6)), 1.5)
        { LineCap = PenLineCap.Round };
}
