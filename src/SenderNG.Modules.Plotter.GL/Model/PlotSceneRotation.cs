/*
 * PlotSceneRotation.cs - display-only scene rotation that keeps the floor grid behind the work
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
/// The display-only rotation a resolved machine needs so its floor grid does
/// not sit between the fixed ISO eye and the work. The camera and the machine
/// frame are deliberately untouched: this only reorients the drawn scene.
/// <para>
/// The default ISO eye looks from <c>-normalize(1,1,-1)</c>, so a grid on the
/// low end of world X or Y stands in front of the model. A stock mill and a
/// horizontal-wire foam machine floor on world Z and are already behind — they
/// get <see cref="Matrix4x4.Identity"/>. The two shapes that need help:
/// </para>
/// <list type="bullet">
/// <item><b>Vertical wire (EDM):</b> the wire is world Z and the grid falls on
/// a horizontal axis. A 180° turn about the <i>other</i> horizontal axis both
/// moves the grid to the far side and swaps the two working planes, so plane 0
/// ends up at the bottom. The work is still readable because each plane is
/// labelled at its own origin;</item>
/// <item><b>Vertical-grid mill:</b> a 90° CCW turn about world Z moves a
/// Y-normal (or X-normal) grid behind the content while keeping the height
/// axis upright.</item>
/// </list>
/// </summary>
public static class PlotSceneRotation
{
    /// <summary>The rotation for <paramref name="kinematics"/>, given the grid
    /// normal axis its <see cref="MachineChrome"/> resolved (0 = X, 1 = Y,
    /// 2 = Z). Identity when the floor is already behind the eye.</summary>
    public static Matrix4x4 For(MachineKinematics kinematics, int gridNormalAxis)
    {
        if (kinematics is null || gridNormalAxis == 2)
            return Matrix4x4.Identity;

        // A Z-normal floor (stock mill, horizontal-wire foam) sits below the
        // eye and needs nothing; only a horizontal grid is in the way.
        if (kinematics.MachineTypeId == MachineTypes.Mill)
        {
            // The quill axis is normally the height (Z) and the grid is the
            // floor; a yaw here is for a remapped mill whose grid is vertical.
            return Matrix4x4.CreateRotationZ(MathF.PI / 2f);
        }

        var wireAxis = kinematics.FreeWorldAxisFor(0);
        if (wireAxis == 2)
        {
            // Vertical wire: turn about the remaining horizontal axis, which
            // is the one neither the wire nor the grid normal uses.
            var axis = 3 - 2 - gridNormalAxis;
            return axis == 0
                ? Matrix4x4.CreateRotationX(MathF.PI)
                : Matrix4x4.CreateRotationY(MathF.PI);
        }

        // Horizontal wire, horizontal grid: a half turn about the vertical
        // axis flips the grid normal and leaves the floor below the eye.
        return Matrix4x4.CreateRotationZ(MathF.PI);
    }
}
