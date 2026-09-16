/*
 * NexGpuReadback.cs - CPU readback of the Vulkan plot scene via the engine's
 * own staging copy (vkCmdCopyImageToBuffer through IContext.Download).
 * Cross-platform: no D3D11, no shared textures, no keyed mutex.
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

using HelixToolkit.Nex;
using HelixToolkit.Nex.Graphics;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace SenderNG.Modules.Plotter.Nex.Rendering;

/// <summary>
/// Cross-platform CPU readback of the Nex engine's Vulkan frame. The engine
/// renders into a plain Vulkan render-target texture created here (returned as
/// <see cref="EngineTarget"/>), and <c>IContext.Download</c> copies it to a
/// tightly-packed RGBA8 CPU buffer that the caller rows into a bitmap.
/// <para>
/// Synchronization: the host calls <c>Engine.WaitForIdle()</c> (WaitAll) after
/// every <c>Submit</c>, so the render is GPU-complete before the download runs;
/// <c>Download</c> performs its own immediate-queue submit + wait for the copy.
/// </para>
/// </summary>
internal sealed unsafe class NexGpuReadback : IDisposable
{
    private readonly IContext _context;
    private TextureResource? _target;
    private byte[]? _scratch;
    private uint _width;
    private uint _height;
    private bool _disposed;

    /// <summary>The Vulkan texture the engine renders into.</summary>
    public TextureHandle EngineTarget => _target?.Handle ?? default;

    public NexGpuReadback(IContext context, uint width, uint height)
    {
        _context = context;
        CreateResources(width, height);
    }

    private void CreateResources(uint width, uint height)
    {
        _target?.Dispose();
        _target = _context.CreateRenderTarget2D(
            Format.RGBA_UN8,
            width,
            height,
            debugName: "plotter-nex-readback");
        _width = width;
        _height = height;
        _scratch = new byte[checked((int)width * (int)height * 4)];
    }

    /// <summary>Fits the device-sized frame into the destination. The bitmap
    /// row stride may differ from the tightly-packed RGBA8 source pitch; the
    /// smaller of the two is copied row by row.</summary>
    /// <param name="dest">Base address of the first destination row.</param>
    /// <param name="destPitch">Destination row stride in bytes.</param>
    /// <returns><see langword="true"/> when a full frame was copied.</returns>
    public bool CopyFrame(IntPtr dest, int destPitch)
    {
        if (_disposed || _target is null || _scratch is null)
            return false;

        var range = new TextureRangeDesc
        {
            Dimensions = new() { Width = _width, Height = _height, Depth = 1 },
            NumLayers = 1,
            NumMipLevels = 1,
        };
        var dataSize = (uint)_scratch.Length;
        fixed (byte* srcBuf = _scratch)
        {
            var result = _context.Download(_target.Handle, range, (nint)srcBuf, dataSize);
            if (result != ResultCode.Ok)
                return false;

            var src = srcBuf;
            var dst = (byte*)dest;
            var srcPitch = (int)_width * 4;
            var copy = Math.Min(srcPitch, destPitch);
            for (var y = 0; y < _height; y++)
            {
                System.Buffer.MemoryCopy(src, dst, (long)destPitch, copy);
                // Force opaque: the engine clears to transparent black, so
                // premultiplied compositing would drop every frame (a fully
                // transparent bitmap shows whatever sits behind the Image —
                // the near-black panel). Scene content is opaque.
                for (var x = 3; x < copy; x += 4)
                    dst[x] = 255;
                src += srcPitch;
                dst += destPitch;
            }
        }
        return true;
    }

    /// <summary>Recreates the render target for a new pixel size. The engine
    /// must not be mid-frame.</summary>
    public void Resize(uint width, uint height)
    {
        if (width == _width && height == _height)
            return;
        CreateResources(width, height);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _target?.Dispose();
        _target = null;
        _scratch = null;
    }
}
