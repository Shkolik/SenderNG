/*
 * PlotSceneAbstractionTests.cs - contract-shape tests for the GL scene abstraction
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

using SenderNG.Modules.Common.Render;
using System.Numerics;
using SenderNG.Modules.Plotter.GL.Scene;

namespace SenderNG.Core.Tests;

public class PlotSceneAbstractionTests
{
    [Fact] public void ViewPreset_has_expected_members() =>
        Assert.Equal(new[] { ViewPreset.XY, ViewPreset.XZ, ViewPreset.YZ, ViewPreset.Isometric3D },
            Enum.GetValues<ViewPreset>());

    [Fact] public void RunState_marks_all_four_frontier_states() =>
        Assert.Equal(4, Enum.GetValues<RunState>().Length);

    [Fact] public void ToolShape_reserves_future_shapes_but_poc_is_cone()
    {
        Assert.True(Enum.IsDefined(typeof(ToolShape), ToolShape.Cone));
        Assert.True(Enum.IsDefined(typeof(ToolShape), ToolShape.Cylinder));
    }

    [Fact] public void LineSegment_and_Endpoint_are_plain_records()
    {
        var s = new LineSegment(new Vector3(0), new Vector3(1, 2, 3));
        Assert.Equal(new Vector3(1, 2, 3), s.To);
        Assert.True(EqualityComparer<LineSegment>.Default.Equals(s, s with { }));
        var e = new Endpoint([new Vector3(1), new Vector3(2, 2, 2)]);
        Assert.Equal(2, e.Points.Length);
    }

    [Fact] public void IPlotScene_member_set_compiles()
    {
        IPlotScene scene = new StubPlotScene();
        ICarriagePlaneLayer layer = new StubCarriagePlaneLayer();
        ILoftSurface loft = new StubLoftSurface();
        IPlotTool tool = new StubPlotTool();

        scene.SetViewPreset(ViewPreset.XY);
        scene.Pan(new PanDelta(new Vector3(1)));
        scene.ZoomAround(new Vector2(0.5f), 1.2);
        scene.SetMachineChrome(
            [(new Vector3(1, 0, 0), Vector3.UnitX, "X")], gridNormalAxis: 2, gridNormalSign: 1);
        scene.AddAxisArrows([(new Vector3(1, 0, 0), Vector3.UnitX, "X"), (new Vector3(0, 1, 0), Vector3.UnitY, "Y")]);
        scene.SetGrid(new BoundingBox(0, 0, 0, 10, 10, 0), 1, 5);
        scene.SetJobEnvelope(new BoundingBox(0, 0, 0, 50, 50, 25));
        var carriage = scene.AddCarriagePlane();
        var surface = scene.AddLoftSurface();
        var plotTool = scene.CreateTool(ToolShape.Cone);

        carriage.AddSegment(new LineSegment(new Vector3(0), new Vector3(1)), MotionType.Rapid);
        carriage.SetSegmentRunState(0, RunState.Executed);

        surface.SetWirePlane([new Vector3(0), new Vector3(1, 0, 0), new Vector3(1, 1, 0)]);

        plotTool.UpdatePosition(new Endpoint([new Vector3(0, 0, 1)]));
        var shape = plotTool.Shape;

        scene.Dispose();
        plotTool.Dispose();

        Assert.NotNull(carriage);
        Assert.NotNull(surface);
        Assert.NotNull(plotTool);
        Assert.Equal(ToolShape.Cone, shape);
    }

    private sealed class StubPlotScene : IPlotScene
    {
        public void SetViewPreset(ViewPreset preset) { }
        public void Pan(PanDelta delta) { }
        public void ZoomAround(Vector2 screenPoint, double factor) { }
        public void SetViewportSize(int wPx, int hPx) { }
        public void PanByScreenDelta(Vector2 screenDeltaPx) { }
        public void RotateByScreenDelta(Vector2 screenDeltaPx) { }
        public void BeginOrbit(Vector2 screenPointPx) { }
        public void EndOrbit() { }
        public void Reframe() { }
        public void SetMachineChrome(
            IReadOnlyList<(Vector3 Pos, Vector3 Direction, string Letter)> arrows,
            int gridNormalAxis, int gridNormalSign) { }
        public void AddAxisArrows(IReadOnlyList<(Vector3 Pos, Vector3 Direction, string Letter)> arrows) { }
        public void SetGrid(BoundingBox bounds, double minor, double major) { }
        public void SetOrigin(Vector3 origin) { }
        public void SetJobEnvelope(BoundingBox box) { }
        public ICarriagePlaneLayer AddCarriagePlane() => new StubCarriagePlaneLayer();
        public ILoftSurface AddLoftSurface() => new StubLoftSurface();
        public IPlotTool CreateTool(ToolShape shape) => new StubPlotTool();
        public void Dispose() { }
    }

    private sealed class StubCarriagePlaneLayer : ICarriagePlaneLayer
    {
        public void AddSegment(LineSegment seg, MotionType type) { }
        public void SetSegmentRunState(int segmentIndex, RunState state) { }
        public void RebuildExecuted() { }
    }

    private sealed class StubLoftSurface : ILoftSurface
    {
        public void SetWirePlane(Vector3[] polygon) { }
        public void SetLoftSheet(IReadOnlyList<Vector3> sideA, IReadOnlyList<Vector3> sideB) { }
    }

    private sealed class StubPlotTool : IPlotTool
    {
        public void UpdatePosition(Endpoint endpoints) { }
        public ToolShape Shape { get; set; }
        public Vector3 PlungeDirection { get; set; } = Vector3.UnitZ;
        public void Dispose() { }
    }
}
