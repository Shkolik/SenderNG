
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

namespace SenderNG.Core.Modules.Probing;

/// <summary>
/// Post-probe calculations: converting captured probe touch positions into
/// workpiece contact points, center/rotation math, and the controller offset
/// commands. Ported from ioSender's OnCompleted handlers.
/// </summary>
public static class ProbeMath
{
    /// <summary>
    /// Sign of the X/Y approach for a given edge/corner. Index 0 = X, 1 = Y;
    /// 0 = axis not probed.
    /// </summary>
    public static int[] ApproachSign(Edge edge, bool internalFinder) => edge switch
    {
        Edge.A => internalFinder ? [-1, -1] : [1, 1],
        Edge.B => internalFinder ? [1, -1] : [-1, 1],
        Edge.C => internalFinder ? [1, 1] : [-1, -1],
        Edge.D => internalFinder ? [-1, 1] : [1, -1],
        // Edge probing direction (ioSender IsProbeEdgeNegativeX/Y):
        // B/C/CB probe -X, C/D/CD probe -Y.
        Edge.AD => [1, 0],
        Edge.AB => [0, 1],
        Edge.CB => [-1, 0],
        Edge.CD => [0, -1],
        Edge.Z => [0, 0, 1],
        _ => [0, 0],
    };

    /// <summary>Which axes are probed for the edge/corner (X/Y/Z letters).</summary>
    public static string ProbedAxes(Edge edge) => edge switch
    {
        Edge.A or Edge.B or Edge.C or Edge.D => "XY",
        Edge.AD or Edge.AB or Edge.CB or Edge.CD => edge is Edge.AD or Edge.CB ? "X" : "Y",
        Edge.Z => "Z",
        _ => string.Empty,
    };

    /// <summary>
    /// Workpiece contact point from the captured probe positions: each probed
    /// axis is compensated by half the probe diameter in the approach direction
    /// (Z is used as probed - the probe tip touches the surface). Probe
    /// positions are consumed in program order (corners probe X then Y).
    /// </summary>
    public static Pos ComputeEdgePoint(Edge edge, Pos[] probePositions, double probeDiameter, bool internalFinder)
    {
        var sign = ApproachSign(edge, internalFinder);
        var first = probePositions.Length > 0 ? probePositions[0] : default;
        double x = first.X, y = first.Y, z = first.Z;
        var index = 0;

        foreach (var axis in ProbedAxes(edge))
        {
            var touch = probePositions[index++];
            switch (axis)
            {
                case 'X': x = touch.X + probeDiameter / 2d * sign[0]; break;
                case 'Y': y = touch.Y + probeDiameter / 2d * sign[1]; break;
                case 'Z': z = touch.Z; break;
            }
        }

        return new Pos(x, y, z);
    }

    /// <summary>Center of the probed span(s). Probe order: X- then X+ then Y- then Y+ (or a subset).
    /// <paramref name="probes"/> must hold at least two positions per probed axis — four for
    /// the two-axis modes, two for a single-axis mode, none when neither axis is probed.
    /// A short array throws <see cref="ArgumentException"/> rather than being indexed:
    /// probing fewer positions than the mode implies used to raise
    /// <see cref="IndexOutOfRangeException"/>, and with three positions it did not even
    /// throw — it silently averaged the wrong pair (audit §1.9).</summary>
    public static (Pos Center, double XDistance, double YDistance) ComputeCenter(
        Center center, Pos start, Pos[] probes, double probeDiameter, double sizeX, double sizeY)
    {
        // Single-axis modes probe exactly one pair; the legacy modes keep
        // ioSender's FindMode convention (Y size gates the X pair and vice versa).
        var (probeX, probeY) = center switch
        {
            Center.XInside or Center.XOutside => (true, false),
            Center.YInside or Center.YOutside => (false, true),
            _ => (sizeY > 0d, sizeX > 0d),
        };

        int required = (probeX ? 2 : 0) + (probeY ? 2 : 0);
        if (probes.Length < required)
            throw new ArgumentException(
                $"ComputeCenter needs at least {required} probe position(s) for {center} but got {probes.Length}.",
                nameof(probes));

        var centerX = probeX ? probes[0].X + (probes[1].X - probes[0].X) / 2d : 0d;
        var centerY = probeY ? probes[^1].Y + (probes[^2].Y - probes[^1].Y) / 2d : 0d;

        var xDistance = probeX ? Math.Abs(probes[1].X - probes[0].X) : 0d;
        var yDistance = probeY ? Math.Abs(probes[^2].Y - probes[^1].Y) : 0d;

        if (center is Center.Inside or Center.XInside or Center.YInside)
        {
            if (probeX) xDistance += probeDiameter;
            if (probeY) yDistance += probeDiameter;
        }
        else
        {
            if (probeX) xDistance -= probeDiameter;
            if (probeY) yDistance -= probeDiameter;
        }

        _ = start;
        return (new Pos(centerX, centerY, start.Z), xDistance, yDistance);
    }

    /// <summary>Edge rotation angle (radians) from two probe points, per ioSender's getAngle.</summary>
    public static double ComputeRotationAngle(Edge edge, Pos p1, Pos p2)
    {
        var angle = (p2.Y - p1.Y) / (p2.X - p1.X);
        if (edge is Edge.CB or Edge.AD)
            angle = -1d / angle;
        return angle;
    }

    /// <summary>
    /// The actual rotation angle (radians, CCW-positive) for the program
    /// transform: the edge <see cref="ComputeRotationAngle"/> slope converted
    /// with Atan. ioSender feeds the raw slope into cos/sin as if it were
    /// radians — a small-angle approximation that over-rotates by ~1% at 10°
    /// and ~10% at 30° of misalignment; Atan is exact and identical for the
    /// 1–3° tilts probing realistically measures.
    /// </summary>
    public static double ComputeRotationRadians(Edge edge, Pos p1, Pos p2) =>
        Math.Atan(ComputeRotationAngle(edge, p1, p2));

    /// <summary>Degrees for display. The argument is radians, not a slope.</summary>
    public static double ToDegrees(double radians) => Math.Round(radians * 180d / Math.PI, 3);

    /// <summary>
    /// The controller command that writes <paramref name="pos"/> into the work
    /// coordinate system (or empty for Measure). G10 L2 Pn for G54..G59.
    /// </summary>
    public static string OffsetCommand(CoordinateMode mode, CoordinateSystem system, Pos pos, string axes) => mode switch
    {
        CoordinateMode.G92 => $"G92{pos.ToString(axes)}",
        CoordinateMode.G10 => $"G10L2P{(int)system}{pos.ToString(axes)}",
        _ => string.Empty,
    };

    /// <summary>
    /// The workpiece corner (X0,Y0 target) from the rotation probe with the
    /// "Determine X0,Y0 for right-angled workpiece" extra (ioSender's
    /// OnActionCompleted). The three raw probe positions are: p1/p2 along the
    /// bottom edge (AB, approached +Y), p3 on the left edge (approached +X);
    /// the corner is the intersection of the two edge lines. The left-edge
    /// line is defined by p3 and a synthetic p4 = p3 + Offset·(slope, −1),
    /// perpendicular to the bottom edge by construction (slope = the probed
    /// edge slope, small-angle — exactly upstream's shape). AB-only: the
    /// approach signs below are hard-coded for AB (upstream forces the AB
    /// edge when the extra is enabled); other edges would need sign
    /// parameters. The returned Z is 0 — the caller only consumes X/Y.
    /// </summary>
    public static Pos ComputeCorner(Pos p1, Pos p2, Pos p3, double probedSlope,
        double probeOffsetX, double probeOffsetY, double probeRadius, double offset)
    {
        // Probe radius + spindle-to-tip offset in each approach direction.
        p1 = p1.With(1, p1.Y + probeOffsetY + probeRadius);
        p2 = p2.With(1, p2.Y + probeOffsetY + probeRadius);
        p3 = p3.With(0, p3.X + probeOffsetX + probeRadius);
        var p4 = new Pos(p3.X + offset * probedSlope, p3.Y - offset, 0d);

        var divisor = (p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X);
        var x = ((p1.X * p2.Y - p1.Y * p2.X) * (p3.X - p4.X) - (p1.X - p2.X) * (p3.X * p4.Y - p3.Y * p4.X)) / divisor;
        var y = ((p1.X * p2.Y - p1.Y * p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X * p4.Y - p3.Y * p4.X)) / divisor;
        return new Pos(x, y, 0d);
    }

    /// <summary>
    /// The controller command that stores the probed tool length <paramref name="z"/>
    /// (mm, tip-down Z) into tool <paramref name="tool"/> of the tool table via
    /// G10 L1 Pn. grblHAL-only: classic grbl 1.1 / 1.2i forks have no G10 L1 and
    /// reject it with error 20, so this is only used when the controller reports a
    /// tool table (ioSender scaffolded this but shipped it commented out — we make
    /// it live).
    /// </summary>
    public static string ToolLengthTableCommand(int tool, double z) =>
        $"G10L1P{tool}Z{Fmt(z)}";

    /// <summary>Invariant number formatting shared by probe UI/tests.</summary>
    public static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
