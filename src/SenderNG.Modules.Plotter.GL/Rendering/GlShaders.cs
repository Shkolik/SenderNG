/*
 * GlShaders.cs - the two GLSL programs the chrome and meshes draw with
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

using Silk.NET.OpenGL;
using GlApi = Silk.NET.OpenGL.GL;

namespace SenderNG.Modules.Plotter.GL.Rendering;

/// <summary>Compile/link the plot scene's two shader programs.
/// <para>
/// The version preamble is the ENTIRE desktop-vs-ES divergence: the bodies
/// below are written once and are valid GLSL 3.30 and GLSL ES 3.00 alike, so
/// <see cref="Preamble(bool)"/> is the only thing a GLES context (Task 8)
/// swaps in. Keep the bodies free of version-specific syntax.
/// </para></summary>
internal static class GlShaders
{
    /// <summary>Desktop GL 3.3 core vs GLES 3.0 — the whole portability
    /// strategy, precision qualifier included (ES has no default float
    /// precision, desktop forbids the qualifier outside the prelude).</summary>
    public static string Preamble(bool gles) => gles
        ? "#version 300 es\nprecision highp float;\n"
        : "#version 330 core\n";

    public const string LineVertex = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec4 aColor;
        uniform mat4 uMvp;
        out vec4 vColor;
        void main() { gl_Position = uMvp * vec4(aPos, 1.0); vColor = aColor; }
        """;

    public const string LineFragment = """
        in vec4 vColor;
        out vec4 FragColor;
        void main() { FragColor = vec4(vColor.rgb, 1.0); }
        """;

    public const string MeshVertex = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        uniform mat4 uMvp;
        uniform vec3 uLightDir;
        out float vShade;
        void main() {
            gl_Position = uMvp * vec4(aPos, 1.0);
            vShade = max(dot(normalize(aNormal), normalize(uLightDir)), 0.0) * 0.7 + 0.3;
        }
        """;

    public const string MeshFragment = """
        in float vShade;
        uniform vec4 uColor;
        out vec4 FragColor;
        void main() { FragColor = vec4(uColor.rgb * vShade, 1.0); }
        """;

    /// <summary>Compile one shader stage. Throws with the driver's info log on
    /// failure — a bad preamble or body is a build-time bug, never a runtime
    /// condition to paper over.</summary>
    public static uint Compile(GlApi gl, bool gles, ShaderType type, string body)
    {
        var shader = gl.CreateShader(type);
        gl.ShaderSource(shader, Preamble(gles) + body);
        gl.CompileShader(shader);
        if (gl.GetShader(shader, ShaderParameterName.CompileStatus) == 0)
        {
            var log = gl.GetShaderInfoLog(shader);
            gl.DeleteShader(shader);
            throw new InvalidOperationException($"shader compile failed ({type}): {log}");
        }
        return shader;
    }

    /// <summary>Link a vertex+fragment pair into a program and release the
    /// stage objects (detached first, so they go now rather than at program
    /// deletion). Throws with the driver's info log on failure.</summary>
    public static uint Link(GlApi gl, uint vs, uint fs)
    {
        var program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        gl.LinkProgram(program);
        if (gl.GetProgram(program, ProgramPropertyARB.LinkStatus) == 0)
        {
            var log = gl.GetProgramInfoLog(program);
            gl.DeleteProgram(program);
            throw new InvalidOperationException($"shader link failed: {log}");
        }
        gl.DetachShader(program, vs);
        gl.DetachShader(program, fs);
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        return program;
    }
}
