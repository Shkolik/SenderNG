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

/// <summary>The 2D chrome painters for the flat panes: the world XY lattice
/// face-on, the machine-origin cross, the world axis arrows, and the job
/// rectangle. Every value arrives already in world coordinates — the caller
/// ran the plane's mapping, so nothing here knows about carriage planes.</summary>
public static class PlotterFlatChrome
{
    /// <summary>The origin cross's pen. Deliberately a 2 px stroke rather than
    /// the view's 1 px <c>PlotterPens.Origin</c> — see <see cref="DrawOrigin"/>
    /// for why a 1 px stroke gets tinted by the new chrome. Private to this
    /// painter: no other call site wants it.</summary>
    private static readonly IPen OriginPen = new Pen(PlotterPens.OriginBrush, 2);

    /// <summary>The world XY grid, face-on. A grid line is a world-space
    /// constant, so it is a straight screen segment — no projection and no
    /// per-tile stepping. <paramref name="viewport"/> clips the lattice to the
    /// pane's whole drawable area, not to one plane's quadrant: see
    /// <c>PlaneCanvas.DrawSlotChrome</c> for why the quadrant is the wrong
    /// boundary.</summary>
    public static void DrawGrid(
        DrawingContext ctx, PlaneMap map,
        (double MinX, double MinY, double MaxX, double MaxY) bounds,
        double zoom, Vector pan, Rect viewport)
    {
        using (ctx.PushClip(viewport))
        {
            foreach (var line in PlotterChrome.FloorLines(
                new Point3(bounds.MinX, bounds.MinY, 0),
                new Point3(bounds.MaxX, bounds.MaxY, 0), 8))
            {
                var pen = line.Major ? PlotterChromePens.GridMajor : PlotterChromePens.GridMinor;
                ctx.DrawLine(pen, Screen(map, line.A, zoom, pan), Screen(map, line.B, zoom, pan));
            }
        }
    }

    /// <summary>The machine-origin cross, 6 px each way. The stroke is 2 px,
    /// not the view's original 1 px <c>PlotterPens.Origin</c>: G-code starts at
    /// the machine origin, so the cross sits on the job envelope's amber corner
    /// with the red tool glyph on the same point. A 1 px stroke at a fractional
    /// coordinate is never opaque — every pixel is a blend with the paint
    /// beneath — so over amber it yields #CB5448, and
    /// <c>Foam_2d_renders_toscale_tiles_and_origin_crosses</c> greyscale probe
    /// drops from 10 neutral pixels to 3 and fails. Draw order cannot fix a
    /// blend; 2 px gives the stroke full coverage, so the cross keeps painting
    /// neutral #888888 over any background. The cross is therefore fatter than
    /// it was before this task — a deliberate, reviewed trade, not an accident.
    /// Do not "restore" it to <c>PlotterPens.Origin</c>.</summary>
    public static void DrawOrigin(DrawingContext ctx, PlaneMap map, double zoom, Vector pan)
    {
        var o = Screen(map, new Point3(0, 0, 0), zoom, pan);
        ctx.DrawLine(OriginPen, new Point(o.X - 6, o.Y), new Point(o.X + 6, o.Y));
        ctx.DrawLine(OriginPen, new Point(o.X, o.Y - 6), new Point(o.X, o.Y + 6));
    }

    /// <summary>The world X and Y axis arrows, leaving the pane's world origin
    /// along each axis for a quarter of the pane's own span. X red, Y green, as
    /// in the iso painter and in Nex — the same two axes, so the same two
    /// colours, and a reader who learned the scheme in one view can read it in
    /// the other. No Z: a face-on pane has no Z direction to point.</summary>
    public static void DrawAxes(
        DrawingContext ctx, PlaneMap map,
        (double MinX, double MinY, double MaxX, double MaxY) bounds,
        double zoom, Vector pan, Rect viewport)
    {
        var o = Screen(map, new Point3(0, 0, 0), zoom, pan);
        var spanX = Math.Max(bounds.MaxX - bounds.MinX, 1e-6) * 0.25;
        var spanY = Math.Max(bounds.MaxY - bounds.MinY, 1e-6) * 0.25;

        var xTip = new Point(o.X + zoom * spanX, o.Y);
        var yTip = new Point(o.X, o.Y - zoom * spanY);

        using (ctx.PushClip(viewport))
        {
            ctx.DrawLine(PlotterChromePens.AxisX, o, xTip);
            PlotterChrome.ArrowHead(ctx, PlotterChromePens.AxisX, o, xTip);
            ctx.DrawLine(PlotterChromePens.AxisY, o, yTip);
            PlotterChrome.ArrowHead(ctx, PlotterChromePens.AxisY, o, yTip);
        }
    }

    /// <summary>The job rectangle: the plane's world bounds.</summary>
    public static void DrawEnvelope(
        DrawingContext ctx, PlaneMap map,
        (double MinX, double MinY, double MaxX, double MaxY) bounds,
        double zoom, Vector pan)
    {
        var topLeft = Screen(map, new Point3(bounds.MinX, bounds.MaxY, 0), zoom, pan);
        var topRight = Screen(map, new Point3(bounds.MaxX, bounds.MaxY, 0), zoom, pan);
        var bottomRight = Screen(map, new Point3(bounds.MaxX, bounds.MinY, 0), zoom, pan);
        var bottomLeft = Screen(map, new Point3(bounds.MinX, bounds.MinY, 0), zoom, pan);
        // The four corners as a GEOMETRY, not a Rect: the caller may hand over
        // Min/Max in either order, and Avalonia 12's DrawRectangle takes only a
        // (normalized) Rect or a RoundedRect, so a four-corner figure is the
        // orientation-agnostic form. The brush argument is null on purpose —
        // the job rectangle is a frame AROUND the toolpath, and filling it
        // would paint over the path it exists to measure.
        var rect = new StreamGeometry();
        using (var sink = rect.Open())
        {
            sink.BeginFigure(topLeft, true);
            sink.LineTo(topRight);
            sink.LineTo(bottomRight);
            sink.LineTo(bottomLeft);
            sink.EndFigure(true);
        }
        ctx.DrawGeometry(null, PlotterChromePens.Envelope, rect);
    }

    /// <summary>World point through the plane map, then to screen. The map's
    /// scale is 1 in both axes, so a world delta is the same delta in the map's
    /// own units — and the screen half is <see cref="PlotterChrome.ToScreen"/>,
    /// the transform the canvases and the iso painter share.</summary>
    private static Point Screen(PlaneMap map, Point3 world, double zoom, Vector pan) =>
        PlotterChrome.ToScreen(map.Wx(world.X), map.Wy(world.Y), zoom, pan);
}
