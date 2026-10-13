/*
 * GlContext.Win32.cs - Windows WGL half of GlContext: a never-shown top-level
 * window + hand-rolled WGL 3.3 core context. Only ever *called* on Windows
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
    private const int WsOverlapped = 0;
    private const uint CsHredraw = 0x0002, CsVredraw = 0x0001;
    private const uint PfdDrawToWindow = 0x00000004, PfdSupportOpengl = 0x00000020,
                       PfdDoublebuffer = 0x00000001;
    private const byte PfdTypeRgba = 0;
    // WGL_CONTEXT_* from the ARB_create_context spec (wglext.h). Note the two
    // DIFFERENT 0x1 bits: inside FLAGS (0x2094), 0x1 is
    // WGL_CONTEXT_DEBUG_BIT_ARB and 0x2 is
    // WGL_CONTEXT_FORWARD_COMPATIBLE_BIT_ARB — while 0x1 inside the
    // PROFILE_MASK (0x9126) value is the core-profile bit. The brief mandates
    // a 3.3 core FORWARD-COMPATIBLE context, so the request below is
    // major/minor + core profile mask + forward-compatible flag.
    private const int WglContextMajorVersion = 0x2091, WglContextMinorVersion = 0x2092,
                      WglContextFlags = 0x2094, WglContextProfileMask = 0x9126,
                      WglContextCoreProfileBit = 0x00000001,
                      WglContextForwardCompatibleBit = 0x00000002;

    private static int s_classSeq;
    private static readonly WndProc s_defWndProc = DefWindowProc;
    private static readonly object s_opengl32Gate = new();
    private static nint s_opengl32;

    private IntPtr _hwnd, _hdc, _legacyCtx, _ctx;
    private string? _className;
    private nint _hInstance;

    private void InitializeWin32()
    {
        // 1. Register a window class under a name unique to this instance
        //    (one process may hold several contexts: test probe + live view).
        _hInstance = GetModuleHandleW(null);
        _className = $"SenderNGPlotGl_{Environment.ProcessId}_{Interlocked.Increment(ref s_classSeq)}";
        var classNamePtr = Marshal.StringToHGlobalUni(_className);
        try
        {
            var wc = new WndClassExW
            {
                Size = (uint)Marshal.SizeOf<WndClassExW>(),
                Style = CsHredraw | CsVredraw,
                Proc = Marshal.GetFunctionPointerForDelegate(s_defWndProc),
                Instance = _hInstance,
                ClassName = classNamePtr,
            };
            if (RegisterClassExW(ref wc) == 0)
                throw new InvalidOperationException($"RegisterClassExW failed: {Marshal.GetLastWin32Error()}");
        }
        finally
        {
            // The system copies the class name; the marshalled copy is ours.
            Marshal.FreeHGlobal(classNamePtr);
        }

        // 2. A 1x1 top-level window that is deliberately NEVER shown: WGL
        //    wants a real HWND + DC, we never pump its messages and never
        //    present to it (frames go to the FBO).
        _hwnd = CreateWindowExW(0, _className, _className, WsOverlapped, 0, 0, 1, 1,
            0, 0, _hInstance, 0);
        if (_hwnd == 0)
            throw new InvalidOperationException($"CreateWindowExW failed: {Marshal.GetLastWin32Error()}");

        // 3. Device context for the window.
        _hdc = GetDC(_hwnd);
        if (_hdc == 0)
            throw new InvalidOperationException($"GetDC failed: {Marshal.GetLastWin32Error()}");

        // 4. Pick and set the pixel format (the driver needs this before any
        //    context can exist; it can only be set once per DC).
        var pfd = new PixelFormatDescriptor
        {
            Size = (ushort)sizeof(PixelFormatDescriptor),
            Version = 1,                    // nVersion is a fixed 1 per wingdi.h
            Flags = PfdDrawToWindow | PfdSupportOpengl | PfdDoublebuffer,
            PixelType = PfdTypeRgba,
            ColorBits = 32,
            DepthBits = 24,
            StencilBits = 8,
        };
        var pf = ChoosePixelFormat(_hdc, ref pfd);
        if (pf == 0)
            throw new InvalidOperationException($"ChoosePixelFormat failed: {Marshal.GetLastWin32Error()}");
        if (SetPixelFormat(_hdc, pf, ref pfd) == 0)
            throw new InvalidOperationException($"SetPixelFormat failed: {Marshal.GetLastWin32Error()}");

        // 5. Legacy context first — it is the only way to get an extension
        //    pointer address on Windows (wglGetProcAddress needs a current
        //    context).
        _legacyCtx = wglCreateContext(_hdc);
        if (_legacyCtx == 0)
            throw new InvalidOperationException($"wglCreateContext failed: {Marshal.GetLastWin32Error()}");
        if (wglMakeCurrent(_hdc, _legacyCtx) == 0)
            throw new InvalidOperationException($"wglMakeCurrent(legacy) failed: {Marshal.GetLastWin32Error()}");

        // 6. Upgrade to a real 3.3 core forward-compatible context through
        //    wglCreateContextAttribsARB; fall back to the legacy context when
        //    the entry point is missing or the request fails (the version
        //    audit below then decides whether legacy is good enough).
        var pfnCreate = GetUsableProc(wglGetProcAddress("wglCreateContextAttribsARB"));
        ArbCreateResolved = pfnCreate != 0;
        if (pfnCreate != 0)
        {
            var createAttribs = Marshal.GetDelegateForFunctionPointer<WglCreateContextAttribsArb>(pfnCreate);
            int* attribs = stackalloc int[]
            {
                WglContextMajorVersion, 3,
                WglContextMinorVersion, 3,
                WglContextProfileMask, WglContextCoreProfileBit,
                WglContextFlags, WglContextForwardCompatibleBit,
                0,
            };
            ArbCreateAttribs =
                $"0x{WglContextProfileMask:X}=0x{WglContextCoreProfileBit:X} (core profile), " +
                $"0x{WglContextFlags:X}=0x{WglContextForwardCompatibleBit:X} (forward compatible), " +
                "0 (terminator) — via major=3 minor=3";
            _ctx = createAttribs(_hdc, 0, attribs);
            if (_ctx != 0)
                ArbCreateSucceeded = true;
        }

        if (_ctx == 0)
        {
            // Review evidence: record that the mandated core path did NOT
            // produce this context — the version audit below may still pass,
            // because a legacy context reports its own (usually higher) version.
            UsedLegacyFallback = true;
            _ctx = _legacyCtx;
            _legacyCtx = 0;                 // already current — becomes THE context
        }
        else
        {
            // 7. The real context is current; the legacy bootstrap goes away.
            wglMakeCurrent(0, 0);
            wglDeleteContext(_legacyCtx);
            _legacyCtx = 0;
            if (wglMakeCurrent(_hdc, _ctx) == 0)
                throw new InvalidOperationException($"wglMakeCurrent failed: {Marshal.GetLastWin32Error()}");
        }

        // 8. Resolve the GL entry points through this context (Silk calls the
        //    INativeContext members). The context must be current.
        _gl = new GlApi(this);

        // 9. Audit what the driver reports. glGetString needs the current
        //    context, which step 7/6 established.
        Version = GlString(StringName.Version);
        Renderer = GlString(StringName.Renderer);
        Vendor = GlString(StringName.Vendor);

        // 10. Read the definitive context identity off the CURRENT context —
        //     profile mask + flags. A legacy bootstrap context reports 0 for
        //     both (it was created without profile attributes); a successful
        //     ARB create reports exactly the mask/flags it was requested with.
        //     Errors are impossible to throw here: an unsupported enum just
        //     leaves the output untouched (0), which is the right answer.
        var profileMask = 0;
        Api.GetInteger(GetPName.ContextProfileMask, &profileMask);
        ProfileMask = profileMask;
        var contextFlags = 0;
        Api.GetInteger(GetPName.ContextFlags, &contextFlags);
        ContextFlagsMask = contextFlags;

        CreationPath = ArbCreateSucceeded ? "path=arb-core3.3"
            : UsedLegacyFallback ? "path=legacy-fallback" : "path=legacy";

        if (!MeetsMinimumVersion(Version, 3, 3))
            throw new PlotGlUnavailableException(
                $"OpenGL {Version} is below the required 3.3 ({Renderer})",
                GlFailureCause.VersionTooLow);
    }

    private void MakeCurrentWin32()
    {
        if (_ctx == 0 || wglMakeCurrent(_hdc, _ctx) == 0)
            throw new InvalidOperationException($"wglMakeCurrent failed: {Marshal.GetLastWin32Error()}");
    }

    private bool IsCurrentWin32() => wglGetCurrentContext() == _ctx;

    /// <summary>Entry-point resolver for Silk's loader: wglGetProcAddress first
    /// (everything 1.2+), the opengl32.dll export table second (the GL 1.1
    /// names wglGetProcAddress refuses). Returns 0 when neither knows the
    /// symbol — extension entry points a driver does not expose stay unloaded
    /// instead of throwing during context creation.</summary>
    private nint GetProcAddressWin32(string procName)
    {
        var p = wglGetProcAddress(procName);
        return GetUsableProc(p) != 0 ? p : GetFromOpengl32(procName);
    }

    /// <summary>wglGetProcAddress reports "not found" as NULL or as the
    /// sentinel values 1, 2, 3 and -1, none of which are callable.</summary>
    private static nint GetUsableProc(nint p) =>
        p != 0 && p != 1 && p != 2 && p != 3 && p != -1 ? p : 0;

    private static nint GetFromOpengl32(string procName)
    {
        nint handle;
        lock (s_opengl32Gate)
        {
            if (s_opengl32 == 0)
            {
                if (!NativeLibrary.TryLoad("opengl32.dll", out handle))
                    return 0;
                s_opengl32 = handle;
            }
            handle = s_opengl32;
        }
        return NativeLibrary.TryGetExport(handle, procName, out var address) ? address : 0;
    }

    private void DisposeWin32()
    {
        // Only unbind when THIS context is current — never clobber another
        // context the thread may have made current in the meantime.
        if (wglGetCurrentContext() == _ctx)
            wglMakeCurrent(0, 0);
        if (_legacyCtx != 0)
        {
            wglDeleteContext(_legacyCtx);
            _legacyCtx = 0;
        }
        if (_ctx != 0)
        {
            wglDeleteContext(_ctx);
            _ctx = 0;
        }
        if (_hdc != 0 && _hwnd != 0)
        {
            ReleaseDC(_hwnd, _hdc);
            _hdc = 0;
        }
        if (_hwnd != 0)
        {
            DestroyWindow(_hwnd);
            _hwnd = 0;
        }
        if (_className is not null)
        {
            UnregisterClassW(_className, _hInstance);
            _className = null;
        }
    }

    // --- hand-rolled P/Invoke (kernel32 / user32 / gdi32 / opengl32) ---

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassExW
    {
        public uint Size, Style;
        public nint Proc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public nint MenuName, ClassName, IconSm;
    }

    // Field order must match wingdi.h PIXELFORMATDESCRIPTOR exactly: the byte
    // run starts at cColorBits (offset 8); iPixelType sits at offset 25, not
    // first. A scrambled order still passes nSize/nVersion validation and the
    // lenient system GDI path, but a strict driver (Mesa) rejects it with
    // ChoosePixelFormat == 0.
    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormatDescriptor
    {
        public ushort Size, Version;
        public uint Flags;
        public byte ColorBits, RedBits, RedShift, GreenBits, GreenShift, BlueBits, BlueShift,
            AlphaBits, AlphaShift,
            AccumBits, AccumRedBits, AccumGreenBits, AccumBlueBits, AccumAlphaBits,
            DepthBits, StencilBits, AuxBuffers, PixelType, LayerType, Reserved;
        public uint LayerMask, VisibleMask, DamageMask;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WglCreateContextAttribsArb(nint hdc, nint share, int* attribs);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassExW c);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style,
        int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("user32.dll")]
    private static extern int DestroyWindow(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort UnregisterClassW(string cls, nint inst);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int ChoosePixelFormat(nint dc, ref PixelFormatDescriptor pfd);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetPixelFormat(nint dc, int format, ref PixelFormatDescriptor pfd);

    [DllImport("opengl32.dll", SetLastError = true)]
    private static extern nint wglCreateContext(nint dc);

    [DllImport("opengl32.dll", SetLastError = true)]
    private static extern int wglMakeCurrent(nint dc, nint ctx);

    [DllImport("opengl32.dll")]
    private static extern int wglDeleteContext(nint ctx);

    [DllImport("opengl32.dll")]
    private static extern nint wglGetCurrentContext();

    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)]
    private static extern nint wglGetProcAddress(string name);
}
