/*
 * ControlPointMesh.cs - headless control-point marker mesh builder
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
/// A CPU-built triangle mesh of small octahedral markers, one at every grid node
/// that carries a finite Z sample — the visible control points of a probe. Same
/// buffer shape as <see cref="HeightSurfaceMesh"/> (interleaved-free
/// <c>pos(3)+color(3)</c> plus a <c>uint[]</c> index list) so the scene uploads
/// it through the same per-vertex-color program.
/// </summary>
public sealed record ControlPointMesh(float[] Positions, float[] Colors, uint[] Indices)
{
    /// <summary>Fraction of the smaller grid cell a marker spans.</summary>
    public const float SizeFraction = 0.05f;

    private static readonly float[] MarkerColor = [0.08f, 0.08f, 0.08f];

    /// <summary>Build one octahedral marker per grid node whose Z is finite,
    /// sized relative to the smaller cell so it scales with the map. Returns
    /// empty buffers for a degenerate layer (no nodes, or a zero cell size).</summary>
    public static ControlPointMesh Build(HeightMapLayer layer, double zScale)
    {
        var cols = layer.Columns;
        var rows = layer.Rows;
        if (cols < 1 || rows < 1 || layer.Z.Count < (long)cols * rows)
            return new ControlPointMesh([], [], []);

        var size = SizeFraction * (float)Math.Min(Math.Abs(layer.CellX), Math.Abs(layer.CellY));
        if (!(size > 0f) || !float.IsFinite(size))
            return new ControlPointMesh([], [], []);

        var positions = new List<float>(cols * rows * 18);
        var colors = new List<float>(cols * rows * 18);
        var indices = new List<uint>(cols * rows * 24);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var z = layer.Z[row * cols + col];
                if (!double.IsFinite(z))
                    continue;
                AppendMarker(positions, colors, indices,
                    (float)(layer.OriginX + col * layer.CellX),
                    (float)(layer.OriginY + row * layer.CellY),
                    (float)(z * zScale),
                    size);
            }
        }

        return new ControlPointMesh(positions.ToArray(), colors.ToArray(), indices.ToArray());
    }

    private static void AppendMarker(List<float> positions, List<float> colors,
        List<uint> indices, float cx, float cy, float cz, float size)
    {
        var baseIndex = (uint)(positions.Count / 3);

        // Octahedron: top, bottom, then the four equator points (+X, -X, +Y, -Y).
        AppendVertex(positions, colors, cx, cy, cz + size);          // 0 top
        AppendVertex(positions, colors, cx, cy, cz - size);          // 1 bottom
        AppendVertex(positions, colors, cx + size, cy, cz);          // 2 +X
        AppendVertex(positions, colors, cx - size, cy, cz);          // 3 -X
        AppendVertex(positions, colors, cx, cy + size, cz);          // 4 +Y
        AppendVertex(positions, colors, cx, cy - size, cz);          // 5 -Y

        // Four upper faces (top + adjacent equator pair) and four lower faces.
        AddFace(indices, baseIndex, 0, 2, 4);
        AddFace(indices, baseIndex, 0, 4, 3);
        AddFace(indices, baseIndex, 0, 3, 5);
        AddFace(indices, baseIndex, 0, 5, 2);
        AddFace(indices, baseIndex, 1, 4, 2);
        AddFace(indices, baseIndex, 1, 3, 4);
        AddFace(indices, baseIndex, 1, 5, 3);
        AddFace(indices, baseIndex, 1, 2, 5);
    }

    private static void AppendVertex(List<float> positions, List<float> colors,
        float x, float y, float z)
    {
        positions.Add(x);
        positions.Add(y);
        positions.Add(z);
        colors.Add(MarkerColor[0]);
        colors.Add(MarkerColor[1]);
        colors.Add(MarkerColor[2]);
    }

    private static void AddFace(List<uint> indices, uint baseIndex, uint a, uint b, uint c)
    {
        indices.Add(baseIndex + a);
        indices.Add(baseIndex + b);
        indices.Add(baseIndex + c);
    }
}
