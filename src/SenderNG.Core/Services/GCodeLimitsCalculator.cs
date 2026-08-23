using CNC.GCode;

namespace CNC.Core.Services;

public sealed record AxisLimits(char Letter, double Min, double Max);

/// <summary>
/// Computes per-axis program limits (bounding box) for G-code text,
/// mirroring ioSender's GCodeJob approach: parse all blocks, run the
/// emulator, accumulate arc/spline bounding boxes and axis-word end points.
/// Returns XYZ limits or null when the program contains no motion.
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

        var bbox = new GcodeBoundingBox();
        var emu = new GCodeEmulator(true);
        foreach (var cmd in emu.Execute(parser.Tokens))
        {
            if (cmd.Token is GCArc arc)
                bbox.AddBoundingBox(arc.GetBoundingBox(emu.Plane, [cmd.Start.X, cmd.Start.Y, cmd.Start.Z], emu.DistanceMode == DistanceMode.Incremental));
            else if (cmd.Token is GCCubicSpline cubic)
                bbox.AddBoundingBox(cubic.GetBoundingBox(emu.Plane, [cmd.Start.X, cmd.Start.Y, cmd.Start.Z], emu.DistanceMode == DistanceMode.Incremental));
            else if (cmd.Token is GCQuadraticSpline quad)
                bbox.AddBoundingBox(quad.GetBoundingBox(emu.Plane, [cmd.Start.X, cmd.Start.Y, cmd.Start.Z], emu.DistanceMode == DistanceMode.Incremental));
            else if (cmd.Token is GCAxisCommand9)
                bbox.AddPoint(cmd.End, ((GCAxisCommand9)cmd.Token).AxisFlags);
        }
        bbox.Conclude();

        return
        [
            new AxisLimits('X', bbox.Min[0], bbox.Max[0]),
            new AxisLimits('Y', bbox.Min[1], bbox.Max[1]),
            new AxisLimits('Z', bbox.Min[2], bbox.Max[2]),
        ];
    }
}
