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

namespace SenderNG.Modules.Plotter.Settings;

/// <summary>Pure plane→world mapping. Nothing here touches Avalonia or the
/// renderer: the flat panes, the iso panes, the header letters and the chrome
/// painters all read the SAME mapping through these functions, so a plane's
/// bounds, path, glyph and letters can never disagree.</summary>
public static class PlotterPlaneMapper
{
    /// <summary>World axes are X, Y, Z.</summary>
    public const int WorldAxisCount = 3;

    /// <summary>Identity mapping for a plane with <paramref name="roles"/>
    /// machine axes: slot <c>i</c> feeds world axis <c>i</c>.</summary>
    public static PlotterPlaneGeometry Identity(int roles) => new()
    {
        WorldAxes = Enumerable.Range(0, roles).ToArray(),
        Origin = [0, 0, 0],
    };

    /// <summary>True when <paramref name="g"/> is a well-formed mapping for a
    /// plane with <paramref name="roles"/> machine axes: right length, no
    /// repeats, every world axis in range.</summary>
    public static bool IsUsable(PlotterPlaneGeometry? g, int roles) =>
        g?.WorldAxes is { } axes
        && axes.Length == roles
        && axes.Distinct().Count() == roles
        && axes.All(w => w >= 0 && w < WorldAxisCount);

    /// <summary><paramref name="g"/> when it is usable, otherwise
    /// <see cref="Identity"/> for <paramref name="roles"/>.</summary>
    public static PlotterPlaneGeometry For(PlotterPlaneGeometry? g, int roles) =>
        IsUsable(g, roles) ? g! : Identity(roles);

    /// <summary>Which plane slot feeds <paramref name="worldAxis"/>, or −1
    /// when the plane does not map it.</summary>
    public static int SlotForWorldAxis(int[]? worldAxes, int worldAxis)
    {
        if (worldAxes is null)
            return -1;
        for (var slot = 0; slot < worldAxes.Length; slot++)
        {
            if (worldAxes[slot] == worldAxis)
                return slot;
        }
        return -1;
    }

    /// <summary>The plane slots that feed world X and world Y, in that order.
    /// Either may be −1 — a plane feeding only X and Z has no world Y, and the
    /// flat 2D pane must then refuse to draw it.</summary>
    public static (int X, int Y) Feeds(PlotterPlaneGeometry geometry)
    {
        var axes = geometry.WorldAxes;
        return (SlotForWorldAxis(axes, 0), SlotForWorldAxis(axes, 1));
    }

    /// <summary>The plane's world-axis offset. 0 when the plane has no origin
    /// array, when the array is the wrong length, or when the axis is out of
    /// range — a settings file is untrusted input, so this is a clamp, not an
    /// index.</summary>
    public static double OriginFor(PlotterPlaneGeometry? g, int worldAxis) =>
        g?.Origin is { Length: WorldAxisCount } origin
        && (uint)worldAxis < WorldAxisCount
            ? origin[worldAxis]
            : 0;

    /// <summary>One world axis's extent, read through the plane's mapping out of
    /// the program frame's per-axis bounds — or false when it cannot be read.
    /// False is the honest answer for a plane that does not feed the world axis
    /// and for a role or machine axis the frame has no bounds for; it is never a
    /// stand-in number.</summary>
    public static bool TryWorldSpan(
        PlotterPlaneGeometry? g, int[] planeAxisIndices, double[][] bounds, int worldAxis,
        out (double Min, double Max) span)
    {
        span = default;
        var role = SlotForWorldAxis(g?.WorldAxes, worldAxis);
        if (role < 0 || planeAxisIndices is null || role >= planeAxisIndices.Length)
            return false;
        var machineAxis = planeAxisIndices[role];
        if (machineAxis < 0 || machineAxis >= bounds.Length
            || bounds[machineAxis] is not { Length: 2 } b)
            return false;
        span = (b[0], b[1]);
        return true;
    }

    /// <summary>The world XY footprint the iso lattice, the iso axis arrows and
    /// the iso job box are all measured against: world X and world Y read
    /// through ONE plane's mapping, each UNIONED with the machine origin so the
    /// lattice reaches the origin the arrows start from. That one-plane scope
    /// is deliberate and is the mill path; the multi-plane counterpart, for the
    /// foam tiles' own envelope, is <see cref="TryJobExtent"/>. Reach for that
    /// one whenever the two carriage planes must agree on a rectangle, and for
    /// this one whenever a single drawn plane's chrome must.
    /// <para>False means no footprint, never a default one. A plane that feeds
    /// neither world X nor world Y has no extent to measure, and a magic 1 mm
    /// square standing in for it would put a lattice and a job box on screen that
    /// belong to no machine — the caller skips the extent-derived chrome
    /// instead. A zero-thickness extent is a real measurement and IS returned:
    /// a program that never moves an axis has a face, and the job box's flat
    /// side is the truth about it (the lattice itself comes back empty from
    /// <c>PlotterChrome.FloorLines</c>, which is a decision, not a fallback).</para></summary>
    public static bool TryWorldFootprint(
        PlotterPlaneGeometry? g, int[] planeAxisIndices, double[][] bounds,
        out (double MinX, double MinY, double MaxX, double MaxY) footprint)
    {
        footprint = default;
        if (!TryWorldSpan(g, planeAxisIndices, bounds, 0, out var x)
            || !TryWorldSpan(g, planeAxisIndices, bounds, 1, out var y))
            return false;
        footprint = (Math.Min(x.Min, 0), Math.Min(y.Min, 0), Math.Max(x.Max, 0), Math.Max(y.Max, 0));
        return true;
    }

    /// <summary>The JOB's world XY extent: every readable plane contributes its
    /// own world X and world Y span, and the planes are UNIONED — min of the
    /// minima, max of the maxima. The envelope is a property of the job, not of
    /// a carriage, so a foam job's two planes must not each bring their own
    /// extent: the two tiles would be different sizes and the same envelope
    /// rectangle would mean a different thing on each. Nex takes Min/Max for
    /// the same reason (<c>NexPlotScene.BuildAutoChrome</c>).
    /// <para>The origin is unioned in here, exactly as
    /// <see cref="TryWorldFootprint"/> does, so the two cannot disagree about
    /// what "the extent" includes and the origin cross stays in frame.
    /// <para>A plane that cannot be read is skipped, never fatal. The rule is
    /// ALL-OR-NOTHING: a plane contributes only if it supplies BOTH world X and
    /// world Y, so an X-only plane has its X contribution discarded along with
    /// the Y it never had. That is <see cref="TryWorldFootprint"/>'s shape kept
    /// deliberately — the two methods read the same planes, so letting one accept
    /// a half-plane would let them disagree about the extent. The cost is a
    /// future preset pairing a world X-only plane with a world Y-only one
    /// under-sizes the envelope rather than widening it, and that is the failure
    /// to suspect if an envelope ever comes out too small. A plane whose axis
    /// index runs past the program frame is the axis-mismatch drop, skipped the
    /// same way. A plane that never MOVED needs no separate check either —
    /// <c>PlotterViewModel.ComputeBounds</c> hands a motion-less axis [0, 0] and
    /// unioning 0 with 0 widens nothing.
    /// <para>False means no plane could be read, never a fabricated extent: the
    /// caller has extent-derived chrome to skip and substitutes its own
    /// fallback.</summary>
    public static bool TryJobExtent(
        IReadOnlyList<PlotterPlaneGeometry?> geometries,
        IReadOnlyList<int[]> planeAxisIndices,
        double[][] bounds,
        out (double MinX, double MinY, double MaxX, double MaxY) extent)
    {
        extent = default;
        if (geometries is null || planeAxisIndices is null || bounds is null)
            return false;
        // Seeded at 0, so the origin is unioned in by the same arithmetic that
        // unions the planes — there is no second rule to keep in step.
        var minX = 0.0;
        var minY = 0.0;
        var maxX = 0.0;
        var maxY = 0.0;
        var any = false;
        var planes = Math.Min(geometries.Count, planeAxisIndices.Count);
        for (var p = 0; p < planes; p++)
        {
            var indices = planeAxisIndices[p];
            if (!TryWorldSpan(geometries[p], indices, bounds, 0, out var x)
                || !TryWorldSpan(geometries[p], indices, bounds, 1, out var y))
                continue;
            minX = Math.Min(minX, x.Min);
            minY = Math.Min(minY, y.Min);
            maxX = Math.Max(maxX, x.Max);
            maxY = Math.Max(maxY, y.Max);
            any = true;
        }
        if (!any)
            return false;
        extent = (minX, minY, maxX, maxY);
        return true;
    }
}
