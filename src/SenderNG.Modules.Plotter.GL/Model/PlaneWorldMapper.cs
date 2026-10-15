/*
 * PlaneWorldMapper.cs - pure plane-local → world mapping helper for Plot 3D
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

namespace SenderNG.Modules.Plotter.GL.Model;

/// <summary>
/// Pure plane-local → world mapping. The mapping is <b>positional</b> — within a
/// plane slot <c>k</c> feeds world axis <c>k</c> — so for each slot <c>s</c>:
/// <c>world[s] = placement[s] + machinePos[planeAxisSlots[s]]</c>. The per-plane
/// <c>WorldAxes</c> table this class used to read is deleted; the only
/// per-plane input left is the plane's <see cref="PlacementFor"/> offset, which
/// is the machine model's single separation scalar applied on the world axis the
/// plane's slots do not span. World axes with no slot stay at the placement
/// value (0, since a plane with no free axis is never displaced).
/// </summary>
public static class PlaneWorldMapper
{
    /// <summary>World axes are X, Y, Z.</summary>
    public const int WorldAxisCount = 3;

    /// <summary>
    /// The plane's placement in world millimetres: <c>SeparationFor(plane)</c>
    /// applied on the world axis the plane's slots do <b>not</b> span. Slot
    /// <c>k</c> feeds world axis <c>k</c>, so a plane with <paramref name="slots"/>
    /// slots occupies world axes <c>0..slots-1</c> and the free axis is the next
    /// one — world Z for the two-slot foam tower, which is exactly the wire
    /// direction the two parallel planes leave free. Plane 0 and every
    /// single-plane machine resolve to a separation of 0, so they sit at the
    /// origin; a plane that spans all three world axes has no free axis and is
    /// never displaced.
    /// </summary>
    public static Vector3 PlacementFor(MachineKinematics? kinematics, int plane, int slots)
    {
        var separation = kinematics is null ? 0 : kinematics.SeparationFor(plane);
        if (separation == 0 || slots <= 0 || slots >= WorldAxisCount)
            return Vector3.Zero;
        // slots == 1 leaves world Y free (the lowest axis the single slot does not
        // span); slots == 2 leaves world Z.
        return slots == 1
            ? new Vector3(0f, (float)separation, 0f)
            : new Vector3(0f, 0f, (float)separation);
    }

    /// <summary>
    /// Maps one machine position into world coordinates through a plane's
    /// <paramref name="placement"/>: slot <c>k</c> feeds world axis <c>k</c>. A
    /// slot that names an axis the reported position does not carry is left at
    /// the placement value, never treated as a zero position — the caller
    /// checks the plane's highest index first (see
    /// <c>PlotterGlViewModel</c>), and this is the belt to that brace.
    /// </summary>
    public static Vector3 ToWorld(
        Vector3 placement, IReadOnlyList<double> machinePos, int[] planeAxisSlots)
    {
        var world = new[] { (double)placement.X, (double)placement.Y, (double)placement.Z };
        for (var slot = 0; slot < planeAxisSlots.Length && slot < WorldAxisCount; slot++)
        {
            var machineAxis = planeAxisSlots[slot];
            if (machineAxis < 0 || machineAxis >= machinePos.Count)
                continue;
            world[slot] += machinePos[machineAxis];
        }
        return new Vector3((float)world[0], (float)world[1], (float)world[2]);
    }
}
