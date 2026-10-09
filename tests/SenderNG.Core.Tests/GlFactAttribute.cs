/*
 * GlFactAttribute.cs - discovery-time skip gate for tests that need a real GL context
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
using SenderNG.Modules.Plotter.GL.Rendering;

namespace SenderNG.Core.Tests;

/// <summary>
/// Discovery-time gate for tests that need a real OpenGL context, in the same
/// spirit as <see cref="HardwareFactAttribute"/>: xunit v2 has no dynamic skip,
/// so the availability probe runs once per process while the attribute is being
/// constructed and sets <see cref="FactAttribute.Skip"/> when it fails. A
/// machine without a GPU (or with only the 1.1 software rasterizer) reports the
/// test as Skipped instead of Failed; this machine has a working GPU, so these
/// tests run for real and the skip reason below would carry the probe's own
/// exception text if that ever stopped being true.
/// </summary>
public sealed class GlFactAttribute : FactAttribute
{
    private static readonly Lazy<Exception?> Probe = new(ProbeContext);

    public GlFactAttribute()
    {
        if (SkipReason is { } reason)
            Skip = reason;
    }

    /// <summary>The skip message when the one-per-process probe failed, else
    /// null — shared with <see cref="GlTheoryAttribute"/> so both gates run
    /// the exact same probe.</summary>
    internal static string? SkipReason =>
        Probe.Value is { } failure
            ? $"no OpenGL context available: {failure.GetType().Name}: {failure.Message}"
            : null;

    private static Exception? ProbeContext()
    {
        try
        {
            PreloadSystemOpenGl();
            using var ctx = GlContext.Create();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>Test-only: prepare the GL stack for the test session. Two jobs,
    /// both in force before the first GL call:
    /// <para>
    /// 1. Load the SYSTEM OpenGL loader by absolute path. The GL test bins
    /// carry an app-local Mesa deployment (the only way to reach GL 3.3 in a
    /// headless CI session — the session has no hardware display path, only
    /// GDI Generic 1.1). gdi32's ChoosePixelFormat resolves "opengl32.dll" by
    /// bare name with app-local first, and Mesa's loader does not register
    /// pixel formats with GDI — ChoosePixelFormat then returns 0. Pre-loading
    /// %SystemRoot%\System32\opengl32.dll pins gdi32's lookup to the system
    /// module (formats work, exactly like a real machine), while .NET's
    /// DllImport probing (full path, app-local first) still binds Mesa for
    /// every wgl/gl call. Without an app-local deployment both paths resolve
    /// to the same system module — a harmless no-op.
    /// </para>
    /// <para>
    /// 2. Pin Mesa's Gallium driver to llvmpipe (software) unless the caller
    /// already chose one. The app-local Mesa build's d3d12 Gallium driver
    /// (26.2.4, Intel UHD 630) passes context/clear/readback but CRASHES THE
    /// TEST HOST when any primitive draw executes — reproduced with a
    /// minimal 2-vertex GL_LINES draw and a 3-vertex triangle, both clean at
    /// glGetError, dying in the first sync (glFinish/glReadPixels) after the
    /// draw. llvmpipe is the same Mesa, same GL 4.6 core context, same
    /// pixels — only the rasterizer is the CPU — so every assertion here
    /// still runs for real. A caller-set GALLIUM_DRIVER always wins.
    /// Deliberately test-only, like the preload above: production must never
    /// override the user's driver choice.
    /// </para></summary>
    internal static void PreloadSystemOpenGl()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GALLIUM_DRIVER")))
            Environment.SetEnvironmentVariable("GALLIUM_DRIVER", "llvmpipe");
        var system = Path.Combine(Environment.SystemDirectory, "opengl32.dll");
        if (File.Exists(system))
            LoadLibraryW(system);
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern nint LoadLibraryW(string path);
}
