/*
 * ToolpathSceneBuilder.cs - pure static builder converting ToolpathBuilder output
 * into the headless scene abstraction's LineSegment stream
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
using SenderNG.Modules.Plotter.Nex.Scene;

namespace SenderNG.Modules.Plotter.Nex.Model;

/// <summary>
/// Pure static builder that converts <see cref="ToolpathBuilder"/> output into
/// the headless scene abstraction's <see cref="LineSegment"/> stream, feeding
/// an <see cref="IPlotScene"/>'s carriage plane layers.
/// </summary>
public static class ToolpathSceneBuilder
{
    /// <summary>
    /// Adds every non-degenerate segment as a <see cref="LineSegment"/> into the
    /// given plane layer, mapping axis indices per <c>planeAxisIndices</c>.
    /// Segments where no mapped axis moves by more than 1e-9 are dropped (mirrors
    /// Skia's per-plane slice degenerate-segment filter).
    /// </summary>
    /// <returns>Count of segments added (for run-state indexing).</returns>
    public static int PopulatePlane(
        ICarriagePlaneLayer layer,
        IReadOnlyList<ToolpathSegment> segments,
        int[] planeAxisIndices)
    {
        var count = 0;
        foreach (var seg in segments)
        {
            var from = new Vector3(
                (float)seg.From[planeAxisIndices[0]],
                (float)seg.From[planeAxisIndices[1]],
                planeAxisIndices.Length >= 3 ? (float)seg.From[planeAxisIndices[2]] : 0f);

            var to = new Vector3(
                (float)seg.To[planeAxisIndices[0]],
                (float)seg.To[planeAxisIndices[1]],
                planeAxisIndices.Length >= 3 ? (float)seg.To[planeAxisIndices[2]] : 0f);

            layer.AddSegment(new LineSegment(from, to), seg.Rapid ? MotionType.Rapid : MotionType.Cut);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Computes [min, max] per axis index over the whole toolpath (program frame),
    /// matching <c>PlotterViewModel.ComputeBounds</c>.
    /// Axes whose min > max after the walk (no segments) map to [0, 0].
    /// </summary>
    public static double[][] ComputeBounds(IReadOnlyList<ToolpathSegment> segments)
    {
        var count = segments.Count > 0 ? segments[0].From.Length : 0;
        var bounds = new double[count][];
        for (var i = 0; i < count; i++)
            bounds[i] = [double.MaxValue, double.MinValue];

        foreach (var s in segments)
        {
            for (var i = 0; i < count; i++)
            {
                if (s.From[i] < bounds[i][0]) bounds[i][0] = s.From[i];
                if (s.From[i] > bounds[i][1]) bounds[i][1] = s.From[i];
                if (s.To[i] < bounds[i][0]) bounds[i][0] = s.To[i];
                if (s.To[i] > bounds[i][1]) bounds[i][1] = s.To[i];
            }
        }

        for (var i = 0; i < count; i++)
            if (bounds[i][0] > bounds[i][1])
                bounds[i] = [0, 0];

        return bounds;
    }
}
