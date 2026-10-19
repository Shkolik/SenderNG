/*
 * GlContext.Cocoa.cs - macOS CGL half of GlContext: a no-drawable
 * core-profile CGL context via hand-rolled P/Invokes against
 * OpenGL.framework (Apple's GL 4.1 ceiling). Only ever *called* on macOS
 * (GlContext.Create dispatches at runtime), but compiled on every OS.
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

using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using GlApi = Silk.NET.OpenGL.GL;

namespace SenderNG.Modules.Common.Render;

public sealed unsafe partial class GlContext
{
    // CGL enums (OpenGL/CGLTypes.h, OpenGL/OpenGL.h). The core-profile request
    // is Apple's newest — kCGLOGLPVersion_3_2_Core — which every Mac with a
    // qualifying renderer realizes as GL 4.1; the version audit afterwards,
    // not the pixel format, decides usability.
    private const uint KCglPFAOpenGLProfile = 99;
    private const uint KCglGLPVersion32Core = 0x3200;
    private const uint KCglPFAColorSize = 8;
    private const uint KCglPFADepthSize = 12;
    private const uint KCglPFAAllowOfflineRenderers = 96;
    private const string OpenGlFrameworkPath = "/System/Library/Frameworks/OpenGL.framework/OpenGL";

    private static readonly object s_glFrameworkGate = new();
    private static nint s_glFramework;

    private nint _cglContext;

    /// <summary>Create the offscreen CGL core-profile context. No drawable is
    /// set: rendering goes to FBOs, the same model as the never-shown WGL
    /// window and the surfaceless EGL context. Offline renderers are allowed
    /// so VM / headless sessions still qualify.</summary>
    private void InitializeCocoa()
    {
        uint* attribs = stackalloc uint[]
        {
            KCglPFAOpenGLProfile, KCglGLPVersion32Core,
            KCglPFAColorSize, 24,
            KCglPFADepthSize, 24,
            KCglPFAAllowOfflineRenderers, 1,
            0,
        };

        var err = CGLChoosePixelFormat(attribs, out var pixelFormat, out var numPixelFormats);
        if (err != 0 || pixelFormat == 0 || numPixelFormats == 0)
            throw new InvalidOperationException($"CGLChoosePixelFormat failed: {CglError(err)}");

        try
        {
            err = CGLCreateContext(pixelFormat, 0, out _cglContext);
        }
        finally
        {
            // The pixel format is only an ingredient of the context — CGL
            // retains what it needs, so release ours the moment the call
            // returns, success or failure.
            CGLReleasePixelFormat(pixelFormat);
        }
        if (err != 0 || _cglContext == 0)
            throw new InvalidOperationException($"CGLCreateContext failed: {CglError(err)}");

        err = CGLSetCurrentContext(_cglContext);
        if (err != 0)
            throw new InvalidOperationException($"CGLSetCurrentContext failed: {CglError(err)}");

        // Resolve the GL entry points through this context (Silk calls the
        // INativeContext members). The context must be current.
        _gl = new GlApi(this);

        Version = GlString(StringName.Version);
        Renderer = GlString(StringName.Renderer);
        Vendor = GlString(StringName.Vendor);

        var profileMask = 0;
        Api.GetInteger(GetPName.ContextProfileMask, &profileMask);
        ProfileMask = profileMask;
        var contextFlags = 0;
        Api.GetInteger(GetPName.ContextFlags, &contextFlags);
        ContextFlagsMask = contextFlags;

        CreationPath = "path=cgl-core3.2";

        if (!MeetsMinimumVersion(Version, 3, 3))
            throw new PlotGlUnavailableException(
                $"OpenGL {Version} is below the required 3.3 ({Renderer})",
                GlFailureCause.VersionTooLow);
    }

    private void MakeCurrentCocoa()
    {
        if (_cglContext == 0)
            throw new InvalidOperationException("CGLSetCurrentContext failed: no context");
        var err = CGLSetCurrentContext(_cglContext);
        if (err != 0)
            throw new InvalidOperationException($"CGLSetCurrentContext failed: {CglError(err)}");
    }

    private bool IsCurrentCocoa() => CGLGetCurrentContext() == _cglContext;

    /// <summary>Entry-point resolver for Silk's loader: dlsym on the loaded
    /// OpenGL framework, whose export table carries the core 1.1–4.1 gl*
    /// symbols (they dispatch to the current renderer internally). Returns 0
    /// for symbols the framework does not export.</summary>
    private nint GetProcAddressCocoa(string procName)
    {
        nint handle;
        lock (s_glFrameworkGate)
        {
            if (s_glFramework == 0)
            {
                if (!NativeLibrary.TryLoad(OpenGlFrameworkPath, out handle))
                    return 0;
                s_glFramework = handle;
            }
            handle = s_glFramework;
        }
        return NativeLibrary.TryGetExport(handle, procName, out var address) ? address : 0;
    }

    private void DisposeCocoa()
    {
        if (_cglContext == 0)
            return;
        // Only unbind when THIS context is current — never clobber another
        // context the thread may have made current in the meantime.
        if (CGLGetCurrentContext() == _cglContext)
            CGLSetCurrentContext(0);
        CGLDestroyContext(_cglContext);
        _cglContext = 0;
    }

    private static string CglError(int code)
    {
        var name = CGLErrorString(code);
        return name == 0 ? $"0x{code:X}" : $"0x{code:X} ({Marshal.PtrToStringUTF8(name)})";
    }

    // --- hand-rolled P/Invoke (OpenGL.framework) ---

    [DllImport(OpenGlFrameworkPath, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CGLChoosePixelFormat(uint* attribs, out nint pixelFormat, out int numPixelFormats);

    [DllImport(OpenGlFrameworkPath, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CGLCreateContext(nint pixelFormat, nint shareContext, out nint context);

    [DllImport(OpenGlFrameworkPath, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CGLSetCurrentContext(nint context);

    [DllImport(OpenGlFrameworkPath, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint CGLGetCurrentContext();

    [DllImport(OpenGlFrameworkPath, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CGLDestroyContext(nint context);

    [DllImport(OpenGlFrameworkPath, CallingConvention = CallingConvention.Cdecl)]
    private static extern void CGLReleasePixelFormat(nint pixelFormat);

    [DllImport(OpenGlFrameworkPath, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint CGLErrorString(int error);
}
