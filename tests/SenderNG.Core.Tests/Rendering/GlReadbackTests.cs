/*
 * GlReadbackTests.cs - the GL context reports itself and a clear survives the
 * FBO glReadPixels roundtrip byte for byte
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
using Silk.NET.OpenGL;

namespace SenderNG.Core.Tests;

/// <summary>The end-to-end pixel path the view tick depends on: real WGL
/// context → FBO clear → glReadPixels → the same bytes the WriteableBitmap
/// would receive. Gated by <see cref="GlFactAttribute"/>, so a machine with no
/// usable GL skips instead of failing; on a GPU machine every assertion below
/// actually runs.</summary>
[Trait("Category", "GPU")]
public class GlReadbackTests
{
    [GlFact]
    public unsafe void Clear_color_survives_readback_roundtrip()
    {
        using var ctx = GlContext.Create();               // throws → test framework skip, mirroring Nex GPU tests
        using var rb = new GlReadback(ctx, 64, 32);
        ctx.MakeCurrent();
        rb.BeginFrame(new byte[] { 255, 0, 0, 255 });     // opaque red clear
        // no draws — clear only
        var pixels = new byte[64 * 32 * 4];
        fixed (byte* p = pixels)
        {
            Assert.True(rb.CopyFrame((nint)p, 64 * 4));
        }
        Assert.Equal(255, pixels[0]);      // R
        Assert.Equal(0, pixels[1]);        // G
        Assert.Equal(0, pixels[2]);        // B
        Assert.Equal(255, pixels[3]);      // A forced opaque
    }

    /// <summary>Checkpoint-1 bug: glReadPixels returns framebuffer rows
    /// bottom-up (row 0 = bottom of the view) while the WriteableBitmap is
    /// top-down. Scissor-clear a band at the GL bottom and assert CopyFrame
    /// writes it into the BOTTOM rows of the destination — the rows the user
    /// sees at the bottom of the panel.</summary>
    [GlFact]
    public unsafe void Gl_bottom_band_lands_in_the_bottom_bitmap_rows()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, 64, 64);
        ctx.MakeCurrent();
        var gl = ctx.Api;
        rb.BeginFrame(new byte[] { 10, 20, 30, 255 });

        // The scissor box origin is the framebuffer's bottom-left, so
        // y = 0..h/4 is what the viewer sees at the BOTTOM of the view.
        gl.Enable(EnableCap.ScissorTest);
        gl.Scissor(0, 0, 64, 16);
        gl.ClearColor(200f / 255f, 0f, 0f, 1f);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        gl.Disable(EnableCap.ScissorTest);

        var pixels = new byte[64 * 64 * 4];
        fixed (byte* p = pixels)
            Assert.True(rb.CopyFrame((nint)p, 64 * 4));

        // Destination rows are top-down: rows 48..63 are the view's bottom
        // quarter and must carry the band; rows 0..15 must be pure background.
        Assert.Equal(16 * 64, CountBand(pixels, 48, 64));
        Assert.Equal(0, CountBand(pixels, 0, 16));
    }

    private static int CountBand(byte[] px, int fromRow, int toRow)
    {
        var n = 0;
        for (var y = fromRow; y < toRow; y++)
        for (var x = 0; x < 64; x++)
        {
            var i = (y * 64 + x) * 4;
            if (px[i] == 200 && px[i + 1] == 0 && px[i + 2] == 0)
                n++;
        }
        return n;
    }

    [GlFact]
    public void Context_reports_a_version_string()
    {
        using var ctx = GlContext.Create();
        Assert.False(string.IsNullOrWhiteSpace(ctx.Version));
        Assert.False(string.IsNullOrWhiteSpace(ctx.Renderer));
    }

    /// <summary>Task 2 review: settle WHICH creation path actually produced
    /// the context. The GL_VERSION string alone cannot — a legacy bootstrap
    /// context reports its own (often higher, "Compatibility Profile")
    /// version, which would sail through the minimum-version audit. So audit
    /// the path itself: the ARB entry point resolved, the ARB call succeeded
    /// with the mandated 3.3-core-forward-compat attribs, no legacy fallback
    /// occurred, and the CURRENT context's profile mask + flags say core and
    /// forward-compatible (glGetIntegerv read back through this very context).</summary>
    [GlFact]
    public void Context_is_the_mandated_3_3_core_forward_compatible_context()
    {
        using var ctx = GlContext.Create();
        Assert.True(ctx.ArbCreateResolved,
            "wglGetProcAddress(\"wglCreateContextAttribsARB\") must resolve — " +
            "without the extension only a legacy context can exist");
        Assert.True(ctx.ArbCreateSucceeded,
            $"the ARB call must succeed, not fall back — attribs: {ctx.ArbCreateAttribs}; " +
            $"version: {ctx.Version}; renderer: {ctx.Renderer}");
        Assert.False(ctx.UsedLegacyFallback,
            "the final context must be the ARB-created one, not the legacy bootstrap");
        Assert.True(ctx.IsCurrent,
            "the audit reads below go through the current context — it must be THE context");
        Assert.Equal(0x1, ctx.ProfileMask);
        // GL_CONTEXT_FLAGS uses GL QUERY numbering (forward = 0x1,
        // debug = 0x2) — the mirror image of the WGL request numbering used
        // in the attribs (WGL forward = 0x2, WGL debug = 0x1).
        Assert.True((ctx.ContextFlagsMask & 0x1) != 0,
            $"GL_CONTEXT_FLAGS must include GL_CONTEXT_FLAG_FORWARD_COMPATIBLE_BIT (got 0x{ctx.ContextFlagsMask:X})");
        Assert.True((ctx.ContextFlagsMask & 0x2) == 0,
            $"GL_CONTEXT_FLAGS must NOT include GL_CONTEXT_FLAG_DEBUG_BIT (got 0x{ctx.ContextFlagsMask:X})");
    }
}
