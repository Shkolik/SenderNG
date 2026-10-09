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

    /// <summary>Fat-line expansion (checkpoint-4 items 3+4): every line
    /// becomes a screen-space quad, because core drivers can refuse
    /// <c>glLineWidth &gt; 1.0</c> while claiming a range up to 255. One
    /// segment in, a four-corner triangle strip out: the perpendicular of the
    /// segment in pixel space, offset by half <c>uLineWidthPx</c> and mapped
    /// back through <c>uViewportPx</c>. Depth (<c>gl_Position.z</c>/w) is kept
    /// per endpoint, so the <c>GL_LEQUAL</c> overlay ordering of the draws
    /// does not change. The quad's long edges are real geometry edges — MSAA
    /// has something to antialias. <para>
    /// Color interface (C1): the FS input is matched to
    /// <c>gColor</c> BY NAME via <see cref="LineFragmentFat"/>. Neither
    /// sanctioned alternative survives GLSL 330 — a same-name
    /// <c>out vec4 vColor</c> collides with this stage's
    /// <c>in vec4 vColor[]</c> (Mesa: <c>redeclaration of 'vColor' has
    /// incorrect type</c>) and <c>layout(location)</c> on a GS output or FS
    /// input needs <c>GL_ARB_separate_shader_objects</c>/GLSL 4.20 (Mesa:
    /// <c>shader output explicit location requires ...</c>) — so the
    /// geometry stage names its output freely and its twin fragment shader
    /// follows suit.</para><para>
    /// Note for Task 8 (GLES): geometry stages need GLSL ES 3.20; on ES 3.0
    /// this stage must be re-cut as vertex-instanced expansion.</para></summary>
    public const string LineGeometry = """
        layout(lines) in;
        layout(triangle_strip, max_vertices = 4) out;
        in vec4 vColor[];
        out vec4 gColor;
        uniform vec2 uViewportPx;
        uniform float uLineWidthPx;
        void main() {
            vec4 c0 = gl_in[0].gl_Position;
            vec4 c1 = gl_in[1].gl_Position;
            vec2 n0 = c0.xy / c0.w;
            vec2 n1 = c1.xy / c1.w;
            vec2 s0 = n0 * uViewportPx * 0.5;
            vec2 s1 = n1 * uViewportPx * 0.5;
            vec2 d = s1 - s0;
            float len = length(d);
            vec2 offset = vec2(0.0);
            if (len > 1e-6) {
                vec2 dir = d / len;
                offset = vec2(-dir.y, dir.x) * (uLineWidthPx * 0.5) / (uViewportPx * 0.5);
            }
            gColor = vColor[0];
            gl_Position = vec4((n0 + offset) * c0.w, c0.z, c0.w); EmitVertex();
            gl_Position = vec4((n0 - offset) * c0.w, c0.z, c0.w); EmitVertex();
            gColor = vColor[1];
            gl_Position = vec4((n1 + offset) * c1.w, c1.z, c1.w); EmitVertex();
            gl_Position = vec4((n1 - offset) * c1.w, c1.z, c1.w); EmitVertex();
            EndPrimitive();
        }
        """;

    /// <summary>The legacy two-stage program's fragment stage (color matched
    /// by name from <see cref="LineVertex"/>'s <c>vColor</c>), also the
    /// body twin of <see cref="LineFragmentFat"/>.</summary>
    public const string LineFragment = """
        in vec4 vColor;
        out vec4 FragColor;
        void main() { FragColor = vec4(vColor.rgb, 1.0); }
        """;

    /// <summary>The three-stage program's fragment stage: same body as
    /// <see cref="LineFragment"/>, input renamed to <c>gColor</c> so it
    /// matches <see cref="LineGeometry"/>'s output by name. A geometry stage
    /// cannot redeclare <c>vColor</c> (its input is already
    /// <c>in vec4 vColor[]</c>) and GLSL 330 forbids explicit locations on
    /// GS outputs / FS inputs, so without this twin the statically-read
    /// FS input would never be written — undefined by spec, black on
    /// Mesa.</summary>
    public const string LineFragmentFat = """
        in vec4 gColor;
        out vec4 FragColor;
        void main() { FragColor = vec4(gColor.rgb, 1.0); }
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
    public static uint Link(GlApi gl, uint vs, uint fs) => Link(gl, vs, 0, fs);

    /// <summary>The three-stage line program: vertex → geometry (fat-line
    /// expansion) → fragment. Same detach-and-release contract as the
    /// two-stage overload; <paramref name="gs"/> 0 means "no geometry stage".
    /// </summary>
    public static uint Link(GlApi gl, uint vs, uint gs, uint fs)
    {
        var program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        if (gs != 0)
            gl.AttachShader(program, gs);
        gl.AttachShader(program, fs);
        gl.LinkProgram(program);
        if (gl.GetProgram(program, ProgramPropertyARB.LinkStatus) == 0)
        {
            var log = gl.GetProgramInfoLog(program);
            gl.DeleteProgram(program);
            throw new InvalidOperationException($"shader link failed: {log}");
        }
        gl.DetachShader(program, vs);
        if (gs != 0)
            gl.DetachShader(program, gs);
        gl.DetachShader(program, fs);
        gl.DeleteShader(vs);
        if (gs != 0)
            gl.DeleteShader(gs);
        gl.DeleteShader(fs);
        return program;
    }
}
