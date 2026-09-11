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
}