/*
 * GlContext.cs - OpenGL context the plotter viewport renders on
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

namespace SenderNG.Modules.Plotter.GL.Rendering;

/// <summary>Owns the GL context the viewport renders on. A GL context renders on
/// the thread that owns it, so creation is synchronous on the UI thread (see
/// PlotterGlView.EnsureScene), never on the thread pool.
/// <para>
/// Task 1 placeholder: no GL is touched yet. <see cref="Create"/> returns an
/// inert instance, so the tab opens with an empty viewport and the failure path
/// (verdict + fallback panel) stays unexercised. Task 2 replaces this with real
/// WGL/GLX context creation and the version check that feeds
/// <see cref="GlFailureCause.VersionTooLow"/>.
/// </para></summary>
internal sealed class GlContext : IDisposable
{
    private GlContext()
    {
    }

    /// <summary>Create the context for the current thread. Task 2: throws
    /// <see cref="PlotGlUnavailableException"/> when the reason is nameable
    /// (loader missing, version too low); anything else propagates untouched to
    /// the caller's catch, which classifies it as ContextCreate.</summary>
    public static GlContext Create() => new();

    public void Dispose()
    {
        // Task 1: nothing was created, so nothing is released. Task 2 owns the
        // wglDeleteContext / glXDestroyContext call.
    }
}
