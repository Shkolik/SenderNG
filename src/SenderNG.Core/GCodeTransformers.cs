/*
 * GCodeTransformers.cs - token-level G-code transforms (ioSender GCodeRotate
 * v0.47 port) for SenderNG
 *
 * Copyright (c) 2018-2025, Io Engineering (Terje Io)
 * Copyright (c) 2026 Andrew Shkolik
 *
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without modification,
 * are permitted provided that the following conditions are met:
 *
 * · Redistributions of source code must retain the above copyright notice, this
 * list of conditions and the following disclaimer.
 *
 * · Redistributions in binary form must reproduce the above copyright notice, this
 * list of conditions and the following disclaimer in the documentation and/or
 * other materials provided with the distribution.
 *
 * · Neither the name of the copyright holder nor the names of its contributors may
 * be used to endorse or promote products derived from this software without
 * specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
 * ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
 * ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *
 */

using CNC.Core;

namespace CNC.GCode;

/// <summary>
/// Token-level G-code transforms. Each transform consumes the engine's canonical
/// token stream (the parse pass the streamer and limits use — see
/// <see cref="GCodeProgram"/>) and returns serialized lines; callers validate the
/// result by re-parsing through the engine gate before it may replace the loaded
/// program. Tokens passed in are never mutated: transforms reuse pass-through
/// tokens by reference and emit new instances for everything they change.
/// </summary>
/// <summary>How <see cref="GCodeTransformers.Compress"/> treats comment tokens.
/// Only (...) comments exist in the token stream — ';' remark lines never
/// tokenize (the parser returns them as non-sendable lines), so any token-level
/// transform drops them inherently; the choice is only whether (...) CAM
/// comments survive.</summary>
public enum GCodeCommentMode
{
    /// <summary>Keep (...) comments (default — CAM comments carry operation/tool notes).</summary>
    Keep,
    /// <summary>Drop all comments — true minimal output.</summary>
    StripAll,
}

public static class GCodeTransformers
{
    /// <summary>
    /// Replaces G2/G3 arcs and G5/G5.1 splines with G1 polyline chains — a
    /// faithful port of ioSender's ArcsToLines transformer. An emulator
    /// pre-pass (the same <see cref="GCodeEmulator"/> run the limits calculator
    /// uses) provides the TRUE per-motion start position and modal state at each
    /// arc/spline token, so chains are correct even when the program starts with
    /// a curve, accumulates G91 deltas, or changes planes mid-file. Point
    /// generation reuses the engine's own tolerance-driven math
    /// (<see cref="GCArc.GeneratePoints"/>, <see cref="GCSpline.GeneratePoints"/>).
    /// G90 output is absolute; G91 output is deltas (the plan's explicit
    /// requirement). Non-motion words sharing the curve's block (feed, spindle,
    /// …) are hoisted ahead of the chain so the feed is in effect for the first
    /// segment. Everything else passes through untouched.
    /// </summary>
    /// <param name="tokens">Engine token stream (caller-owned, never mutated).</param>
    /// <param name="tolerance">Chord error in mm (or points-per-circle when &gt; 1 —
    /// the engine's resolution convention); clamped to ≥ 0.001.</param>
    /// <param name="decimals">Coordinate rounding precision (use <see cref="GCodeProgram.Decimals"/>).</param>
    /// <param name="startX">Current position X — where curve chains start if the program
    /// begins with a curve (the emulator is seeded with it).</param>
    /// <param name="startY">Current position Y.</param>
    /// <param name="startZ">Current position Z.</param>
    public static List<string> ArcsToLines(
        IReadOnlyList<GCodeToken> tokens,
        double tolerance = 0.1d,
        int decimals = 3,
        double startX = 0d, double startY = 0d, double startZ = 0d)
    {
        if (tolerance < 0.001d)
            tolerance = 0.001d;

        // Pass 1: emulator — true start position + modal state per curve token
        // (keyed by token reference; the emulator yields the original token for
        // G2/G3/G5/G5.1, exactly like the limits calculator consumes it).
        var curveInfo = new Dictionary<GCodeToken, CurveInfo>();
        var emu = new GCodeEmulator(true);
        emu.SetStartPosition(new Point3D(startX, startY, startZ));
        foreach (var cmd in emu.Execute([.. tokens]))
        {
            if (cmd.Token is GCArc or GCCubicSpline or GCQuadraticSpline)
                curveInfo[cmd.Token] = new CurveInfo(cmd.Start, emu.Plane,
                    emu.DistanceMode == DistanceMode.Incremental);
        }

        var toolPath = new List<GCodeToken>
        {
            new GCComment(Commands.Comment, 0, "Arcs to lines transform applied"),
        };

        // Expanded lines get synthetic line numbers (far above any real file
        // line) so the serializer keeps each G1 on its own block — merging them
        // would make the controller run only the last G1. The parse gate
        // renumbers the result anyway.
        uint nextLnr = 0x40000000u;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            switch (token.Command)
            {
                case Commands.G2:
                case Commands.G3:
                case Commands.G5:
                case Commands.G5_1:
                    {
                        // Hoist same-block non-motion words (F/S/M…) ahead of the
                        // chain: the feed must be in effect for the first G1.
                        // The parser allows only one motion per block, so every
                        // same-line token following the curve is a word token.
                        while (i + 1 < tokens.Count && tokens[i + 1].LineNumber == token.LineNumber)
                            toolPath.Add(tokens[++i]);

                        if (!curveInfo.TryGetValue(token, out var info))
                        {
                            toolPath.Add(token); // defensive: emulator did not yield it — keep the curve
                            break;
                        }

                        // GeneratePoints MUTATES the start array it is given (it
                        // walks the arc through it), so `prev` must be an
                        // independent copy of the true start position.
                        var start = new double[] { info.Start.X, info.Start.Y, info.Start.Z };
                        var trueStart = new double[] { info.Start.X, info.Start.Y, info.Start.Z };
                        var points = token switch
                        {
                            GCArc arc => arc.GeneratePoints(info.Plane, start, tolerance, info.Incremental),
                            GCCubicSpline cubic => SplinePoints(cubic, start, tolerance, info.Incremental),
                            GCQuadraticSpline quad => SplinePoints(quad, start, tolerance, info.Incremental),
                            _ => throw new InvalidOperationException("Unexpected curve token."),
                        };

                        // The token classes HIDE ToString() with "new", so string
                        // interpolation (which dispatches virtually) would print the
                        // type name — call ToString() explicitly to get the words.
                        var label = token switch
                        {
                            GCArc arc => "Arc to lines start: " + arc.ToString(),
                            GCCubicSpline cubic => "Spline to lines start: " + cubic.ToString(),
                            GCQuadraticSpline quad => "Spline to lines start: " + quad.ToString(),
                            _ => "Curve to lines start",
                        };
                        toolPath.Add(new GCComment(Commands.Comment, nextLnr++, label));

                        var prev = trueStart;
                        foreach (var point in points)
                        {
                            var values = new double[] { point.X, point.Y, point.Z };

                            if (info.Incremental)
                            {
                                // G91: emit deltas between consecutive points; the
                                // first point equals the start — a zero no-op, skip.
                                for (var a = 0; a < 3; a++)
                                    values[a] = NormalizeZero(Math.Round(values[a] - prev[a], decimals));
                                prev = new double[] { point.X, point.Y, point.Z };
                                if (values[0] == 0d && values[1] == 0d && values[2] == 0d)
                                    continue;

                                var flags = AxisFlags.None;
                                if (values[0] != 0d) flags |= AxisFlags.X;
                                if (values[1] != 0d) flags |= AxisFlags.Y;
                                if (values[2] != 0d) flags |= AxisFlags.Z;
                                toolPath.Add(new GCLinearMotion(Commands.G1, nextLnr++, values, flags));
                            }
                            else
                            {
                                for (var a = 0; a < 3; a++)
                                    values[a] = NormalizeZero(Math.Round(values[a], decimals));
                                toolPath.Add(new GCLinearMotion(Commands.G1, nextLnr++, values, AxisFlags.XYZ));
                            }
                        }

                        if (token.Command is Commands.G5 or Commands.G5_1)
                            toolPath.Add(new GCComment(Commands.Comment, nextLnr++, "Spline to lines end"));
                    }
                    break;

                default:
                    toolPath.Add(token);
                    break;
            }
        }

        return GCodeParser.TokensToGCode(toolPath);
    }

    /// <summary>True start position + modal state of a curve, captured by the emulator pre-pass.</summary>
    private readonly record struct CurveInfo(Point3D Start, GCPlane Plane, bool Incremental);

    /// <summary>
    /// Bezier control points and endpoint resolved to the work frame (the token's
    /// raw X/Y/Z words are deltas in G91), then the engine's adaptive
    /// tolerance-driven point generator.
    /// </summary>
    // Internal: also consumed by ToolpathBuilder (the plotter's Core walk) —
    // same control-point math, one implementation.
    internal static List<Point3D> SplinePoints(GCCubicSpline spline, double[] start, double tolerance, bool incremental)
    {
        var end = new double[] { spline.X, spline.Y, spline.Z };
        if (incremental)
            for (var a = 0; a < 3; a++)
                end[a] += start[a];

        var first = new Point(start[0] + spline.I, start[1] + spline.J);
        var second = new Point(end[0] + spline.P, end[1] + spline.Q);

        return GCSpline.GeneratePoints(start, first, second, end, tolerance);
    }

    internal static List<Point3D> SplinePoints(GCQuadraticSpline spline, double[] start, double tolerance, bool incremental)
    {
        var end = new double[] { spline.X, spline.Y, spline.Z };
        if (incremental)
            for (var a = 0; a < 3; a++)
                end[a] += start[a];

        // Quadratic→cubic conversion (the engine's own formulas, control point 1
        // at 2/3 of the I/J offset from start, control point 2 mirrored from end).
        var first = new Point(start[0] + spline.I * 2d / 3d, start[1] + spline.J * 2d / 3d);
        var second = new Point(end[0] + (start[0] + spline.I - end[0]) * 2d / 3d,
            end[1] + (start[1] + spline.J - end[1]) * 2d / 3d);

        return GCSpline.GeneratePoints(start, first, second, end, tolerance);
    }

    /// <summary>
    /// Rotates X/Y geometry of motion tokens by <paramref name="angleRadians"/>
    /// around (originX, originY) — a faithful port of ioSender's GCodeRotate
    /// v0.47. Coverage mirrors upstream: G0/G1 endpoints (G53 machine-coordinate
    /// moves pass through untouched), XY-plane IJK-mode G2/G3 arcs (endpoint AND
    /// I/J center words rotate; radius-mode and non-G17 arcs are refused, as
    /// upstream does), G5/G5.1 splines (endpoint + control points), and canned
    /// drills (XY position, repeats tracked). G91 programs rotate their deltas
    /// about the origin. A leading comment records the applied angle.
    /// </summary>
    /// <param name="tokens">Engine token stream (caller-owned, never mutated).</param>
    /// <param name="angleRadians">Rotation angle, radians (CCW-positive math convention).</param>
    /// <param name="originX">Rotation origin X — where the tool path pivots around.</param>
    /// <param name="originY">Rotation origin Y.</param>
    /// <param name="decimals">Coordinate rounding precision (use <see cref="GCodeProgram.Decimals"/>).</param>
    /// <param name="startX">Current position X — where relative (G91) accumulation starts.</param>
    /// <param name="startY">Current position Y.</param>
    /// <param name="startZ">Current position Z.</param>
    /// <exception cref="InvalidOperationException">Radius-mode arc (R word) or an arc not
    /// in the G17 (XY) plane — both are refused exactly like ioSender's transform.</exception>
    public static List<string> Rotate(
        IReadOnlyList<GCodeToken> tokens,
        double angleRadians,
        double originX, double originY,
        int decimals = 3,
        double startX = 0d, double startY = 0d, double startZ = 0d)
    {
        var plane = Plane.XY; // ioSender seeds this from the controller's modal state — a bare file defaults to XY.
        var distanceMode = DistanceMode.Absolute;
        var pos = new double[] { startX, startY, startZ };
        uint lastLineNumber = 0;
        var inMachineCoords = false; // set by a G53 token; applies to the motion sharing its block (G53 is non-modal)
        var toolPath = new List<GCodeToken>
        {
            new GCComment(Commands.Comment, 0,
                $"{Math.Round(angleRadians * 180d / Math.PI, 1).ToInvariantString()} degree rotation applied"),
        };

        foreach (var token in tokens)
        {
            if (token.LineNumber != lastLineNumber)
            {
                inMachineCoords = false; // G53 never carries across blocks
                lastLineNumber = token.LineNumber;
            }

            switch (token.Command)
            {
                // G0, G1: linear move
                case Commands.G0:
                case Commands.G1:
                    {
                        var motion = (GCLinearMotion)token;
                        if (inMachineCoords || motion.AxisFlags == AxisFlags.None)
                        {
                            inMachineCoords = false; // consumed by the G53-prefixed motion — pass it through untouched
                            toolPath.Add(motion);
                            break;
                        }

                        var target = ToAbsolute(pos, motion.Values, distanceMode == DistanceMode.Incremental);

                        if (distanceMode == DistanceMode.Incremental)
                        {
                            // Keep only the moved axes, rotate the delta about the origin.
                            if (!motion.AxisFlags.HasFlag(AxisFlags.X))
                                target[0] = 0d;
                            if (!motion.AxisFlags.HasFlag(AxisFlags.Y))
                                target[1] = 0d;
                            RotateXY(target, angleRadians, 0d, 0d, decimals);
                        }
                        else
                            RotateXY(target, angleRadians, originX, originY, decimals);

                        var flags = motion.AxisFlags;
                        if (target[0] != pos[0])
                            flags |= AxisFlags.X;
                        if (target[1] != pos[1])
                            flags |= AxisFlags.Y;

                        if (distanceMode == DistanceMode.Incremental)
                        {
                            pos[0] += target[0];
                            pos[1] += target[1];
                            pos[2] += target[2];
                        }
                        else
                            pos = target;

                        toolPath.Add(new GCLinearMotion(motion.Command, motion.LineNumber, target, flags));
                    }
                    break;

                // G2, G3: arc move (XY plane, IJK mode only — upstream parity)
                case Commands.G2:
                case Commands.G3:
                    {
                        var arc = (GCArc)token;

                        if (plane != Plane.XY)
                            throw new InvalidOperationException("Rotation is not supported for arcs outside the G17 (XY) plane.");
                        if (arc.IsRadiusMode)
                            throw new InvalidOperationException("Rotation is not supported for radius-mode (R word) arcs.");

                        var target = ToAbsolute(pos, arc.Values, distanceMode == DistanceMode.Incremental);
                        RotateXY(target, angleRadians, originX, originY, decimals);

                        var ijk = new double[] { arc.I, arc.J, arc.K };
                        RotateXY(ijk, angleRadians, 0d, 0d, decimals);

                        var flags = arc.AxisFlags;
                        if (target[0] != pos[0])
                            flags |= AxisFlags.X;
                        if (target[1] != pos[1])
                            flags |= AxisFlags.Y;

                        pos = target;

                        toolPath.Add(new GCArc(arc.Command, arc.LineNumber, target, flags,
                            ijk, arc.IjkFlags, arc.R, arc.P, arc.IJKMode));
                    }
                    break;

                // G5: cubic spline
                case Commands.G5:
                    {
                        var spline = (GCCubicSpline)token;

                        var target = new double[] { spline.X, spline.Y, 0d };
                        RotateXY(target, angleRadians, originX, originY, decimals);

                        var ijpq = new double[] { spline.I, spline.J, spline.P, spline.Q };
                        RotatePairsXY(ijpq, angleRadians, originX, originY, decimals);

                        pos = target; // upstream parity: splines are XY-plane, Z does not advance position

                        toolPath.Add(new GCCubicSpline(spline.Command, spline.LineNumber, target,
                            spline.AxisFlags, ijpq));
                    }
                    break;

                // G5.1: quadratic spline
                case Commands.G5_1:
                    {
                        var spline = (GCQuadraticSpline)token;

                        var target = new double[] { spline.X, spline.Y, 0d };
                        RotateXY(target, angleRadians, originX, originY, decimals);

                        var ij = new double[] { spline.I, spline.J };
                        RotatePairsXY(ij, angleRadians, originX, originY, decimals);

                        pos = target;

                        toolPath.Add(new GCQuadraticSpline(spline.Command, spline.LineNumber, target,
                            spline.AxisFlags, ij));
                    }
                    break;

                case Commands.G17:
                case Commands.G18:
                case Commands.G19:
                    plane = token is GCPlane p ? p.Plane : plane;
                    toolPath.Add(token);
                    break;

                // G53 machine-coordinate moves pass through — never rotate. The parser
                // splits "G53 G0 X.. Y.." into a G53 marker token plus the motion on the
                // same block, so the marker arms inMachineCoords for the motion that follows.
                case Commands.G53:
                    inMachineCoords = true;
                    toolPath.Add(token);
                    break;

                // Canned drill cycles: rotate the hole position, track repeats.
                case Commands.G73:
                case Commands.G81:
                case Commands.G82:
                case Commands.G83:
                case Commands.G85:
                case Commands.G86:
                case Commands.G89:
                    {
                        var drill = (GCCannedDrill)token;

                        var target = ToAbsolute(pos, drill.Values, distanceMode == DistanceMode.Incremental);
                        RotateXY(target, angleRadians, originX, originY, decimals);

                        var flags = drill.AxisFlags;
                        if (target[0] != pos[0])
                            flags |= AxisFlags.X;
                        if (target[1] != pos[1])
                            flags |= AxisFlags.Y;

                        if (distanceMode == DistanceMode.Incremental)
                        {
                            pos[0] += target[0] * drill.L;
                            pos[1] += target[1] * drill.L;
                            pos[2] += target[2];
                        }
                        else
                            pos = target;

                        toolPath.Add(new GCCannedDrill(drill.Command, drill.LineNumber, target,
                            flags, drill.R, drill.L, drill.P, drill.Q));
                    }
                    break;

                case Commands.G90:
                case Commands.G91:
                    distanceMode = token is GCDistanceMode dm
                        ? dm.DistanceMode
                        : (token.Command == Commands.G91 ? DistanceMode.Incremental : DistanceMode.Absolute);
                    toolPath.Add(token);
                    break;

                default:
                    toolPath.Add(token);
                    break;
            }
        }

        return GCodeParser.TokensToGCode(toolPath);
    }

    /// <summary>
    /// Applies a Z-height correction surface to the tool path: every feed
    /// move's Z is adjusted by the interpolated surface height at the move's
    /// XY. Feeds are split at <paramref name="segmentLength"/> (the map's grid
    /// spacing) so the corrected path follows the surface; rapids are
    /// corrected once at their endpoint (upstream parity). XY-plane G2/G3
    /// arcs are split into sub-arcs (they stay G2/G3 — geometry beyond Z is
    /// unchanged) with per-segment Z (helix + surface) and recomputed I/J
    /// (IJK mode) or the original R (radius mode); non-G17 arcs are refused.
    /// G90 output is absolute; G91 output is deltas (upstream emits absolute
    /// targets in G91 — broken on a controller). G53 machine-coordinate moves
    /// pass through untouched — the surface is defined in work coordinates.
    /// A port of the OpenCNCPilot-derived height-map apply in ioSender's CNC
    /// Controls Probing (<c>GCodeTransform.ApplyHeightMap</c>).
    /// </summary>
    /// <param name="tokens">Engine token stream (caller-owned, never mutated).</param>
    /// <param name="interpolateZ">Surface height at (x, y) — e.g.
    /// <c>HeightMap.InterpolateZ</c> (the probing module's grid surface).</param>
    /// <param name="segmentLength">Split spacing for feeds (the map's min grid
    /// spacing); ≤ 0 disables splitting.</param>
    /// <param name="decimals">Coordinate rounding precision (use <see cref="GCodeProgram.Decimals"/>).</param>
    /// <param name="startX">Current position X — where relative (G91) accumulation starts.</param>
    /// <param name="startY">Current position Y.</param>
    /// <param name="startZ">Current position Z.</param>
    /// <exception cref="InvalidOperationException">An arc outside the G17 (XY)
    /// plane — refused exactly like upstream.</exception>
    public static List<string> ApplyHeightMap(
        IReadOnlyList<GCodeToken> tokens,
        Func<double, double, double> interpolateZ,
        double segmentLength,
        int decimals = 3,
        double startX = 0d, double startY = 0d, double startZ = 0d)
    {
        // G53 is its own token (GCAbsLinearMotion, Command == G53) and falls
        // through the default pass-through below — machine-coordinate moves
        // are never corrected, the surface lives in work coordinates.
        var plane = new GCPlane(Commands.G17, 0);
        var distanceMode = DistanceMode.Absolute;
        var pos = new double[] { startX, startY, startZ };
        var toolPath = new List<GCodeToken>
        {
            new GCComment(Commands.Comment, 0, "Height map applied"),
        };

        // Expanded lines get synthetic line numbers (far above any real file
        // line) so the serializer keeps each segment on its own block. The
        // parse gate renumbers the result anyway.
        uint nextLnr = 0x40000000u;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            switch (token.Command)
            {
                case Commands.G0:
                case Commands.G1:
                    {
                        var motion = (GCLinearMotion)token;

                        // Hoist same-block non-motion words (F/S/M…) ahead of
                        // the segments: the feed must be in effect for the
                        // first segment (ArcsToLines precedent — the segments
                        // carry synthetic line numbers, so the serializer can
                        // not group the words with them).
                        var blockWords = new List<GCodeToken>();
                        while (i + 1 < tokens.Count && tokens[i + 1].LineNumber == token.LineNumber)
                            blockWords.Add(tokens[++i]);

                        if (motion.AxisFlags == AxisFlags.None)
                        {
                            toolPath.Add(motion);
                            toolPath.AddRange(blockWords);
                            break;
                        }
                        toolPath.AddRange(blockWords);

                        var isRelative = distanceMode == DistanceMode.Incremental;
                        var end = ToAbsolute(pos, motion.Values, isRelative);

                        // Feeds split at the surface grid spacing (upstream
                        // parity); rapids are corrected once at the endpoint.
                        var divisions = 1;
                        if (token.Command == Commands.G1 && segmentLength > 0d)
                        {
                            var dx = end[0] - pos[0];
                            var dy = end[1] - pos[1];
                            var dz = end[2] - pos[2];
                            var length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            divisions = Math.Max(1, (int)Math.Ceiling(length / segmentLength));
                        }

                        var prevAbs = pos;
                        for (var s = 1; s <= divisions; s++)
                        {
                            var ratio = (double)s / divisions;
                            var segEnd = new double[]
                            {
                                pos[0] + (end[0] - pos[0]) * ratio,
                                pos[1] + (end[1] - pos[1]) * ratio,
                                pos[2] + (end[2] - pos[2]) * ratio,
                            };

                            if (isRelative)
                            {
                                // G91: deltas between consecutive segment ends.
                                var delta = new double[]
                                {
                                    NormalizeZero(Math.Round(segEnd[0] - prevAbs[0], decimals)),
                                    NormalizeZero(Math.Round(segEnd[1] - prevAbs[1], decimals)),
                                    NormalizeZero(Math.Round(segEnd[2] + interpolateZ(segEnd[0], segEnd[1]) - prevAbs[2], decimals)),
                                };
                                if (delta[0] == 0d && delta[1] == 0d && delta[2] == 0d)
                                {
                                    prevAbs = segEnd;
                                    continue;
                                }
                                var flags = AxisFlags.None;
                                if (delta[0] != 0d) flags |= AxisFlags.X;
                                if (delta[1] != 0d) flags |= AxisFlags.Y;
                                if (delta[2] != 0d) flags |= AxisFlags.Z;
                                toolPath.Add(new GCLinearMotion(motion.Command, nextLnr++, delta, flags));
                            }
                            else
                            {
                                var target = new double[]
                                {
                                    NormalizeZero(Math.Round(segEnd[0], decimals)),
                                    NormalizeZero(Math.Round(segEnd[1], decimals)),
                                    NormalizeZero(Math.Round(segEnd[2] + interpolateZ(segEnd[0], segEnd[1]), decimals)),
                                };
                                // Upstream parity: original flags + Z — every
                                // segment re-states its corrected Z.
                                toolPath.Add(new GCLinearMotion(motion.Command, nextLnr++, target,
                                    motion.AxisFlags | AxisFlags.Z));
                            }
                            prevAbs = segEnd;
                        }

                        pos = end;
                    }
                    break;

                case Commands.G2:
                case Commands.G3:
                    {
                        var arc = (GCArc)token;
                        if (plane.Plane != Plane.XY)
                            throw new InvalidOperationException(
                                "Height-map apply is not supported for arcs outside the G17 (XY) plane.");

                        // Hoist same-block non-motion words (F/S/M…) ahead of
                        // the sub-arcs — the feed must be in effect for the
                        // first segment (ArcsToLines precedent).
                        while (i + 1 < tokens.Count && tokens[i + 1].LineNumber == token.LineNumber)
                            toolPath.Add(tokens[++i]);

                        var isRelative = distanceMode == DistanceMode.Incremental;
                        var start = (double[])pos.Clone();
                        var center = arc.GetCenter(plane, start, isRelative);
                        var end = ToAbsolute(pos, arc.Values, isRelative);

                        // Segment endpoints from the engine's arc machinery
                        // (full circles, P repeats, helix Z, IJK and R modes);
                        // 0.01 mm chord stepping is far below any grid spacing.
                        var fine = arc.GeneratePoints(plane, (double[])pos.Clone(), 0.01d, isRelative);

                        // Greedy merge into sub-arcs of <= segmentLength chord.
                        var segments = new List<(Point3D From, Point3D To)>();
                        if (segmentLength > 0d && fine.Count >= 2)
                        {
                            var segStart = fine[0];
                            var acc = 0d;
                            for (var k = 1; k < fine.Count; k++)
                            {
                                var dx = fine[k].X - fine[k - 1].X;
                                var dy = fine[k].Y - fine[k - 1].Y;
                                acc += Math.Sqrt(dx * dx + dy * dy);
                                if (acc >= segmentLength || k == fine.Count - 1)
                                {
                                    segments.Add((segStart, fine[k]));
                                    segStart = fine[k];
                                    acc = 0d;
                                }
                            }
                        }
                        else
                        {
                            // Degenerate arc (no generated points) or splitting
                            // disabled: single sub-arc, endpoint corrected.
                            segments.Add((new Point3D(pos[0], pos[1], pos[2]),
                                new Point3D(end[0], end[1], end[2])));
                        }

                        var prevAbs = new double[] { pos[0], pos[1], pos[2] };
                        Point3D? lastTarget = null;
                        foreach (var (from, to) in segments)
                        {
                            var targetAbs = new double[]
                            {
                                NormalizeZero(Math.Round(to.X, decimals)),
                                NormalizeZero(Math.Round(to.Y, decimals)),
                                NormalizeZero(Math.Round(to.Z + interpolateZ(to.X, to.Y), decimals)),
                            };

                            // I/J per sub-arc start: center − start (incremental
                            // IJK) or the absolute center (G90.1); radius-mode
                            // sub-arcs keep the original R.
                            double[] ijk;
                            if (arc.IsRadiusMode)
                                ijk = [double.NaN, double.NaN, double.NaN];
                            else if (arc.IJKMode == IJKMode.Incremental)
                                ijk = [center[0] - from.X, center[1] - from.Y, 0d];
                            else
                                ijk = [center[0], center[1], 0d];
                            for (var a = 0; a < 3; a++)
                                ijk[a] = NormalizeZero(Math.Round(ijk[a], decimals));
                            var ijkFlags = arc.IsRadiusMode
                                ? IJKFlags.None
                                : (arc.IjkFlags | IJKFlags.I | IJKFlags.J);

                            if (isRelative)
                            {
                                // G91: deltas between consecutive sub-arc ends.
                                var delta = new double[]
                                {
                                    NormalizeZero(Math.Round(to.X - prevAbs[0], decimals)),
                                    NormalizeZero(Math.Round(to.Y - prevAbs[1], decimals)),
                                    NormalizeZero(Math.Round(to.Z + interpolateZ(to.X, to.Y) - prevAbs[2], decimals)),
                                };
                                prevAbs = new double[] { to.X, to.Y, to.Z };
                                if (delta[0] == 0d && delta[1] == 0d && delta[2] == 0d)
                                    continue;
                                var flags = AxisFlags.None;
                                if (delta[0] != 0d) flags |= AxisFlags.X;
                                if (delta[1] != 0d) flags |= AxisFlags.Y;
                                if (delta[2] != 0d) flags |= AxisFlags.Z;
                                toolPath.Add(new GCArc(arc.Command, nextLnr++, delta, flags,
                                    ijk, ijkFlags, arc.R, 0, arc.IJKMode));
                            }
                            else
                            {
                                var flags = AxisFlags.XYZ;
                                if (lastTarget is { } lt)
                                {
                                    if (targetAbs[0] == lt.X) flags &= ~AxisFlags.X;
                                    if (targetAbs[1] == lt.Y) flags &= ~AxisFlags.Y;
                                    if (targetAbs[2] == lt.Z) flags &= ~AxisFlags.Z;
                                }
                                lastTarget = new Point3D(targetAbs[0], targetAbs[1], targetAbs[2]);
                                toolPath.Add(new GCArc(arc.Command, nextLnr++, targetAbs, flags,
                                    ijk, ijkFlags, arc.R, 0, arc.IJKMode));
                            }
                        }

                        pos = end;
                    }
                    break;

                case Commands.G17:
                case Commands.G18:
                case Commands.G19:
                    if (token is GCPlane p)
                        plane = p;
                    toolPath.Add(token);
                    break;

                case Commands.G90:
                case Commands.G91:
                    distanceMode = token is GCDistanceMode dm
                        ? dm.DistanceMode
                        : (token.Command == Commands.G91 ? DistanceMode.Incremental : DistanceMode.Absolute);
                    toolPath.Add(token);
                    break;

                default:
                    toolPath.Add(token);
                    break;
            }
        }

        return GCodeParser.TokensToGCode(toolPath);
    }

    /// <summary>Absolute endpoint of a move: (pos + delta) when relative, else the raw word values.</summary>
    private static double[] ToAbsolute(double[] pos, double[] values, bool isRelative)
    {
        return isRelative
            ? new double[] { pos[0] + values[0], pos[1] + values[1], pos[2] + values[2] }
            : new double[] { values[0], values[1], values[2] };
    }

    /// <summary>Rotates the X/Y components of an XYZ vector about (originX, originY) and rounds all three to <paramref name="decimals"/>.</summary>
    private static void RotateXY(double[] xyz, double angle, double originX, double originY, int decimals)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var x = xyz[0] - originX;
        var y = xyz[1] - originY;

        xyz[0] = NormalizeZero(Math.Round(originX + cos * x - sin * y, decimals));
        xyz[1] = NormalizeZero(Math.Round(originY + sin * x + cos * y, decimals));
        xyz[2] = NormalizeZero(Math.Round(xyz[2], decimals));
    }

    /// <summary>Rotates consecutive (x, y) point pairs of a control array (I/J, P/Q, …) about the origin, rounding to <paramref name="decimals"/>.</summary>
    private static void RotatePairsXY(double[] points, double angle, double originX, double originY, int decimals)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);

        for (var i = 0; i + 1 < points.Length; i += 2)
        {
            var x = points[i] - originX;
            var y = points[i + 1] - originY;

            points[i] = NormalizeZero(Math.Round(originX + cos * x - sin * y, decimals));
            points[i + 1] = NormalizeZero(Math.Round(originY + sin * x + cos * y, decimals));
        }
    }

    /// <summary>Rounds can land on -0 (tiny negative residuals after rotation) — serialize as plain 0.</summary>
    private static double NormalizeZero(double value) => value == 0d ? 0d : value;

    /// <summary>
    /// Canonical re-serialization of the token stream: the serializer's compress
    /// mode merges modal motion words (G0/G1/G2/G3/G5, canned drills) within a
    /// block, dropping words that repeat the previous motion's value. Comments
    /// are filtered per <paramref name="commentMode"/> (configurable — the
    /// default keeps them because CAM comments carry operation/tool notes;
    /// ';' remark lines never tokenize and are dropped by ANY token transform).
    /// The serializer MUTATES the motion tokens it merges (endpoint words fold
    /// into the block's previous motion), so motion tokens are cloned first —
    /// the tokens-never-mutated invariant holds for the caller's stream.
    /// </summary>
    /// <param name="tokens">Engine token stream (caller-owned, never mutated).</param>
    /// <param name="commentMode">Comment filtering policy.</param>
    public static List<string> Compress(
        IReadOnlyList<GCodeToken> tokens,
        GCodeCommentMode commentMode = GCodeCommentMode.Keep)
    {
        var working = new List<GCodeToken>(tokens.Count);
        foreach (var token in tokens)
        {
            switch (token.Command)
            {
                // The only tokens compress mode mutates — clone them so the
                // caller's stream stays untouched.
                case Commands.G0:
                case Commands.G1:
                case Commands.G2:
                case Commands.G3:
                case Commands.G73:
                case Commands.G81:
                case Commands.G82:
                case Commands.G83:
                case Commands.G85:
                case Commands.G86:
                case Commands.G89:
                    working.Add(CloneMotion(token));
                    break;

                case Commands.Comment:
                    if (commentMode == GCodeCommentMode.Keep)
                        working.Add(token);
                    break; // StripAll: comments are dropped

                default:
                    working.Add(token);
                    break;
            }
        }

        return GCodeParser.TokensToGCode(working, compress: true);
    }

    /// <summary>Shallow clone of a motion token (values array copied) — compress
    /// mode merges motion words by mutating them, and transforms never mutate
    /// the caller's stream.</summary>
    private static GCodeToken CloneMotion(GCodeToken token) => token switch
    {
        // Note: GCAbsLinearMotion is a G53-bearing subclass of GCLinearMotion —
        // it must be matched first (its ctor takes the wrapped motion command).
        GCAbsLinearMotion abs => new GCAbsLinearMotion(abs.Command, abs.Motion, abs.LineNumber,
            (double[])abs.Values.Clone(), abs.AxisFlags),
        GCLinearMotion lin => new GCLinearMotion(lin.Command, lin.LineNumber,
            (double[])lin.Values.Clone(), lin.AxisFlags),
        GCArc arc => new GCArc(arc.Command, arc.LineNumber,
            (double[])arc.Values.Clone(), arc.AxisFlags,
            (double[])arc.IJKvalues.Clone(), arc.IjkFlags, arc.R, arc.P, arc.IJKMode),
        GCCannedDrill drill => new GCCannedDrill(drill.Command, drill.LineNumber,
            (double[])drill.Values.Clone(), drill.AxisFlags, drill.R, drill.L, drill.P, drill.Q),
        _ => token,
    };
}
