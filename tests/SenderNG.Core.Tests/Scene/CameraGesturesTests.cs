/*
 * CameraGesturesTests.cs - pure camera basis + pan math tests
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
using SenderNG.Modules.Plotter.Nex.Scene;

namespace SenderNG.Core.Tests;

public class CameraGesturesTests
{
    [Fact]
    public void Ortho_pan_maps_pixels_to_world_one_to_one_on_both_axes()
    {
        var cam = new OrthographicCamera
        {
            Position = new Vector3(0f, 0f, 100f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            Width = 100f,
        };

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
        var cam = new PerspectiveCamera
        {
            Position = new Vector3(0f, 0f, 100f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            Fov = fov,
        };
        var wpp = 2f * 100f * MathF.Tan(fov * 0.5f) / 400f;

        var right = CameraGestures.ScreenPanDelta(cam, new Vector2(37f, 0f), 600f, 400f);
        Assert.Equal(37f * wpp, right.X, 4);
        Assert.Equal(0f, right.Y, 4);
        Assert.Equal(0f, right.Z, 4);
    }

    [Fact]
    public void Pan_degrades_to_zero_for_invalid_viewports_or_fov()
    {
        var cam = new PerspectiveCamera
        {
            Position = new Vector3(0f, 0f, 100f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            Fov = 0f,
        };
        Assert.Equal(Vector3.Zero, CameraGestures.ScreenPanDelta(cam, new Vector2(5f, 5f), 100f, 100f));
        Assert.Equal(Vector3.Zero, CameraGestures.ScreenPanDelta(cam, new Vector2(5f, 5f), 0f, 100f));
        Assert.Equal(Vector3.Zero, CameraGestures.ScreenPanDelta(cam, new Vector2(5f, 5f), 100f, 0f));
    }

    [Fact]
    public void Basis_reverts_to_minus_z_when_target_coincides_with_position()
    {
        var cam = new OrthographicCamera
        {
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
        var cam = new OrthographicCamera
        {
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
        var cam = new OrthographicCamera
        {
            Position = new Vector3(0f, 0f, 100f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            Width = 100f,
        };
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
        var cam = new OrthographicCamera
        {
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
        var cam = new PerspectiveCamera
        {
            Position = new Vector3(0f, 0f, 100f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            Fov = 0.8f,
        };
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
        var cam = new OrthographicCamera
        {
            Position = new Vector3(0f, 0f, 100f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            Width = 100f,
        };
        CameraGestures.AnchorZoom(cam, new Vector2(5f, 5f), 0f, 100f, 100f);
        Assert.Equal(100f, cam.Width, 5);

        var persp = new PerspectiveCamera
        {
            Position = new Vector3(0f, 0f, 100f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            Fov = 0f,
        };
        var pos = persp.Position;
        CameraGestures.AnchorZoom(persp, new Vector2(5f, 5f), 1.5, 100f, 100f);
        Assert.Equal(pos, persp.Position);
        Assert.Equal(Vector3.Zero, persp.Target);
    }

    private static Vector3 WorldAtCursorOrtho(OrthographicCamera cam, Vector2 cursor)
    {
        CameraGestures.GetCameraBasis(cam, out _, out var r, out var u);
        var wpp = cam.Width / 1000f;
        var sxW = (cursor.X - 500f) * wpp;
        var syW = (cursor.Y - 250f) * wpp;
        return cam.Target + r * sxW - u * syW;
    }

    private static Vector3 WorldAtCursorPersp(PerspectiveCamera cam, Vector2 cursor)
    {
        var dist = (cam.Target - cam.Position).Length();
        var wpp = 2f * dist * MathF.Tan(cam.Fov * 0.5f) / 500f;
        CameraGestures.GetCameraBasis(cam, out _, out var r, out var u);
        var sxW = (cursor.X - 500f) * wpp;
        var syW = (cursor.Y - 250f) * wpp;
        return cam.Target + r * sxW - u * syW;
    }
}
