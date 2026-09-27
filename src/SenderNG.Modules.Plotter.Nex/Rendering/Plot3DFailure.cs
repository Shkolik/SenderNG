/*
 * Plot3DFailure.cs - why the Plot 3D tab cannot start: a module-local failure
 * kind, the exception that carries the engine result code out of the scene
 * constructor, and the single result-code to kind mapping
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

namespace SenderNG.Modules.Plotter.Nex.Rendering;

/// <summary>Why Plot 3D cannot start. Module-local so the view model keeps
/// referencing no HelixToolkit types at all.</summary>
internal enum Plot3DFailure
{
    /// <summary>Nothing reported; the tab is healthy.</summary>
    None,

    /// <summary>Vulkan itself is unusable on this system: the loader is
    /// missing or vkInitialize failed. Reached through the vkInitialize()
    /// pre-check, not from an engine return value — see Task 3 Step 0.</summary>
    NotSupported,

    /// <summary>Vulkan is available but no 1.3 device could be initialised.
    /// One bucket for several upstream causes (sub-1.3 device, no suitable
    /// queues, zero devices, driver error) because OnInitializing collapses
    /// them; the precise cause goes to the diagnostics log.</summary>
    RuntimeError,

    /// <summary>Anything else, including a bug in the scene build, and any
    /// failure of a later operation rather than initialisation (Snap,
    /// SetupRenderTarget).</summary>
    Other,
}

/// <summary>Thrown by the scene constructor when the Plot 3D context cannot be
/// created. Carries the engine's result code, plus the originating exception
/// when there was one, so the diagnostics log can record both the verdict and
/// the cause instead of only a code.</summary>
internal sealed class Plot3DUnavailableException : Exception
{
    public Plot3DUnavailableException(ResultCode code, Exception? inner = null)
        : base($"Vulkan context initialization failed: {code}.", inner)
    {
        Code = code;
    }

    /// <summary>The engine's own verdict, or the verdict our own capability
    /// probe reached. Never shown to the user; it selects the localized hint
    /// and is written to the diagnostics log.</summary>
    public ResultCode Code { get; }
}

/// <summary>The single place a HelixToolkit result code becomes a
/// user-facing failure kind.</summary>
internal static class Plot3DFailures
{
    /// <summary>Map an engine result code onto a failure kind.
    /// <para>CompileError is deliberately absent: shaders compile long after
    /// context creation, so Initialize() cannot return it on this path. Any
    /// unrecognised code - including Ok, which the caller never maps - becomes
    /// Other, the generic bucket.</para></summary>
    public static Plot3DFailure Map(ResultCode code) => code switch
    {
        ResultCode.NotSupported => Plot3DFailure.NotSupported,
        ResultCode.RuntimeError => Plot3DFailure.RuntimeError,
        _ => Plot3DFailure.Other,
    };
}
