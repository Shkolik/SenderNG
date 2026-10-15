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
/// Pure plane-local → world mapping. Slot <c>k</c> feeds world axis
/// <c>worldAxes[k]</c>, which the machine model derives from that axis's own
/// direction (Width→X, Length→Y, Height→Z), so
/// <c>world[worldAxes[k]] = placement + machinePos[planeAxisSlots[k]]</c>. The
/// per-plane <c>WorldAxes</c> <i>setting</i> this class used to read is deleted —
/// the mapping belongs to the machine model, not to a view — and the one
/// per-plane input left here is the <see cref="PlacementFor"/> offset: the model's
/// single separation scalar applied on the world axis the plane does not span.
/// World axes with no slot stay at the placement value.
/// </summary>
public static class PlaneWorldMapper
{
    /// <summary>World axes are X, Y, Z.</summary>
    public const int WorldAxisCount = 3;

    /// <summary>
    /// The plane's placement in world millimetres: <c>SeparationFor(plane)</c>
    /// applied on the world axis the plane does <b>not</b> span
    /// (<see cref="MachineKinematics.FreeWorldAxisFor"/>). A foam tower spans
    /// Length × Height → world Y × Z, so it is displaced along world <b>X</b>: the
    /// two towers stand side by side, which is what they physically are — not
    /// stacked like shelves. Plane 0 and every single-plane machine resolve to a
    /// separation of 0 and sit at the origin; a plane that spans all three world
    /// axes (a mill) has no free axis and is never displaced.
    /// </summary>
    public static Vector3 PlacementFor(MachineKinematics? kinematics, int plane)
    {
        var separation = kinematics is null ? 0 : kinematics.SeparationFor(plane);
        var free = kinematics?.FreeWorldAxisFor(plane) ?? -1;
        if (separation == 0 || free < 0)
            return Vector3.Zero;
        return free switch
        {
            0 => new Vector3((float)separation, 0f, 0f),
            1 => new Vector3(0f, (float)separation, 0f),
            _ => new Vector3(0f, 0f, (float)separation),
        };
    }

    /// <summary>
    /// Maps one machine position into world coordinates through a plane's
    /// <paramref name="placement"/> and its <paramref name="worldAxes"/>: slot
    /// <c>k</c> feeds <c>worldAxes[k]</c>, which the machine model derives from the
    /// axis's own direction (Length→world Y, Height→world Z on a foam tower). A
    /// slot that names an axis the reported position does not carry is left at the
    /// placement value, never treated as a zero position — the caller checks the
    /// plane's highest index first (see <c>PlotterGlViewModel</c>), and this is the
    /// belt to that brace.
    /// </summary>
    public static Vector3 ToWorld(
        Vector3 placement, IReadOnlyList<double> machinePos, int[] planeAxisSlots,
        int[] worldAxes)
    {
        var world = new[] { (double)placement.X, (double)placement.Y, (double)placement.Z };
        for (var slot = 0; slot < planeAxisSlots.Length; slot++)
        {
            var worldAxis = slot < worldAxes.Length ? worldAxes[slot] : -1;
            var machineAxis = planeAxisSlots[slot];
            if (worldAxis < 0 || worldAxis >= WorldAxisCount
                || machineAxis < 0 || machineAxis >= machinePos.Count)
                continue;
            world[worldAxis] += machinePos[machineAxis];
        }
        return new Vector3((float)world[0], (float)world[1], (float)world[2]);
    }
}
