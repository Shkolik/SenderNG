/*
 * GlLineShaderTests.cs - fat-line geometry: palette widths really cover
 * pixels, a one-pixel quad matches the legacy raster, frames stay clean
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
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using SenderNG.Modules.Plotter.GL.Rendering;
using SenderNG.Modules.Plotter.GL.Scene;
using SenderNG.Modules.Plotter.GL.Settings;
using Xunit.Abstractions;
using GlApi = Silk.NET.OpenGL.GL;

namespace SenderNG.Core.Tests;

/// <summary>Checkpoint-4 items 3+4: every line draws as a screen-space quad
/// (geometry-stage expansion), so the palette widths render for real instead
/// of collapsing to the driver's accepted 1.0 — and their edges become real
/// geometry edges MSAA can antialias. Gated by <see cref="GlFactAttribute"/>;
/// on a machine with no usable GL these skip instead of failing.</summary>
[Trait("Category", "GPU")]
public class GlLineShaderTests
{
    private static readonly byte[] Background = [32, 35, 40, 255];

    private readonly ITestOutputHelper _output;

    public GlLineShaderTests(ITestOutputHelper output) => _output = output;

    /// <summary>RED (a): the toolpath draws at the 3.2px palette width —
    /// a horizontal segment must cover at least three pixel rows in a column
    /// it crosses. Pre-geometry-shader this fails: the driver only accepts
    /// glLineWidth 1.0, so the line rasterizes one row.</summary>
    [GlFact]
    public unsafe void Palette_width_line_covers_at_least_three_pixel_rows()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, 256, 256);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        scene.SetViewportSize(256, 256);
        var layer = (GlCarriagePlaneLayer)scene.AddCarriagePlane();
        layer.AddSegment(
            new LineSegment(new Vector3(-40, 0, 0), new Vector3(40, 0, 0)), MotionType.Cut);
        scene.PumpCarriagePlanes();
        scene.SetViewPreset(ViewPreset.XY);
        scene.SetView(new PlotterGlSettings
        {
            ShowOrigin = false,
            ShowEnvelope = false,
            ShowGrid = false,
        });

        rb.BeginFrame(Background);
        scene.RenderFrame(256, 256);
        var px = new byte[256 * 256 * 4];
        fixed (byte* p = px)
            Assert.True(rb.CopyFrame((nint)p, 256 * 4));

        var rows = LitPixelsInColumn(px, 256, 128);
        _output.WriteLine(
            $"palette width {GlCarriagePlaneLayer.BaseLineThickness}px -> {rows} rows in column 128");
        Assert.True(rows >= 3,
            $"a {GlCarriagePlaneLayer.BaseLineThickness}px line covered {rows} rows " +
            "in the center column, expected >= 3");
    }

    /// <summary>RED (b): at width 1 the quad path must rasterize like the
    /// legacy GL_LINES draw it replaces — same segment, same frame size,
    /// legacy program vs geometry-shader program, lit totals within the
    /// end-cap allowance. (The legacy side is today's output; the quad side
    /// is the new path forced to today's width.)</summary>
    [GlFact]
    public unsafe void One_pixel_expansion_matches_the_legacy_line_raster()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, 256, 256);
        ctx.MakeCurrent();
        var gl = ctx.Api;

        // Horizontal segment through the CENTER of pixel row 64
        // (fb y = (ndc + 1) / 2 * 256 = 64.5) so neither path sits on a
        // pixel boundary.
        var ndcY = (64.5f / 256f) * 2f - 1f;

        var legacy = GlShaders.Link(gl,
            GlShaders.Compile(gl, ctx.IsGles, ShaderType.VertexShader, GlShaders.LineVertex),
            GlShaders.Compile(gl, ctx.IsGles, ShaderType.FragmentShader, GlShaders.LineFragment));
        var quad = GlShaders.Link(gl,
            GlShaders.Compile(gl, ctx.IsGles, ShaderType.VertexShader, GlShaders.LineVertex),
            GlShaders.Compile(gl, ctx.IsGles, ShaderType.GeometryShader, GlShaders.LineGeometry),
            GlShaders.Compile(gl, ctx.IsGles, ShaderType.FragmentShader, GlShaders.LineFragmentFat));
        var (vao, vbo) = CreateSegment(gl, -0.5f, 0.5f, ndcY);
        try
        {
            var legacyLit = DrawSegment(ctx, rb, legacy, vao, widthPx: null);
            var quadLit = DrawSegment(ctx, rb, quad, vao, widthPx: 1f);
            _output.WriteLine(
                $"1px: legacy GL_LINES {legacyLit} lit px vs quad expansion {quadLit} lit px");

            Assert.True(legacyLit > 100,
                $"precondition: the legacy 1px line must draw, got {legacyLit} lit pixels");
            Assert.InRange(quadLit, legacyLit - 2, legacyLit + 2);
        }
        finally
        {
            gl.DeleteVertexArray(vao);
            gl.DeleteBuffer(vbo);
            gl.DeleteProgram(legacy);
            gl.DeleteProgram(quad);
        }
    }

    /// <summary>Guard (c): a full frame with every palette width &gt; 1
    /// (chrome 1.6/3.6, plane + done overlay 3.2) leaves a clean GL error queue —
    /// uniforms, geometry stage and all draw sites included.</summary>
    [GlFact]
    public unsafe void A_frame_with_palette_widths_leaves_no_gl_error()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, 256, 256);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        var gl = ctx.Api;
        scene.SetViewportSize(256, 256);
        var layer = (GlCarriagePlaneLayer)scene.AddCarriagePlane();
        layer.AddSegment(
            new LineSegment(new Vector3(-40, 0, 0), new Vector3(40, 0, 0)), MotionType.Cut);
        layer.SetSegmentRunState(0, RunState.Executed);
        scene.PumpCarriagePlanes();
        scene.SetViewPreset(ViewPreset.XY);
        Drain(gl);

        rb.BeginFrame(Background);
        scene.RenderFrame(256, 256); // chrome 1.6/3.6 + plane 3.2 + done overlay 3.2

        Assert.Equal(GLEnum.NoError, FirstError(gl));
    }

    /// <summary>Checkpoint-4 item 4: the one-shot scene-constructor line the
    /// due-diligence doc needs — accepted set + renderer, permanent.</summary>
    [GlFact]
    public void Scene_ctor_logs_the_accepted_widths_and_renderer()
    {
        using var ctx = GlContext.Create();
        using var scene = new GlPlotScene(ctx);

        var log = scene.LineWidthLogLine;
        Assert.False(string.IsNullOrWhiteSpace(log));
        Assert.StartsWith("[Plotter.GL] line widths accepted=[", log);
        Assert.Contains(
            $"accepted=[{string.Join(", ", scene.AcceptedLineSizes)}]", log);
        Assert.Contains($"\"{ctx.Renderer}\"", log);
    }

    /// <summary>C1 guard: a line reaches the fragment shader in its palette
    /// color — not merely non-background. The geometry stage's output
    /// (<c>gColor</c>) must be statically matched by the fragment stage the
    /// three-stage program links (<see cref="GlShaders.LineFragmentFat"/>,
    /// input <c>gColor</c>); linking any FS whose input name never gets
    /// written leaves it undefined by spec — black on Mesa. Rapid red
    /// through the production path (scene program, layer draw).</summary>
    [GlFact]
    public unsafe void Rapid_line_pixel_carries_the_rapid_palette_color()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, 256, 256);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        scene.SetViewportSize(256, 256);
        var layer = (GlCarriagePlaneLayer)scene.AddCarriagePlane();
        layer.AddSegment(
            new LineSegment(new Vector3(-40, 0, 0), new Vector3(40, 0, 0)), MotionType.Rapid);
        scene.PumpCarriagePlanes();
        scene.SetViewPreset(ViewPreset.XY);
        scene.SetView(new PlotterGlSettings
        {
            ShowOrigin = false,
            ShowEnvelope = false,
            ShowGrid = false,
        });

        rb.BeginFrame(Background);
        scene.RenderFrame(256, 256);
        var px = new byte[256 * 256 * 4];
        fixed (byte* p = px)
            Assert.True(rb.CopyFrame((nint)p, 256 * 4));

        var rows = LitPixelsInColumn(px, 256, 128);
        Assert.True(rows >= 1,
            $"precondition: the Rapid line must light column 128, got {rows} rows");

        // Task 7b re-baseline (old expectation -> new, formula cited): lines
        // now pass through the ACES filmic curve (psToneGamma.glsl:25-32) —
        // the engine tones the whole frame, palette lines included. Old:
        // raw palette bytes Round((0.9,0.12,0.12)*255) = (230,31,31). New:
        // ACES((0.9,0.12,0.12))*255 = (199,41,41). The C1 property under test
        // (the palette colour reaches the fragment stage, matched by name
        // through the geometry stage) is unchanged — only the on-screen
        // bytes moved with the pipeline.
        var rapid = GlCarriagePlaneLayer.RapidColor;
        var aces = GlColourParityTests.Aces(new Vector3(
            rapid.X, rapid.Y, rapid.Z));
        var er = (byte)MathF.Round(aces.X * 255f);
        var eg = (byte)MathF.Round(aces.Y * 255f);
        var eb = (byte)MathF.Round(aces.Z * 255f);
        var mismatches = 0;
        var fr = -1;
        var fg = -1;
        var fb = -1;
        for (var y = 0; y < 256; y++)
        {
            var i = (y * 256 + 128) * 4;
            if (px[i] == 32 && px[i + 1] == 35 && px[i + 2] == 40)
                continue;
            if (Math.Abs(px[i] - er) > 2 || Math.Abs(px[i + 1] - eg) > 2 ||
                Math.Abs(px[i + 2] - eb) > 2)
            {
                if (mismatches == 0)
                    (fr, fg, fb) = (px[i], px[i + 1], px[i + 2]);
                mismatches++;
            }
        }
        _output.WriteLine(
            $"rapid palette rgb=({er},{eg},{eb}) over {rows} rows in column 128; " +
            $"{mismatches} mismatched" +
            (mismatches > 0 ? $", first bad rgb=({fr},{fg},{fb})" : string.Empty));
        Assert.Equal(0, mismatches);
    }

    /// <summary>One horizontal line, interleaved vec3 + vec4 (stride 28) —
    /// the production line layout.</summary>
    private static unsafe (uint Vao, uint Vbo) CreateSegment(
        GlApi gl, float x0, float x1, float y)
    {
        var vao = gl.GenVertexArray();
        var vbo = gl.GenBuffer();
        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        float[] data =
        [
            x0, y, 0f, 1f, 1f, 1f, 1f,
            x1, y, 0f, 1f, 1f, 1f, 1f,
        ];
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, data, BufferUsageARB.StaticDraw);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 28, IntPtr.Zero);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 28, (IntPtr)12);
        gl.EnableVertexAttribArray(1);
        gl.BindVertexArray(0);
        return (vao, vbo);
    }

    /// <summary>Clear → one DrawArrays of the two-vertex segment through
    /// <paramref name="prog"/> → read back → lit-pixel total. Width comes
    /// from <c>uLineWidthPx</c> when given, else the legacy
    /// <c>glLineWidth(1)</c>.</summary>
    private static unsafe int DrawSegment(
        GlContext ctx, GlReadback rb, uint prog, uint vao, float? widthPx)
    {
        var gl = ctx.Api;
        rb.BeginFrame(Background);
        gl.UseProgram(prog);
        var mvp = Matrix4x4.Identity;
        gl.UniformMatrix4(gl.GetUniformLocation(prog, "uMvp"), false,
            MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));
        if (widthPx is { } width)
        {
            gl.Uniform2(gl.GetUniformLocation(prog, "uViewportPx"), 256f, 256f);
            gl.Uniform1(gl.GetUniformLocation(prog, "uLineWidthPx"), width);
        }
        else
        {
            gl.LineWidth(1f);
        }
        gl.BindVertexArray(vao);
        gl.DrawArrays(PrimitiveType.Lines, 0, 2);
        gl.BindVertexArray(0);
        gl.UseProgram(0);

        var px = new byte[256 * 256 * 4];
        fixed (byte* p = px)
            Assert.True(rb.CopyFrame((nint)p, 256 * 4));
        return LitPixels(px);
    }

    private static int LitPixels(byte[] px)
    {
        var lit = 0;
        for (var i = 0; i < px.Length; i += 4)
            if (px[i] != 32 || px[i + 1] != 35 || px[i + 2] != 40)
                lit++;
        return lit;
    }

    /// <summary>Non-background pixels in one column — for an isolated
    /// horizontal line this is its row span.</summary>
    private static int LitPixelsInColumn(byte[] px, int width, int column)
    {
        var lit = 0;
        for (var y = 0; y < width; y++)
        {
            var i = (y * width + column) * 4;
            if (px[i] != 32 || px[i + 1] != 35 || px[i + 2] != 40)
                lit++;
        }
        return lit;
    }

    private static void Drain(GlApi gl)
    {
        for (var i = 0; i < 16 && gl.GetError() != GLEnum.NoError; i++)
        {
        }
    }

    private static GLEnum FirstError(GlApi gl)
    {
        var first = GLEnum.NoError;
        for (var i = 0; i < 16; i++)
        {
            var e = gl.GetError();
            if (e == GLEnum.NoError)
                break;
            if (first == GLEnum.NoError)
                first = e;
        }
        return first;
    }
}
