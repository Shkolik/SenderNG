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

    /// <summary>Center of the probed span(s). Probe order: X- then X+ then Y- then Y+ (or a subset).</summary>
    public static (Pos Center, double XDistance, double YDistance) ComputeCenter(
        Center center, Pos start, Pos[] probes, double probeDiameter, double sizeX, double sizeY)
    {
        var probeX = sizeY > 0d;
        var probeY = sizeX > 0d;

        var centerX = probeX ? probes[0].X + (probes[1].X - probes[0].X) / 2d : 0d;
        var centerY = probeY ? probes[^1].Y + (probes[^2].Y - probes[^1].Y) / 2d : 0d;

        var xDistance = probeX ? Math.Abs(probes[1].X - probes[0].X) : 0d;
        var yDistance = probeY ? Math.Abs(probes[^2].Y - probes[^1].Y) : 0d;

        if (center == Center.Inside)
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

    /// <summary>Degrees for display.</summary>
    public static double ToDegrees(double radians) => Math.Round(Math.Atan(radians) * 180d / Math.PI, 3);

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

    /// <summary>Invariant number formatting shared by probe UI/tests.</summary>
    public static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
