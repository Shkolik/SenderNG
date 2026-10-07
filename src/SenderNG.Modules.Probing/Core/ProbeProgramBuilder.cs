
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

namespace SenderNG.Modules.Probing.Core;

/// <summary>
/// A position in machine coordinates used by probe program generation.
/// </summary>
public readonly struct Pos(double x, double y, double z)
{
    public double X { get; } = x;
    public double Y { get; } = y;
    public double Z { get; } = z;

    public double Get(int axis) => axis switch { 0 => X, 1 => Y, _ => Z };

    public Pos With(int axis, double value) => axis switch
    {
        0 => new Pos(value, Y, Z),
        1 => new Pos(X, value, Z),
        _ => new Pos(X, Y, value),
    };

    public static Pos From(double[] values) => new(
        values.Length > 0 ? values[0] : 0d,
        values.Length > 1 ? values[1] : 0d,
        values.Length > 2 ? values[2] : 0d);

    /// <summary>"X1.5Y2.5" for the requested axis letters (e.g. "XY"), invariant formatting.</summary>
    public string ToString(string axes)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in axes)
        {
            var v = ch switch { 'X' => X, 'Y' => Y, 'Z' => Z, _ => double.NaN };
            if (!double.IsNaN(v))
                sb.Append(ch).Append(v.ToString("0.###", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}

/// <summary>
/// Builds the gcode program for a probing run, ported from ioSender's
/// <c>CNC.Controls.Probing</c> control code-behinds (ToolLength/EdgeFinder/
/// EdgeFinderInt/CenterFinder/Rotation/HeightMap). Produces the motion+probe
/// moves only; offset writing (G10/G92) happens after the run on the captured
/// probe positions (see <see cref="ProbeMath"/>).
/// </summary>
public sealed class ProbeProgramBuilder
{
    private readonly List<string> _program = [];

    public ProbeProgramBuilder(ProbingProfile profile, string probeCommand = "G38.3", double unitFactor = 1d)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        ProbeCommand = probeCommand;
        UnitFactor = unitFactor;
    }

    public ProbingProfile Profile { get; }
    public string ProbeCommand { get; }
    public double UnitFactor { get; }

    /// <summary>The generated program (also exposed for preview).</summary>
    public IReadOnlyList<string> Lines => _program;

    public override string ToString() => string.Join("\n", _program);

    public void Clear() => _program.Clear();

    private double RapidsFeedRate => Profile.RapidsFeedRate;
    private double ProbeFeedRate => Profile.ProbeFeedRate;
    private double LatchFeedRate => Profile.LatchFeedRate;
    private double ProbeDistance => Profile.ProbeDistance;
    private double LatchDistance => Profile.LatchDistance;
    private double ProbeDiameter => Profile.ProbeDiameter;
    private double ProbeRadius => ProbeDiameter / 2d;
    private double XYClearance => Profile.XYClearance;
    private double Offset => Profile.Offset;
    private double Depth => Profile.Depth;

    /// <summary>Rapid move command: G0, or G1 with an explicit feed rate when one is set.</summary>
    private string RapidCommand =>
        RapidsFeedRate == 0d ? "G0" : $"G1F{RapidsFeedRate.ToString("0.###", CultureInfo.InvariantCulture)}";

    private void Add(string cmd) => _program.Add(cmd);

    private void AddMessage(string msg) => _program.Add("#" + msg);

    private void AddRapid(string cmd) => _program.Add(RapidCommand + cmd);

    private void AddRapidToMPos(string cmd) => _program.Add("G53" + RapidCommand + cmd);

    private void AddRapidToMPos(Pos pos, string axes) =>
        _program.Add("G53" + RapidCommand + pos.ToString(axes));

    /// <summary>Fast search probe + optional retract and slow latch probe (ioSender AddProbingAction).</summary>
    private void AddProbingAction(string axisLetter, bool negative)
    {
        var dir = negative ? "-" : "";
        _program.Add($"{ProbeCommand}F{ProbeFeedRate.ToString("0.###", CultureInfo.InvariantCulture)}{axisLetter}{dir}{ProbeDistance.ToString("0.###", CultureInfo.InvariantCulture)}");
        if (LatchDistance > 0d)
        {
            // Retract away from the workpiece, dropping the search-probe position.
            _program.Add("!" + RapidCommand + axisLetter + (negative ? "" : "-") + LatchDistance.ToString("0.###", CultureInfo.InvariantCulture));
            var slow = Math.Max(LatchDistance * 1.5d, 2d / UnitFactor);
            _program.Add($"{ProbeCommand}F{LatchFeedRate.ToString("0.###", CultureInfo.InvariantCulture)}{axisLetter}{dir}{slow.ToString("0.###", CultureInfo.InvariantCulture)}");
        }
    }

    private void AddPause() => _program.Add("pause");

    /// <summary>
    /// Tool length offset probe: probe Z down from the current position. When
    /// probing a fixture the program first rapids to the fixture origin.
    /// </summary>
    public ProbeProgramBuilder BuildToolLength(bool probeFixture, double homeZ = double.NaN,
        double fixtureX = 0d, double fixtureY = 0d, double fixtureZ = 0d)
    {
        _program.Clear();
        Add($"G91F{ProbeFeedRate.ToString("0.###", CultureInfo.InvariantCulture)}");

        if (probeFixture)
        {
            // ioSender: rapid to home Z, then the fixture origin, then to the
            // probing depth (capped by home Z). G59.3 offsets are not parsed yet,
            // so the fixture origin comes from the caller (defaults to 0,0).
            var zSafe = double.IsNaN(homeZ) ? fixtureZ : homeZ;
            var zTarget = Math.Min(zSafe, fixtureZ + Depth);
            AddRapidToMPos(new Pos(fixtureX, fixtureY, zSafe), "Z");
            AddRapidToMPos(new Pos(fixtureX, fixtureY, 0d), "XY");
            AddRapidToMPos(new Pos(fixtureX, fixtureY, zTarget), "Z");
        }

        AddProbingAction("Z", true);
        return this;
    }

    /// <summary>
    /// Edge finder program (external or internal) for the selected edge/corner.
    /// </summary>
    public ProbeProgramBuilder BuildEdgeFinder(Edge edge, Pos start, bool internalFinder)
    {
        _program.Clear();
        Add($"G91F{ProbeFeedRate.ToString("0.###", CultureInfo.InvariantCulture)}");

        var xyClearance = XYClearance + ProbeRadius;
        var af = new double[3];

        void SetAf(int axis, bool negative) => af[axis] = negative ? -1d : 1d;

        void AddEdge(char axisLetter, bool negative)
        {
            var axis = axisLetter switch { 'X' => 0, 'Y' => 1, _ => 2 };
            var axes = axisLetter.ToString();
            SetAf(axis, negative);

            var rapidto = start.With(axis, start.Get(axis) - xyClearance * af[axis]).With(2, start.Z - Depth);
            AddRapidToMPos(rapidto, axes);
            AddRapidToMPos(rapidto, "Z");

            AddProbingAction(axes, negative);

            rapidto = rapidto.With(axis, start.Get(axis) - xyClearance * af[axis]);
            AddRapidToMPos(rapidto, axes);
            AddRapidToMPos(start, "Z");
        }

        void AddCornerExternal(bool negx, bool negy)
        {
            SetAf(0, negx);
            SetAf(1, negy);

            var rapidto = start.With(0, start.X - xyClearance * af[0])
                               .With(1, start.Y + Offset * af[1])
                               .With(2, start.Z - Depth);

            AddRapidToMPos(rapidto, "Y");
            AddRapidToMPos(rapidto, "X");
            AddRapidToMPos(rapidto, "Z");

            AddProbingAction("X", negx);

            AddRapidToMPos(rapidto, "X");
            AddRapidToMPos(start, "Z");
            AddRapidToMPos(start, "X");

            rapidto = rapidto.With(0, start.X + Offset * af[0]).With(1, start.Y);
            AddRapidToMPos(rapidto, "XY");
            rapidto = rapidto.With(1, start.Y - xyClearance * af[1]);
            AddRapidToMPos(rapidto, "Y");
            AddRapidToMPos(rapidto, "Z");

            AddProbingAction("Y", negy);

            AddRapidToMPos(rapidto, "Y");
            AddRapidToMPos(start, "Z");
        }

        void AddCornerInternal(bool negx, bool negy)
        {
            SetAf(0, negx);
            SetAf(1, negy);

            var clearance = Math.Min(xyClearance, Offset);

            var rapidto = start.With(0, start.X - clearance * af[0])
                               .With(1, start.Y - Offset * af[1])
                               .With(2, start.Z - Depth);

            AddRapidToMPos(rapidto, "XY");
            AddRapidToMPos(rapidto, "Z");

            AddProbingAction("X", negx);

            AddRapidToMPos(rapidto, "X");
            rapidto = rapidto.With(0, start.X - Offset * af[0])
                             .With(1, start.Y - clearance * af[1]);
            AddRapidToMPos(rapidto, "XY");

            AddProbingAction("Y", negy);

            AddRapidToMPos(rapidto, "Y");
            AddRapidToMPos(start, "Z");
        }

        switch (edge)
        {
            case Edge.A:
                if (internalFinder) AddCornerInternal(true, true); else AddCornerExternal(false, false);
                break;
            case Edge.B:
                if (internalFinder) AddCornerInternal(false, true); else AddCornerExternal(true, false);
                break;
            case Edge.C:
                if (internalFinder) AddCornerInternal(false, false); else AddCornerExternal(true, true);
                break;
            case Edge.D:
                if (internalFinder) AddCornerInternal(true, false); else AddCornerExternal(false, true);
                break;
            case Edge.Z:
                SetAf(2, true);
                AddProbingAction("Z", true);
                if (!internalFinder)
                    AddRapidToMPos(start, "Z");
                break;
            // Single edges: an internal finder probes from inside the pocket,
            // so the whole sequence (approach side, probe and retract signs)
            // mirrors about the edge — exactly what flipping "negative" does.
            case Edge.AD:
                AddEdge('X', internalFinder);
                break;
            case Edge.AB:
                AddEdge('Y', internalFinder);
                break;
            case Edge.CB:
                AddEdge('X', !internalFinder);
                break;
            case Edge.CD:
                AddEdge('Y', !internalFinder);
                break;
        }

        return this;
    }

    /// <summary>
    /// Center finder program for one pass. The legacy <see cref="Center.Inside"/>/<see cref="Center.Outside"/>
    /// modes probe both axes (a size of 0 skips that axis, mirroring ioSender's FindMode); the
    /// X/Y variants probe a single axis only and need the matching size.
    /// </summary>
    public ProbeProgramBuilder BuildCenterFinder(Center center, Pos start, double sizeX, double sizeY)
    {
        _program.Clear();
        Add($"G91F{ProbeFeedRate.ToString("0.###", CultureInfo.InvariantCulture)}");

        var xyClearance = XYClearance + ProbeRadius;
        var rapidto = start.With(2, start.Z - Depth);

        // Inside pocket: probe -axis from the near wall side, then +axis from the other side.
        void AddInsideX()
        {
            var rapid = sizeX / 2d - xyClearance;
            if (rapid > 1d)
            {
                rapidto = rapidto.With(0, start.X - rapid);
                AddRapidToMPos(rapidto, "X");
                rapidto = rapidto.With(0, start.X + rapid);
            }

            AddProbingAction("X", true);
            AddRapidToMPos(rapidto, "X");
            AddProbingAction("X", false);
            AddRapidToMPos(start, "X");
        }

        void AddInsideY()
        {
            var rapid = sizeY / 2d - xyClearance;
            if (rapid > 1d)
            {
                rapidto = rapidto.With(1, start.Y - rapid);
                AddRapidToMPos(rapidto, "Y");
                rapidto = rapidto.With(1, start.Y + rapid);
            }

            AddProbingAction("Y", true);
            AddRapidToMPos(rapidto, "Y");
            AddProbingAction("Y", false);
            AddRapidToMPos(start, "Y");
        }

        // Outside workpiece: probe +axis from the negative side, then -axis from the positive side.
        void AddOutsideX()
        {
            AddRapidToMPos(rapidto, "X");
            AddRapidToMPos(rapidto, "Z");
            AddProbingAction("X", false);
            AddRapidToMPos(rapidto, "X");
            AddRapidToMPos(start, "Z");
            rapidto = rapidto.With(0, rapidto.X + sizeX + xyClearance * 2d);
            AddRapidToMPos(rapidto, "X");
            AddRapidToMPos(rapidto, "Z");
            AddProbingAction("X", true);
            AddRapidToMPos(rapidto, "X");
            AddRapidToMPos(start, "Z");
            AddRapidToMPos(start, "X");
        }

        void AddOutsideY()
        {
            AddRapidToMPos(rapidto, "Y");
            AddRapidToMPos(rapidto, "Z");
            AddProbingAction("Y", false);
            AddRapidToMPos(rapidto, "Y");
            AddRapidToMPos(start, "Z");
            rapidto = rapidto.With(1, rapidto.Y + sizeY + xyClearance * 2d);
            AddRapidToMPos(rapidto, "Y");
            AddRapidToMPos(rapidto, "Z");
            AddProbingAction("Y", true);
            AddRapidToMPos(rapidto, "Y");
        }

        switch (center)
        {
            case Center.Inside:
                AddRapidToMPos(rapidto, "Z");

                if (sizeY > 0d)
                    AddInsideX();

                if (sizeX > 0d)
                    AddInsideY();

                AddRapidToMPos(start, "Z");
                break;

            case Center.XInside:
                AddRapidToMPos(rapidto, "Z");
                if (sizeX > 0d)
                    AddInsideX();
                AddRapidToMPos(start, "Z");
                break;

            case Center.YInside:
                AddRapidToMPos(rapidto, "Z");
                if (sizeY > 0d)
                    AddInsideY();
                AddRapidToMPos(start, "Z");
                break;

            case Center.Outside:
                rapidto = rapidto.With(0, start.X - (sizeX / 2d + xyClearance))
                                 .With(1, start.Y - (sizeY / 2d + xyClearance));

                if (sizeY > 0d)
                    AddOutsideX();

                if (sizeX > 0d)
                    AddOutsideY();

                AddRapidToMPos(start, "Z");
                break;

            case Center.XOutside:
                rapidto = rapidto.With(0, start.X - (sizeX / 2d + xyClearance));
                if (sizeX > 0d)
                    AddOutsideX();
                AddRapidToMPos(start, "Z");
                break;

            case Center.YOutside:
                rapidto = rapidto.With(1, start.Y - (sizeY / 2d + xyClearance));
                if (sizeY > 0d)
                    AddOutsideY();
                AddRapidToMPos(start, "Z");
                break;
        }

        return this;
    }

    /// <summary>Rotation probe: two probe points along the selected edge.</summary>
    /// <summary>
    /// Rotation program: two probes along the selected edge, plus — when
    /// <paramref name="determineX0Y0"/> — a third probe on the left edge
    /// (AB only, ioSender's AddActionEdge) whose touch position lets the VM
    /// compute the workpiece corner (see <see cref="ProbeMath.ComputeCorner"/>).
    /// </summary>
    public ProbeProgramBuilder BuildRotation(Edge edge, Pos start, bool determineX0Y0 = false)
    {
        _program.Clear();
        Add($"G91F{ProbeFeedRate.ToString("0.###", CultureInfo.InvariantCulture)}");

        var xyClearance = XYClearance + ProbeRadius;
        var af = new double[3];

        switch (edge)
        {
            case Edge.AD: af[0] = 1.0d; af[1] = -1.0d; break;
            case Edge.AB: af[0] = 1.0d; af[1] = 1.0d; break;
            case Edge.CB: af[0] = -1.0d; af[1] = 1.0d; break;
            case Edge.CD: af[0] = -1.0d; af[1] = -1.0d; break;
            default: return this;
        }

        var offsetAxis = edge is Edge.AD or Edge.CB ? 1 : 0;
        var clearanceAxis = edge is Edge.AD or Edge.CB ? 0 : 1;
        var probeAxes = clearanceAxis == 0 ? "X" : "Y";
        var offsetAxes = offsetAxis == 0 ? "X" : "Y";

        var rapidto = start.With(clearanceAxis, start.Get(clearanceAxis) - xyClearance * af[clearanceAxis])
                           .With(2, start.Z - Depth);

        AddRapidToMPos(rapidto, probeAxes);
        AddRapidToMPos(rapidto, "Z");

        AddProbingAction(probeAxes, af[clearanceAxis] == -1.0d);

        AddRapidToMPos(rapidto, probeAxes);
        rapidto = rapidto.With(offsetAxis, start.Get(offsetAxis) + Offset * af[offsetAxis]);
        AddRapidToMPos(rapidto, offsetAxes);

        AddProbingAction(probeAxes, af[clearanceAxis] == -1.0d);

        AddRapidToMPos(rapidto, probeAxes);
        AddRapidToMPos(start, "Z");
        AddRapidToMPos(start, "XY");

        if (determineX0Y0)
        {
            if (edge != Edge.AB)
                throw new InvalidOperationException("X0/Y0 determination requires the AB edge");

            // Left-edge probe (ioSender AddActionEdge): the approach axis is
            // the edge probe's offset axis — perpendicular to the probed edge.
            var actionClearanceAxis = offsetAxis;   // 0 (X) for AB: approach from -X
            var actionOffsetAxis = clearanceAxis;   // 1 (Y) for AB: Offset past start
            var actionProbeAxes = actionClearanceAxis == 0 ? "X" : "Y";

            var actionRapid = start.With(actionClearanceAxis, start.Get(actionClearanceAxis) - xyClearance * af[actionClearanceAxis])
                                   .With(actionOffsetAxis, start.Get(actionOffsetAxis) + Offset * af[actionOffsetAxis])
                                   .With(2, start.Z - Depth);

            AddRapidToMPos(actionRapid, "XY");
            AddRapidToMPos(actionRapid, "Z");

            AddProbingAction(actionProbeAxes, af[actionClearanceAxis] == -1.0d);

            AddRapidToMPos(actionRapid, actionProbeAxes);
            AddRapidToMPos(start, "Z");
            AddRapidToMPos(start, "XY");
        }

        return this;
    }

    /// <summary>
    /// Height map program: probe a Z grid row by row in a boustrophedon (snake)
    /// path, retracting to the start Z between points.
    /// </summary>
    public ProbeProgramBuilder BuildHeightMap(HeightMap map, Pos start, bool addPause)
    {
        _program.Clear();
        Add($"G91F{ProbeFeedRate.ToString("0.###", CultureInfo.InvariantCulture)}");

        var dir = 1d;
        var point = 0;
        var points = map.SizeX * map.SizeY;

        for (var x = 0; x < map.SizeX; x++)
        {
            for (var y = 0; y < map.SizeY; y++)
            {
                AddMessage($"Probing point {++point} of {points}");
                if (addPause && (x > 0 || y > 0))
                    AddPause();

                AddProbingAction("Z", true);
                AddRapidToMPos(start, "Z");

                if (y < map.SizeY - 1)
                    AddRapid($"Y{(map.GridY * dir).ToString("0.###", CultureInfo.InvariantCulture)}");
            }

            if (x < map.SizeX - 1)
                AddRapid($"X{map.GridX.ToString("0.###", CultureInfo.InvariantCulture)}");

            dir *= -1d;
        }

        return this;
    }
}
