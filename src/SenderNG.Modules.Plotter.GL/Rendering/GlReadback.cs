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

namespace SenderNG.Modules.Plotter.GL.Rendering;

/// <summary>The offscreen target the view tick renders into: one RGBA8 +
/// depth24 FBO the scene draws into, and a <c>glReadPixels</c> + row copy that
/// lands the frame in the caller's locked WriteableBitmap. Public surface
/// mirrors NexGpuReadback (ctor(w,h), BeginFrame, CopyFrame, Resize, Dispose)
/// so the view-side pipeline stays renderer-shaped.
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
    private byte[] _scratch;
    private bool _disposed;

    public uint Width => _w;

    public uint Height => _h;

    /// <summary>The FBO this readback renders into (diagnostics + Task 4's
    /// draw path binds it).</summary>
    public int Fbo => (int)_fbo;

    public GlReadback(GlContext ctx, uint width, uint height)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (width == 0 || height == 0)
            throw new ArgumentOutOfRangeException(nameof(width), "readback size must be non-zero");
        _ctx = ctx;
        _w = width;
        _h = height;
        _scratch = new byte[checked((int)width * height * 4)];
        CreateResources();
    }

    private void CreateResources()
    {
        _ctx.MakeCurrent();
        var gl = _ctx.Api;

        gl.GenFramebuffers(1, out _fbo);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

        gl.GenRenderbuffers(1, out _colorRb);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _colorRb);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, _w, _h);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _colorRb);

        gl.GenRenderbuffers(1, out _depthRb);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRb);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, _w, _h);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _depthRb);

        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            throw new InvalidOperationException($"framebuffer incomplete: {status}");

        // Tight rows: CopyFrame's pitch math assumes no GL-side padding.
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
    }

    /// <summary>Bind the FBO, set the viewport and clear for the frame the
    /// scene is about to draw. Called by the view every tick.</summary>
    public void BeginFrame(byte[] rgbaClearColor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(rgbaClearColor);
        if (rgbaClearColor.Length < 4)
            throw new ArgumentException("clear color needs 4 RGBA bytes", nameof(rgbaClearColor));

        _ctx.MakeCurrent();
        var gl = _ctx.Api;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
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
                // Force opaque: the engine clears to transparent black, so
                // premultiplied compositing would drop every frame (a fully
                // transparent bitmap shows whatever sits behind the Image —
                // the near-black panel). Scene content is opaque.
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
            return;
        }
        if (_fbo == 0 && _colorRb == 0 && _depthRb == 0)
            return;

        _ctx.MakeCurrent();
        var gl = _ctx.Api;
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
