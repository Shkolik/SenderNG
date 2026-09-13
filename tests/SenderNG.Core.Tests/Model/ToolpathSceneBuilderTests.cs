/*
 * ToolpathSceneBuilderTests.cs - tests for Toolpath → scene geometry assembly
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

using System.Numerics;
using CNC.Core.Services;
using SenderNG.Modules.Plotter.Nex.Model;
using SenderNG.Modules.Plotter.Nex.Scene;

namespace SenderNG.Core.Tests;

public class ToolpathSceneBuilderTests
{
    [Fact]
    public void PopulatePlane_maps_axes_and_flags_rapid()
    {
        var layer = new RecorderPlaneLayer();
        var segs = new[]
        {
            new ToolpathSegment(new[]{0.0,0,0}, new[]{10.0,5,0}, Rapid:false, LineNumber:1),
            new ToolpathSegment(new[]{10.0,5,0}, new[]{20.0,5,0}, Rapid:true,  LineNumber:2),
            new ToolpathSegment(new[]{20.0,5,0}, new[]{20.0,5,0}, Rapid:false, LineNumber:3),
        };
        var n = ToolpathSceneBuilder.PopulatePlane(layer, segs, new[]{0,1});
        Assert.Equal(2, n);
        Assert.Equal(2, layer.Added.Count);
        Assert.Equal(new Vector3(0,0,0), layer.Added[0].From);
        Assert.Equal(new Vector3(10,5,0), layer.Added[0].To);
        Assert.Equal(MotionType.Cut, layer.Added[0].Type);
        Assert.Equal(MotionType.Rapid, layer.Added[1].Type);
    }

    [Fact]
    public void ComputeBounds_tracks_min_max_across_axes()
    {
        var segs = new[]
        {
            new ToolpathSegment(new[]{0.0,10}, new[]{5.0,0}, Rapid:false, LineNumber:1),
        };
        var b = ToolpathSceneBuilder.ComputeBounds(segs);
        Assert.Equal(2, b.Length);
        Assert.Equal(new[]{0.0,5.0}, b[0]);
        Assert.Equal(new[]{0.0,10.0}, b[1]);
    }
}

/// <summary>Test double that records AddSegment calls for assertions.</summary>
public sealed class RecorderPlaneLayer : ICarriagePlaneLayer
{
    public List<(Vector3 From, Vector3 To, MotionType Type)> Added { get; } = [];
    public List<(int SegmentIndex, RunState State)> RunStateCalls { get; } = [];
    public List<int> RebuildCalls { get; } = [];
    private readonly Dictionary<int, RunState> _applied = new();

    public void AddSegment(LineSegment seg, MotionType type) =>
        Added.Add((seg.From, seg.To, type));

    // Mirrors NexCarriagePlaneLayer: re-applying an unchanged state records
    // nothing, so the recorder stays faithful to the real layer's no-op.
    public void SetSegmentRunState(int segmentIndex, RunState state)
    {
        if (_applied.TryGetValue(segmentIndex, out var last) && last == state)
            return;
        _applied[segmentIndex] = state;
        RunStateCalls.Add((segmentIndex, state));
    }

    public void RebuildExecuted() => RebuildCalls.Add(1);
}
