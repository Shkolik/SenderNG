/*
 * GlContext.Egl.cs - Linux EGL half of GlContext: hand-rolled P/Invokes
 * against libEGL.so.1 (surfaceless, EGL_KHR_surfaceless_context). Desktop
 * GL 3.3 on x64; GLES 3.0 on arm64. Only ever *called* on non-Windows
 * (GlContext.Create dispatches at runtime), but compiled on every OS — so
 * these DllImports never fire on Windows.
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

namespace SenderNG.Modules.Plotter.GL.Rendering;

internal sealed unsafe partial class GlContext
{
    // EGL 1.5 / platform enums (egl.h, EGL_KHR_surfaceless_context,
    // EGL_MESA_platform_surfaceless). Hand-rolled per the Task 8 ruling:
    // Silk.NET.EGL has no 2.x package (sunset at Silk 2.0), so same
    // DllImport / NativeLibrary pattern as the WGL half.
    private const string LibEgl = "libEGL.so.1";
    private const uint EglFalse = 0;
    private const int EglNone = 0x3038;
    private const uint EglOpenGLBit = 0x0008;
    private const uint EglOpenGLES3Bit = 0x0040;
    private const int EglAlphaSize = 0x3021, EglBlueSize = 0x3022, EglGreenSize = 0x3023,
                      EglRedSize = 0x3024, EglDepthSize = 0x3025;
    private const int EglRenderableType = 0x3040;
    private const int EglContextClientVersion = 0x3098, EglContextMinorVersion = 0x30FB;
    private const uint EglOpenGlEsApi = 0x30A0, EglOpenGlApi = 0x30A2;
    private const uint EglPlatformSurfacelessMesa = 0x31DD;

    private static readonly object s_glLibGate = new();
    private static nint s_glLib;

    private nint _eglDisplay, _eglContext;

    /// <summary>Create the surfaceless EGL context per the Task 8 call
    /// sequence: MESA surfaceless display (default-display fallback) →
    /// initialize → bind ES/GL API → choose one config → create context →
    /// surfaceless eglMakeCurrent → version audit. <see cref="IsGles"/> is
    /// set to <see cref="IsArm64"/>.</summary>
    private void InitializeEgl()
    {
        // 1. Prefer MESA's surfaceless platform — no X11/Wayland connection
        //    needed, which is exactly what an offscreen FBO renderer wants.
        //    Fall back to the default display when the platform is missing:
        //    eglGetPlatformDisplay returns 0 for an unsupported platform, and
        //    it is an EGL 1.5 entry point, so a pre-1.5 libEGL lacks the
        //    symbol entirely (EntryPointNotFoundException) — catch that too
        //    and let eglGetDisplay (present since EGL 1.0) decide.
        nint display;
        try
        {
            display = eglGetPlatformDisplay(EglPlatformSurfacelessMesa, 0, null);
        }
        catch (EntryPointNotFoundException)
        {
            display = 0;
        }
        if (display == 0)
            display = eglGetDisplay(0);
        if (display == 0)
            throw new InvalidOperationException($"eglGetDisplay failed: {EglError()}");
        _eglDisplay = display;

        // 2.
        if (eglInitialize(_eglDisplay, out _, out _) == EglFalse)
            throw new InvalidOperationException($"eglInitialize failed: {EglError()}");

        // 3. arm64 is the GLES 3.0 flavor; x64 binds desktop GL.
        if (eglBindAPI(IsArm64 ? EglOpenGlEsApi : EglOpenGlApi) == EglFalse)
            throw new InvalidOperationException($"eglBindAPI failed: {EglError()}");

        // 4. One config: RGBA8 + depth 24 ("COLOR_SIZE 8, DEPTH_SIZE 24" —
        //    realized as R=G=B=A=8) with the matching renderable type.
        int* configAttribs = stackalloc int[]
        {
            EglRenderableType, (int)(IsArm64 ? EglOpenGLES3Bit : EglOpenGLBit),
            EglRedSize, 8, EglGreenSize, 8, EglBlueSize, 8, EglAlphaSize, 8,
            EglDepthSize, 24,
            EglNone,
        };
        nint config = 0;
        if (eglChooseConfig(_eglDisplay, configAttribs, &config, 1, out var numConfigs) == EglFalse
            || numConfigs < 1)
            throw new InvalidOperationException($"eglChooseConfig failed: {EglError()}");

        // 5. x64: CONTEXT_MAJOR 3 + CONTEXT_MINOR 3 (desktop GL).
        //    arm64: CONTEXT_CLIENT_VERSION (= MAJOR) 3 only — GLES. A
        //    stackalloc cannot be retargeted at a different size, so the
        //    GLES list just plants an early terminator at slot 2; EGL reads
        //    attribs up to EGL_NONE and ignores the rest.
        int* ctxAttribs = stackalloc int[]
        {
            EglContextClientVersion, 3,
            EglContextMinorVersion, 3,
            EglNone,
        };
        if (IsArm64)
            ctxAttribs[2] = EglNone;
        _eglContext = eglCreateContext(_eglDisplay, config, 0, ctxAttribs);
        if (_eglContext == 0)
            throw new InvalidOperationException($"eglCreateContext failed: {EglError()}");

        // 6. EGL_KHR_surfaceless_context: NO_SURFACE for both draw and read.
        if (eglMakeCurrent(_eglDisplay, 0, 0, _eglContext) == EglFalse)
            throw new InvalidOperationException($"eglMakeCurrent failed: {EglError()}");

        // 7. Resolve the GL entry points through this context (Silk calls the
        //    INativeContext members). The context must be current.
        _gl = new GlApi(this);

        // 8. Audit what the driver reports + IsGles.
        IsGles = IsArm64;
        Version = GlString(StringName.Version);
        Renderer = GlString(StringName.Renderer);
        Vendor = GlString(StringName.Vendor);
        CreationPath = "path=egl-surfaceless";

        var minMinor = IsArm64 ? 0 : 3;    // GLES 3.0 on arm64, GL 3.3 on x64
        if (!MeetsMinimumVersion(Version, 3, minMinor))
            throw new PlotGlUnavailableException(
                $"OpenGL {Version} is below the required {(IsArm64 ? "ES 3.0" : "3.3")} ({Renderer})",
                GlFailureCause.VersionTooLow);
    }

    private void MakeCurrentEgl()
    {
        if (_eglContext == 0 || eglMakeCurrent(_eglDisplay, 0, 0, _eglContext) == EglFalse)
            throw new InvalidOperationException($"eglMakeCurrent failed: {EglError()}");
    }

    private bool IsCurrentEgl() => eglGetCurrentContext() == _eglContext;

    /// <summary>Entry-point resolver for Silk's loader: eglGetProcAddress
    /// first (extensions + most core), the driver library's export table
    /// second (core 1.x entry points Mesa may not expose through EGL).
    /// Returns 0 when neither knows the symbol — extension entry points a
    /// driver does not expose stay unloaded instead of throwing during
    /// context creation.</summary>
    private nint GetProcAddressEgl(string procName)
    {
        var p = eglGetProcAddress(procName);
        return p != 0 ? p : GetFromGlLibrary(procName);
    }

    private static nint GetFromGlLibrary(string procName)
    {
        nint handle;
        lock (s_glLibGate)
        {
            if (s_glLib == 0)
            {
                if (!NativeLibrary.TryLoad(IsArm64 ? "libGLESv2.so.2" : "libGL.so.1", out handle))
                    return 0;
                s_glLib = handle;
            }
            handle = s_glLib;
        }
        return NativeLibrary.TryGetExport(handle, procName, out var address) ? address : 0;
    }

    private void DisposeEgl()
    {
        if (_eglDisplay == 0)
        {
            _eglContext = 0;
            return;
        }
        // Only unbind when THIS context is current — never clobber another
        // context the thread may have made current in the meantime.
        if (eglGetCurrentContext() == _eglContext)
            eglMakeCurrent(_eglDisplay, 0, 0, 0);
        if (_eglContext != 0)
        {
            eglDestroyContext(_eglDisplay, _eglContext);
            _eglContext = 0;
        }
        eglTerminate(_eglDisplay);
        _eglDisplay = 0;
    }

    private static string EglError() => $"0x{eglGetError():X}";

    // --- hand-rolled P/Invoke (libEGL.so.1) ---

    [DllImport(LibEgl, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint eglGetDisplay(nint nativeDisplay);

    [DllImport(LibEgl, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint eglGetPlatformDisplay(uint platform, nint nativeDisplay, int* attribs);

    [DllImport(LibEgl, CallingConvention = CallingConvention.Cdecl)]
    private static extern int eglInitialize(nint display, out int major, out int minor);

    [DllImport(LibEgl, CallingConvention = CallingConvention.Cdecl)]
    private static extern int eglBindAPI(uint api);

    [DllImport(LibEgl, CallingConvention = CallingConvention.Cdecl)]
    private static extern int eglChooseConfig(nint display, int* attribs, nint* configs,
        int configSize, out int numConfigs);

    [DllImport(LibEgl, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint eglCreateContext(nint display, nint config, nint shareContext,
        int* attribs);

    [DllImport(LibEgl, CallingConvention = CallingConvention.Cdecl)]
    private static extern int eglMakeCurrent(nint display, nint draw, nint read, nint context);

    [DllImport(LibEgl, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint eglGetCurrentContext();

    [DllImport(LibEgl, CallingConvention = CallingConvention.Cdecl)]
    private static extern int eglDestroyContext(nint display, nint context);

    [DllImport(LibEgl, CallingConvention = CallingConvention.Cdecl)]
    private static extern int eglTerminate(nint display);

    [DllImport(LibEgl, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint eglGetProcAddress(string name);

    [DllImport(LibEgl, CallingConvention = CallingConvention.Cdecl)]
    private static extern int eglGetError();
}
