/*
 * Machine.cs - part of CNC Core library
 *
 * v0.44 / 2023-11-30 / Io Engineering (Terje Io)
 *
 */

/*

Copyright (c) 2020-2023, Io Engineering (Terje Io)
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

using System.Collections.Generic;
using System.Linq;
using CNC.GCode;
using CNC.Core.Services;
// portable: removed Media3D

namespace CNC.Core
{
    public class Machine
    {
        protected double _rpm = 0d;
        protected bool isRelative = false;

        protected int _tool = 0;
        protected double[] offsets = new double[9];
        protected double[] origin = new double[9];
        protected double[] scaleFactors = new double[9];
        protected double[] toolOffsets = new double[9];
        protected List<CoordinateSystem> coordinateSystems = new List<CoordinateSystem>();
        protected CoordinateSystem? coordinateSystem, g28, g30, g92;
        protected List<Tool> toolTable = new List<Tool>();
        protected Point6D machinePos = new Point6D();

        /// <summary>
        /// Fixed post-reset defaults. These never vary with controller state; they are
        /// named here so the reset path reads as a table rather than as live sync.
        /// </summary>
        private static class ResetDefaults
        {
            public const string WorkOffset = "G54";
            public const bool IsMetric = true;
            public const DistanceMode DistanceMode = CNC.GCode.DistanceMode.Absolute;
            public const LatheMode LatheMode = CNC.GCode.LatheMode.Disabled;
            public const CoolantState CoolantState = CNC.GCode.CoolantState.Off;
            public const SpindleState SpindleState = CNC.GCode.SpindleState.Off;
            public const ToolLengthOffset ToolLengthOffset = CNC.GCode.ToolLengthOffset.Cancel;
            public const Plane Plane = CNC.GCode.Plane.XY;
            public const FeedRateMode FeedRateMode = CNC.GCode.FeedRateMode.UnitsPerMin;
            public const IJKMode IJKMode = CNC.GCode.IJKMode.Incremental;
            public const MotionMode MotionMode = CNC.GCode.MotionMode.G0;
        }

        public void Reset()
        {
            // The values written below come from the fixed ResetDefaults table in
            // this port. Live controller data arrives through WorkParametersProvider
            // (registered by the host from IGrblService.WorkParametersChanges); when
            // it is null the emulator starts from an empty table, which is the
            // pre-bridge behavior.
            WorkParameters? seed = WorkParametersProvider?.Invoke();

            coordinateSystems.Clear();
            if (seed is not null)
                foreach (WorkCoordinateSystem cs in seed.CoordinateSystems)
                {
                    var legacy = new CoordinateSystem(cs.Code, string.Empty)
                    {
                        Id = WorkParameters.CoordinateSystemNumber(cs.Code)
                    };
                    int n = System.Math.Min(cs.Values.Length, legacy.Values.Length);
                    for (int i = 0; i < n; i++)
                        legacy.Values[i] = cs.Values[i];
                    coordinateSystems.Add(legacy);
                }

            toolTable.Clear();
            if (seed is not null)
                foreach (ToolTableEntry t in seed.Tools)
                {
                    var legacy = new Tool(t.Id.ToString());
                    int n = System.Math.Min(t.Offsets.Length, legacy.Values.Length);
                    for (int i = 0; i < n; i++)
                        legacy.Values[i] = t.Offsets[i];
                    legacy.R = t.Radius;
                    toolTable.Add(legacy);
                }

            coordinateSystem = coordinateSystems.Where(x => x.Code == (seed?.ActiveCoordinateSystem ?? ResetDefaults.WorkOffset)).FirstOrDefault();
            g28 = coordinateSystems.Where(x => x.Code == "G28").FirstOrDefault();
            g30 = coordinateSystems.Where(x => x.Code == "G30").FirstOrDefault();
            g92 = coordinateSystems.Where(x => x.Code == "G92").FirstOrDefault();

            isRelative = ResetDefaults.DistanceMode == DistanceMode.Incremental;
            IsImperial = !ResetDefaults.IsMetric;
            CoolantState = ResetDefaults.CoolantState;
            SpindleState = ResetDefaults.SpindleState;
            LatheMode = ResetDefaults.LatheMode;
            LatheUVWMode = false;
            DistanceMode = ResetDefaults.DistanceMode;
            ToolLengthOffset = ResetDefaults.ToolLengthOffset;
            FeedRateMode = ResetDefaults.FeedRateMode;
            MotionMode = ResetDefaults.MotionMode;
            IJKMode = ResetDefaults.IJKMode;
            IsScaled = false;

            // F, S and T have always been zero here, and did not get there by
            // parsing anything: the IsActive lookup this used to consult was a stub
            // that always returned null, and the parser-state Tool word was never
            // assigned, so it only ever held GrblConstants.NO_TOOL. The
            // double.Parse/int.Parse that used to sit behind those checks were
            // unreachable, so they have been dropped rather than guarded. Whoever
            // fills in the parser state has to read the F, S and T words here for
            // the first time.
            Feedrate = 0d;
            _rpm = 0d;
            _tool = 0;

            SelectedTool = null;
            RetractOldZ = true;
            SpindleRpmMode = true;
            G92Active = false;

            Line = 0;

            foreach (int i in AxisFlags.All.ToIndices())
            {
                offsets[i] = coordinateSystem == null ? 0d :  coordinateSystem.Values[i];
                origin[i] = g92 == null ? 0d : g92.Values[i];
                machinePos[i] = 0d;
                scaleFactors[i] = 1d;
                toolOffsets[i] = SelectedTool == null ? 0d : SelectedTool.Values[i];
            }

            // Copied to a local so the plane mapping below stays a runtime switch:
            // switching on the const directly constant-folds it and CS0162 flags the
            // XZ/YZ arms as unreachable while the default plane is XY.
            Plane defaultPlane = ResetDefaults.Plane;
            switch (defaultPlane)
            {
                case GCode.Plane.XY:
                    Plane = new GCPlane(Commands.G17, 0);
                    break;

                case GCode.Plane.XZ:
                    Plane = new GCPlane(Commands.G18, 0);
                    break;

                case GCode.Plane.YZ:
                    Plane = new GCPlane(Commands.G19, 0);
                    break;
            }
        }

        public int Line { get; protected set; }
        public int CoordSystem {
            get {
                return coordinateSystem == null ? 0 : coordinateSystem.Id;
            }
            set
            {
                coordinateSystem = coordinateSystems.Where(x => x.Id == value).FirstOrDefault();
            }
        }
        public MotionMode MotionMode { get; protected set; }
        public FeedRateMode FeedRateMode { get; protected set; }
        public DistanceMode DistanceMode
        {
            get { return isRelative ? DistanceMode.Incremental : DistanceMode.Absolute; }
            set { isRelative = value == DistanceMode.Incremental; }
        }
        public GCPlane Plane { get; protected set; } = new GCPlane(Commands.G17, 0);
        public IJKMode IJKMode { get; protected set; }
        public LatheMode LatheMode { get; protected set; }
        public bool LatheUVWMode { get; protected set; }
        public CoolantState CoolantState { get; protected set; }
        public SpindleState SpindleState { get; protected set; }
        public ToolLengthOffset ToolLengthOffset { get; protected set; }
        public bool SpindleRpmMode { get; protected set; }
        public double SpindleRPM { get { return SpindleState == SpindleState.Off ? 0d : _rpm; } }
        public double Feedrate { get; protected set; }
        public int Tool {
            get { return _tool; }
            protected set
            {
                _tool = value;
                if (toolTable.Count > 0)
                    SelectedTool = toolTable.Where(t => t.Code == _tool.ToString()).FirstOrDefault();
            }
        }
        public bool RetractOldZ { get; protected set; }
        public Tool? SelectedTool { get; protected set; }
        public bool IsImperial { get; protected set; }
        public bool IsScaled { get; protected set; }
        public double GetScaleFactor(int axis)
        {
            return axis < GrblInfo.NumAxes ? scaleFactors[axis] : 0d;
        }

        public double GetPosition(int axis)
        {
            return machinePos[axis];
        }

        public double GetG28Position (int axis)
        {
            return g28 != null && axis < GrblInfo.NumAxes ? g28.Values[axis] : 0d;
        }
        public double GetG30Position(int axis)
        {
            return g30 != null && axis < GrblInfo.NumAxes ? g30.Values[axis] : 0d;
        }

        /// <summary>
        /// Live probe result from the WorkParameters snapshot (used by NGCExpr PRB
        /// expressions). NaN when no probe data has been reported.
        /// </summary>
        public double GetProbePosition(int axis)
        {
            double[]? probe = WorkParametersProvider?.Invoke()?.ProbePosition;
            return probe is not null && axis >= 0 && axis < probe.Length ? probe[axis] : double.NaN;
        }

        /// <summary>Whether the controller's last probe was successful (PRB expression).</summary>
        public bool ProbeSuccess => WorkParametersProvider?.Invoke()?.ProbeSuccess ?? false;

        /// <summary>
        /// Ambient source of controller work parameters for the emulator/expression
        /// engine. Registered once by the host (e.g. from IGrblService.WorkParametersChanges);
        /// returns the latest immutable snapshot or null when none has arrived yet.
        /// </summary>
        public static Func<WorkParameters?>? WorkParametersProvider { get; set; }
        public bool G92Active { get; protected set; }
        public void G92Clear()
        {
            if (g92 != null)
                g92.Clear();

            G92Active = false;
        }
        public double GetG92Offset(int axis)
        {
            return g92 != null && axis < GrblInfo.NumAxes ? g92.Values[axis] : 0d;
        }

        public bool SetG92Offset(GCCoordinateSystem token)
        {
            G92Active = g92 != null;
            if (g92 is not null)
                foreach (int i in token.AxisFlags.ToIndices())
                    g92.Values[i] = token.Values[i];

            return G92Active;
        }

        protected void setEndP(double[] values, AxisFlags axisFlags)
        {
            if(LatheUVWMode)
            {
                double[] target = (double[])values.Clone();
                if(axisFlags.HasFlag(AxisFlags.U)) {
                    axisFlags |= AxisFlags.X;
                    axisFlags &= ~AxisFlags.U;
                    target[GrblConstants.U_AXIS] /= 2d;
                    if (!isRelative)
                        target[GrblConstants.X_AXIS] = machinePos.X + target[GrblConstants.U_AXIS];
                    target[GrblConstants.U_AXIS] = 0d;
                }
                if (axisFlags.HasFlag(AxisFlags.V))
                {
                    axisFlags |= AxisFlags.Y;
                    axisFlags &= ~AxisFlags.V;
                    if (!isRelative)
                        target[GrblConstants.Y_AXIS] = machinePos.Y + target[GrblConstants.V_AXIS];
                    target[GrblConstants.V_AXIS] = 0d;
                }
                if (axisFlags.HasFlag(AxisFlags.W))
                {
                    axisFlags |= AxisFlags.Z;
                    axisFlags &= ~AxisFlags.W;
                    if(!isRelative)
                        target[GrblConstants.Z_AXIS] = machinePos.Z + target[GrblConstants.W_AXIS];
                    target[GrblConstants.W_AXIS] = 0d;
                }

                machinePos.Set(target, axisFlags, isRelative);
            }
            else
                machinePos.Set(values, axisFlags, isRelative);
        }

        public CoordinateSystem GetCoordSystem(int id)
        {
            return coordinateSystems[id];
        }

        public bool SetCoordinateSystem(GCCoordinateSystem token)
        {
            // G10 L2 P0 targets the active system (token.Code is "Current");
            // P10/P11/P12 address the G92/G28/G30 rows. Without seeded data both
            // resolve to null and the write is rejected (pre-bridge behavior).
            var csys = token.P == 0 ? coordinateSystem : coordinateSystems.Where(x => x.Code == token.Code).FirstOrDefault();

            if (csys != null)
                foreach (int i in token.AxisFlags.ToIndices())
                    csys.Values[i] = token.Values[i];

            return csys != null;
        }

        public double GetToolOffset(int axis)
        {
            return toolOffsets[axis];
        }

        public bool SetToolOffset(GCToolOffset token)
        {
            var tool = SelectedTool;
            if (token.H != 0)
                tool = toolTable.Where(t => t.Code == token.H.ToString()).FirstOrDefault();
            if (tool != null && tool.Code != 0.ToString())
                foreach (int i in AxisFlags.All.ToIndices())
                    toolOffsets[i] = tool.Values[i];

            return tool != null;
        }

        public bool AddToolOffset(GCToolOffset token)
        {
            var tool = toolTable.Where(t => t.Code == token.H.ToString()).FirstOrDefault();
            if (tool != null)
                foreach (int i in AxisFlags.All.ToIndices())
                    toolOffsets[i] += tool.Values[i];

            return tool != null;
        }

        public void DynamicToolOffset(GCToolOffsets token)
        {
            foreach (int i in token.AxisFlags.ToIndices())
                toolOffsets[i] -= token.Values[i];
        }

        public void CancelToolCompensation()
        {
            foreach (int i in AxisFlags.All.ToIndices())
                toolOffsets[i] = 0d;
        }

        public bool SetToolTable(GCToolTable token)
        {
            var tool = toolTable.Where(t => t.Code == token.P.ToString()).FirstOrDefault();
            if (tool != null)
            {
                foreach (int i in token.AxisFlags.ToIndices())
                {
                    tool.Values[i] = token.Values[i];
                    if(tool == SelectedTool)
                        toolOffsets[i] = tool.Values[i];
                }

                if (!double.IsNaN(token.R))
                    tool.R = token.R;
            }

            return tool != null;
        }
    }
}
