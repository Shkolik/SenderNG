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
/// painters all read the SAME mapping, so a plane's bounds, path, glyph and
/// letters can never disagree.
/// <para>The mapping is <b>positional</b>: within a plane slot <c>k</c> feeds
/// world axis <c>k</c> (the plan's "the machine is drawn the way it is laid
/// out"). The per-plane <c>WorldAxes</c> table this class used to interpret is
/// deleted rather than moved, so the only thing left to decide here is which
/// world axes a plane's slot COUNT reaches — a 2-slot plane spans world X and
/// world Y and nothing else, a 3-slot one spans X, Y and Z.</para></summary>
public static class PlotterPlaneMapper
{
    /// <summary>World axes are X, Y, Z.</summary>
    public const int WorldAxisCount = 3;

    /// <summary>True when a plane whose slots are <paramref name="planeAxisIndices"/>
    /// feeds <paramref name="worldAxis"/>: slot <c>k</c> feeds world axis <c>k</c>,
    /// so a plane feeds exactly world axes <c>0..slots-1</c>. A plane that feeds
    /// neither world X nor world Y therefore has no world-XY extent at all, and
    /// the callers below say so with a <c>false</c> rather than a stand-in
    /// rectangle.</summary>
    public static bool FeedsWorldAxis(IReadOnlyList<int>? planeAxisIndices, int worldAxis) =>
        planeAxisIndices is not null
        && worldAxis >= 0 && worldAxis < WorldAxisCount
        && worldAxis < planeAxisIndices.Count;

    /// <summary>One world axis's extent, read positionally out of the program
    /// frame's per-axis bounds — or false when it cannot be read. False is the
    /// honest answer for a plane that does not span the world axis and for a
    /// machine axis the frame has no bounds for; it is never a stand-in number.</summary>
    public static bool TryWorldSpan(
        int[]? planeAxisIndices, double[][]? bounds, int worldAxis,
        out (double Min, double Max) span)
    {
        span = default;
        if (bounds is null || !FeedsWorldAxis(planeAxisIndices, worldAxis))
            return false;
        var machineAxis = planeAxisIndices![worldAxis];
        if (machineAxis < 0 || machineAxis >= bounds.Length
            || bounds[machineAxis] is not { Length: 2 } b)
            return false;
        span = (b[0], b[1]);
        return true;
    }

    /// <summary>The world XY footprint the iso lattice, the iso axis arrows and
    /// the iso job box are all measured against: world X and world Y read
    /// positionally out of ONE plane's slots, each UNIONED with the machine
    /// origin so the lattice reaches the origin the arrows start from. That
    /// one-plane scope is deliberate and is the mill path; the multi-plane
    /// counterpart, for the foam tiles' own envelope, is
    /// <see cref="TryJobExtent"/>. Reach for that one whenever the two carriage
    /// planes must agree on a rectangle, and for this one whenever a single
    /// drawn plane's chrome must.
    /// <para>False means no footprint, never a default one. A plane with fewer
    /// than two slots has no world XY extent to measure, and a magic 1 mm
    /// square standing in for it would put a lattice and a job box on screen that
    /// belong to no machine — the caller skips the extent-derived chrome
    /// instead. A zero-thickness extent is a real measurement and IS returned:
    /// a program that never moves an axis has a face, and the job box's flat
    /// side is the truth about it (the lattice itself comes back empty from
    /// <c>PlotterChrome.FloorLines</c>, which is a decision, not a fallback).</para></summary>
    public static bool TryWorldFootprint(
        int[]? planeAxisIndices, double[][]? bounds,
        out (double MinX, double MinY, double MaxX, double MaxY) footprint)
    {
        footprint = default;
        if (!TryWorldSpan(planeAxisIndices, bounds, 0, out var x)
            || !TryWorldSpan(planeAxisIndices, bounds, 1, out var y))
            return false;
        footprint = (Math.Min(x.Min, 0), Math.Min(y.Min, 0), Math.Max(x.Max, 0), Math.Max(y.Max, 0));
        return true;
    }

    /// <summary>The JOB's world XY extent: every readable plane contributes its
    /// own world X and world Y span, and the planes are UNIONED — min of the
    /// minima, max of the maxima. The envelope is a property of the job, not of
    /// a carriage, so a foam job's two planes must not each bring their own
    /// extent: the two tiles would be different sizes and the same envelope
    /// rectangle would mean a different thing on each; the envelope is a
    /// single Min/Max union over all of them.
    /// <para>The origin is unioned in here, exactly as
    /// <see cref="TryWorldFootprint"/> does, so the two cannot disagree about
    /// what "the extent" includes and the origin stays in frame.
    /// <para>A plane that cannot be read is skipped, never fatal. The rule is
    /// ALL-OR-NOTHING: a plane contributes only if it supplies BOTH world X and
    /// world Y, so a plane with a single slot has its X contribution discarded
    /// along with the Y it never had. That is <see cref="TryWorldFootprint"/>'s
    /// shape kept deliberately — the two methods read the same planes, so
    /// letting one accept a half-plane would let them disagree about the extent.
    /// The cost is a future preset pairing an X-only plane with a Y-only one
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
        IReadOnlyList<int[]>? planeAxisIndices,
        double[][]? bounds,
        out (double MinX, double MinY, double MaxX, double MaxY) extent)
    {
        extent = default;
        if (planeAxisIndices is null || bounds is null)
            return false;
        // Seeded at 0, so the origin is unioned in by the same arithmetic that
        // unions the planes — there is no second rule to keep in step.
        var minX = 0.0;
        var minY = 0.0;
        var maxX = 0.0;
        var maxY = 0.0;
        var any = false;
        foreach (var indices in planeAxisIndices)
        {
            if (indices is null)
                continue;
            if (!TryWorldSpan(indices, bounds, 0, out var x)
                || !TryWorldSpan(indices, bounds, 1, out var y))
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
