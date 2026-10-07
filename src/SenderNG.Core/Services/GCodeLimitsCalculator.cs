
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

using CNC.GCode;

namespace CNC.Core.Services;

public sealed record AxisLimits(char Letter, double Min, double Max);

/// <summary>
/// Computes per-axis program limits (bounding box) for G-code text,
/// mirroring ioSender's load-path approach: parse all blocks, run the
/// emulator, accumulate arc/spline bounding boxes and axis-word end points.
/// Returns limits for every axis addressed by the program (XYZABCUVW), or
/// null when the program contains no motion.
/// </summary>
public static class GCodeLimitsCalculator
{
    public static IReadOnlyList<AxisLimits>? Compute(string? gcodeText) =>
        Compute(gcodeText, GrblInfo.AxisLetters);

    /// <summary>Core of <see cref="Compute(string)"/> with the axis letters supplied by the caller so
    /// sub-3-axis machines can be exercised without mutating the ambient GrblInfo state.</summary>
    internal static IReadOnlyList<AxisLimits>? Compute(string? gcodeText, string axisLetters)
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

        return Compute(parser.Tokens, axisLetters);
    }

    /// <summary>Program limits for an already-parsed token stream (single-parse path: <see cref="GCodeProgram"/>).</summary>
    public static IReadOnlyList<AxisLimits>? Compute(IReadOnlyList<GCodeToken> tokens) =>
        Compute(tokens, GrblInfo.AxisLetters);

    /// <summary>Core of <see cref="Compute"/> with the axis letters supplied by the caller so
    /// sub-3-axis machines can be exercised without mutating the ambient GrblInfo state.</summary>
    internal static IReadOnlyList<AxisLimits>? Compute(IReadOnlyList<GCodeToken> tokens, string axisLetters)
    {
        if (tokens.Count == 0)
            return null;
        var tokenList = tokens as List<GCodeToken> ?? tokens.ToList();

        // Per-axis bounding box across all machine axes (not just XYZ):
        // foam/multiaxis programs move A/B/C alongside XYZ and those limits
        // must be reported too.
        var count = axisLetters.Length;
        var min = Enumerable.Repeat(double.MaxValue, count).ToArray();
        var max = Enumerable.Repeat(double.MinValue, count).ToArray();
        var present = AxisFlags.None;

        var emu = new GCodeEmulator(true);
        foreach (var cmd in emu.Execute(tokenList))
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
            .Select(i => new AxisLimits(axisLetters[i], min[i], max[i]))
            .ToList();
    }

    private static void AddBoundingBox(GcodeBoundingBox bbox, double[] min, double[] max, ref AxisFlags present)
    {
        // Arcs/splines run on the selected plane (XYZ) — fold their box in, but
        // only for axes this board actually has: the arrays are sized from the
        // board's own AxisLetters, which can be shorter than three on a 2-axis
        // machine (audit §1.10). The present bits are raised inside the loop so
        // they describe the axes that really got a value, as the linear path above
        // already does with its `i >= count` skip.
        for (var i = 0; i < 3 && i < min.Length; i++)
        {
            min[i] = Math.Min(min[i], bbox.Min[i]);
            max[i] = Math.Max(max[i], bbox.Max[i]);
            present |= (AxisFlags)(1 << i);
        }
    }
}
