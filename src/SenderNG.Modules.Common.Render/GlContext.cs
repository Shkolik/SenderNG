/*
 * GlContext.cs - offscreen GL context, managed surface + runtime platform
 * dispatch (hand-rolled WGL on Windows, EGL surfaceless on Linux, CGL on macOS).
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

namespace SenderNG.Modules.Common.Render;

/// <summary>Offscreen GL context: GL 3.3 core on Windows (never-shown
/// top-level window + WGL, GlContext.Win32.cs) and surfaceless EGL on Linux
/// (GlContext.Egl.cs — desktop GL 3.3 on x64, GLES 3.0 on arm64) and a
/// no-drawable CGL core-profile context on macOS (GlContext.Cocoa.cs).
/// Platform
/// construction is dispatched at runtime (Create), not with #if WINDOWS: the
/// repo targets a single net9.0 TFM.
/// <para>
/// Creation is synchronous on the owning (UI) thread — see
/// PlotterGlView.EnsureScene — because a GL context renders on the thread that
/// owns it. <see cref="Create"/> throws <see cref="InvalidOperationException"/>
/// for a nameless bring-up failure (window, pixel format, wglCreateContext) and
/// <see cref="PlotGlUnavailableException"/> when the reason is nameable (the
/// driver is below 3.3), so the view's catch maps it through
/// <see cref="PlotGlFailures.Classify"/>.
/// </para></summary>
public sealed unsafe partial class GlContext : IDisposable, INativeContext
{
    private GlApi? _gl;
    private bool _disposed;

    private GlContext()
    {
    }

    /// <summary>true on the EGL path when the context is GLES (Linux arm64);
    /// always false on the Win32 and CGL paths. Shader preambles key off this.</summary>
    public bool IsGles { get; private set; }

    public string Version { get; private set; } = "";

    public string Renderer { get; private set; } = "";

    public string Vendor { get; private set; } = "";

    // --- context-path evidence (Task 2 review): which creation path actually
    //     produced the current context, and what the driver says it is. The
    //     test GlReadbackTests.Context_is_the_mandated_3_3_core_context audits
    //     these on Windows; the EGL path never sets them (WGL-only concepts).

    /// <summary>wglGetProcAddress("wglCreateContextAttribsARB") returned a
    /// callable pointer (extension present, not a NULL/sentinel).</summary>
    internal bool ArbCreateResolved { get; private set; }

    /// <summary>The ARB call itself returned a context. False either when the
    /// extension was missing (see <see cref="ArbCreateResolved"/>) or when the
    /// driver rejected the request — both fall back to the legacy bootstrap
    /// context (see <see cref="UsedLegacyFallback"/>).</summary>
    internal bool ArbCreateSucceeded { get; private set; }

    /// <summary>The final context IS the legacy wglCreateContext bootstrap
    /// (ARB path absent or failed) — i.e. NOT a created 3.3 core context.</summary>
    internal bool UsedLegacyFallback { get; private set; }

    /// <summary>The exact attribute list passed to the ARB call
    /// (key=value, terminated by 0), or "" when the call never ran.</summary>
    internal string ArbCreateAttribs { get; private set; } = "";

    /// <summary>glGetIntegerv(GL_CONTEXT_PROFILE_MASK) on the final context:
    /// 0x1 = core, 0x2 = compatibility, 0 = legacy context created without
    /// profile attributes (or an invalid enum for pre-3.2 drivers).</summary>
    internal int ProfileMask { get; private set; }

    /// <summary>glGetIntegerv(GL_CONTEXT_FLAGS) on the final context. Uses
    /// GL's QUERY numbering — GL_CONTEXT_FLAG_FORWARD_COMPATIBLE_BIT = 0x1,
    /// GL_CONTEXT_FLAG_DEBUG_BIT = 0x2 — which is the mirror image of the
    /// WGL request numbering (WGL debug = 0x1, WGL forward-compatible = 0x2);
    /// EGL numbering matches WGL. Read bit 0x1 for forward-compatible here.</summary>
    internal int ContextFlagsMask { get; private set; }

    /// <summary>"path=..." token for the one-line creation log.</summary>
    internal string CreationPath { get; private set; } = "";

    /// <summary>The loaded GL API. Throws once disposed, so a stray frame
    /// after teardown fails loudly instead of calling into a dead loader.</summary>
    public GlApi Api => _gl ?? throw new ObjectDisposedException(nameof(GlContext));

    public bool IsDisposed => _disposed;

    /// <summary>ARM64 selects the GLES 3.0 flavor of the EGL path; the WGL
    /// path is desktop GL regardless.</summary>
    internal static bool IsArm64 =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    /// <summary>Runtime platform dispatch (not #if WINDOWS — single net9.0
    /// TFM). Each backend's P/Invokes simply never fire on the wrong OS, so a
    /// DllNotFound could only come from a broken dispatch.</summary>
    private static GlBackend Backend =>
        OperatingSystem.IsWindows() ? GlBackend.Win32 :
        OperatingSystem.IsMacOS() ? GlBackend.Cgl : GlBackend.Egl;

    private enum GlBackend
    {
        Win32,
        Egl,
        Cgl
    }

    public static GlContext Create()
    {
        var ctx = new GlContext();
        try
        {
            switch (Backend)
            {
                case GlBackend.Win32:
                    ctx.InitializeWin32();
                    break;
                case GlBackend.Egl:
                    ctx.InitializeEgl();
                    break;
                case GlBackend.Cgl:
                    ctx.InitializeCocoa();
                    break;
                default:
                    throw new InvalidOperationException($"unknown GL backend: {Backend}");
            }
            // One line per created context (platform init runs exactly once)
            // for the app's captured stdout: which driver did THIS process
            // bind — the hardware ICD or a Mesa deployment? Version/Renderer/
            // Vendor are the glGetString reads and profileMask/flags the
            // glGetIntegerv reads performed on the audited current context.
            Console.WriteLine(
                $"[Plotter.GL] context ok: {ctx.Version} | renderer \"{ctx.Renderer}\" | " +
                $"vendor \"{ctx.Vendor}\" | profileMask=0x{ctx.ProfileMask:X} | " +
                $"flags=0x{ctx.ContextFlagsMask:X} | {ctx.CreationPath}");
            return ctx;
        }
        catch
        {
            // A failure at any stage releases whatever the earlier stages
            // allocated (window class, window, DC, contexts / EGL display,
            // context).
            ctx.Dispose();
            throw;
        }
    }

    private string GlString(StringName name)
    {
        var p = Api.GetString(name);
        if (p == null)
            throw new InvalidOperationException($"glGetString({name}) returned null (no current context?)");
        return Marshal.PtrToStringUTF8((nint)p) ?? "";
    }

    /// <summary>Parse the leading MAJOR.MINOR of a glGetString(GL_VERSION)
    /// string ("4.6.0 NVIDIA 546.33", "3.3.0 - Build 30.0.101.1340"). GLES
    /// drivers report the spec-mandated "OpenGL ES 3.2 &lt;info&gt;" form, so
    /// the "OpenGL ES" prefix is stripped before the digit parse — desktop
    /// strings never carry it and are unaffected. Unparseable input reports
    /// "too old" — the conservative answer, since we cannot claim a version
    /// the driver did not state.</summary>
    private static bool MeetsMinimumVersion(string version, int major, int minor)
    {
        var s = version.AsSpan().TrimStart();
        if (s.StartsWith("OpenGL ES".AsSpan()))
            s = s["OpenGL ES".Length..].TrimStart();
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
        switch (Backend)
        {
            case GlBackend.Win32:
                MakeCurrentWin32();
                break;
            case GlBackend.Egl:
                MakeCurrentEgl();
                break;
            case GlBackend.Cgl:
                MakeCurrentCocoa();
                break;
            default:
                throw new InvalidOperationException($"unknown GL backend: {Backend}");
        }
    }

    public bool IsCurrent =>
        !_disposed && Backend switch
        {
            GlBackend.Win32 => IsCurrentWin32(),
            GlBackend.Egl => IsCurrentEgl(),
            GlBackend.Cgl => IsCurrentCocoa(),
            _ => false
        };

    /// <summary>Entry-point resolver for Silk's loader. Platform halves:
    /// wglGetProcAddress + opengl32.dll on Windows; eglGetProcAddress + the
    /// driver library's export table on Linux; dlopen/dlsym against
    /// OpenGL.framework on macOS. Returns 0 when none knows the symbol —
    /// extension entry points a driver does not expose stay unloaded instead
    /// of throwing during context creation.</summary>
    public nint GetProcAddress(string procName) => Backend switch
    {
        GlBackend.Win32 => GetProcAddressWin32(procName),
        GlBackend.Egl => GetProcAddressEgl(procName),
        GlBackend.Cgl => GetProcAddressCocoa(procName),
        _ => 0
    };

    IntPtr INativeContext.GetProcAddress(string procName, int? slot) => GetProcAddress(procName);

    bool INativeContext.TryGetProcAddress(string procName, out IntPtr address, int? slot)
    {
        address = GetProcAddress(procName);
        return address != 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gl?.Dispose();
        _gl = null;
        switch (Backend)
        {
            case GlBackend.Win32:
                DisposeWin32();
                break;
            case GlBackend.Egl:
                DisposeEgl();
                break;
            case GlBackend.Cgl:
                DisposeCocoa();
                break;
        }
    }
}
