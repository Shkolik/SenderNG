/*
 * PlotGlFailure.cs - why the Plot GL tab cannot start: a module-local failure
 * kind, the exception that carries the GL failure cause out of the context
 * constructor, and the single cause to kind mapping
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

namespace SenderNG.Modules.Common.Render;

/// <summary>The named reason a GL context could not be brought up. Carried by
/// <see cref="PlotGlUnavailableException"/> so the caller can map it to a
/// failure kind without pattern-matching on exception types.</summary>
public enum GlFailureCause { None, ContextCreate, VersionTooLow, Runtime, Other }

/// <summary>Why Plot GL cannot start. Module-local so the view model keeps
/// referencing no OpenGL binding types at all.</summary>
public enum PlotGlFailure
{
    /// <summary>Nothing reported; the tab is healthy.</summary>
    None,

    /// <summary>No usable OpenGL on this system: the library is missing, fails to
    /// load, or cannot produce a context. Decided by the context create attempt
    /// itself, never by a later render call.</summary>
    NotSupported,

    /// <summary>OpenGL loads and a context is creatable, but the version the
    /// viewport needs is not reported, so the GPU or driver is too old for what
    /// Plot GL needs. Narrow on purpose: this is the one cause the user can act
    /// on differently from NotSupported (update the driver), so nothing else may
    /// be folded into it. Extension problems are not detectable by a version
    /// check and land in Other.</summary>
    RuntimeError,

    /// <summary>Anything else, including a bug in the scene build, and any
    /// failure of a later operation rather than initialisation (Snap,
    /// SetupReadback).</summary>
    Other,
}

/// <summary>Thrown when the Plot GL context cannot be created for a reason we
/// can name. Carries the cause that reason maps from, so the diagnostics log
/// records a verdict rather than only a message.
/// <para>
/// There is deliberately no inner-exception parameter. An earlier version took
/// one and wrapped every failure from the engine as RuntimeError; that both
/// invented a cause (see the taxonomy on <see cref="PlotGlFailure.RuntimeError"/>)
/// and lost the original stack. Unexpected exceptions now propagate untouched to
/// the caller's catch, which logs them as they are, so nothing needs to be
/// carried here. An exception that cannot be attributed to a cause is not
/// thrown as if it could be - it is allowed to arrive unlabelled.
/// </para></summary>
internal sealed class PlotGlUnavailableException : Exception
{
    public PlotGlUnavailableException(string message, GlFailureCause cause)
        : base(message)
    {
        Cause = cause;
    }

    /// <summary>The named reason context creation failed. Never shown to the
    /// user; it selects the localized hint and is written to the diagnostics
    /// log.</summary>
    public GlFailureCause Cause { get; }
}

/// <summary>The single place a GL failure cause becomes a user-facing failure
/// kind.</summary>
public static class PlotGlFailures
{
    /// <summary>Map an exception + named cause onto a failure kind.
    /// <para>An exception that already knows its cause (a
    /// <see cref="PlotGlUnavailableException"/>) overrides the caller's cause:
    /// it is the more specific verdict. A version shortfall is the one
    /// actionable RuntimeError; context creation failure with no better
    /// attribution is NotSupported (the loader/driver story users can act on);
    /// everything unrecognised - including Ok-path noise - becomes Other, the
    /// generic bucket.</para></summary>
    public static PlotGlFailure Classify(Exception ex, GlFailureCause cause)
    {
        if (ex is PlotGlUnavailableException ue)
            cause = ue.Cause;

        return cause switch
        {
            GlFailureCause.None => PlotGlFailure.None,
            GlFailureCause.ContextCreate => PlotGlFailure.NotSupported,
            GlFailureCause.VersionTooLow => PlotGlFailure.RuntimeError,
            _ => PlotGlFailure.Other,
        };
    }
}
