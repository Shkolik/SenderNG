/*
 * MachineChrome.cs - the chrome (gnomon, grid plane, plunge) a machine's own
 * letters imply for the 3D plotter
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
/// The chrome a resolved machine implies — geometry untouched, these only make
/// the chrome speak the machine's own letters:
/// <list type="bullet">
/// <item><see cref="Arrows"/> — one per world direction (named tuples: start
/// point, world direction, and the machine letter naming the arrow, which
/// drives its color; <c>"?"</c> = no machine axis rides that direction, drawn
/// gray), each placed at the gnomon point (the free-axis midpoint for a
/// multi-plane machine, so foam's origin sits <i>between</i> the towers);</item>
/// <item><see cref="GridNormalAxis"/>/<see cref="GridNormalSign"/> — the grid
/// spans the plane perpendicular to the machine's driving axis: a single-plane
/// machine with a perpendicular slot (a mill) is driven by that slot — its
/// quill — so the grid lies on the work pair's own worlds (slots 0 × 1); a
/// two-slot topology is driven by the Height-role axis (world Z; with no such
/// axis, the Width-role axis instead). The sign is the driving axis's own: an
/// inverted axis puts the machine's floor at the opposite end (its values run
/// downward), so the grid end and the plunge both flip with it;</item>
/// <item><see cref="PlungeDirection"/> — the single-point (mill) tool cone's
/// opening direction: toward the spindle along the driving axis (the mill's
/// perpendicular slot, else Height), flipped for an inverted one.</item>
/// </list>
/// </summary>
public sealed record MachineChrome(
    IReadOnlyList<(Vector3 Pos, Vector3 Direction, string Letter)> Arrows,
    int GridNormalAxis,
    int GridNormalSign,
    Vector3 PlungeDirection)
{
    /// <summary>Derive the chrome from the resolved kinematics.</summary>
    public static MachineChrome For(MachineKinematics kinematics)
    {
        var pos = GnomonPos(kinematics);
        var arrows = new List<(Vector3 Pos, Vector3 Direction, string Letter)>(3);
        for (var world = 0; world < 3; world++)
        {
            // The first axis in plane-then-slot order names the direction; a
            // second tower axis feeding the same world direction still moves
            // there, it just does not rename the arrow.
            var letter = "?";
            foreach (var axis in kinematics.Planes.SelectMany(p => p.Axes))
            {
                if (MachineKinematics.WorldAxisForDirection(axis.Direction) != world)
                    continue;
                letter = kinematics.LetterFor(axis.Index).ToString();
                break;
            }
            arrows.Add((pos, WorldUnit(world), letter));
        }

        var height = FirstWithDirection(kinematics, AxisDirection.Height);
        var width = FirstWithDirection(kinematics, AxisDirection.Width);
        // A single-plane machine with a perpendicular slot (a mill) is driven by
        // that slot — its quill: the grid spans the work pair's own worlds
        // (slots 0 × 1) and the cone lies along slot 2, wherever the machine's
        // letters put them. Two-slot topologies (foam/EDM towers) have no
        // perpendicular slot and keep the Height — else Width — perpendicular
        // grid.
        var perpendicular = kinematics.Planes.Count == 1 && kinematics.Planes[0].Axes.Count >= 3
            ? kinematics.Planes[0].Axes[2]
            : null;
        int gridAxis, gridSign;
        if (perpendicular is not null)
        {
            gridAxis = MachineKinematics.WorldAxisForDirection(perpendicular.Direction);
            gridSign = perpendicular.Sign < 0 ? -1 : 1;
        }
        else if (height is not null)
        {
            gridAxis = MachineKinematics.WorldAxisForDirection(AxisDirection.Height);
            gridSign = height.Sign < 0 ? -1 : 1;
        }
        else if (width is not null)
        {
            gridAxis = MachineKinematics.WorldAxisForDirection(AxisDirection.Width);
            gridSign = width.Sign < 0 ? -1 : 1;
        }
        else
        {
            // No Height and no Width to orient on at all — keep the classic
            // world-XY floor for whatever topology this is.
            gridAxis = 2;
            gridSign = 1;
        }

        var plunge = perpendicular is not null
            ? (perpendicular.Sign < 0 ? -1f : 1f) * WorldUnit(gridAxis)
            : height is not null
                ? (height.Sign < 0 ? -1f : 1f) * WorldUnit(gridAxis)
                : Vector3.UnitZ;

        return new MachineChrome(arrows, gridAxis, gridSign, plunge);
    }

    /// <summary>
    /// The gnomon anchor: the origin, moved along the free axis to the midpoint
    /// of the plane placements for a multi-plane machine (foam towers stand at
    /// ±separation/2 around it, so the origin sits <i>between</i> them).
    /// </summary>
    private static Vector3 GnomonPos(MachineKinematics kinematics)
    {
        if (kinematics.Planes.Count < 2)
            return Vector3.Zero;
        var free = kinematics.FreeWorldAxisFor(0);
        if (free < 0)
            return Vector3.Zero;
        var min = float.PositiveInfinity;
        var max = float.NegativeInfinity;
        for (var plane = 0; plane < kinematics.Planes.Count; plane++)
        {
            var v = PlaneWorldMapper.PlacementFor(kinematics, plane)[free];
            min = MathF.Min(min, v);
            max = MathF.Max(max, v);
        }
        var mid = (min + max) * 0.5f;
        return free switch
        {
            0 => new Vector3(mid, 0f, 0f),
            1 => new Vector3(0f, mid, 0f),
            _ => new Vector3(0f, 0f, mid),
        };
    }

    private static MachineAxis? FirstWithDirection(MachineKinematics kinematics, AxisDirection direction)
    {
        foreach (var axis in kinematics.Planes.SelectMany(p => p.Axes))
            if (axis.Direction == direction)
                return axis;
        return null;
    }

    private static Vector3 WorldUnit(int world) => world switch
    {
        0 => Vector3.UnitX,
        1 => Vector3.UnitY,
        _ => Vector3.UnitZ,
    };
}
