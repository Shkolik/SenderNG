/*
 * GlCamera.cs - renderer-neutral camera state for the GL plot scene
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

namespace SenderNG.Modules.Plotter.GL.Rendering;

/// <summary>Renderer-neutral camera state — the six properties CameraGestures
/// touches. Replaces Helix's OrthographicCamera/PerspectiveCamera without
/// inventing a camera abstraction: one mutable record-ish class, two modes.</summary>
internal sealed class GlCamera
{
    public Vector3 Position;
    public Vector3 Target;
    public Vector3 Up = Vector3.UnitY;
    public float Width = 100f;      // ortho world width across the viewport
    public float Fov = MathF.PI / 4f;
    public bool IsPerspective;

    public Matrix4x4 View() => Matrix4x4.CreateLookAt(Position, Target, Up);

    public Matrix4x4 Projection(float aspectW, float aspectH)
    {
        if (IsPerspective)
            return Matrix4x4.CreatePerspectiveFieldOfView(Fov, aspectW / MathF.Max(aspectH, 1f), 0.01f, 1e6f);
        var h = Width * aspectH / MathF.Max(aspectW, 1f);
        return Matrix4x4.CreateOrthographic(Width, h, 0.01f, 1e6f);
    }
}
