/*
 * GlPlotSceneProjectionTests.cs - the ISO preset really projects through the
 * perspective branch, and the readout and gesture paths agree on the mode
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

/// <summary>The fix for the flagged bug: <c>GlCamera.IsPerspective</c> was
/// never set, so the ISO preset rendered through the orthographic branch with
/// a stray width. The projection is asserted directly against the two
/// reference matrices, and the readout plus a live zoom prove the
/// <see cref="CameraGestures"/> dispatch runs the same mode.</summary>
[Trait("Category", "GPU")]
public class GlPlotSceneProjectionTests
{
    [GlFact]
    public void Iso_preset_projects_and_dispatches_perspective()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        scene.SetViewportSize(256, 256);
        scene.SetViewPreset(ViewPreset.Isometric3D);

        var cam = scene.ActiveCamera;

        // Gesture dispatch flag: CameraGestures branches on this field.
        Assert.True(cam.IsPerspective);

        // The projection itself: exactly the perspective matrix, and not the
        // orthographic one (the ortho reference uses the camera's untouched
        // default Width — the branch never reads it).
        Assert.Equal(
            Matrix4x4.CreatePerspectiveFieldOfView(cam.Fov, 1f, 0.01f, 1e6f),
            cam.Projection(256, 256));
        Assert.NotEqual(
            Matrix4x4.CreateOrthographic(cam.Width, cam.Width, 0.01f, 1e6f),
            cam.Projection(256, 256));

        // ZoomPercent reads via _perspectiveActive: the ortho path would
        // report 100 * _fitOrthoWidth / _ortho.Width = 1, the persp path 100.
        Assert.Equal(100f, scene.ZoomPercent, 3);

        // A centered zoom in moves the readout to 50 (dist / 0.5): only the
        // perspective branch of AnchorZoom changes the camera distance; the
        // ortho branch would rescale Width and leave the persp readout alone.
        scene.ZoomAround(new Vector2(128, 128), 0.5);
        Assert.Equal(50f, scene.ZoomPercent, 3);
    }

    [GlFact]
    public void Ortho_presets_keep_the_ortho_camera()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);
        scene.SetViewPreset(ViewPreset.XY);
        Assert.False(scene.ActiveCamera.IsPerspective);
    }
}
