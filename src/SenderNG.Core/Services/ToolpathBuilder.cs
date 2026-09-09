/*
 * ToolpathBuilder.cs - G-code token stream to plotter-ready toolpath segments
 */

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

using CNC.Core;
using CNC.GCode;

namespace CNC.Core.Services;

/// <summary>
/// One straight piece of the toolpath in the <b>program (work) frame</b> — the
/// coordinates as written in the G-code. The emulator folds G53 machine-coordinate
/// moves into that frame (it subtracts the active offsets), and the live tool
/// position aligns with it because <see cref="MachineState.Positions"/> is WPos —
/// the same frame. Positions are per controller-axis index (grbl axis order,
/// index 0 = first axis), so a foam program's second carriage (Z/A = indices
/// 2,3) slices out positionally, no letter mapping involved.
/// </summary>
/// <param name="From">Axis positions at the segment start (one slot per axis).</param>
/// <param name="To">Axis positions at the segment end.</param>
/// <param name="Rapid">True for G0 (and rapid canned-cycle) motion.</param>
/// <param name="LineNumber">1-based source file line the motion came from —
/// the run-frontier mapping key (rows are line-aligned).</param>
public sealed record ToolpathSegment(double[] From, double[] To, bool Rapid, uint LineNumber);

/// <summary>
/// Walks the engine's emulator (<see cref="GCodeEmulator"/>) over a parsed token
/// stream and flattens every motion into straight segments — the same walk
/// <see cref="GCodeLimitsCalculator"/> performs for limits, so a plot built on
/// this and the program view's LimitRows always agree. The emulator runs with
/// <c>translate: true</c> so canned cycles (G81.., G28/G30, probe moves) emit
/// their motion instead of being opaque tokens.
/// <para>
/// Linear motions (including the emulator's synthesized canned-cycle and probe
/// moves) become one segment each; arcs and splines are flattened with the
/// engine's own tolerance-driven math (<see cref="GCArc.GeneratePoints"/>,
/// <see cref="GCSpline.GeneratePoints"/> — the same helpers the ArcsToLines
/// transformer uses), with off-plane axes interpolated linearly (helical
/// motion). Zero-length yields (F/S/M and other modal tokens yield too) are
/// dropped. Known limitation: G76 threading passes bypass the emulator's
/// machine-position snapshot, so they contribute no segments — lathe is out of
/// scope (deferred module).
/// </para>
/// </summary>
public static class ToolpathBuilder
{
    /// <summary>Chord tolerance (mm) for arc/spline flattening — the ArcsToLines
    /// pre-pass default; orders of magnitude below a visible pixel at plot scale.</summary>
    public const double CurveTolerance = 0.01d;

    /// <summary>Flatten the token stream into toolpath segments. The input tokens
    /// are never mutated; the returned list is a fresh, caller-owned snapshot.</summary>
    public static IReadOnlyList<ToolpathSegment> Build(IReadOnlyList<GCodeToken> tokens)
    {
        var segments = new List<ToolpathSegment>();
        if (tokens.Count == 0)
            return segments;

        var emu = new GCodeEmulator(true);
        var from = Snapshot(emu);

        // Execute takes the concrete List<T> the parser produces; the public
        // parameter stays IReadOnlyList for test/caller convenience.
        foreach (var cmd in emu.Execute(tokens is List<GCodeToken> list ? list : [.. tokens]))
        {
            var to = Snapshot(emu);
            var line = cmd.Token.LineNumber;
            var incremental = emu.DistanceMode == DistanceMode.Incremental;

            switch (cmd.Token)
            {
                case GCLinearMotion:
                case GCAbsLinearMotion:
                    AddSegment(segments, from, to, rapid: cmd.Token.Command == Commands.G0, line);
                    break;

                case GCArc arc:
                    // GeneratePoints MUTATES the start array it is given (it walks
                    // the arc through it) — pass a fresh copy of the true start
                    // (the same rule the ArcsToLines pre-pass follows).
                    var arcStart = new[] { cmd.Start.X, cmd.Start.Y, cmd.Start.Z };
                    AddPolyline(segments, from, to,
                        arc.GeneratePoints(emu.Plane, arcStart, CurveTolerance, incremental), line);
                    break;

                case GCCubicSpline cubic:
                    AddPolyline(segments, from, to,
                        GCodeTransformers.SplinePoints(cubic,
                            new[] { cmd.Start.X, cmd.Start.Y, cmd.Start.Z }, CurveTolerance, incremental), line);
                    break;

                case GCQuadraticSpline quad:
                    AddPolyline(segments, from, to,
                        GCodeTransformers.SplinePoints(quad,
                            new[] { cmd.Start.X, cmd.Start.Y, cmd.Start.Z }, CurveTolerance, incremental), line);
                    break;
            }

            from = to;
        }

        return segments;
    }

    /// <summary>Per-axis positions from the emulator's machine-position snapshot
    /// (9 slots — the Point6D backing store covers X..W). The width is the
    /// EMULATOR's, never the connected controller's reported axis count: a
    /// 4-axis foam program must keep its A data even when the session's board
    /// reports 3 axes (the plotter would otherwise silently drop the second
    /// carriage plane and degenerate to a mill-style render — the reported
    /// "wing standing on its tip" when a 3-axis profile/board was active).</summary>
    private static double[] Snapshot(GCodeEmulator emu)
    {
        var pos = new double[Point6DAxisCount];
        for (var i = 0; i < pos.Length; i++)
            pos[i] = emu.GetPosition(i);
        return pos;
    }

    /// <summary>Width of the emulator's machine-position store (X..W).</summary>
    private const int Point6DAxisCount = 9;

    private static void AddSegment(List<ToolpathSegment> segments, double[] from, double[] to, bool rapid, uint line)
    {
        // Zero-length filter: modal yields (F/S/M tokens), repeated coordinates
        // and the canned-cycle marker yields all collapse away here.
        for (var i = 0; i < from.Length; i++)
        {
            if (Math.Abs(from[i] - to[i]) <= 1e-9)
                continue;
            segments.Add(new ToolpathSegment((double[])from.Clone(), (double[])to.Clone(), rapid, line));
            return;
        }
    }

    /// <summary>Chain a flattened curve (points include the exact end) into
    /// feed segments; XYZ come from the points, remaining axes interpolate
    /// linearly from <paramref name="from"/> to <paramref name="to"/> (helical).</summary>
    private static void AddPolyline(List<ToolpathSegment> segments, double[] from, double[] to, List<Point3D> points, uint line)
    {
        if (points.Count == 0)
            return;

        var cur = (double[])from.Clone();
        for (var j = 0; j < points.Count; j++)
        {
            var t = points.Count == 1 ? 1d : (double)j / (points.Count - 1);
            var next = new double[from.Length];
            next[0] = points[j].X;
            next[1] = points[j].Y;
            next[2] = points[j].Z;
            for (var i = 3; i < next.Length; i++)
                next[i] = from[i] + (to[i] - from[i]) * t;

            AddSegment(segments, cur, next, rapid: false, line);
            cur = next;
        }
    }
}
