using CNC.GCode;

namespace CNC.Core.Services;

public sealed record AxisLimits(char Letter, double Min, double Max);

/// <summary>
/// Computes per-axis program limits (bounding box) for G-code text,
/// mirroring ioSender's GCodeJob approach: parse all blocks, run the
/// emulator, accumulate arc/spline bounding boxes and axis-word end points.
/// Returns limits for every axis addressed by the program (XYZABCUVW), or
/// null when the program contains no motion.
/// </summary>
public static class GCodeLimitsCalculator
{
    public static IReadOnlyList<AxisLimits>? Compute(string? gcodeText)
    {
        if (string.IsNullOrWhiteSpace(gcodeText))
            return null;

        var parser = new GCodeParser();
        foreach (var raw in gcodeText.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            var block = raw.Trim();
            if (block.Length == 0 || block.StartsWith(";") || block.StartsWith("%") || block.StartsWith("("))
                continue;
            try
            {
                parser.ParseBlock(ref block, false);
            }
            catch
            {
                // Unparsable lines contribute no limits.
            }
        }

        if (parser.Tokens.Count == 0)
            return null;

        // Per-axis bounding box across all machine axes (not just XYZ):
        // foam/multiaxis programs move A/B/C alongside XYZ and those limits
        // must be reported too.
        var count = GrblInfo.AxisLetters.Length;
        var min = Enumerable.Repeat(double.MaxValue, count).ToArray();
        var max = Enumerable.Repeat(double.MinValue, count).ToArray();
        var present = AxisFlags.None;

        var emu = new GCodeEmulator(true);
        foreach (var cmd in emu.Execute(parser.Tokens))
        {
            switch (cmd.Token)
            {
                case GCAxisCommand9 axisCmd:
                    // End position for each axis the block addressed — the
                    // emulator already applied absolute/incremental math.
                    foreach (var i in axisCmd.AxisFlags.ToIndices())
                    {
                        if (i >= count)
                            continue;
                        var pos = emu.GetPosition(i);
                        min[i] = Math.Min(min[i], pos);
                        max[i] = Math.Max(max[i], pos);
                        present |= (AxisFlags)(1 << i);
                    }
                    break;

                case GCArc arc:
                    AddBoundingBox(arc.GetBoundingBox(emu.Plane, [cmd.Start.X, cmd.Start.Y, cmd.Start.Z], emu.DistanceMode == DistanceMode.Incremental), min, max, ref present);
                    break;

                case GCCubicSpline cubic:
                    AddBoundingBox(cubic.GetBoundingBox(emu.Plane, [cmd.Start.X, cmd.Start.Y, cmd.Start.Z], emu.DistanceMode == DistanceMode.Incremental), min, max, ref present);
                    break;

                case GCQuadraticSpline quad:
                    AddBoundingBox(quad.GetBoundingBox(emu.Plane, [cmd.Start.X, cmd.Start.Y, cmd.Start.Z], emu.DistanceMode == DistanceMode.Incremental), min, max, ref present);
                    break;
            }
        }

        if (present == AxisFlags.None)
            return null;

        return Enumerable.Range(0, count)
            .Where(i => ((AxisFlags)(1 << i) & present) != 0)
            .Select(i => new AxisLimits(GrblInfo.AxisLetters[i], min[i], max[i]))
            .ToList();
    }

    private static void AddBoundingBox(GcodeBoundingBox bbox, double[] min, double[] max, ref AxisFlags present)
    {
        // Arcs/splines run on the selected plane (XYZ) — fold their box in.
        for (var i = 0; i < 3; i++)
        {
            min[i] = Math.Min(min[i], bbox.Min[i]);
            max[i] = Math.Max(max[i], bbox.Max[i]);
        }
        present |= AxisFlags.X | AxisFlags.Y | AxisFlags.Z;
    }
}
