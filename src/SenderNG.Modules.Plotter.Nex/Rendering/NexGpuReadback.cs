/*
 * NexGpuReadback.cs - CPU readback of the Vulkan plot scene via a D3D11
 * shared-texture bridge (avoids the Avalonia composition presenter path).
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

using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Interop.DirectX;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Vulkan;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace SenderNG.Modules.Plotter.Nex.Rendering;

/// <summary>
/// D3D11 readback of the Nex engine's Vulkan frame. The engine renders into a
/// D3D11 shared texture (NT handle + keyed mutex, same description as the
/// official WinUI interop bridge) and the CPU reads it back into a caller-
/// supplied buffer, bypassing the Avalonia composition presenter that freezes
/// (upstream bug: the composition server never runs the keyed-mutex update).
/// <para>
/// Synchronization: NONE, by design. The host calls <c>Engine.WaitForIdle()</c>
/// after every <c>Submit</c>, so the Vulkan write is GPU-complete before the
/// D3D11 <c>CopyResource</c> runs. The two-party keyed-mutex handshake
/// (engine acquire 0 / release 1, reader acquire 1 / release 0) is only
/// required for the upstream double-buffered async path — with a single
/// buffer fully serialized by WaitForIdle the keyed calls conflict
/// (<c>DXGI_ERROR_INVALID_CALL</c>) and are simply omitted. The keyed-mutex
/// misc flag is kept on the texture so the Vulkan import works unmodified.
/// </para>
/// </summary>
internal sealed unsafe class NexGpuReadback : IDisposable
{
    private readonly IContext _context;
    private readonly D3D11DeviceManager _d3d11;

    private SharedTextureResult? _shared;
    private ID3D11Texture2D? _staging;
    private ImportedVulkanTexture? _imported;
    private ID3D11RenderTargetView? _diagRtv;
    private IDXGIKeyedMutex? _keyedMutex;
    private uint _width;
    private uint _height;
    private bool _disposed;

    /// <summary>The imported Vulkan texture the engine renders into.</summary>
    public TextureHandle EngineTarget => _imported?.Handle ?? TextureHandle.Null;

    /// <summary>Present/readback handoff for <c>Engine.Submit</c>. Left at
    /// <c>None</c>: GPU ordering comes from the host's <c>WaitForIdle()</c>
    /// between submit and readback, so no keyed-mutex traffic is needed.</summary>
    public KeyedMutexSyncInfo EngineSyncInfo => default;

    public NexGpuReadback(IContext context, uint width, uint height)
    {
        _context = context;
        _d3d11 = new D3D11DeviceManager();
        CreateResources(width, height);
    }

    private void CreateResources(uint width, uint height)
    {
        _shared = SharedTextureFactory.CreateForWinUI(_d3d11, width, height);
        _keyedMutex = _shared.Texture.QueryInterface<IDXGIKeyedMutex>();
        _imported = VulkanExternalMemoryImporter.Import(
            _context,
            _shared.SharedHandle,
            VkExternalMemoryHandleTypeFlags.D3D11Texture,
            VkFormat.R8G8B8A8Unorm,
            width,
            height
        );

        _width = width;
        _height = height;
        CreateStaging();
    }

    private void CreateStaging()
    {
        var desc = new Texture2DDescription
        {
            Width = _width,
            Height = _height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Vortice.DXGI.Format.R8G8B8A8_UNorm,
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
            SampleDescription = new(1u, 0u),
        };
        _staging?.Dispose();
        _staging = _d3d11.Device.CreateTexture2D(desc);
    }

    /// <summary>Fits the device-sized frame into the destination. The bitmap
    /// row stride may differ from the texture pitch; the smaller of the two is
    /// copied row by row.</summary>
    /// <param name="dest">Base address of the first destination row.</param>
    /// <param name="destPitch">Destination row stride in bytes.</param>
    /// <returns><see langword="true"/> when a full frame was copied.</returns>
    public bool CopyFrame(IntPtr dest, int destPitch)
    {
        if (_disposed || _shared is null || _staging is null)
            return false;

        AcquireMutex();
        try
        {
            _d3d11.DeviceContext.CopyResource(_staging, _shared.Texture);
        }
        finally
        {
            ReleaseMutex();
        }
        _d3d11
            .DeviceContext.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped)
            .CheckError();
        try
        {
            var src = (byte*)mapped.DataPointer;
            var dst = (byte*)dest;
            var copy = Math.Min(mapped.RowPitch, destPitch);
            for (var y = 0; y < _height; y++)
            {
                System.Buffer.MemoryCopy(src, dst, (long)destPitch, copy);
                // Force opaque: the engine clears to transparent black, so
                // premultiplied compositing would drop every frame (a fully
                // transparent bitmap shows whatever sits behind the Image —
                // the near-black panel). Scene content is opaque.
                for (var x = 3; x < copy; x += 4)
                    dst[x] = 255;
                src += mapped.RowPitch;
                dst += destPitch;
            }
        }
        finally
        {
            _d3d11.DeviceContext.Unmap(_staging, 0);
        }
        return true;
    }

    /// <summary>DIAGNOSTIC: clears the shared texture to red from the D3D11 side,
    /// then reads it back with the same staging path used by <see cref="CopyFrame"/>.
    /// If the probe comes back red the D3D11 readback path works and the missing
    /// link is the Vulkan import/write; if it comes back black the readback itself
    /// is broken. Scans a grid to stay cheap.</summary>
    public string DiagTestD3D11Write()
    {
        try
        {
            if (_shared is null || _staging is null)
                return "diag(d3d11-write): no resources";
            _diagRtv ??= _d3d11.Device.CreateRenderTargetView(_shared.Texture);
            _d3d11
                .DeviceContext.ClearRenderTargetView(
                    _diagRtv,
                    new Vortice.Mathematics.Color4(1f, 0f, 0.1f, 1f)
                );
            AcquireMutex();
            try
            {
                _d3d11
                    .DeviceContext.CopyResource(_staging, _shared.Texture);
            }
            finally
            {
                ReleaseMutex();
            }
            _d3d11
                .DeviceContext.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped)
                .CheckError();
            try
            {
                var src = (byte*)mapped.DataPointer;
                var step = Math.Max(1u, Math.Min(_width, _height) / 64);
                var checkedPx = 0;
                var redPx = 0;
                var totalLum = 0L;
                var maxLum = 0;
                for (uint y = 0; y < _height; y += step)
                {
                    var row = src + y * mapped.RowPitch;
                    for (uint x = 0; x < _width; x += step)
                    {
                        var r = row[x * 4];
                        var g = row[x * 4 + 1];
                        var b = row[x * 4 + 2];
                        var lum = (r * 299 + g * 587 + b * 114) / 1000;
                        checkedPx++;
                        totalLum += lum;
                        if (lum > maxLum)
                            maxLum = lum;
                        if (r > 200 && g < 100 && b < 100)
                            redPx++;
                    }
                }
                var avg = checkedPx == 0 ? 0 : totalLum / checkedPx;
                return $"diag(d3d11-write-red): grid={step}px maxLum={maxLum} avgLum={avg} redPx={redPx}/{checkedPx}";
            }
            finally
            {
                _d3d11.DeviceContext.Unmap(_staging, 0);
            }
        }
        catch (Exception ex)
        {
            return $"diag(d3d11-write-red) FAILED: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>Acquires the shared-texture keyed mutex before GPU access. The
    /// engine's Vulkan side never participates in the two-party keyed-mutex
    /// handshake (we pass default <c>EngineSyncInfo</c>, so there is no engine
    /// acquire-0/release-1 waiter), therefore this single D3D11 reader must
    /// acquire and release with the SAME key — a release key that no one is
    /// waiting to acquire returns <c>DXGI_ERROR_INVALID_CALL</c>. Without the
    /// acquire at all, D3D11 refuses to access the <c>SHARED_KEYEDMUTEX</c>
    /// resource and always reads black.</summary>
    private void AcquireMutex()
    {
        _keyedMutex?.AcquireSync(0u, 1000);
    }

    /// <summary>Releases the keyed mutex with the same key used for the acquire.</summary>
    private void ReleaseMutex()
    {
        _keyedMutex?.ReleaseSync(0u);
    }

    /// <summary>Recreates the shared texture, import and staging for a new
    /// pixel size. The engine must not be mid-frame.</summary>
    public void Resize(uint width, uint height)
    {
        if (width == _width && height == _height)
            return;
        _staging?.Dispose();
        _staging = null;
        _imported?.Dispose();
        _imported = null;
        _shared?.Dispose();
        _shared = null;
        _keyedMutex?.Dispose();
        _keyedMutex = null;
        CreateResources(width, height);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _staging?.Dispose();
        _staging = null;
        _imported?.Dispose();
        _imported = null;
        _shared?.Dispose();
        _shared = null;
        _keyedMutex?.Dispose();
        _keyedMutex = null;
        _d3d11.Dispose();
    }
}