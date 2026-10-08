/*
 * GlContext.cs - offscreen GL 3.3 core context on Windows (hand-rolled WGL)
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
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;
using GlApi = Silk.NET.OpenGL.GL;

namespace SenderNG.Modules.Plotter.GL.Rendering;

/// <summary>Offscreen GL 3.3 core context on Windows: a never-shown top-level
/// window + WGL. No message pump is required; we only ever render into an FBO.
/// Linux/EGL lives in GlContext.Egl.cs (Task 8) and shares this surface.
/// <para>
/// Creation is synchronous on the owning (UI) thread — see
/// PlotterGlView.EnsureScene — because a GL context renders on the thread that
/// owns it. <see cref="Create"/> throws <see cref="InvalidOperationException"/>
/// for a nameless bring-up failure (window, pixel format, wglCreateContext) and
/// <see cref="PlotGlUnavailableException"/> when the reason is nameable (the
/// driver is below 3.3), so the view's catch maps it through
/// <see cref="PlotGlFailures.Classify"/>.
/// </para></summary>
internal sealed unsafe class GlContext : IDisposable, INativeContext
{
    private const int WsOverlapped = 0;
    private const uint CsHredraw = 0x0002, CsVredraw = 0x0001;
    private const uint PfdDrawToWindow = 0x00000004, PfdSupportOpengl = 0x00000020,
                       PfdDoublebuffer = 0x00000001;
    private const byte PfdTypeRgba = 0;
    // WGL_CONTEXT_* from the ARB_create_context spec (the values from the
    // task sketch did not match the spec: flags bit 0x1 is
    // WGL_CONTEXT_FORWARD_COMPATIBLE_BIT, the core-profile bit 0x1 belongs to
    // the PROFILE_MASK attribute, not FLAGS). A core 3.3 context only needs
    // major/minor + profile mask; forward compatibility is implicit on Windows.
    private const int WglContextMajorVersion = 0x2091, WglContextMinorVersion = 0x2092,
                      WglContextProfileMask = 0x9126, WglContextCoreProfileBit = 0x00000001;

    private static int s_classSeq;
    private static readonly WndProc s_defWndProc = DefWindowProc;
    private static readonly object s_opengl32Gate = new();
    private static nint s_opengl32;

    private IntPtr _hwnd, _hdc, _legacyCtx, _ctx;
    private string? _className;
    private nint _hInstance;
    private GlApi? _gl;
    private bool _disposed;

    private GlContext()
    {
    }

    public string Version { get; private set; } = "";

    public string Renderer { get; private set; } = "";

    public string Vendor { get; private set; } = "";

    public bool IsGles => false;

    /// <summary>The loaded GL API. Throws once disposed, so a stray frame
    /// after teardown fails loudly instead of calling into a dead loader.</summary>
    internal GlApi Api => _gl ?? throw new ObjectDisposedException(nameof(GlContext));

    internal bool IsDisposed => _disposed;

    public static GlContext Create()
    {
        var ctx = new GlContext();
        try
        {
            ctx.Initialize();
            return ctx;
        }
        catch
        {
            // A failure at any stage releases whatever the earlier stages
            // allocated (window class, window, DC, contexts).
            ctx.Dispose();
            throw;
        }
    }

    private void Initialize()
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

        // 6. Upgrade to a real 3.3 core context through
        //    wglCreateContextAttribsARB; fall back to the legacy context when
        //    the entry point is missing or the request fails (the version
        //    audit below then decides whether legacy is good enough).
        var pfnCreate = GetUsableProc(wglGetProcAddress("wglCreateContextAttribsARB"));
        if (pfnCreate != 0)
        {
            var createAttribs = Marshal.GetDelegateForFunctionPointer<WglCreateContextAttribsArb>(pfnCreate);
            int* attribs = stackalloc int[]
            {
                WglContextMajorVersion, 3,
                WglContextMinorVersion, 3,
                WglContextProfileMask, WglContextCoreProfileBit,
                0,
            };
            _ctx = createAttribs(_hdc, 0, attribs);
        }

        if (_ctx == 0)
        {
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
        //    INativeContext members below). The context must be current.
        _gl = new GlApi(this);

        // 9. Audit what the driver reports. glGetString needs the current
        //    context, which step 7/6 established.
        Version = GlString(StringName.Version);
        Renderer = GlString(StringName.Renderer);
        Vendor = GlString(StringName.Vendor);
        if (!MeetsMinimumVersion(Version, 3, 3))
            throw new PlotGlUnavailableException(
                $"OpenGL {Version} is below the required 3.3 ({Renderer})",
                GlFailureCause.VersionTooLow);
    }

    private string GlString(StringName name)
    {
        var p = Api.GetString(name);
        if (p == null)
            throw new InvalidOperationException($"glGetString({name}) returned null (no current context?)");
        return Marshal.PtrToStringUTF8((nint)p) ?? "";
    }

    /// <summary>Parse the leading MAJOR.MINOR of a glGetString(GL_VERSION)
    /// string ("4.6.0 NVIDIA 546.33", "3.3.0 - Build 30.0.101.1340").
    /// Unparseable input reports "too old" — the conservative answer, since we
    /// cannot claim a version the driver did not state.</summary>
    private static bool MeetsMinimumVersion(string version, int major, int minor)
    {
        var s = version.AsSpan().TrimStart();
        var dot = s.IndexOf('.');
        if (dot <= 0 || !int.TryParse(s[..dot], out var gotMajor))
            return false;
        var rest = s[(dot + 1)..];
        var end = 0;
        while (end < rest.Length && char.IsAsciiDigit(rest[end]))
            end++;
        if (end == 0 || !int.TryParse(rest[..end], out var gotMinor))
            return false;
        return gotMajor > major || (gotMajor == major && gotMinor >= minor);
    }

    public void MakeCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_ctx == 0 || wglMakeCurrent(_hdc, _ctx) == 0)
            throw new InvalidOperationException($"wglMakeCurrent failed: {Marshal.GetLastWin32Error()}");
    }

    public bool IsCurrent => !_disposed && wglGetCurrentContext() == _ctx;

    /// <summary>Entry-point resolver for Silk's loader: wglGetProcAddress first
    /// (everything 1.2+), the opengl32.dll export table second (the GL 1.1
    /// names wglGetProcAddress refuses). Returns 0 when neither knows the
    /// symbol — extension entry points a driver does not expose stay unloaded
    /// instead of throwing during context creation.</summary>
    public nint GetProcAddress(string procName)
    {
        var p = wglGetProcAddress(procName);
        return GetUsableProc(p) != 0 ? p : GetFromOpengl32(procName);
    }

    IntPtr INativeContext.GetProcAddress(string procName, int? slot) => GetProcAddress(procName);

    bool INativeContext.TryGetProcAddress(string procName, out IntPtr address, int? slot)
    {
        address = GetProcAddress(procName);
        return address != 0;
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gl?.Dispose();
        _gl = null;
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
