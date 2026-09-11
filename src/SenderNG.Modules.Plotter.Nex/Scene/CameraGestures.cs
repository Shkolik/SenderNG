/*
 * CameraGestures.cs - pure camera basis + pan math for the plotter cameras
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
using HelixToolkit.Nex.Engine.Cameras;

namespace SenderNG.Modules.Plotter.Nex.Scene;

/// <summary>Pure screen↔world navigation math for the plotter cameras.
/// No GPU, no scene — operates directly on engine camera objects so the
/// pixel-scale and orbit conventions are unit-testable headless.</summary>
public static class CameraGestures
{
    /// <summary>Camera-side basis from direction of view: forward toward the
    /// target, right, and planar up (perpendicular to forward). Degenerate
    /// views (target at the camera, or up parallel to forward) resolve to a
    /// fixed right-handed basis instead of NaNs.</summary>
    public static void GetCameraBasis(Camera camera, out Vector3 forward, out Vector3 right, out Vector3 up)
    {
        var toTarget = camera.Target - camera.Position;
        if (toTarget.LengthSquared() < 1e-12f)
            forward = -Vector3.UnitZ;
        else
            forward = Vector3.Normalize(toTarget);
        var side = Vector3.Cross(forward, camera.Up);
        if (side.LengthSquared() < 1e-12f)
            right = Vector3.Normalize(Vector3.Cross(forward, ReferenceAxis(forward)));
        else
            right = Vector3.Normalize(side);
        up = Vector3.Normalize(Vector3.Cross(right, forward));
    }

    /// <summary>Unit axis with the smallest-magnitude component against
    /// <paramref name="v"/>, guaranteeing a non-zero cross product even for
    /// axis-aligned vectors (a fixed UnitY reference degenerates for Y-axial
    /// views).</summary>
    private static Vector3 ReferenceAxis(Vector3 v)
    {
        var ax = MathF.Abs(v.X);
        var ay = MathF.Abs(v.Y);
        var az = MathF.Abs(v.Z);
        if (ax <= ay && ax <= az)
            return Vector3.UnitX;
        if (ay <= az)
            return Vector3.UnitY;
        return Vector3.UnitZ;
    }

    /// <summary>World delta for a "drag the model" pan in an orthographic
    /// view. Drag right/down moves the model right/down on screen.</summary>
    public static Vector3 ScreenPanDelta(OrthographicCamera camera, Vector2 deltaPx, float viewportWpx, float viewportHpx)
    {
        if (viewportWpx < 1f || viewportHpx < 1f)
            return Vector3.Zero;
        var wpp = camera.Width / viewportWpx;
        GetCameraBasis(camera, out _, out var right, out var up);
        return (right * deltaPx.X - up * deltaPx.Y) * wpp;
    }

    /// <summary>World delta for a "drag the model" pan in a perspective view.
    /// Per-pixel world scale at the target plane: vertical FOV in radians,
    /// same per-pixel scale for both screen axes.</summary>
    public static Vector3 ScreenPanDelta(PerspectiveCamera camera, Vector2 deltaPx, float viewportWpx, float viewportHpx)
    {
        if (viewportWpx < 1f || viewportHpx < 1f || camera.Fov <= 0f)
            return Vector3.Zero;
        var dist = (camera.Target - camera.Position).Length();
        var wpp = 2f * dist * MathF.Tan(camera.Fov * 0.5f) / viewportHpx;
        GetCameraBasis(camera, out _, out var right, out var up);
        return (right * deltaPx.X - up * deltaPx.Y) * wpp;
    }
}