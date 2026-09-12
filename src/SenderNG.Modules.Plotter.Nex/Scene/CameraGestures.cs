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

    /// <summary>Ortho zoom anchoring the world point under the cursor: rescale
    /// Width by 1/factor and shift Position/Target so that world point stays at
    /// the same screen position. No-op on degenerate input; width clamped to
    /// [0.1, 100000].</summary>
    public static void AnchorZoom(OrthographicCamera camera, Vector2 screenPx, double factor, float viewportWpx, float viewportHpx)
    {
        if (factor <= 0 || viewportWpx < 1f || viewportHpx < 1f)
            return;
        GetCameraBasis(camera, out _, out var right, out var up);
        var wppOld = camera.Width / viewportWpx;
        var sxW = (screenPx.X - viewportWpx * 0.5f) * wppOld;
        var syW = (screenPx.Y - viewportHpx * 0.5f) * wppOld;
        var anchor = camera.Target + right * sxW - up * syW;

        var newW = Math.Clamp(camera.Width / (float)factor, 0.1f, 100000f);
        var wppNew = newW / viewportWpx;
        var sxW2 = (screenPx.X - viewportWpx * 0.5f) * wppNew;
        var syW2 = (screenPx.Y - viewportHpx * 0.5f) * wppNew;
        var newTarget = anchor - (right * sxW2 - up * syW2);

        camera.Position += newTarget - camera.Target;
        camera.Target = newTarget;
        camera.Width = newW;
    }

    /// <summary>Perspective zoom anchoring the world point under the cursor at
    /// target depth. Distance clamped to [0.05, 10000]; no-op on degenerate
    /// input.</summary>
    public static void AnchorZoom(PerspectiveCamera camera, Vector2 screenPx, double factor, float viewportWpx, float viewportHpx)
    {
        if (factor <= 0 || viewportWpx < 1f || viewportHpx < 1f || camera.Fov <= 0f)
            return;
        var offset = camera.Target - camera.Position;
        var dist = offset.Length();
        if (dist < 0.01f)
            return;
        GetCameraBasis(camera, out _, out var right, out var up);
        var wppOld = 2f * dist * MathF.Tan(camera.Fov * 0.5f) / viewportHpx;
        var sxW = (screenPx.X - viewportWpx * 0.5f) * wppOld;
        var syW = (screenPx.Y - viewportHpx * 0.5f) * wppOld;
        var anchor = camera.Target + right * sxW - up * syW;

        var newDist = Math.Clamp(dist / (float)factor, 0.05f, 10000f);
        var wppNew = 2f * newDist * MathF.Tan(camera.Fov * 0.5f) / viewportHpx;
        var sxW2 = (screenPx.X - viewportWpx * 0.5f) * wppNew;
        var syW2 = (screenPx.Y - viewportHpx * 0.5f) * wppNew;
        var newTarget = anchor - (right * sxW2 - up * syW2);

        var dir = Vector3.Normalize(offset);
        camera.Position = newTarget - dir * newDist;
        camera.Target = newTarget;
    }

    /// <summary>Derive orbit state from a camera: theta/XZ-plane-from-+Z,
    /// phi/from-+Y, radius — the stock OrbitCameraController convention.
    /// Used to resync the scene's orbit state after a preset reframe.</summary>
    public static void SphericalState(PerspectiveCamera camera, out float theta, out float phi, out float radius)
    {
        var offset = camera.Position - camera.Target;
        radius = offset.Length();
        if (radius < 1e-3f)
            radius = 1f;
        var normalized = offset / radius;
        phi = MathF.Acos(Math.Clamp(normalized.Y, -1f, 1f));
        theta = MathF.Atan2(normalized.X, normalized.Z);
    }

    /// <summary>Recompute camera Position/Up from orbit state, mirroring the
    /// stock controller's formulas exactly.</summary>
    public static void ApplySpherical(PerspectiveCamera camera, float theta, float phi, float radius)
    {
        phi = Math.Clamp(phi, 0.1f, MathF.PI - 0.1f);
        var sinPhi = MathF.Sin(phi);
        var cosPhi = MathF.Cos(phi);
        var sinTheta = MathF.Sin(theta);
        var cosTheta = MathF.Cos(theta);
        var offset = new Vector3(
            radius * sinPhi * sinTheta,
            radius * cosPhi,
            radius * sinPhi * cosTheta
        );
        camera.Position = camera.Target + offset;
        var right = new Vector3(cosTheta, 0f, -sinTheta);
        var offsetDir = offset / radius;
        var up = Vector3.Normalize(Vector3.Cross(offsetDir, right));
        if (up.LengthSquared() <= 1e-12f)
            up = Vector3.UnitY;
        camera.Up = up;
    }

    /// <summary>Orbit the camera around a fixed world <paramref name="origin"/>
    /// by a screen-pixel delta, ioSender/Helix-turntable style: left/right
    /// rotates about the camera-up axis (or the fixed model-up when
    /// <paramref name="useWorldUp"/> is true) so dragging right spins the model
    /// right; up/down rotates about the camera-right axis. Both the camera
    /// Target and Position orbit <paramref name="origin"/> together and the Up
    /// transforms with the rotation; angles are radians per pixel and are not
    /// clamped (full 360°).</summary>
    public static void RotateAroundPoint(PerspectiveCamera camera, Vector3 origin, Vector2 deltaPx, float sensitivity, bool invertY, bool useWorldUp)
    {
        if (sensitivity <= 0f)
            return;
        GetCameraBasis(camera, out _, out var right, out var up);

        var yaw = -deltaPx.X * sensitivity;
        var pitch = (invertY ? -1f : 1f) * deltaPx.Y * sensitivity;
        var axisX = useWorldUp ? Vector3.UnitY : up;
        var axisY = right;

        var q = Quaternion.CreateFromAxisAngle(axisX, yaw)
              * Quaternion.CreateFromAxisAngle(axisY, pitch);

        var relativeTarget = Vector3.Transform(origin - camera.Target, q);
        var relativePosition = Vector3.Transform(origin - camera.Position, q);

        camera.Target = origin - relativeTarget;
        camera.Position = origin - relativePosition;
        camera.Up = Vector3.Transform(camera.Up, q);
        if (camera.Up.LengthSquared() <= 1e-12f)
            camera.Up = Vector3.UnitY;
    }
}
