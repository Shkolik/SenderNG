/*
 * GlCameraGesturesTests.cs - retyped camera gesture tests over GlCamera
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

using SenderNG.Modules.Common.Render;
using System.Numerics;
using SenderNG.Modules.Plotter.GL.Rendering;
using SenderNG.Modules.Plotter.GL.Scene;

namespace SenderNG.Core.Tests;

public class GlCameraGesturesTests
{
    // Pose helpers: the ported assertions hardcode the source tests' dist/radius
    // of 100, so the shared pose sits at z = 100 like every original camera.
    private static GlCamera Ortho(float width = 100f) => new()
    {
        IsPerspective = false, Width = width,
        Position = new Vector3(0, 0, 100), Target = Vector3.Zero, Up = Vector3.UnitY,
    };

    private static GlCamera Persp(float fov = MathF.PI / 4f) => new()
    {
        IsPerspective = true, Fov = fov,
        Position = new Vector3(0, 0, 100), Target = Vector3.Zero, Up = Vector3.UnitY,
    };

    [Fact]
    public void Ortho_pan_maps_pixels_to_world_one_to_one_on_both_axes()
    {
        var cam = Ortho();

        var right = CameraGestures.ScreenPanDelta(cam, new Vector2(10f, 0f), 1000f, 500f);
        Assert.Equal(1f, right.X, 5);
        Assert.Equal(0f, right.Y, 5);
        Assert.Equal(0f, right.Z, 5);

        var down = CameraGestures.ScreenPanDelta(cam, new Vector2(0f, 20f), 1000f, 500f);
        Assert.Equal(0f, down.X, 5);
        Assert.Equal(-2f, down.Y, 5);
        Assert.Equal(0f, down.Z, 5);
    }

    [Fact]
    public void Perspective_pan_scale_follows_distance_and_fov()
    {
        var fov = 0.8f;
        var cam = Persp(fov);
        var wpp = 2f * 100f * MathF.Tan(fov * 0.5f) / 400f;

        var right = CameraGestures.ScreenPanDelta(cam, new Vector2(37f, 0f), 600f, 400f);
        Assert.Equal(37f * wpp, right.X, 4);
        Assert.Equal(0f, right.Y, 4);
        Assert.Equal(0f, right.Z, 4);
    }

    [Fact]
    public void Pan_degrades_to_zero_for_invalid_viewports_or_fov()
    {
        var cam = Persp(0f);
        Assert.Equal(Vector3.Zero, CameraGestures.ScreenPanDelta(cam, new Vector2(5f, 5f), 100f, 100f));
        Assert.Equal(Vector3.Zero, CameraGestures.ScreenPanDelta(cam, new Vector2(5f, 5f), 0f, 100f));
        Assert.Equal(Vector3.Zero, CameraGestures.ScreenPanDelta(cam, new Vector2(5f, 5f), 100f, 0f));
    }

    [Fact]
    public void Basis_reverts_to_minus_z_when_target_coincides_with_position()
    {
        var cam = new GlCamera
        {
            IsPerspective = false,
            Position = new Vector3(0f, 0f, 100f),
            Target = new Vector3(0f, 0f, 100f),
            Up = Vector3.UnitY,
            Width = 100f,
        };

        CameraGestures.GetCameraBasis(cam, out var forward, out var right, out var up);
        Assert.Equal(-Vector3.UnitZ, forward);
        Assert.True(float.IsFinite(right.Length()));
        Assert.True(float.IsFinite(up.Length()));
        Assert.Equal(1f, right.Length(), 5);
        Assert.Equal(1f, up.Length(), 5);
        Assert.Equal(0f, Vector3.Dot(right, up), 5);
    }

    [Fact]
    public void Basis_and_pan_stay_finite_when_up_is_parallel_to_forward()
    {
        var cam = new GlCamera
        {
            IsPerspective = false,
            Position = new Vector3(0f, 100f, 0f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            Width = 100f,
        };

        CameraGestures.GetCameraBasis(cam, out var forward, out var right, out var up);
        Assert.Equal(1f, forward.Length(), 5);
        Assert.Equal(1f, right.Length(), 5);
        Assert.Equal(1f, up.Length(), 5);
        Assert.Equal(0f, Vector3.Dot(right, up), 5);
        Assert.Equal(0f, Vector3.Dot(right, forward), 5);
        Assert.Equal(0f, Vector3.Dot(up, forward), 5);
        Assert.All(new[] { right.X, right.Y, right.Z, up.X, up.Y, up.Z }, f => Assert.True(float.IsFinite(f)));

        var delta = CameraGestures.ScreenPanDelta(cam, new Vector2(10f, 20f), 1000f, 500f);
        Assert.True(float.IsFinite(delta.X) && float.IsFinite(delta.Y) && float.IsFinite(delta.Z));
    }

    [Fact]
    public void Ortho_zoom_keeps_cursor_world_point_fixed_for_in_and_out()
    {
        var cam = Ortho();
        var cursor = new Vector2(700f, 300f);

        foreach (var factor in new[] { 1.4, 0.7 })
        {
            var before = WorldAtCursorOrtho(cam, cursor);
            CameraGestures.AnchorZoom(cam, cursor, factor, 1000f, 500f);
            var after = WorldAtCursorOrtho(cam, cursor);
            Assert.Equal(before.X, after.X, 4);
            Assert.Equal(before.Y, after.Y, 4);
            Assert.Equal(before.Z, after.Z, 4);
        }
    }

    [Fact]
    public void Ortho_zoom_clamps_width_at_bounds()
    {
        var cam = new GlCamera
        {
            IsPerspective = false,
            Position = new Vector3(0f, 0f, 100f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            Width = 1f,
        };
        CameraGestures.AnchorZoom(cam, new Vector2(10f, 10f), 1e9f, 100f, 100f);
        Assert.Equal(0.1f, cam.Width, 5);
        CameraGestures.AnchorZoom(cam, new Vector2(10f, 10f), 1e-9f, 100f, 100f);
        Assert.Equal(100000f, cam.Width, 1);
    }

    [Fact]
    public void Perspective_zoom_keeps_cursor_world_point_fixed()
    {
        var cam = Persp(0.8f);
        var cursor = new Vector2(650f, 350f);

        foreach (var factor in new[] { 1.4, 0.7 })
        {
            var before = WorldAtCursorPersp(cam, cursor);
            CameraGestures.AnchorZoom(cam, cursor, factor, 1000f, 500f);
            var after = WorldAtCursorPersp(cam, cursor);
            Assert.Equal(before.X, after.X, 4);
            Assert.Equal(before.Y, after.Y, 4);
            Assert.Equal(before.Z, after.Z, 4);
        }
    }

    [Fact]
    public void Zoom_degrades_to_noop_for_invalid_inputs()
    {
        var cam = Ortho();
        CameraGestures.AnchorZoom(cam, new Vector2(5f, 5f), 0f, 100f, 100f);
        Assert.Equal(100f, cam.Width, 5);

        var persp = Persp(0f);
        var pos = persp.Position;
        CameraGestures.AnchorZoom(persp, new Vector2(5f, 5f), 1.5, 100f, 100f);
        Assert.Equal(pos, persp.Position);
        Assert.Equal(Vector3.Zero, persp.Target);
    }

    [Fact]
    public void Spherical_state_round_trips_through_ApplySpherical()
    {
        var cam = new GlCamera
        {
            IsPerspective = true,
            Position = new Vector3(3f, 4f, 5f),
            Target = new Vector3(1f, 1f, 1f),
            Up = Vector3.UnitY,
            Fov = 0.8f,
        };
        CameraGestures.SphericalState(cam, out var theta, out var phi, out var radius);
        CameraGestures.ApplySpherical(cam, theta, phi, radius);

        Assert.Equal(3f, cam.Position.X, 4);
        Assert.Equal(4f, cam.Position.Y, 4);
        Assert.Equal(5f, cam.Position.Z, 4);
        Assert.Equal(1f, cam.Up.Length(), 4);
    }

    [Fact]
    public void RotateAroundPoint_right_drag_spins_model_right_and_preserves_radius()
    {
        var origin = Vector3.Zero;
        var cam = Persp(0.8f);
        CameraGestures.RotateAroundPoint(cam, origin, new Vector2(30f, 0f), 0.01f, invertY: true, useWorldUp: false);

        // Pure yaw about the (screen/camera) up axis from the base pose: the camera
        // swings toward -X so the model's right (+X) side turns to face the viewer.
        Assert.True(cam.Position.X < 0f, $"expected Position.X<0, got {cam.Position.X}");
        Assert.Equal(0f, cam.Position.Y, 4);
        var forward = Vector3.Normalize(cam.Target - cam.Position);
        Assert.True(forward.X > 0f, $"expected forward.X>0, got {forward.X}");
        Assert.Equal(100f, (origin - cam.Position).Length(), 4);
        Assert.Equal(origin.X, cam.Target.X, 4);
        Assert.Equal(origin.Y, cam.Target.Y, 4);
        Assert.Equal(origin.Z, cam.Target.Z, 4);
    }

    [Fact]
    public void RotateAroundPoint_pitch_is_unclamped_rounds_full_circle()
    {
        var origin = Vector3.Zero;
        var cam = Persp(0.8f);
        // The old clamp kept |Position.Y| <= radius * cos(0.1) ~= 99.5. Dragging
        // down (Δy>0) with invertY pitches about the camera-right axis; iterations
        // accumulate well past a full 360° orbit, proving nothing clamps.
        var crossedPole = false;
        for (var i = 0; i < 500 && !crossedPole; i++)
        {
            CameraGestures.RotateAroundPoint(cam, origin, new Vector2(0f, 10f), 0.005f, invertY: true, useWorldUp: false);
            if (cam.Position.Y > 99.5f)
                crossedPole = true;
        }
        Assert.True(crossedPole, "camera never passed the old clamped band (no-clamp broken)");
        Assert.True(float.IsFinite(cam.Position.X) && float.IsFinite(cam.Position.Y) && float.IsFinite(cam.Position.Z));
        Assert.Equal(100f, (origin - cam.Position).Length(), 2);
    }

    [Fact]
    public void RotateAroundPoint_origin_stays_fixed_under_cursor()
    {
        var cam = Persp(0.8f);
        var cursor = new Vector2(650f, 250f);
        var origin = TargetDepthWorld(cam, cursor);
        for (var i = 0; i < 40; i++)
        {
            CameraGestures.RotateAroundPoint(cam, origin, new Vector2(5f, 0f), 0.005f, invertY: true, useWorldUp: false);
            var screen = WorldToScreen(cam, origin);
            Assert.Equal(650f, screen.X, 1);
            Assert.Equal(250f, screen.Y, 1);
        }
    }

    [Fact]
    public void RotateAroundPoint_camera_up_yaw_differs_from_world_up_on_rolled_camera()
    {
        var camP = new GlCamera
        {
            IsPerspective = true,
            Position = new Vector3(10f, 0f, 0f),
            Target = Vector3.Zero,
            Up = new Vector3(0f, 0f, 1f),
            Fov = 0.8f,
        };
        var camQ = new GlCamera
        {
            IsPerspective = true,
            Position = camP.Position,
            Target = camP.Target,
            Up = camP.Up,
            Fov = camP.Fov,
        };
        var delta = new Vector2(20f, 0f);
        CameraGestures.RotateAroundPoint(camP, Vector3.Zero, delta, 0.01f, invertY: true, useWorldUp: false);
        CameraGestures.RotateAroundPoint(camQ, Vector3.Zero, delta, 0.01f, invertY: true, useWorldUp: true);
        Assert.NotEqual(camP.Position, camQ.Position);
    }

    private static Vector3 WorldAtCursorOrtho(GlCamera cam, Vector2 cursor)
    {
        CameraGestures.GetCameraBasis(cam, out _, out var r, out var u);
        var wpp = cam.Width / 1000f;
        var sxW = (cursor.X - 500f) * wpp;
        var syW = (cursor.Y - 250f) * wpp;
        return cam.Target + r * sxW - u * syW;
    }

    private static Vector3 WorldAtCursorPersp(GlCamera cam, Vector2 cursor)
    {
        var dist = (cam.Target - cam.Position).Length();
        var wpp = 2f * dist * MathF.Tan(cam.Fov * 0.5f) / 500f;
        CameraGestures.GetCameraBasis(cam, out _, out var r, out var u);
        var sxW = (cursor.X - 500f) * wpp;
        var syW = (cursor.Y - 250f) * wpp;
        return cam.Target + r * sxW - u * syW;
    }

    private static Vector3 TargetDepthWorld(GlCamera cam, Vector2 cursor)
    {
        var dist = (cam.Target - cam.Position).Length();
        var wpp = 2f * dist * MathF.Tan(cam.Fov * 0.5f) / 500f;
        CameraGestures.GetCameraBasis(cam, out _, out var r, out var u);
        var sxW = (cursor.X - 500f) * wpp;
        var syW = (cursor.Y - 250f) * wpp;
        return cam.Target + r * sxW - u * syW;
    }

    private static Vector2 WorldToScreen(GlCamera cam, Vector3 world)
    {
        var dist = (cam.Target - cam.Position).Length();
        var wpp = 2f * dist * MathF.Tan(cam.Fov * 0.5f) / 500f;
        CameraGestures.GetCameraBasis(cam, out _, out var r, out var u);
        var rel = world - cam.Target;
        return new Vector2(500f + Vector3.Dot(rel, r) / wpp, 250f - Vector3.Dot(rel, u) / wpp);
    }

    [Fact]
    public void CursorRayPlanePivot_off_center_cursor_round_trips_to_exact_pixel()
    {
        // Purpose: the module's old hand-rolled world-per-pixel math diverged from
        // the real projection. The new pivot must project BACK to the exact cursor
        // pixel through the same matrix path the GPU renders with.
        var (inv, svp, w, h) = PerspectiveMatrices(
            new Vector3(0f, 0f, 100f), Vector3.Zero, Vector3.UnitY, 0.8f, 0.1f, 1000f, 1000f, 500f);
        var cursor = new Vector2(750f, 150f);
        var pivot = CameraGestures.CursorRayPlanePivot(
            inv, cursor, w, h, 1f, Vector3.Zero, -Vector3.UnitZ);

        var screen = ProjectToScreen(svp, pivot, w, h);
        // Precision 1 (0.05 px): the test helper's float Matrix4x4.Invert on
        // near=0.1/far=1000 commits ~0.023 px round-trip error; 0.005 px is not
        // attainable by any spec-compliant implementation from that float inverse.
        Assert.Equal(cursor.X, screen.X, 1);
        Assert.Equal(cursor.Y, screen.Y, 1);
        // Pivot lies on the target-depth plane through the origin (z = 0).
        Assert.Equal(0f, pivot.Z, 3);
    }

    [Fact]
    public void CursorRayPlanePivot_tall_viewport_round_trips_to_exact_pixel()
    {
        // Non-square viewport (500x1000): the horizontal/vertical pixel scales now differ.
        var (inv, svp, w, h) = PerspectiveMatrices(
            new Vector3(0f, 0f, 100f), Vector3.Zero, Vector3.UnitY, 0.8f, 0.1f, 1000f, 500f, 1000f);
        var cursor = new Vector2(125f, 375f);
        var pivot = CameraGestures.CursorRayPlanePivot(
            inv, cursor, w, h, 1f, Vector3.Zero, -Vector3.UnitZ);

        var screen = ProjectToScreen(svp, pivot, w, h);
        Assert.Equal(cursor.X, screen.X, 1);
        Assert.Equal(cursor.Y, screen.Y, 1);
    }

    [Fact]
    public void CursorRayPlanePivot_dpi_scale_is_invariant_for_physical_pixels()
    {
        // Cursor and viewport are both physical px; normalizing both by DpiScale
        // must not move the pivot.
        var (inv, _, w, h) = PerspectiveMatrices(
            new Vector3(0f, 0f, 100f), Vector3.Zero, Vector3.UnitY, 0.8f, 0.1f, 1000f, 1000f, 500f);
        var cursor = new Vector2(750f, 150f);
        var atDpi1 = CameraGestures.CursorRayPlanePivot(inv, cursor, w, h, 1f, Vector3.Zero, -Vector3.UnitZ);
        var atDpi2 = CameraGestures.CursorRayPlanePivot(inv, cursor, w, h, 2f, Vector3.Zero, -Vector3.UnitZ);
        Assert.Equal(atDpi1, atDpi2);
    }

    [Fact]
    public void CursorRayPlanePivot_centered_cursor_hits_plane_point()
    {
        var (inv, _, w, h) = PerspectiveMatrices(
            new Vector3(0f, 0f, 100f), Vector3.Zero, Vector3.UnitY, 0.8f, 0.1f, 1000f, 1000f, 500f);
        var pivot = CameraGestures.CursorRayPlanePivot(
            inv, new Vector2(500f, 250f), w, h, 1f, Vector3.Zero, -Vector3.UnitZ);
        Assert.Equal(0f, pivot.X, 3);
        Assert.Equal(0f, pivot.Y, 3);
        Assert.Equal(0f, pivot.Z, 3);
    }

    [Fact]
    public void CursorRayPlanePivot_parallel_ray_falls_back_to_plane_point()
    {
        // Center cursor ray runs exactly along -Z. A plane x = 5 is parallel to it.
        var (inv, _, w, h) = PerspectiveMatrices(
            new Vector3(0f, 0f, 100f), Vector3.Zero, Vector3.UnitY, 0.8f, 0.1f, 1000f, 1000f, 500f);
        var planePoint = new Vector3(5f, 0f, 42f);
        var pivot = CameraGestures.CursorRayPlanePivot(
            inv, new Vector2(500f, 250f), w, h, 1f, planePoint, Vector3.UnitX);
        Assert.Equal(planePoint.X, pivot.X, 4);
        Assert.Equal(planePoint.Y, pivot.Y, 4);
        Assert.Equal(planePoint.Z, pivot.Z, 4);
    }

    [Fact]
    public void CursorRayPlanePivot_degenerate_viewport_returns_plane_point()
    {
        var (inv, _, w, h) = PerspectiveMatrices(
            new Vector3(0f, 0f, 100f), Vector3.Zero, Vector3.UnitY, 0.8f, 0.1f, 1000f, 1000f, 500f);
        var planePoint = new Vector3(1f, 2f, 3f);
        Assert.Equal(planePoint, CameraGestures.CursorRayPlanePivot(inv, new Vector2(500f, 250f), 0f, h, 1f, planePoint, -Vector3.UnitZ));
        Assert.Equal(planePoint, CameraGestures.CursorRayPlanePivot(inv, new Vector2(500f, 250f), w, 0f, 1f, planePoint, -Vector3.UnitZ));
    }

    [Fact]
    public void CursorRayPlanePivot_uses_gl_ndc_depth_range()
    {
        // GL NDC z ∈ [-1, 1]: near plane unprojects at z = -1, far at z = +1.
        var view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 10), Vector3.Zero, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, 1f, 0.1f, 100f);
        Assert.True(Matrix4x4.Invert(view * proj, out var inv));
        var p = CameraGestures.CursorRayPlanePivot(inv, new Vector2(100, 100), 200, 200, 1f,
                                                   Vector3.Zero, Vector3.UnitZ);
        Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z));
    }

    private static (Matrix4x4 Inv, Matrix4x4 ScreenViewProj, float W, float H) PerspectiveMatrices(
        Vector3 position, Vector3 target, Vector3 up, float fov, float near, float far, float w, float h)
    {
        // Row-vector pipeline (matches CameraParams.ViewProjection = view * projection,
        // the engine's Inverse = invProjection * invView). The reverse-Z variant only
        // changes the depth distribution of z, not the (x, y) ray direction.
        var view = Matrix4x4.CreateLookAt(position, target, up);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(fov, w / h, near, far);
        var screenViewProjection = view * projection;
        Matrix4x4.Invert(screenViewProjection, out var inv);
        return (inv, screenViewProjection, w, h);
    }

    private static Vector2 ProjectToScreen(Matrix4x4 screenViewProjection, Vector3 world, float w, float h)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), screenViewProjection);
        var ndc = new Vector3(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
        return new Vector2((ndc.X + 1f) * w / 2f, (1f - ndc.Y) * h / 2f);
    }
}
