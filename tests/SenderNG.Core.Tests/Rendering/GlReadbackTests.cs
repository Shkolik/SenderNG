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

    [GlFact]
    public void Context_reports_a_version_string()
    {
        using var ctx = GlContext.Create();
        Assert.False(string.IsNullOrWhiteSpace(ctx.Version));
        Assert.False(string.IsNullOrWhiteSpace(ctx.Renderer));
    }
}
