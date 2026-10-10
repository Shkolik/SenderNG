/*
 * GlReadback.cs - offscreen FBO frame readback for GpuImage + screenshots
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
using SenderNG.Modules.Plotter.GL.Settings;

namespace SenderNG.Modules.Plotter.GL.Rendering;

/// <summary>The offscreen target the view tick renders into: the FBO the scene
/// draws into (single-sample RGBA8 + depth24 by default; multisampled color +
/// depth renderbuffers with a blit-resolve when <see cref="Msaa"/> is on), and
/// a <c>glReadPixels</c> + row copy that lands the frame in the caller's locked
/// WriteableBitmap. Public surface mirrors NexGpuReadback (ctor(w,h),
/// BeginFrame, CopyFrame, Resize, Dispose) so the view-side pipeline stays
/// renderer-shaped.
/// <para>
/// Synchronization: <c>glReadPixels</c> on the same context synchronizes with
/// all prior draws, so no fence or wait is needed (the Nexus "second WaitAll"
/// equivalent — see CopyFrame).
/// </para>
/// <para>
/// Every member runs on the thread that owns the context (the UI thread — see
/// PlotterGlView.OnTick); there is no cross-thread use to guard.
/// </para></summary>
internal sealed unsafe class GlReadback : IDisposable
{
    private readonly GlContext _ctx;
    private uint _w;
    private uint _h;
    private uint _fbo, _colorRb, _depthRb;
    private uint _msaaFbo, _msaaColorRb, _msaaDepthRb, _resolveTex;
    private MsaaMode _msaa;
    private byte[] _scratch;
    private bool _disposed;

    public uint Width => _w;

    public uint Height => _h;

    /// <summary>The multisample mode the FBO set is currently built for —
    /// read seam for the mode-switch tests.</summary>
    public MsaaMode Msaa => _msaa;

    /// <summary>Samples per pixel the current mode actually builds with — the
    /// mode's request already clamped to this driver's <c>GL_MAX_SAMPLES</c>
    /// (read seam for the mode tests: never a count the driver does not
    /// offer; 0 = the plain path).</summary>
    public uint Samples => SampleCount();

    /// <summary>The FBO draws go into this frame (diagnostics + Task 4's draw
    /// path bind it): the multisampled target while <see cref="Msaa"/> is on,
    /// the plain single-sample one otherwise.</summary>
    public int Fbo => (int)(_msaa == MsaaMode.None ? _fbo : _msaaFbo);

    /// <summary>Switch the multisample mode at runtime: the FBO set is
    /// rebuilt from scratch on the current context, so nothing from the old
    /// mode leaks. No-op when unchanged — the view calls this every tick.</summary>
    public void SetMsaa(MsaaMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_msaa == mode)
            return;
        DeleteResources();
        _msaa = mode;
        CreateResources();
    }

    public GlReadback(GlContext ctx, uint width, uint height, MsaaMode msaa = MsaaMode.None)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (width == 0 || height == 0)
            throw new ArgumentOutOfRangeException(nameof(width), "readback size must be non-zero");
        _ctx = ctx;
        _w = width;
        _h = height;
        _msaa = msaa;
        _scratch = new byte[checked((int)width * height * 4)];
        CreateResources();
    }

    private void CreateResources()
    {
        _ctx.MakeCurrent();
        var gl = _ctx.Api;
        var samples = SampleCount();

        // The single-sample target: always the color source CopyFrame reads,
        // and the ONLY framebuffer the scene draws into in None mode. With MSAA
        // on, its color attachment is a texture instead of a renderbuffer —
        // the multisampled draw target blit-resolves into it, and glReadPixels
        // then reads the resolved frame.
        gl.GenFramebuffers(1, out _fbo);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        if (samples == 0)
        {
            gl.GenRenderbuffers(1, out _colorRb);
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _colorRb);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, _w, _h);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _colorRb);
        }
        else
        {
            gl.GenTextures(1, out _resolveTex);
            gl.BindTexture(TextureTarget.Texture2D, _resolveTex);
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, _w, _h, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, null);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _resolveTex, 0);
            // Leave no texture bound: the frame's own draws assume the default
            // texture unit state.
            gl.BindTexture(TextureTarget.Texture2D, 0);
        }

        gl.GenRenderbuffers(1, out _depthRb);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRb);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, _w, _h);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _depthRb);
        CheckComplete(_fbo, "resolve target");

        if (samples > 0)
        {
            gl.GenFramebuffers(1, out _msaaFbo);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);

            gl.GenRenderbuffers(1, out _msaaColorRb);
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaColorRb);
            gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, samples,
                InternalFormat.Rgba8, _w, _h);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _msaaColorRb);

            gl.GenRenderbuffers(1, out _msaaDepthRb);
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaDepthRb);
            gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, samples,
                InternalFormat.DepthComponent24, _w, _h);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _msaaDepthRb);

            CheckComplete(_msaaFbo, $"multisample target ({samples}x)");
        }

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)Fbo);

        // Tight rows: CopyFrame's pitch math assumes no GL-side padding.
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
    }

    /// <summary>Samples per pixel for the current mode, clamped to what this
    /// driver's renderbuffers actually support (0 = the plain path).</summary>
    private uint SampleCount()
    {
        if (_msaa == MsaaMode.None)
            return 0;
        uint want = _msaa switch
        {
            MsaaMode.X2 => 2u,
            MsaaMode.X4 => 4u,
            MsaaMode.X8 => 8u,
            _ => 4u, // undefined value: the pre-X8 fallback (its msaa FBO exists)
        };
        // GL_MAX_SAMPLES lives on GLEnum, not GetPName, in Silk.NET's bindings.
        _ctx.Api.GetInteger((GetPName)GLEnum.MaxSamples, out var max);
        return max > 0 ? Math.Min(want, (uint)max) : want;
    }

    private void CheckComplete(uint fbo, string what)
    {
        var gl = _ctx.Api;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            throw new InvalidOperationException($"framebuffer incomplete ({what}): {status}");
    }

    /// <summary>Bind the draw target for the frame the scene is about to draw,
    /// set the viewport and clear. Called by the view every tick.</summary>
    public void BeginFrame(byte[] rgbaClearColor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(rgbaClearColor);
        if (rgbaClearColor.Length < 4)
            throw new ArgumentException("clear color needs 4 RGBA bytes", nameof(rgbaClearColor));

        _ctx.MakeCurrent();
        var gl = _ctx.Api;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)Fbo);
        gl.Viewport(0, 0, _w, _h);
        gl.ClearColor(
            rgbaClearColor[0] / 255f,
            rgbaClearColor[1] / 255f,
            rgbaClearColor[2] / 255f,
            rgbaClearColor[3] / 255f);
        gl.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.ColorBufferBit);
    }

    /// <summary>Copies the finished frame into the caller-locked WriteableBitmap.
    /// glReadPixels on the same context synchronizes with all prior draws, so no
    /// explicit fence is required (the Nexus "second WaitAll" equivalent).
    /// The bitmap row stride may differ from the tightly-packed RGBA8 source
    /// pitch; the smaller of the two is copied row by row.</summary>
    /// <param name="dest">Base address of the first destination row.</param>
    /// <param name="destPitch">Destination row stride in bytes.</param>
    /// <returns><see langword="true"/> when a full frame was copied.</returns>
    public bool CopyFrame(nint dest, int destPitch)
    {
        if (_disposed || dest == 0 || destPitch <= 0)
            return false;

        _ctx.MakeCurrent();
        var gl = _ctx.Api;
        if (_msaa != MsaaMode.None)
        {
            // Resolve first: the multisampled draw target -> the single-sample
            // texture the read below comes from. Same size, RGBA8 on both
            // sides; Nearest is the canonical resolve filter (a multisampled
            // buffer is involved, so Linear is not allowed).
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo);
            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _fbo);
            gl.BlitFramebuffer(0, 0, (int)_w, (int)_h, 0, 0, (int)_w, (int)_h,
                ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        }
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _fbo);
        fixed (byte* srcBuf = _scratch)
        {
            gl.ReadPixels(0, 0, _w, _h, PixelFormat.Rgba, PixelType.UnsignedByte, srcBuf);

            var src = srcBuf;
            // Start at the LAST destination row: glReadPixels returns rows
            // bottom-up (row 0 = bottom of the view) while the WriteableBitmap
            // is top-down, so src row y lands in dst row height-1-y — each
            // source row walks the destination upward. This is the sanctioned
            // deviation from Nex's verbatim loop (plan Step 2.4 amended):
            // Nex's Vulkan Download is already top-down and needs no flip;
            // keeping the loop literal mirrored the whole viewport
            // (checkpoint-1: Z axis appeared to point down, mouse up/down
            // felt inverted).
            var dst = (byte*)dest + (long)(_h - 1) * destPitch;
            var srcPitch = (int)_w * 4;
            var copy = Math.Min(srcPitch, destPitch);
            for (var y = 0; y < _h; y++)
            {
                System.Buffer.MemoryCopy(src, dst, (long)destPitch, copy);
                // Force opaque: BeginFrame always clears to the view's opaque
                // background (_backgroundRgba, alpha 255) and scene content is
                // opaque, so the pass normally rewrites 255 over 255 — it is
                // the guarantee, not the fix-up: a non-opaque frame premultiplies
                // away under the Image (a fully transparent bitmap shows
                // whatever sits behind it — the panel).
                for (var x = 3; x < copy; x += 4)
                    dst[x] = 255;
                src += srcPitch;
                dst -= destPitch;
            }
        }
        return true;
    }

    /// <summary>Recreates the FBO and renderbuffers for a new pixel size.
    /// No-op when the size is unchanged (parity with NexGpuReadback.Resize).
    /// The context must not be mid-frame — the view calls this between frames.</summary>
    public void Resize(uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width == _w && height == _h)
            return;
        if (width == 0 || height == 0)
            throw new ArgumentOutOfRangeException(nameof(width), "readback size must be non-zero");

        DeleteResources();
        _w = width;
        _h = height;
        _scratch = new byte[checked((int)width * height * 4)];
        CreateResources();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DeleteResources();
        _scratch = [];
    }

    private void DeleteResources()
    {
        // A disposed context already destroyed the GL objects it owned (they
        // are per-context, not shared), so there is nothing left to delete —
        // and Api would throw. This is the readback-disposed-after-context
        // ordering, e.g. while a failure path unwinds.
        if (_ctx.IsDisposed)
        {
            _fbo = _colorRb = _depthRb = 0;
            _msaaFbo = _msaaColorRb = _msaaDepthRb = _resolveTex = 0;
            return;
        }
        if (_fbo == 0 && _colorRb == 0 && _depthRb == 0
            && _msaaFbo == 0 && _msaaColorRb == 0 && _msaaDepthRb == 0 && _resolveTex == 0)
            return;

        _ctx.MakeCurrent();
        var gl = _ctx.Api;
        if (_msaaColorRb != 0)
        {
            gl.DeleteRenderbuffers(1, ref _msaaColorRb);
            _msaaColorRb = 0;
        }
        if (_msaaDepthRb != 0)
        {
            gl.DeleteRenderbuffers(1, ref _msaaDepthRb);
            _msaaDepthRb = 0;
        }
        if (_msaaFbo != 0)
        {
            gl.DeleteFramebuffers(1, ref _msaaFbo);
            _msaaFbo = 0;
        }
        if (_resolveTex != 0)
        {
            gl.DeleteTextures(1, ref _resolveTex);
            _resolveTex = 0;
        }
        if (_colorRb != 0)
        {
            gl.DeleteRenderbuffers(1, ref _colorRb);
            _colorRb = 0;
        }
        if (_depthRb != 0)
        {
            gl.DeleteRenderbuffers(1, ref _depthRb);
            _depthRb = 0;
        }
        if (_fbo != 0)
        {
            gl.DeleteFramebuffers(1, ref _fbo);
            _fbo = 0;
        }
    }
}
