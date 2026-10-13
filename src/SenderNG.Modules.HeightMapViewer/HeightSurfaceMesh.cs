/*
 * HeightSurfaceMesh.cs - headless height-surface mesh builder
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

using SenderNG.Core.Modules;

namespace SenderNG.Modules.HeightMapViewer;

/// <summary>
/// A CPU-built triangle mesh for a <see cref="HeightMapLayer"/>, in the same
/// shape <c>SenderNG.Modules.Common.Render.GlMeshBuilder</c> emits: interleaved
/// <c>pos(3)+color(3)</c> float buffers plus a <c>uint[]</c> triangle-index list.
/// <para>
/// One vertex per grid node at <c>(OriginX + col*CellX, OriginY + row*CellY,
/// z*zScale)</c>, row-major. Cells with any <see cref="double.NaN"/> corner emit
/// no triangles, so <see cref="Indices"/> never references a NaN vertex. Vertex
/// colors are a blue→red gradient over the layer's min/max Z.
/// </para>
/// <para>
/// This builder is pure/headless: it touches no Avalonia or GL types and is safe
/// to unit-test off the UI thread.
/// </para>
/// </summary>
public sealed record HeightSurfaceMesh(float[] Positions, float[] Colors, uint[] Indices)
{
    /// <summary>Build the height-surface mesh from <paramref name="layer"/>, scaling
    /// sample Z by <paramref name="zScale"/>. Returns empty buffers for a degenerate
    /// layer (fewer than two rows or columns).</summary>
    public static HeightSurfaceMesh Build(HeightMapLayer layer, double zScale)
    {
        var cols = layer.Columns;
        var rows = layer.Rows;
        var positions = new float[cols * rows * 3];
        var colors = new float[cols * rows * 3];
        var indices = new List<uint>();

        if (cols < 2 || rows < 2)
            return new HeightSurfaceMesh(positions, colors, indices.ToArray());

        var (minZ, maxZ) = ZRange(layer);
        var span = maxZ > minZ ? maxZ - minZ : 1.0;

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var v = row * cols + col;
                var z = layer.Z[v];
                positions[v * 3 + 0] = (float)(layer.OriginX + col * layer.CellX);
                positions[v * 3 + 1] = (float)(layer.OriginY + row * layer.CellY);
                positions[v * 3 + 2] = (float)(z * zScale);
                var t = double.IsNaN(z) ? 0.0 : (z - minZ) / span;
                colors[v * 3 + 0] = (float)t;
                colors[v * 3 + 1] = 0f;
                colors[v * 3 + 2] = (float)(1.0 - t);
            }
        }

        for (var row = 0; row < rows - 1; row++)
        {
            for (var col = 0; col < cols - 1; col++)
            {
                var a = row * cols + col;
                var b = a + 1;
                var c = a + cols;
                var d = c + 1;
                if (double.IsNaN(layer.Z[a]) || double.IsNaN(layer.Z[b])
                    || double.IsNaN(layer.Z[c]) || double.IsNaN(layer.Z[d]))
                    continue;

                // CCW when viewed from +Z so the surface's front face is up.
                indices.Add((uint)a);
                indices.Add((uint)b);
                indices.Add((uint)c);
                indices.Add((uint)b);
                indices.Add((uint)d);
                indices.Add((uint)c);
            }
        }

        return new HeightSurfaceMesh(positions, colors, indices.ToArray());
    }

    private static (double Min, double Max) ZRange(HeightMapLayer layer)
    {
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        foreach (var z in layer.Z)
        {
            if (double.IsNaN(z)) continue;
            if (z < min) min = z;
            if (z > max) max = z;
        }
        return double.IsPositiveInfinity(min) ? (0.0, 0.0) : (min, max);
    }
}
