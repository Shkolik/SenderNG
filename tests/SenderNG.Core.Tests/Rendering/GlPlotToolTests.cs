/*
 * GlPlotToolTests.cs - tool cone orientation, park and wire dedup semantics
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
using SenderNG.Modules.Plotter.GL.Rendering;
using SenderNG.Modules.Plotter.GL.Scene;

namespace SenderNG.Core.Tests;

/// <summary>The Task 6 tool: the cone orients along the wire (assertion style
/// retyped onto the GL tool's internal model-matrix accessors —
/// InternalsVisibleTo), and the parked/dedup halves of <c>UpdatePosition</c> /
/// <c>Pump</c> keep their visible behavior. Gated by
/// <see cref="GlFactAttribute"/> /
/// <see cref="GlTheoryAttribute"/> like the other GPU
/// tests.</summary>
[Trait("Category", "GPU")]
public class GlPlotToolTests
{
    [GlFact]
    public void UpdatePosition_orients_cone_toward_first_endpoint()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var tool = scene.CreateTool(ToolShape.Cone);
        tool.UpdatePosition(new Endpoint(new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 10) }));
        var m = ((GlPlotTool)tool).ModelMatrix;                       // internal, for tests
        var apex = Vector3.Transform(new Vector3(0, 0, 0), m);        // cone apex at origin
        var tip = Vector3.Transform(new Vector3(0, 0, 1), m);         // +Z direction maps toward wire
        Assert.True((tip - apex).LengthSquared() > 0.9f);
        Assert.True(float.IsFinite(apex.X + apex.Y + apex.Z));
    }

    /// <summary>The multi-axis orientation theory: both cone model matrices
    /// must map local +Z away from / along the wire direction. GL-flavored:
    /// the direction is read off the matrix as
    /// <c>Transform(UnitZ) - Transform(Zero)</c> (the translation cancels).
    /// <c>[GlTheory]</c>, not a
    /// bare <c>[Theory]</c>: the rows create a GL context, so without the
    /// discovery-time gate they FAIL on GL-less machines where the
    /// <c>[GlFact]</c> siblings skip.</summary>
    [GlTheory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(-1f, 0f, 0f)]
    [InlineData(10f, 5f, -3f)]
    public void Cone_orientations_aim_each_cone_away_from_the_other_end(
        float x, float y, float z)
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var tool = (GlPlotTool)scene.CreateTool(ToolShape.Cone);
        var p0 = Vector3.Zero;
        var p1 = new Vector3(x, y, z);
        var d = Vector3.Normalize(p1 - p0);

        tool.UpdatePosition(new Endpoint(new[] { p0, p1 }));

        var mappedA = MappedDirection(tool.ModelMatrix);
        var mappedB = MappedDirection(tool.ModelMatrixB);
        Assert.True(Vector3.Distance(mappedA, -d) < 1e-4f, $"A got {mappedA}");
        Assert.True(Vector3.Distance(mappedB, d) < 1e-4f, $"B got {mappedB}");
    }

    [GlFact]
    public void Cone_orientations_follow_the_wire_direction_not_the_origin()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var tool = (GlPlotTool)scene.CreateTool(ToolShape.Cone);
        var p0 = new Vector3(-4f, 7f, 2f);
        var p1 = new Vector3(2f, 7f, 2f);
        var d = Vector3.Normalize(p1 - p0);

        tool.UpdatePosition(new Endpoint(new[] { p0, p1 }));

        var mappedA = MappedDirection(tool.ModelMatrix);
        var mappedB = MappedDirection(tool.ModelMatrixB);
        Assert.True(Vector3.Distance(mappedA, -d) < 1e-4f, $"A got {mappedA}");
        Assert.True(Vector3.Distance(mappedB, d) < 1e-4f, $"B got {mappedB}");
    }

    /// <summary>The park half of UpdatePosition: one endpoint keeps the primary
    /// cone on the point (mill) and switches
    /// the connector wire and second cone off; zero endpoints parks
    /// everything. GL parks by skipping the draw — no far-off ParkedPosition
    /// — so the observable state is the visibility flags plus the parked
    /// latch.</summary>
    [GlFact]
    public void UpdatePosition_parks_wire_and_second_cone_on_a_single_endpoint()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var tool = (GlPlotTool)scene.CreateTool(ToolShape.Cone);

        Assert.True(tool.Parked, "a fresh tool has no endpoints yet — parked");

        tool.UpdatePosition(new Endpoint(new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 10) }));
        Assert.False(tool.Parked);
        Assert.True(tool.WireVisible);
        Assert.True(tool.SecondConeVisible);

        var p = new Vector3(5f, -3f, 2f);
        tool.UpdatePosition(new Endpoint(new[] { p }));
        Assert.False(tool.Parked, "one endpoint parks the primary cone ON the point");
        Assert.False(tool.WireVisible, "no wire without a second endpoint");
        Assert.False(tool.SecondConeVisible, "no second cone without a second endpoint");
        Assert.Equal(Matrix4x4.CreateTranslation(p), tool.ModelMatrix);

        tool.UpdatePosition(new Endpoint(Array.Empty<Vector3>()));
        Assert.True(tool.Parked, "zero endpoints parks the whole tool");
    }

    /// <summary>The dedup half of UpdatePosition: idle status
    /// polls re-send the same endpoints tens of times a second, and only a
    /// change may mark the swap-pump. The gate's PendingMutations is the seam
    /// (one mark per real change).</summary>
    [GlFact]
    public void UpdatePosition_dedups_identical_endpoints()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var tool = (GlPlotTool)scene.CreateTool(ToolShape.Cone);
        var p0 = new Vector3(1f, 2f, 3f);
        var p1 = new Vector3(4f, 5f, 6f);

        tool.UpdatePosition(new Endpoint(new[] { p0, p1 }));
        Assert.Equal(1, tool.PendingMutations);

        tool.UpdatePosition(new Endpoint(new[] { p0, p1 }));          // identical — no mark
        Assert.Equal(1, tool.PendingMutations);

        tool.UpdatePosition(new Endpoint(new[] { p0, p1 + Vector3.UnitX }));
        Assert.Equal(2, tool.PendingMutations);
    }

    /// <summary>The Pump half: the dirty wire uploads once and
    /// the pending flag clears; a clean tool pumps nothing.</summary>
    [GlFact]
    public void Pump_uploads_the_dirty_wire_once()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var tool = (GlPlotTool)scene.CreateTool(ToolShape.Cone);

        tool.UpdatePosition(new Endpoint(new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 10) }));
        Assert.True(tool.WireUploadPending);

        tool.Pump();
        Assert.False(tool.WireUploadPending, "the granted pump must consume the dirty flag");

        tool.Pump();                                                  // clean — no upload
        Assert.False(tool.WireUploadPending);
    }

    /// <summary>An inverted height axis hands the tool a flipped plunge: the
    /// single-point cone must open along it, apex still on the point.</summary>
    [GlFact]
    public void Single_point_cone_opens_along_the_plunge_direction()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        var tool = (GlPlotTool)scene.CreateTool(ToolShape.Cone);

        Assert.Equal(Vector3.UnitZ, tool.PlungeDirection);            // default: +Z, identity

        tool.PlungeDirection = -Vector3.UnitZ;
        var p = new Vector3(1f, 2f, 3f);
        tool.UpdatePosition(new Endpoint(new[] { p }));

        Assert.True(Vector3.Distance(MappedDirection(tool.ModelMatrix), -Vector3.UnitZ) < 1e-4f);
        var apex = Vector3.Transform(Vector3.Zero, tool.ModelMatrix);
        Assert.True(Vector3.Distance(apex, p) < 1e-4f, "the apex stays on the point");
    }

    /// <summary>Local +Z direction of a cone model matrix, translation
    /// removed — the matrix form of <c>Vector3.Transform(UnitZ, q)</c>.</summary>
    private static Vector3 MappedDirection(Matrix4x4 m) =>
        Vector3.Transform(Vector3.UnitZ, m) - Vector3.Transform(Vector3.Zero, m);
}
