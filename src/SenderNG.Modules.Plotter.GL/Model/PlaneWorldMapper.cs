/*
 * PlaneWorldMapper.cs - pure plane-local → world mapping helper for Plot 3D (NEX)
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
using SenderNG.Modules.Plotter.GL.Settings;

namespace SenderNG.Modules.Plotter.GL.Model;

/// <summary>
/// Pure plane-local → world mapping. For each slot <c>s</c>:
/// <c>world[g.WorldAxes[s]] = g.Origin[g.WorldAxes[s]] + machinePos[planeAxisSlots[s]]</c>;
/// world axes with no slot mapping stay at the captured <see cref="GlPlaneGeometry.Origin"/> value.
/// </summary>
public static class PlaneWorldMapper
{
    public static Vector3 ToWorld(GlPlaneGeometry g, IReadOnlyList<double> machinePos, int[] planeAxisSlots)
    {
        var world = new double[]
        {
            g.Origin.Length > 0 ? g.Origin[0] : 0,
            g.Origin.Length > 1 ? g.Origin[1] : 0,
            g.Origin.Length > 2 ? g.Origin[2] : 0,
        };
        for (var s = 0; s < g.WorldAxes.Length && s < planeAxisSlots.Length; s++)
        {
            var wa = g.WorldAxes[s];
            var slot = planeAxisSlots[s];
            if (wa < 0 || wa > 2 || slot < 0 || slot >= machinePos.Count)
                continue;
            world[wa] = (g.Origin.Length > wa ? g.Origin[wa] : 0) + machinePos[slot];
        }
        return new Vector3((float)world[0], (float)world[1], (float)world[2]);
    }
}