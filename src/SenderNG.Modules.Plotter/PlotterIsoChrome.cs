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

/// <summary>The 3D chrome painters for the iso view: the world XY floor lattice
/// under the projection, the machine-origin cross, the three world axis arrows
/// and the job box. World coordinates in, projected screen lines out.
/// <para>One transform only. Every point these painters draw goes through
/// <see cref="Projected"/>, which projects and then hands off to
/// <see cref="PlotterChrome.ToScreen"/> — the same helper
/// <c>PlotCanvas.WorldToScreen</c> calls, so the zoom, pan and Y flip are
/// written down once in the whole chrome instead of once per painter.</para></summary>
public static class PlotterIsoChrome
{
    /// <summary>The world XY floor, run through the iso projection. A world grid
    /// line is straight in 3D, so it is one projected segment — which is the
    /// whole difference from the flat painter: the lattice recedes with the
    /// zoom because its geometry is world, not screen.
    /// <para><paramref name="floorZ"/> is the work envelope's FLOOR, not world
    /// Z = 0, and not the same value for every job: it is the job box's own
    /// <c>MinZ</c>. Pinned to 0 the lattice floats above the box for any program
    /// that cuts below the origin, which is most foam work — the box bottom is
    /// the surface the tool actually works on, so that is where the grid belongs.
    /// parameter, so the two cannot drift apart when the envelope toggle is off.
    /// <para>The painter sets NO clip of its own: an export canvas loses every
    /// draw AFTER a <c>PushClip</c> that precedes the toolpath's
    /// <c>PushTransform</c>, so the grid must stay clip-free and let the target
    /// surface trim the overhang (the control's render clip on screen, the
    /// bitmap's own bounds in the PNG export). A viewport that tightens the
    /// lattice to anything less than the surface is wrong anyway: see
    /// <c>PlaneCanvas</c> for why one slot's quadrant is the wrong
    /// boundary.</para></summary>
    public static void DrawGrid(
        DrawingContext ctx,
        (double MinX, double MinY, double MaxX, double MaxY) footprint,
        double floorZ, double zoom, Vector pan)
    {
        foreach (var line in PlotterChrome.FloorLines(
            new Point3(footprint.MinX, footprint.MinY, floorZ),
            new Point3(footprint.MaxX, footprint.MaxY, floorZ), 8))
        {
            var pen = line.Major ? PlotterChromePens.GridMajor : PlotterChromePens.GridMinor;
            ctx.DrawLine(pen,
                Projected(line.A, zoom, pan), Projected(line.B, zoom, pan));
        }
    }

    /// <summary>The machine origin, projected — the same 6 px cross this view
    /// drew before the chrome existed, on the pre-existing 1 px
    /// <c>PlotterPens.Origin</c>. The flat panes' cross is 2 px
    /// (<c>PlotterFlatChrome</c>); that difference is deliberate and recorded,
    /// not a defect to close here.</summary>
    public static void DrawOrigin(DrawingContext ctx, double zoom, Vector pan)
    {
        var o = Projected(new Point3(0, 0, 0), zoom, pan);
        ctx.DrawLine(PlotterPens.Origin, new Point(o.X - 6, o.Y), new Point(o.X + 6, o.Y));
        ctx.DrawLine(PlotterPens.Origin, new Point(o.X, o.Y - 6), new Point(o.X, o.Y + 6));
    }

    /// <summary>The three world axis arrows leaving the origin, each
    /// <paramref name="reach"/> long along its own world axis, and each in ITS OWN
    /// colour: X red, Y green, Z blue, the scheme
    /// <c>NexPlotScene.AxisColor</c> uses. One blue pen for all three said
    /// nothing about which arrow was which, which is the one job these arrows
    /// have. The header carries the letters; the arrows carry the directions.
    /// Not toggleable — neither are the flat pane's.</summary>
    public static void DrawAxes(
        DrawingContext ctx, double zoom, Vector pan, Rect viewport, double reach)
    {
        var o = Projected(new Point3(0, 0, 0), zoom, pan);
        var tips = new[]
        {
            Projected(new Point3(reach, 0, 0), zoom, pan),
            Projected(new Point3(0, reach, 0), zoom, pan),
            Projected(new Point3(0, 0, reach), zoom, pan),
        };

        using (ctx.PushClip(viewport))
        {
            for (var axis = 0; axis < tips.Length; axis++)
            {
                ctx.DrawLine(PlotterChromePens.AxisByIndex[axis], o, tips[axis]);
                PlotterChrome.ArrowHead(ctx, PlotterChromePens.AxisByIndex[axis], o, tips[axis], 5);
            }
        }
    }

    /// <summary>The job box: the world footprint extruded between
    /// <paramref name="minZ"/> and <paramref name="maxZ"/>. Equal bounds give a
    /// flat box on Z = 0, which is what a plane that does not feed world Z
    /// really occupies — <c>BoxEdges</c>' degenerate vertical edges draw
    /// nothing, so no case is needed here.</summary>
    public static void DrawEnvelope(
        DrawingContext ctx,
        (double MinX, double MinY, double MaxX, double MaxY) footprint,
        double minZ, double maxZ, double zoom, Vector pan, Rect viewport)
    {
        var a = new Point3(footprint.MinX, footprint.MinY, minZ);
        var b = new Point3(footprint.MaxX, footprint.MaxY, maxZ);

        using (ctx.PushClip(viewport))
        {
            foreach (var edge in PlotterChrome.BoxEdges(a, b))
                ctx.DrawLine(PlotterChromePens.Envelope,
                    Projected(edge.A, zoom, pan), Projected(edge.B, zoom, pan));
        }
    }

    /// <summary>One world point to composition pixels: project, then the shared
    /// screen transform. Public because the caller is the only other party that
    /// needs it (a tooltip position, a future export), and public so that it is
    /// the visible single point of entry — a private copy is how the second and
    /// third sign conventions got here in the first place.</summary>
    public static Point Projected(Point3 world, double zoom, Vector pan)
    {
        var (px, py) = PlotterProjection.Project(world.X, world.Y, world.Z);
        return PlotterChrome.ToScreen(px, py, zoom, pan);
    }
}
