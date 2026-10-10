/*
 * GlShaderCapabilityTests.cs - pure version-string gate for geometry shaders
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

using SenderNG.Modules.Plotter.GL.Rendering;

namespace SenderNG.Core.Tests;

/// <summary>Task 8d: GLSL ES 3.0/3.1 has no geometry shaders (they landed
/// in ES 3.20), so the fat-line expansion program must not be compiled on a
/// GLES 3.1 context — the Pi's V3D hard-fails the GeometryShader compile.
/// Desktop GL always keeps the fat path (GS is core since GL 3.2). Pure
/// version-string gate: no GPU, runs anywhere.</summary>
public class GlShaderCapabilityTests
{
    [Theory]
    // GLES: ES 3.0/3.1 lack geometry shaders — thin path.
    [InlineData("OpenGL ES 3.0 Mesa", true, false)]
    [InlineData("OpenGL ES 3.1 Mesa 26.2.2", true, false)]
    [InlineData("OpenGL ES 3.1-V3D 4.2.14.0", true, false)]
    // GLES: ES 3.2+ has geometry shaders — fat path.
    [InlineData("OpenGL ES 3.2 Mesa", true, true)]
    [InlineData("OpenGL ES 3.2 Qualcomm Adreno (TM) 640", true, true)]
    // Desktop GL: isGles false is always fat, whatever the version string
    // says (GS core since GL 3.2; the suite's Mesa desktop context is 4.5+).
    [InlineData("4.5 (Core Profile) Mesa 24.0", false, true)]
    [InlineData("4.6.0 NVIDIA 546.33", false, true)]
    [InlineData("3.3.0 - Build 30.0.101.1340", false, true)]
    // Unparseable / empty GLES strings: conservative thin answer — we cannot
    // claim a capability the driver did not state.
    [InlineData("", true, false)]
    [InlineData("OpenGL ES-CM 1.1", true, false)]
    [InlineData("garbage", true, false)]
    public void Supports_geometry_shaders_keys_off_gles_and_es_minor(
        string version, bool isGles, bool expected)
    {
        Assert.Equal(expected, GlShaders.SupportsGeometryShaders(version, isGles));
    }
}
